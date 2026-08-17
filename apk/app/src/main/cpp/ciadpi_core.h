#ifndef CIADPI_CORE_H
#define CIADPI_CORE_H

#include <stdint.h>
#include <stdbool.h>

typedef struct {
    int split_pos;          // -s
    int split_http;         // -d
    int split_tls;          // -e
    int fake_ttl;           // -f
    int fake_offset;        // -k
    int out_of_order;       // -r
    int bad_checksum;       // -a
    int socks_port;         // listening port (default 1080)
} dpi_params_t;

int ciadpi_start(const char *params_str, int port);
void ciadpi_stop(void);
bool ciadpi_is_running(void);

#endif // CIADPI_CORE_H
