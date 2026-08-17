FROM gcc:14-bookworm AS builder

WORKDIR /src
COPY src/pwn_note.c ./pwn_note.c

RUN mkdir -p /out \
    && gcc -std=c17 -O2 -Wall -Wextra -Werror -DVARIANT=0 pwn_note.c -o /out/pwn-note-vulnerable \
    && gcc -std=c17 -O2 -Wall -Wextra -Werror -DVARIANT=1 pwn_note.c -o /out/pwn-note-fixed \
    && gcc -std=c17 -O2 -Wall -Wextra -Werror -DVARIANT=2 pwn_note.c -o /out/pwn-note-rule-violation

FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends socat tar coreutils \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 challenge \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin challenge \
    && install -d -o 10001 -g 10001 -m 0755 /opt/challenge/bin /noctf/fix

COPY --from=builder --chown=10001:10001 --chmod=0555 /out/pwn-note-vulnerable /opt/challenge/bin/pwn-note

USER 10001:10001
EXPOSE 31337

ENTRYPOINT ["socat", "TCP-LISTEN:31337,reuseaddr,fork", "EXEC:/opt/challenge/bin/pwn-note"]
