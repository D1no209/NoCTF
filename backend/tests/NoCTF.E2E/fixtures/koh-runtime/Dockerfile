FROM python:3.11.9-slim-bookworm

COPY server.py /app/server.py
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "/app/server.py"]
