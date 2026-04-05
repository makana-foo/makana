ARG DEX_REF=410a58f5ac7769f2ab9b98deabce323a93a8c73a
ARG FINAL_BASE_IMAGE=ghcr.io/dexidp/dex:latest-alpine

FROM golang:1.26-alpine AS build
ARG DEX_REF

RUN apk add --no-cache git patch ca-certificates build-base sqlite-dev

WORKDIR /src
RUN git clone https://github.com/dexidp/dex.git . \
    && git checkout "${DEX_REF}"

COPY dex-oidc-logout-id-token.patch /tmp/dex-oidc-logout-id-token.patch
RUN git apply /tmp/dex-oidc-logout-id-token.patch

RUN CGO_ENABLED=1 GOOS=linux GOARCH=amd64 go build -trimpath -o /out/dex ./cmd/dex

FROM ${FINAL_BASE_IMAGE} AS final

COPY --from=build /out/dex /usr/local/bin/dex
