FROM m.daocloud.io/docker.io/library/python:3.13-alpine@sha256:540c7d91f98ff6880174c40e99067bf5941eb54d818a7a5e094d188b196a934d
ADD --checksum=sha256:3cf934477f4fb1ee9e19e49c31fb33f5ffe3283300076f59afad8b8ccf1e1621 https://github.com/fatedier/frp/releases/download/v0.68.0/frp_0.68.0_linux_amd64.tar.gz /tmp/frp.tar.gz
RUN tar -xzf /tmp/frp.tar.gz -C /tmp \
    && cp /tmp/frp_0.68.0_linux_amd64/frpc /usr/local/bin/frpc \
    && cp /tmp/frp_0.68.0_linux_amd64/frps /usr/local/bin/frps \
    && chmod 0755 /usr/local/bin/frpc /usr/local/bin/frps \
    && rm -rf /tmp/frp.tar.gz /tmp/frp_0.68.0_linux_amd64 \
    && mkdir -p /run/noctf-gateway \
    && chown 65532:65532 /run/noctf-gateway
COPY deploy/public-gateway/lease_guard.py /app/lease_guard.py
COPY deploy/public-gateway/status_probe.py /app/status_probe.py
COPY deploy/public-gateway/healthcheck.py /app/healthcheck.py
USER 65532:65532
HEALTHCHECK --interval=10s --timeout=2s --start-period=5s --retries=2 CMD ["python", "/app/healthcheck.py"]
ENTRYPOINT ["python", "/app/lease_guard.py"]
