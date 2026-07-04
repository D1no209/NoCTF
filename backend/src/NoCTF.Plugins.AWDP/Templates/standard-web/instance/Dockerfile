FROM python:3.12-alpine

RUN apk add --no-cache curl tar unzip

WORKDIR /app

COPY app.py /app/app.py
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

RUN chmod +x /usr/local/bin/docker-entrypoint.sh \
    && mkdir -p /app/data /flag \
    && printf 'Welcome to the NoCTF AWDP standard template.\n' > /app/data/welcome.txt

EXPOSE 80

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
