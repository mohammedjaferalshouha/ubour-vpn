#ifndef TUN2SOCKS_CORE_H
#define TUN2SOCKS_CORE_H

#include <stdint.h>
#include <stdbool.h>

int tun2socks_start(int tun_fd, const char *socks_host, int socks_port, const char *dns_server);
void tun2socks_stop(void);
void tun2socks_get_stats(uint64_t *rx_bytes, uint64_t *tx_bytes);

#endif // TUN2SOCKS_CORE_H
