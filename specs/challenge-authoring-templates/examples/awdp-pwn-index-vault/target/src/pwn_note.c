#define _POSIX_C_SOURCE 200809L

#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <signal.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <unistd.h>

#ifndef VARIANT
#define VARIANT 0
#endif

enum
{
    NOTE_COUNT = 4,
    NOTE_SIZE = 96,
    FLAG_MAX_BYTES = 4096,
    REQUEST_SIZE = 256
};

typedef struct
{
    char notes[NOTE_COUNT][NOTE_SIZE];
    char secret[FLAG_MAX_BYTES + 1];
} Vault;

static Vault vault;

static void initialize_vault(void)
{
    const char *defaults[NOTE_COUNT] = {
        "training-service-online",
        "patches-must-preserve-read",
        "one-index-per-note",
        "reserved-for-operators"
    };

    memset(&vault, 0, sizeof(vault));
    for (size_t index = 0; index < NOTE_COUNT; ++index)
        snprintf(vault.notes[index], NOTE_SIZE, "%s", defaults[index]);

    const char *flag = getenv("FLAG");
    if (flag == NULL || flag[0] == '\0')
        flag = "NOCTF_RUNTIME_FLAG_UNAVAILABLE";
    snprintf(vault.secret, sizeof(vault.secret), "%s", flag);
}

static void handle_request(char *request)
{
    request[strcspn(request, "\r\n")] = '\0';
    if (strcmp(request, "PING") == 0)
    {
        puts("PONG");
        return;
    }

    if (strncmp(request, "READ ", 5) != 0)
    {
        puts("ERR command");
        return;
    }

#if VARIANT == 2
    puts("ERR disabled");
    return;
#else
    char *end = NULL;
    long index = 0;
    errno = 0;
    index = strtol(request + 5, &end, 10);
    if (errno != 0 || end == request + 5)
    {
        puts("ERR index");
        return;
    }
    while (*end == ' ' || *end == '\t')
        ++end;
    if (*end != '\0')
    {
        puts("ERR index");
        return;
    }

#if VARIANT == 1
    if (index < 0 || index >= NOTE_COUNT)
#else
    if (index < 0 || index > NOTE_COUNT)
#endif
    {
        puts("ERR range");
        return;
    }

    const char *object_bytes = (const char *)&vault;
    const char *value = object_bytes + ((size_t)index * NOTE_SIZE);
    printf("VALUE:%s\n", value);
#endif
}

int main(void)
{
    signal(SIGPIPE, SIG_IGN);
    initialize_vault();

    char request[REQUEST_SIZE] = {0};
    if (fgets(request, sizeof(request), stdin) == NULL)
        return 1;
    handle_request(request);
    return 0;
}
