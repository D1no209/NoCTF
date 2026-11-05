FROM python:3.12-slim-bookworm

COPY --chmod=0444 server.py /server.py
RUN mkdir -p /noctf/fix && chown 10001:10001 /noctf/fix

USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/server.py"]
