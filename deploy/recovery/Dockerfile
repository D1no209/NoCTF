ARG POSTGRES_IMAGE=postgres@sha256:16bc17c64a573ef34162af9298258d1aec548232985b33ed7b1eac33ba35c229
ARG MINIO_CLIENT_IMAGE=minio/mc@sha256:a7fe349ef4bd8521fb8497f55c6042871b2ae640607cf99d9bede5e9bdf11727

FROM ${MINIO_CLIENT_IMAGE} AS minio_client
FROM ${POSTGRES_IMAGE}

RUN apk add --no-cache age coreutils gzip jq minisign tar

COPY --from=minio_client /usr/bin/mc /usr/local/bin/mc
COPY common.sh backup.sh restore.sh verify.sh /usr/local/lib/noctf-recovery/
COPY entrypoint.sh /usr/local/bin/noctf-recovery

ENTRYPOINT ["/usr/local/bin/noctf-recovery"]
