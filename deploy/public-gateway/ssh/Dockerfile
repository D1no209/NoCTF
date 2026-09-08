FROM docker.m.daocloud.io/library/alpine:3.24@sha256:28bd5fe8b56d1bd048e5babf5b10710ebe0bae67db86916198a6eec434943f8b AS server
RUN apk add --no-cache openssh-server=10.3_p1-r1 \
    && adduser -D -u 65532 -h /home/noctf -s /sbin/nologin noctf \
    && passwd -d noctf \
    && mkdir -p /run/sshd /config \
    && chmod 0755 /run/sshd
# Password, session, SFTP and arbitrary-forward restrictions are mandatory in the supplied configuration.
HEALTHCHECK --interval=30s --timeout=2s --retries=2 CMD kill -0 1
ENTRYPOINT ["/usr/sbin/sshd", "-D", "-e", "-f", "/config/sshd_config"]

FROM docker.m.daocloud.io/library/alpine:3.24@sha256:28bd5fe8b56d1bd048e5babf5b10710ebe0bae67db86916198a6eec434943f8b AS client
RUN apk add --no-cache openssh-client-default=10.3_p1-r1 autossh=1.4g-r3 \
    && adduser -D -u 65532 -h /home/noctf -s /sbin/nologin noctf \
    && mkdir -p /control /config \
    && chown 65532:65532 /control
USER 65532:65532
HEALTHCHECK --interval=10s --timeout=2s --retries=2 CMD ["ssh", "-F", "none", "-S", "/control/master", "-O", "check", "gateway"]
ENTRYPOINT ["autossh", "-M", "0", "-N", "-F", "/config/client.conf", "gateway"]

# Local integration fixture only; deployment images keep client/server packages separate.
FROM server AS prototype
RUN apk add --no-cache openssh-client-default=10.3_p1-r1 autossh=1.4g-r3 \
    && mkdir -p /control \
    && chown 65532:65532 /control
