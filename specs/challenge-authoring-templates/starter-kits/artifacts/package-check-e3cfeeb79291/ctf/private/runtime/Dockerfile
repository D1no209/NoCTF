FROM python:3.12-slim-bookworm
WORKDIR /opt/challenge
RUN mkdir -p public private && chown -R 10001:10001 /opt/challenge
COPY --chmod=0444 server.py /opt/challenge/server.py
COPY --chmod=0444 welcome.txt /opt/challenge/public/welcome.txt
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/opt/challenge/server.py"]
