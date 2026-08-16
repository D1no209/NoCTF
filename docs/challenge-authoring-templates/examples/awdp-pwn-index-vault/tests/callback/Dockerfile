FROM python:3.13-alpine

COPY --chmod=0444 server.py /server.py

USER nobody:nobody
EXPOSE 8080
ENTRYPOINT ["python", "-B", "/server.py"]
