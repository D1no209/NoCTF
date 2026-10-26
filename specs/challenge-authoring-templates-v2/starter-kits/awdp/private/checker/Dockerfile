FROM alpine:3.22
RUN apk add --no-cache python3 wget \
    && addgroup -S -g 10001 checker \
    && adduser -S -u 10001 -G checker checker
COPY --chown=checker:checker check.sh exploit.sh inspect-file.py /checker/
RUN chmod 0555 /checker/*.sh
USER 10001:10001
ENTRYPOINT ["/checker/check.sh"]
