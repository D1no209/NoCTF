ARG TARGET_IMAGE=noctf-awdp-v2-target:local
FROM ${TARGET_IMAGE} AS target-baseline

FROM alpine:3.22
RUN apk add --no-cache diffutils findutils python3 wget \
    && addgroup -S checker \
    && adduser -S -G checker checker
COPY --from=target-baseline /opt/challenge /opt/noctf-baseline
COPY --chown=checker:checker check.sh exploit.sh replay-and-diff.sh /checker/
RUN chmod 0555 /checker/*.sh \
    && chmod -R a-w /opt/noctf-baseline
USER checker
ENTRYPOINT ["/checker/check.sh"]
