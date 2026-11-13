FROM python:3.12-slim-bookworm
WORKDIR /opt/challenge
RUN mkdir -p /opt/challenge/state && chown 10001:10001 /opt/challenge/state
COPY --chmod=0444 server.py /opt/challenge/server.py
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/opt/challenge/server.py"]
