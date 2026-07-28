FROM python:3.13-alpine

RUN addgroup -g 10001 noctf && adduser -D -u 10001 -G noctf noctf
COPY server.py /app/server.py
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "/app/server.py"]
