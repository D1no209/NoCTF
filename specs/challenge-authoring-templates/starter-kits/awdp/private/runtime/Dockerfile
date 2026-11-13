FROM python:3.12-slim-bookworm
WORKDIR /opt/challenge
RUN mkdir -p public/docs private /noctf/fix && chown -R 10001:10001 /opt/challenge /noctf
COPY --chmod=0444 server.py /opt/challenge/server.py
COPY --chown=10001:10001 resolver.py /opt/challenge/resolver.py
COPY --chmod=0444 welcome.txt /opt/challenge/public/welcome.txt
COPY --chmod=0444 guide.txt /opt/challenge/public/docs/guide.txt
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/opt/challenge/server.py"]
