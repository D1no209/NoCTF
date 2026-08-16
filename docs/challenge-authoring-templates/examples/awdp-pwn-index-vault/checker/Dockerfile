FROM python:3.13-alpine

WORKDIR /checker
COPY --chmod=0555 checker.py /checker/checker.py

USER 65534:65534
ENTRYPOINT ["python", "/checker/checker.py"]
