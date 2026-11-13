FROM docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8

COPY --chmod=0444 checker.py /checker.py
COPY --chmod=0555 exploit.sh /exploit.sh
USER 10001:10001
ENTRYPOINT ["python", "/checker.py"]
