FROM python:3.12-alpine

WORKDIR /app

RUN apk add --no-cache curl tar

COPY service.py /app/service.py
COPY docker-entrypoint.sh /usr/local/bin/noctf-awdp-entrypoint

RUN chmod +x /usr/local/bin/noctf-awdp-entrypoint

ENV NOCTF_FLAG=flag{awdp_template_flag}
EXPOSE 80

ENTRYPOINT ["noctf-awdp-entrypoint"]
