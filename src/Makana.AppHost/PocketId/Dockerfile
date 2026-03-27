FROM ghcr.io/pocket-id/pocket-id:v2

WORKDIR /bootstrap
COPY --chmod=+x bootstrap-pocketid.sh ./
COPY bootstrap-pocketid.sh pocketid-seed.zip ./

ENTRYPOINT ["/bin/sh", "-c", "/bootstrap/bootstrap-pocketid.sh && exec /app/docker/entrypoint.sh /app/pocket-id"]