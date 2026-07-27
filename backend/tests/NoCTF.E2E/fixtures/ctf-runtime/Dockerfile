FROM busybox:1.37

USER 10001:10001
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "mkdir -p /dev/shm/www && printf '%s' \"$FLAG\" > /dev/shm/www/index.html && exec httpd -f -p 8080 -h /dev/shm/www"]
