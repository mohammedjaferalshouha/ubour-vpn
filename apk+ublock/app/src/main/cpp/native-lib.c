#include <string.h>
#include <netdb.h>
#include <unistd.h>
#include <ctype.h>

#include <jni.h>
#include <android/log.h>

#include "byedpi/error.h"
#include "byedpi/proxy.h"
#include "byedpi/params.h"
#include "byedpi/packets.h"
#include "main.h"
#include "utils.h"

#define TAG "UbourVPN_Native"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

const enum demode DESYNC_METHODS[] = {
    DESYNC_NONE,
    DESYNC_SPLIT,
    DESYNC_DISORDER,
    DESYNC_FAKE,
    DESYNC_OOB,
    DESYNC_DISOOB,
};

enum hosts_mode {
    HOSTS_DISABLE,
    HOSTS_BLACKLIST,
    HOSTS_WHITELIST,
};

static JavaVM *g_vm = NULL;
static jclass g_adblock_class = NULL;
static jmethodID g_check_domain_mid = NULL;

JNIEXPORT jint JNI_OnLoad(
        JavaVM *vm,
        __attribute__((unused)) void *reserved) {
    g_vm = vm;
    default_params = params;
    
    JNIEnv *env = NULL;
    if ((*vm)->GetEnv(vm, (void **)&env, JNI_VERSION_1_6) == JNI_OK && env != NULL) {
        jclass clazz = (*env)->FindClass(env, "com/ubour/vpn/adblock/AdBlockEngine");
        if (clazz != NULL) {
            g_adblock_class = (jclass)(*env)->NewGlobalRef(env, clazz);
            g_check_domain_mid = (*env)->GetStaticMethodID(env, g_adblock_class, "checkDomainFromNative", "(Ljava/lang/String;)I");
            LOGI("AdBlockEngine JNI method registered successfully in ByeDpi");
        }
    }
    LOGI("ByeDPI JNI_OnLoad successful");
    return JNI_VERSION_1_6;
}

int check_domain_adblock(const char *domain) {
    if (!domain || !domain[0]) return 0;
    if (!g_vm || !g_adblock_class || !g_check_domain_mid) return 0;

    JNIEnv *env = NULL;
    int attached = 0;
    jint res = (*g_vm)->GetEnv(g_vm, (void **)&env, JNI_VERSION_1_6);
    if (res == JNI_EDETACHED) {
        if ((*g_vm)->AttachCurrentThread(g_vm, (void *)&env, NULL) != 0) {
            return 0;
        }
        attached = 1;
    } else if (res != JNI_OK || env == NULL) {
        return 0;
    }

    jstring jDomain = (*env)->NewStringUTF(env, domain);
    jint blockType = 0;
    if (jDomain != NULL) {
        blockType = (*env)->CallStaticIntMethod(env, g_adblock_class, g_check_domain_mid, jDomain);
        (*env)->DeleteLocalRef(env, jDomain);
    }

    if (attached) {
        (*g_vm)->DetachCurrentThread(g_vm);
    }
    return (int)blockType;
}

int parse_dns_qname(const char *data, size_t len, char *out, size_t max_out) {
    if (!data || len < 14 || !out || max_out < 4) return 0;
    
    // Check TCP offset (14) and raw UDP offset (12)
    size_t offsets[] = { 14, 12 };
    for (int idx = 0; idx < 2; idx++) {
        size_t offset = offsets[idx];
        if (offset >= len) continue;
        size_t out_len = 0;
        int valid = 1;
        while (offset < len && out_len < max_out - 1) {
            uint8_t label_len = (uint8_t)data[offset++];
            if (label_len == 0) break;
            if (label_len > 63 || offset + label_len > len) {
                valid = 0;
                break;
            }
            if (out_len > 0 && out_len < max_out - 1) {
                out[out_len++] = '.';
            }
            for (uint8_t i = 0; i < label_len && out_len < max_out - 1; i++) {
                char ch = (char)data[offset++];
                if (!isalnum((unsigned char)ch) && ch != '-' && ch != '_') {
                    valid = 0;
                    break;
                }
                out[out_len++] = (char)tolower((unsigned char)ch);
            }
            if (!valid) break;
        }
        out[out_len] = '\0';
        if (valid && out_len > 3 && strchr(out, '.') != NULL) {
            return 1;
        }
    }
    return 0;
}

JNIEXPORT jint JNICALL
Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniCreateSocketWithCommandLine(
        JNIEnv *env,
        __attribute__((unused)) jobject thiz,
        jobjectArray args) {
    int argc = (*env)->GetArrayLength(env, args);
    char *argv[argc];
    for (int i = 0; i < argc; i++) {
        jstring arg = (jstring) (*env)->GetObjectArrayElement(env, args, i);
        const char *arg_str = (*env)->GetStringUTFChars(env, arg, 0);
        argv[i] = strdup(arg_str);
        LOGI("Arg[%d]: %s", i, argv[i]);
        (*env)->ReleaseStringUTFChars(env, arg, arg_str);
    }

    reset_params();
    int res = parse_args(argc, argv);
    for (int i = 0; i < argc; i++) {
        free(argv[i]);
    }

    if (res < 0) {
        LOGE("parse_args failed with code %d", res);
        return -1;
    }

    int fd = listen_socket((struct sockaddr_ina *)&params.laddr);
    if (fd < 0) {
        LOGE("listen_socket failed");
        return -1;
    }
    LOGI("listen_socket succeeded, fd: %d", fd);

    return fd;
}

JNIEXPORT jint JNICALL
Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniCreateSocket(
        JNIEnv *env,
        __attribute__((unused)) jobject thiz,
        jstring ip,
        jint port,
        jint max_connections,
        jint buffer_size,
        jint default_ttl,
        jboolean custom_ttl,
        jboolean no_domain,
        jboolean desync_http,
        jboolean desync_https,
        jboolean desync_udp,
        jint desync_method,
        jint split_position,
        jboolean split_at_host,
        jint fake_ttl,
        jstring fake_sni,
        jbyte custom_oob_char,
        jboolean host_mixed_case,
        jboolean domain_mixed_case,
        jboolean host_remove_spaces,
        jboolean tls_record_split,
        jint tls_record_split_position,
        jboolean tls_record_split_at_sni,
        jint hosts_mode,
        jstring hosts,
        jboolean tfo,
        jint udp_fake_count,
        jboolean drop_sack,
        jint fake_offset) {
    
    reset_params();
    struct sockaddr_ina s;

    const char *address = (*env)->GetStringUTFChars(env, ip, 0);
    int res = get_addr(address, &s);
    (*env)->ReleaseStringUTFChars(env, ip, address);
    if (res < 0) {
        LOGE("get_addr failed for %s", address);
        return -1;
    }

    s.in.sin_port = htons(port);

    params.max_open = max_connections;
    params.bfsize = buffer_size;
    params.resolve = !no_domain;
    params.tfo = tfo;

    if (custom_ttl) {
        params.def_ttl = default_ttl;
        params.custom_ttl = 1;
    }

    if (!params.def_ttl) {
        if ((params.def_ttl = get_default_ttl()) < 1) {
            LOGE("get_default_ttl failed");
            reset_params();
            return -1;
        }
    }

    if (hosts_mode == HOSTS_WHITELIST && hosts != NULL) {
        struct desync_params *dp = add(
                (void *) &params.dp,
                &params.dp_count,
                sizeof(struct desync_params)
        );
        if (!dp) {
            reset_params();
            return -1;
        }

        const char *str = (*env)->GetStringUTFChars(env, hosts, 0);
        dp->file_ptr = data_from_str(str, &dp->file_size);
        (*env)->ReleaseStringUTFChars(env, hosts, str);
        dp->hosts = parse_hosts(dp->file_ptr, dp->file_size);
        if (!dp->hosts) {
            clear_params();
            return -1;
        }
    }

    struct desync_params *dp = add(
            (void *) &params.dp,
            &params.dp_count,
            sizeof(struct desync_params)
    );
    if (!dp) {
        reset_params();
        return -1;
    }

    if (hosts_mode == HOSTS_BLACKLIST && hosts != NULL) {
        const char *str = (*env)->GetStringUTFChars(env, hosts, 0);
        dp->file_ptr = data_from_str(str, &dp->file_size);
        (*env)->ReleaseStringUTFChars(env, hosts, str);
        dp->hosts = parse_hosts(dp->file_ptr, dp->file_size);
        if (!dp->hosts) {
            clear_params();
            return -1;
        }
    }

    dp->ttl = fake_ttl;
    dp->udp_fake_count = udp_fake_count;
    dp->drop_sack = drop_sack;
    dp->proto =
            IS_HTTP * desync_http |
            IS_HTTPS * desync_https |
            IS_UDP * desync_udp;
    dp->mod_http =
            MH_HMIX * host_mixed_case |
            MH_DMIX * domain_mixed_case |
            MH_SPACE * host_remove_spaces;

    struct part *part = add(
            (void *) &dp->parts,
            &dp->parts_n,
            sizeof(struct part)
    );
    if (!part) {
        reset_params();
        return -1;
    }

    enum demode mode = DESYNC_METHODS[desync_method];

    int offset_flag = dp->proto || desync_https ? OFFSET_SNI : OFFSET_HOST;

    part->flag = split_at_host ? offset_flag : 0;
    part->pos = split_position;
    part->m = mode;

    if (tls_record_split) {
        struct part *tlsrec_part = add(
                (void *) &dp->tlsrec,
                &dp->tlsrec_n,
                sizeof(struct part)
        );

        if (!tlsrec_part) {
            reset_params();
            return -1;
        }

        tlsrec_part->flag = tls_record_split_at_sni ? offset_flag : 0;
        tlsrec_part->pos = tls_record_split_position;
    }

    if (mode == DESYNC_FAKE && fake_sni != NULL) {
        dp->fake_offset = fake_offset;

        const char *sni = (*env)->GetStringUTFChars(env, fake_sni, 0);
        LOGI("fake_sni: %s", sni);
        res = change_tls_sni(sni, fake_tls.data, fake_tls.size);
        (*env)->ReleaseStringUTFChars(env, fake_sni, sni);
        if (res) {
            LOGE("error change_tls_sni");
            return -1;
        }
    }

    if (mode == DESYNC_OOB) {
        dp->oob_char[0] = custom_oob_char;
        dp->oob_char[1] = 1;
    }

    if (dp->proto) {
        dp = add((void *)&params.dp,
                 &params.dp_count, sizeof(struct desync_params));
        if (!dp) {
            clear_params();
            return -1;
        }
    }

    params.mempool = mem_pool(0);
    if (!params.mempool) {
        LOGE("mem_pool allocation failed");
        clear_params();
        return -1;
    }

    int fd = listen_socket(&s);
    if (fd < 0) {
        LOGE("listen_socket failed for port %d", port);
        return -1;
    }
    LOGI("listen_socket succeeded, fd: %d, port: %d", fd, port);

    return fd;
}

JNIEXPORT jint JNICALL
Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniStartProxy(
        __attribute__((unused)) JNIEnv *env,
        __attribute__((unused)) jobject thiz,
        jint fd) {
    LOGI("Starting proxy event loop on fd: %d", fd);
    NOT_EXIT = 1;
    int res = event_loop(fd);
    LOGI("Proxy event loop finished with code: %d", res);
    return res;
}

JNIEXPORT jint JNICALL
Java_io_github_dovecoteescapee_byedpi_core_ByeDpiProxy_jniStopProxy(
        __attribute__((unused)) JNIEnv *env,
        __attribute__((unused)) jobject thiz,
        jint fd) {
    LOGI("Stopping proxy on fd: %d", fd);

    NOT_EXIT = 0;
    int res = shutdown(fd, SHUT_RDWR);
    close(fd);
    reset_params();

    return 0;
}