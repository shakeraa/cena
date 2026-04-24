FROM node:20-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jre-headless curl ca-certificates \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

RUN npm install -g firebase-tools@13

WORKDIR /app
COPY firebase.json /app/firebase.json

EXPOSE 9099 4000

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:9099/ > /dev/null || exit 1

CMD ["firebase", "emulators:start", "--project", "cena-platform", "--only", "auth"]
