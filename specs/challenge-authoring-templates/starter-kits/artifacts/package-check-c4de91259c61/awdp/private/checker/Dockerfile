FROM python:3.12-slim-bookworm
RUN mkdir -p /noctf && chown -R 10001:10001 /noctf
COPY --chmod=0444 checker.py /checker.py
USER 10001:10001
ENTRYPOINT ["python", "-B", "/checker.py"]
