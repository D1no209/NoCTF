FROM docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8

COPY server.py /app/server.py
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "/app/server.py"]
