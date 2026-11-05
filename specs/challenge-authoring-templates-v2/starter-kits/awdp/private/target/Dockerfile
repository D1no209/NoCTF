ARG PYTHON_IMAGE=python:3.13-alpine
FROM ${PYTHON_IMAGE}

RUN addgroup -S -g 10001 challenge && adduser -S -u 10001 -G challenge challenge
WORKDIR /opt/challenge
COPY --chown=challenge:challenge server.py public.txt secret.txt policy.txt ./
USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["python3", "/opt/challenge/server.py"]
