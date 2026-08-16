FROM alpine:3.22

COPY --chmod=0555 check.sh /check.sh

USER 10001:10001
ENTRYPOINT ["/check.sh"]
