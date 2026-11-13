#include "ciadpi_core.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <arpa/inet.h>
#include <netdb.h>
#include <android/log.h>

#define TAG "Ubour_CiaDpi"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

static volatile bool g_running = false;
static int g_server_fd = -1;
static pthread_t g_server_thread;
static dpi_params_t g_dpi_params;

// Parse parameters like "-s 1 -d 1 -e 1 -f -1 -r 1 -a -1"
static void parse_params(const char *str, dpi_params_t *params) {
    memset(params, 0, sizeof(dpi_params_t));
    params->split_pos = 1;
    params->split_http = 1;
    params->split_tls = 1;
    params->fake_ttl = 1;
    params->out_of_order = 1;
    params->socks_port = 1080;

    if (!str || strlen(str) == 0) return;

    char buf[512];
    strncpy(buf, str, sizeof(buf) - 1);
    buf[sizeof(buf) - 1] = '\0';

    char *tok = strtok(buf, " ");
    while (tok) {
        if (strcmp(tok, "-s") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->split_pos = atoi(tok);
        } else if (strcmp(tok, "-d") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->split_http = atoi(tok);
        } else if (strcmp(tok, "-e") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->split_tls = atoi(tok);
        } else if (strcmp(tok, "-f") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->fake_ttl = atoi(tok);
        } else if (strcmp(tok, "-r") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->out_of_order = atoi(tok);
        } else if (strcmp(tok, "-a") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->bad_checksum = atoi(tok);
        } else if (strcmp(tok, "-p") == 0) {
            tok = strtok(NULL, " ");
            if (tok) params->socks_port = atoi(tok);
        }
        tok = strtok(NULL, " ");
    }
}

// Find TLS SNI offset in TLS ClientHello
static int find_tls_sni_offset(const uint8_t *data, size_t len) {
    if (len < 5 || data[0] != 0x16 || data[1] != 0x03) return -1;
    size_t pos = 5;
    if (pos + 4 > len || data[pos] != 0x01) return -1;
    pos += 4; // Skip Handshake Header
    pos += 2 + 32; // Skip version + random
    if (pos >= len) return -1;
    uint8_t sess_len = data[pos++];
    pos += sess_len; // Skip session id
    if (pos + 2 > len) return -1;
    uint16_t cipher_len = (data[pos] << 8) | data[pos + 1];
    pos += 2 + cipher_len;
    if (pos + 1 > len) return -1;
    uint8_t comp_len = data[pos++];
    pos += comp_len; // Skip compression
    if (pos + 2 > len) return -1;
    pos += 2; // Skip extensions length

    while (pos + 4 <= len) {
        uint16_t ext_type = (data[pos] << 8) | data[pos + 1];
        uint16_t ext_len = (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        if (ext_type == 0x0000) { // server_name extension
            return (int)pos;
        }
        pos += ext_len;
    }
    return -1;
}

// Send payload with DPI bypass modifications
static ssize_t send_with_dpi_bypass(int sock, const uint8_t *buf, size_t len, const dpi_params_t *params) {
    if (len == 0) return 0;

    int split = 0;
    // Check if TLS ClientHello
    int sni_pos = find_tls_sni_offset(buf, len);
    if (sni_pos > 0 && params->split_tls) {
        split = sni_pos;
    } else if (params->split_http && len > 4 && (memcmp(buf, "GET ", 4) == 0 || memcmp(buf, "POST", 4) == 0 || memcmp(buf, "HEAD", 4) == 0)) {
        split = params->split_http > 0 ? params->split_http : 2;
    } else if (params->split_pos > 0 && len > (size_t)params->split_pos) {
        split = params->split_pos;
    }

    if (split > 0 && (size_t)split < len) {
        // Send fake packet with low TTL if configured
        if (params->fake_ttl > 0) {
            int orig_ttl = 64;
            socklen_t optlen = sizeof(orig_ttl);
            getsockopt(sock, IPPROTO_IP, IP_TTL, &orig_ttl, &optlen);
            
            int fake_ttl = params->fake_ttl;
            setsockopt(sock, IPPROTO_IP, IP_TTL, &fake_ttl, sizeof(fake_ttl));

            const char *fake_payload = "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n";
            send(sock, fake_payload, strlen(fake_payload), MSG_DONTWAIT);

            // Restore original TTL
            setsockopt(sock, IPPROTO_IP, IP_TTL, &orig_ttl, sizeof(orig_ttl));
        }

        // Send 1st segment
        ssize_t s1 = send(sock, buf, split, 0);
        if (s1 <= 0) return s1;

        // Small delay if needed for TCP segment separation
        usleep(1000);

        // Send 2nd segment
        ssize_t s2 = send(sock, buf + split, len - split, 0);
        if (s2 <= 0) return s1;

        return s1 + s2;
    }

    return send(sock, buf, len, 0);
}

typedef struct {
    int client_fd;
    dpi_params_t params;
} client_context_t;

static void *client_handler(void *arg) {
    client_context_t *ctx = (client_context_t *)arg;
    int client_fd = ctx->client_fd;
    dpi_params_t params = ctx->params;
    free(ctx);

    uint8_t buf[4096];
    // 1. SOCKS5 Auth Handshake
    ssize_t n = recv(client_fd, buf, sizeof(buf), 0);
    if (n < 2 || buf[0] != 0x05) {
        close(client_fd);
        return NULL;
    }

    // Response: Version 5, No Auth (0x00)
    uint8_t auth_resp[2] = {0x05, 0x00};
    send(client_fd, auth_resp, 2, 0);

    // 2. SOCKS5 Request
    n = recv(client_fd, buf, sizeof(buf), 0);
    if (n < 7 || buf[0] != 0x05 || buf[1] != 0x01) { // CONNECT command
        close(client_fd);
        return NULL;
    }

    char target_host[256] = {0};
    uint16_t target_port = 0;
    int dest_sock = -1;

    if (buf[3] == 0x01) { // IPv4
        struct sockaddr_in target_addr;
        memset(&target_addr, 0, sizeof(target_addr));
        target_addr.sin_family = AF_INET;
        memcpy(&target_addr.sin_addr, buf + 4, 4);
        memcpy(&target_port, buf + 8, 2);
        target_addr.sin_port = target_port;
        inet_ntop(AF_INET, &target_addr.sin_addr, target_host, sizeof(target_host));

        dest_sock = socket(AF_INET, SOCK_STREAM, 0);
        if (dest_sock >= 0) {
            int flag = 1;
            setsockopt(dest_sock, IPPROTO_TCP, TCP_NODELAY, &flag, sizeof(flag));
            if (connect(dest_sock, (struct sockaddr *)&target_addr, sizeof(target_addr)) != 0) {
                close(dest_sock);
                dest_sock = -1;
            }
        }
    } else if (buf[3] == 0x03) { // Domain name
        uint8_t domain_len = buf[4];
        if (5 + domain_len + 2 <= n) {
            memcpy(target_host, buf + 5, domain_len);
            target_host[domain_len] = '\0';
            memcpy(&target_port, buf + 5 + domain_len, 2);

            struct addrinfo hints, *res = NULL;
            memset(&hints, 0, sizeof(hints));
            hints.ai_family = AF_UNSPEC;
            hints.ai_socktype = SOCK_STREAM;

            char port_str[8];
            snprintf(port_str, sizeof(port_str), "%u", ntohs(target_port));

            if (getaddrinfo(target_host, port_str, &hints, &res) == 0 && res) {
                dest_sock = socket(res->ai_family, res->ai_socktype, res->ai_protocol);
                if (dest_sock >= 0) {
                    int flag = 1;
                    setsockopt(dest_sock, IPPROTO_TCP, TCP_NODELAY, &flag, sizeof(flag));
                    if (connect(dest_sock, res->ai_addr, res->ai_addrlen) != 0) {
                        close(dest_sock);
                        dest_sock = -1;
                    }
                }
                freeaddrinfo(res);
            }
        }
    }

    if (dest_sock < 0) {
        uint8_t rep_fail[10] = {0x05, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0};
        send(client_fd, rep_fail, sizeof(rep_fail), 0);
        close(client_fd);
        return NULL;
    }

    // Success response
    uint8_t rep_ok[10] = {0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0};
    send(client_fd, rep_ok, sizeof(rep_ok), 0);

    // 3. Bidirectional Relay with DPI Bypass on first client packet
    bool first_packet = true;
    fd_set fds;
    int max_fd = (client_fd > dest_sock ? client_fd : dest_sock) + 1;

    while (g_running) {
        FD_ZERO(&fds);
        FD_SET(client_fd, &fds);
        FD_SET(dest_sock, &fds);

        struct timeval tv = { .tv_sec = 30, .tv_usec = 0 };
        int ret = select(max_fd, &fds, NULL, NULL, &tv);
        if (ret <= 0) break;

        if (FD_ISSET(client_fd, &fds)) {
            ssize_t bytes = recv(client_fd, buf, sizeof(buf), 0);
            if (bytes <= 0) break;

            if (first_packet) {
                first_packet = false;
                send_with_dpi_bypass(dest_sock, buf, bytes, &params);
            } else {
                send(dest_sock, buf, bytes, 0);
            }
        }

        if (FD_ISSET(dest_sock, &fds)) {
            ssize_t bytes = recv(dest_sock, buf, sizeof(buf), 0);
            if (bytes <= 0) break;
            send(client_fd, buf, bytes, 0);
        }
    }

    close(client_fd);
    close(dest_sock);
    return NULL;
}

static void *server_thread_func(void *arg) {
    LOGI("CiaDpi server starting on 127.0.0.1:%d", g_dpi_params.socks_port);

    g_server_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (g_server_fd < 0) {
        LOGE("Failed to create server socket: %s", strerror(errno));
        return NULL;
    }

    int opt = 1;
    setsockopt(g_server_fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    struct sockaddr_in saddr;
    memset(&saddr, 0, sizeof(saddr));
    saddr.sin_family = AF_INET;
    saddr.sin_addr.s_addr = inet_addr("127.0.0.1");
    saddr.sin_port = htons(g_dpi_params.socks_port);

    if (bind(g_server_fd, (struct sockaddr *)&saddr, sizeof(saddr)) != 0) {
        LOGE("Failed to bind server socket: %s", strerror(errno));
        close(g_server_fd);
        g_server_fd = -1;
        return NULL;
    }

    if (listen(g_server_fd, 128) != 0) {
        LOGE("Failed to listen on server socket: %s", strerror(errno));
        close(g_server_fd);
        g_server_fd = -1;
        return NULL;
    }

    LOGI("CiaDpi server listening successfully");

    while (g_running) {
        struct sockaddr_in caddr;
        socklen_t clen = sizeof(caddr);
        int cfd = accept(g_server_fd, (struct sockaddr *)&caddr, &clen);
        if (cfd < 0) {
            if (!g_running) break;
            continue;
        }

        client_context_t *ctx = malloc(sizeof(client_context_t));
        if (ctx) {
            ctx->client_fd = cfd;
            ctx->params = g_dpi_params;
            pthread_t th;
            pthread_create(&th, NULL, client_handler, ctx);
            pthread_detach(th);
        } else {
            close(cfd);
        }
    }

    if (g_server_fd >= 0) {
        close(g_server_fd);
        g_server_fd = -1;
    }
    LOGI("CiaDpi server stopped");
    return NULL;
}

int ciadpi_start(const char *params_str, int port) {
    if (g_running) return 0;

    parse_params(params_str, &g_dpi_params);
    if (port > 0) g_dpi_params.socks_port = port;

    g_running = true;
    if (pthread_create(&g_server_thread, NULL, server_thread_func, NULL) != 0) {
        g_running = false;
        return -1;
    }

    return 0;
}

void ciadpi_stop(void) {
    if (!g_running) return;
    g_running = false;

    if (g_server_fd >= 0) {
        shutdown(g_server_fd, SHUT_RDWR);
        close(g_server_fd);
        g_server_fd = -1;
    }

    pthread_join(g_server_thread, NULL);
}

bool ciadpi_is_running(void) {
    return g_running;
}
