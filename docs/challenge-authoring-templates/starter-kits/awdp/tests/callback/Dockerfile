FROM python:3.12-alpine

COPY --chmod=0444 server.py /server.py

USER 10001:10001
EXPOSE 8090
ENTRYPOINT ["python", "-B", "/server.py"]
