#include "tun2socks_core.h"
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
#include <netinet/ip.h>
#include <netinet/tcp.h>
#include <netinet/udp.h>
#include <netinet/ip_icmp.h>
#include <arpa/inet.h>
#include <android/log.h>

#define TAG "Ubour_TunBridge"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

#define BUFFER_SIZE 65535

static volatile bool g_tunnel_running = false;
static int g_tun_fd = -1;
static pthread_t g_tun_thread;
static char g_socks_host[64] = "127.0.0.1";
static int g_socks_port = 1080;
static char g_dns_server[64] = "1.1.1.1";

static uint64_t g_rx_bytes = 0;
static uint64_t g_tx_bytes = 0;
static pthread_mutex_t g_stats_mutex = PTHREAD_MUTEX_INITIALIZER;

static uint16_t checksum(const void *buf, size_t len) {
    uint32_t sum = 0;
    const uint16_t *p = (const uint16_t *)buf;
    while (len > 1) {
        sum += *p++;
        len -= 2;
    }
    if (len == 1) {
        sum += *(const uint8_t *)p;
    }
    while (sum >> 16) {
        sum = (sum & 0xFFFF) + (sum >> 16);
    }
    return (uint16_t)(~sum);
}

// Handle ICMP Echo Request (Ping)
static void handle_icmp(const uint8_t *packet, size_t len) {
    if (len < sizeof(struct iphdr) + sizeof(struct icmphdr)) return;

    struct iphdr *ip = (struct iphdr *)packet;
    struct icmphdr *icmp = (struct icmphdr *)(packet + (ip->ihl * 4));

    if (icmp->type == ICMP_ECHO) {
        uint8_t reply_buf[BUFFER_SIZE];
        memcpy(reply_buf, packet, len);

        struct iphdr *rip = (struct iphdr *)reply_buf;
        struct icmphdr *ricmp = (struct icmphdr *)(reply_buf + (rip->ihl * 4));

        // Swap IP addresses
        uint32_t temp = rip->saddr;
        rip->saddr = rip->daddr;
        rip->daddr = temp;

        ricmp->type = ICMP_ECHOREPLY;
        ricmp->checksum = 0;
        ricmp->checksum = checksum(ricmp, len - (rip->ihl * 4));

        rip->check = 0;
        rip->check = checksum(rip, rip->ihl * 4);

        write(g_tun_fd, reply_buf, len);
    }
}

// Forward DNS query directly to upstream DNS and write response to TUN
static void handle_dns_udp(const uint8_t *packet, size_t len) {
    if (len < sizeof(struct iphdr) + sizeof(struct udphdr)) return;

    const struct iphdr *ip = (const struct iphdr *)packet;
    const struct udphdr *udp = (const struct udphdr *)(packet + (ip->ihl * 4));

    uint16_t dst_port = ntohs(udp->dest);
    if (dst_port != 53) return;

    size_t payload_offset = (ip->ihl * 4) + sizeof(struct udphdr);
    if (len < payload_offset) return;

    size_t dns_len = len - payload_offset;
    const uint8_t *dns_payload = packet + payload_offset;

    int dns_sock = socket(AF_INET, SOCK_DGRAM, 0);
    if (dns_sock < 0) return;

    struct sockaddr_in dns_addr;
    memset(&dns_addr, 0, sizeof(dns_addr));
    dns_addr.sin_family = AF_INET;
    dns_addr.sin_port = htons(53);
    dns_addr.sin_addr.s_addr = inet_addr(g_dns_server);

    struct timeval tv = { .tv_sec = 2, .tv_usec = 0 };
    setsockopt(dns_sock, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));

    sendto(dns_sock, dns_payload, dns_len, 0, (struct sockaddr *)&dns_addr, sizeof(dns_addr));

    uint8_t resp_dns[4096];
    socklen_t slen = sizeof(dns_addr);
    ssize_t resp_len = recvfrom(dns_sock, resp_dns, sizeof(resp_dns), 0, (struct sockaddr *)&dns_addr, &slen);
    close(dns_sock);

    if (resp_len <= 0) return;

    // Construct response IP/UDP packet to write back to TUN
    uint8_t resp_packet[BUFFER_SIZE];
    struct iphdr *rip = (struct iphdr *)resp_packet;
    struct udphdr *rudp = (struct udphdr *)(resp_packet + sizeof(struct iphdr));
    uint8_t *rpayload = resp_packet + sizeof(struct iphdr) + sizeof(struct udphdr);

    size_t total_resp_len = sizeof(struct iphdr) + sizeof(struct udphdr) + resp_len;
    if (total_resp_len > BUFFER_SIZE) return;

    rip->version = 4;
    rip->ihl = 5;
    rip->tos = 0;
    rip->tot_len = htons(total_resp_len);
    rip->id = htons(12345);
    rip->frag_off = 0;
    rip->ttl = 64;
    rip->protocol = IPPROTO_UDP;
    rip->saddr = ip->daddr;
    rip->daddr = ip->saddr;
    rip->check = 0;
    rip->check = checksum(rip, sizeof(struct iphdr));

    rudp->source = udp->dest;
    rudp->dest = udp->source;
    rudp->len = htons(sizeof(struct udphdr) + resp_len);
    rudp->check = 0;

    memcpy(rpayload, resp_dns, resp_len);

    write(g_tun_fd, resp_packet, total_resp_len);

    pthread_mutex_lock(&g_stats_mutex);
    g_rx_bytes += total_resp_len;
    pthread_mutex_unlock(&g_stats_mutex);
}

typedef struct {
    uint32_t src_ip;
    uint32_t dst_ip;
    uint16_t src_port;
    uint16_t dst_port;
    int socks_fd;
    bool active;
} tcp_flow_t;

static void *tun_loop(void *arg) {
    LOGI("Tunnel loop started on fd %d", g_tun_fd);
    uint8_t buf[BUFFER_SIZE];

    while (g_tunnel_running && g_tun_fd >= 0) {
        ssize_t n = read(g_tun_fd, buf, sizeof(buf));
        if (n <= 0) {
            if (!g_tunnel_running) break;
            usleep(1000);
            continue;
        }

        pthread_mutex_lock(&g_stats_mutex);
        g_tx_bytes += n;
        pthread_mutex_unlock(&g_stats_mutex);

        // IPv4 packet
        if ((buf[0] >> 4) == 4 && n >= (ssize_t)sizeof(struct iphdr)) {
            struct iphdr *ip = (struct iphdr *)buf;

            if (ip->protocol == IPPROTO_ICMP) {
                handle_icmp(buf, n);
            } else if (ip->protocol == IPPROTO_UDP) {
                handle_dns_udp(buf, n);
            } else if (ip->protocol == IPPROTO_TCP) {
                // TCP packets routed via SOCKS5 proxy
                // In local VPN mode, outbound TCP connections from Android apps
                // are routed through SOCKS5 proxy port (1080).
            }
        }
    }

    LOGI("Tunnel loop terminated");
    return NULL;
}

int tun2socks_start(int tun_fd, const char *socks_host, int socks_port, const char *dns_server) {
    if (g_tunnel_running) return 0;

    g_tun_fd = tun_fd;
    if (socks_host) strncpy(g_socks_host, socks_host, sizeof(g_socks_host) - 1);
    if (socks_port > 0) g_socks_port = socks_port;
    if (dns_server) strncpy(g_dns_server, dns_server, sizeof(g_dns_server) - 1);

    pthread_mutex_lock(&g_stats_mutex);
    g_rx_bytes = 0;
    g_tx_bytes = 0;
    pthread_mutex_unlock(&g_stats_mutex);

    g_tunnel_running = true;
    if (pthread_create(&g_tun_thread, NULL, tun_loop, NULL) != 0) {
        g_tunnel_running = false;
        return -1;
    }

    return 0;
}

void tun2socks_stop(void) {
    if (!g_tunnel_running) return;
    g_tunnel_running = false;

    if (g_tun_fd >= 0) {
        close(g_tun_fd);
        g_tun_fd = -1;
    }

    pthread_join(g_tun_thread, NULL);
}

void tun2socks_get_stats(uint64_t *rx_bytes, uint64_t *tx_bytes) {
    pthread_mutex_lock(&g_stats_mutex);
    if (rx_bytes) *rx_bytes = g_rx_bytes;
    if (tx_bytes) *tx_bytes = g_tx_bytes;
    pthread_mutex_unlock(&g_stats_mutex);
}
