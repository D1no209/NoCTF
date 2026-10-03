FROM busybox:1.37@sha256:9db7b59979c38555a39def84a31fb98b5296952f9e3afd4f6f11f05b07adfab0
USER 10001:10001
EXPOSE 80
ENTRYPOINT ["sh", "-c", "mkdir -p /dev/shm/www && printf 'NoCTF capacity fixture' > /dev/shm/www/index.html && exec httpd -f -p 80 -h /dev/shm/www"]
