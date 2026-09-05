FROM python:3.13-alpine

RUN addgroup -S challenge && adduser -S -G challenge challenge
WORKDIR /opt/challenge
COPY --chown=challenge:challenge server.py public.txt secret.txt policy.txt ./
USER challenge
EXPOSE 8080
ENTRYPOINT ["python3", "/opt/challenge/server.py"]
