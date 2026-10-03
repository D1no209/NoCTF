ARG POSTGRES_IMAGE=postgres@sha256:16bc17c64a573ef34162af9298258d1aec548232985b33ed7b1eac33ba35c229
FROM ${POSTGRES_IMAGE}

RUN apk add --no-cache age coreutils gzip jq minisign tar python3 py3-pip
COPY requirements.lock /tmp/requirements.lock
RUN python3 -m venv /opt/recovery && /opt/recovery/bin/pip install --require-hashes -r /tmp/requirements.lock
COPY recovery.py /usr/local/lib/noctf-recovery/recovery.py
ENTRYPOINT ["/opt/recovery/bin/python", "/usr/local/lib/noctf-recovery/recovery.py"]
