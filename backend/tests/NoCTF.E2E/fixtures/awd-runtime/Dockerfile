FROM python:3.13-alpine

COPY --chmod=0444 server.py /server.py

USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/server.py"]
