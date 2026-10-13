FROM python:3.12-slim-bookworm

COPY --chmod=0555 check.sh /check.sh
COPY --chmod=0444 checker.py /checker.py

USER 10001:10001
ENTRYPOINT ["/check.sh"]
