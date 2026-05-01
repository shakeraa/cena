FROM node:20-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jre-headless curl ca-certificates \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

RUN npm install -g firebase-tools@13

WORKDIR /app
COPY firebase.json /app/firebase.json
# 2026-05-01: bake the import-or-seed + export-on-exit entrypoint into
# the image. The seed script itself stays bind-mounted so updating the
# dev roster doesn't require an image rebuild.
COPY entrypoint.sh /usr/local/bin/cena-firebase-entrypoint.sh
RUN chmod +x /usr/local/bin/cena-firebase-entrypoint.sh

EXPOSE 9099 4000

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:9099/ > /dev/null || exit 1

ENTRYPOINT ["/usr/local/bin/cena-firebase-entrypoint.sh"]
