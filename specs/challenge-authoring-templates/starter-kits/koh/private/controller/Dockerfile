FROM docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8
COPY --chmod=0444 controller.py /controller.py
USER 10001:10001
ENTRYPOINT ["python", "/controller.py"]
