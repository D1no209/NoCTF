FROM docker.m.daocloud.io/library/golang:1.27.1-alpine@sha256:cf6fca6641884b8433441b2b0652976f975e1d0fdd26d177eaaf8596087f3125 AS build
WORKDIR /src
COPY deploy/public-gateway/relay/go.mod deploy/public-gateway/relay/main.go ./
RUN CGO_ENABLED=0 GOTOOLCHAIN=local go build -trimpath -ldflags="-s -w" -o /relay .

FROM scratch
COPY --from=build /relay /relay
USER 65532:65532
ENTRYPOINT ["/relay"]
# The relay is PID 1 and exits on lease loss; health is queried using /relay health.
# No interpreter, shell, certificate, Docker socket, or per-probe process is needed for supervision.
