#!/bin/sh
# ADR-0058: bootstrap the dev ingest bucket on LocalStack first boot.
# Runs from /etc/localstack/init/ready.d/ after LocalStack's S3 service
# reports healthy. Idempotent — `create-bucket` on an existing bucket
# is a no-op for LocalStack.

set -eu

BUCKET="cena-ingest-dev"
REGION="us-east-1"

echo "[localstack-init] creating bucket s3://${BUCKET} in ${REGION}"
awslocal s3 mb "s3://${BUCKET}" --region "${REGION}" 2>/dev/null || true

# Seed a marker object so `aws s3 ls` doesn't return empty on a fresh
# cluster — makes it obvious the bucket is reachable.
echo "# Cena dev ingest bucket — drop PDFs here to test S3 ingestion" \
  | awslocal s3 cp - "s3://${BUCKET}/README.txt" --region "${REGION}"

echo "[localstack-init] done"
