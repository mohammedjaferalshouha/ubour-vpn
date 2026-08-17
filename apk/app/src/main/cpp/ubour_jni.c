#include <jni.h>
#include <string.h>
#include <android/log.h>
#include "ciadpi_core.h"
#include "tun2socks_core.h"

#define TAG "Ubour_JNI"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, TAG, __VA_ARGS__)

JNIEXPORT jint JNICALL
Java_com_ubour_vpn_core_NativeEngine_startEngine(JNIEnv *env, jclass clazz, jstring params_, jint port) {
    const char *params = (*env)->GetStringUTFChars(env, params_, 0);
    LOGI("Starting native engine with params: %s on port %d", params, port);
    int res = ciadpi_start(params, (int)port);
    (*env)->ReleaseStringUTFChars(env, params_, params);
    return res;
}

JNIEXPORT void JNICALL
Java_com_ubour_vpn_core_NativeEngine_stopEngine(JNIEnv *env, jclass clazz) {
    LOGI("Stopping native engine");
    ciadpi_stop();
}

JNIEXPORT jint JNICALL
Java_com_ubour_vpn_core_NativeEngine_startTunnel(JNIEnv *env, jclass clazz, jint tun_fd, jstring socks_host_, jint socks_port, jstring dns_server_) {
    const char *socks_host = (*env)->GetStringUTFChars(env, socks_host_, 0);
    const char *dns_server = (*env)->GetStringUTFChars(env, dns_server_, 0);
    LOGI("Starting tunnel on fd %d -> %s:%d (DNS: %s)", tun_fd, socks_host, socks_port, dns_server);
    int res = tun2socks_start((int)tun_fd, socks_host, (int)socks_port, dns_server);
    (*env)->ReleaseStringUTFChars(env, socks_host_, socks_host);
    (*env)->ReleaseStringUTFChars(env, dns_server_, dns_server);
    return res;
}

JNIEXPORT void JNICALL
Java_com_ubour_vpn_core_NativeEngine_stopTunnel(JNIEnv *env, jclass clazz) {
    LOGI("Stopping tunnel");
    tun2socks_stop();
}

JNIEXPORT jlongArray JNICALL
Java_com_ubour_vpn_core_NativeEngine_getTrafficStats(JNIEnv *env, jclass clazz) {
    uint64_t rx = 0, tx = 0;
    tun2socks_get_stats(&rx, &tx);
    jlongArray result = (*env)->NewLongArray(env, 2);
    if (result == NULL) return NULL;
    jlong fill[2];
    fill[0] = (jlong)rx;
    fill[1] = (jlong)tx;
    (*env)->SetLongArrayRegion(env, result, 0, 2, fill);
    return result;
}
