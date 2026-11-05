FROM python:3.12-alpine

COPY --chmod=0555 check.sh /check.sh
COPY --chmod=0444 check.py /check.py
COPY --chmod=0555 exploit.sh /exploit.sh

USER 10001:10001
ENTRYPOINT ["/check.sh"]
