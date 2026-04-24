# ADR-0058 — Admin ingestion cloud-directory S3 provider

- **Status**: Accepted
- **Date**: 2026-04-22
- **Decision Makers**: Shaker (project owner), Architecture
- **Task**: queue `t_fc741f5aad44` (Scope B: code + ops handoff ADR)
- **Related**: [ADR-0033](0033-cena-ocr-stack.md), [ADR-0053](0053-external-integration-adapter-pattern.md), [production-content-pipeline-roadmap §11.2](../roadmap/production-content-pipeline-roadmap.md)

---

## Context

The admin ingestion endpoint (`POST /api/admin/ingestion/cloud-dir/*` via `IngestionPipelineService.ListCloudDirectoryAsync` / `IngestCloudDirectoryAsync`) lets curators point at an external directory full of PDFs/images and stream them into the OCR cascade.

Today (`IngestionPipelineCloudDir.cs:35-42`) the `s3` provider branch is a placeholder returning empty results. Only the `local` provider is implemented (path-traversal guard via `Ingestion:CloudWatchDirs` allowlist, SHA-256 dedup, content-type mapping, lastModified ordering).

Prod requires object storage, not local directories: .NET host pods are stateless, batches are large (hundreds of Bagrut PDFs), and ops needs to drop content into a durable bucket from outside the cluster.

## Decisions

### 1. Provider abstraction, not conditional strings

Replace the string-switch at `IngestionPipelineCloudDir.cs:35` / `:97` with an `ICloudDirectoryProvider` seam implemented by `LocalDirectoryProvider` + `S3DirectoryProvider`. `IngestionPipelineService` dispatches on `request.Provider` via an `ICloudDirectoryProviderRegistry`. Adding Azure Blob / GCS later = drop in another provider, no dispatch-site edits.

### 2. IAM model — IRSA primary, static-key fallback

**EKS (production)**: IAM Roles for Service Accounts. The K8s `ServiceAccount` for admin-api gets `eks.amazonaws.com/role-arn: arn:aws:iam::…:role/cena-ingest-reader` annotated onto it; AWS SDK picks up the role via projected OIDC token automatically — zero secrets in the cluster.

**Non-EKS / kind / local dev**: set `Ingestion:S3:AccessKey` + `Ingestion:S3:SecretKey` via K8s Secret or config. The SDK's default credential chain honors both paths without branching in app code (`AmazonS3Client(new AmazonS3Config{…})` + `DefaultAWSCredentialsIdentityResolver` picks IRSA → env → profile in that order).

**Rationale**: IRSA is the only option that avoids long-lived secrets in a cluster, which is a standing security posture win. Static keys exist for development environments where IRSA isn't available — not for production use.

### 3. Bucket allowlist — `Ingestion:S3Buckets`

Direct analog of the existing `Ingestion:CloudWatchDirs` allowlist that prevents directory-traversal on the `local` provider. An admin typing `prod-secrets-bucket` into the cloud-dir UI must produce a 401, not a silent slurp. Empty allowlist = S3 disabled.

### 4. Dedup strategy — two-tier (ETag-fast at list, SHA-256-accurate at ingest)

**At `list` time**: S3 `ListObjectsV2` returns an ETag per object. Cross-reference against `PipelineItemDocument.S3ETag + S3Bucket + S3Key` in Marten. If match → `AlreadyIngested=true` without a `GetObject` call. Cheap (~one catalog query per list).

**At `ingest` time**: download the object, compute SHA-256, check `ContentHash` (the existing dedup path). Slower but accurate — handles the edge case where the same file content appears under two different ETags (multipart boundaries differ) or two different buckets.

Contract extension: `PipelineItemDocument` gains two optional nullable fields — `S3Bucket` and `S3ETag`. Existing rows: null. Local-ingest rows: null. S3-ingest rows: populated. Zero-migration schema evolution per Marten's additive policy.

### 5. Permissions — read-only, `s3:ListBucket` + `s3:GetObject`

No writes, no deletes, no tags, no ACLs. If a curator needs to move objects out of the ingest bucket, they use the AWS console or a separate lifecycle rule — not our service.

### 6. Batch-size gate

Reject any ingest request with > 1000 files OR > 10 GB total size (`ContentLength` sum pre-download). Bounds S3 GET egress cost per click. Gate evaluated in the service before any `GetObject` call.

### 7. LocalStack for dev/prod parity

New profile-gated service in `docker-compose.yml`:

```yaml
localstack:
  image: localstack/localstack:3
  profiles: ["localstack"]
  ports: ["4566:4566"]
  environment:
    - SERVICES=s3
    - PERSISTENCE=1
  volumes:
    - localstack_data:/var/lib/localstack
```

Opt-in via `docker compose --profile localstack up -d`. `appsettings.Development.json` points at `http://localhost:4566` with test credentials. Dev exercises the exact code paths prod does, matching the dev/prod parity directive (ADR-docker-ocr-parity 2026-04-22).

## Ops handoff — required infrastructure changes

### IAM policy (attach to role `cena-ingest-reader`)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "CenaIngestList",
      "Effect": "Allow",
      "Action": ["s3:ListBucket"],
      "Resource": [
        "arn:aws:s3:::cena-ingest-prod",
        "arn:aws:s3:::cena-ingest-staging"
      ]
    },
    {
      "Sid": "CenaIngestGet",
      "Effect": "Allow",
      "Action": ["s3:GetObject"],
      "Resource": [
        "arn:aws:s3:::cena-ingest-prod/*",
        "arn:aws:s3:::cena-ingest-staging/*"
      ]
    }
  ]
}
```

### IRSA trust policy (role `cena-ingest-reader`)

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": {
      "Federated": "arn:aws:iam::ACCOUNT_ID:oidc-provider/oidc.eks.REGION.amazonaws.com/id/CLUSTER_ID"
    },
    "Action": "sts:AssumeRoleWithWebIdentity",
    "Condition": {
      "StringEquals": {
        "oidc.eks.REGION.amazonaws.com/id/CLUSTER_ID:sub":
          "system:serviceaccount:cena:cena-admin-api",
        "oidc.eks.REGION.amazonaws.com/id/CLUSTER_ID:aud": "sts.amazonaws.com"
      }
    }
  }]
}
```

### Terraform snippet

```hcl
resource "aws_iam_role" "cena_ingest_reader" {
  name               = "cena-ingest-reader"
  assume_role_policy = data.aws_iam_policy_document.cena_ingest_trust.json
}

resource "aws_iam_role_policy" "cena_ingest_s3" {
  role   = aws_iam_role.cena_ingest_reader.id
  policy = data.aws_iam_policy_document.cena_ingest_s3.json
}

resource "aws_s3_bucket" "cena_ingest_prod" {
  bucket = "cena-ingest-prod"
}

resource "aws_s3_bucket_public_access_block" "cena_ingest_prod" {
  bucket                  = aws_s3_bucket.cena_ingest_prod.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "cena_ingest_prod" {
  bucket = aws_s3_bucket.cena_ingest_prod.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}
```

### Helm values diff

```yaml
# deploy/helm/cena/values-production.yaml
serviceAccount:
  create: true
  name: cena-admin-api
  annotations:
    eks.amazonaws.com/role-arn: "arn:aws:iam::ACCOUNT_ID:role/cena-ingest-reader"

ingestion:
  s3:
    enabled: true
    region: "us-east-1"
    allowedBuckets:
      - "cena-ingest-prod"
    # IRSA is detected automatically from the SA annotation above.
    # accessKeySecretName: ""  # leave empty
```

### Env var mapping (config binding)

| Config key                           | Env var                         | Value source |
|--------------------------------------|---------------------------------|---------------|
| `Ingestion:S3:Enabled`               | `Ingestion__S3__Enabled`        | Helm value    |
| `Ingestion:S3:Region`                | `Ingestion__S3__Region`         | Helm value    |
| `Ingestion:S3:AllowedBuckets:0`      | `Ingestion__S3Buckets__0`       | Helm value    |
| `Ingestion:S3:ServiceUrl`            | `Ingestion__S3__ServiceUrl`     | Dev only (LocalStack endpoint) |
| `Ingestion:S3:AccessKey`             | `Ingestion__S3__AccessKey`      | K8s Secret (non-EKS only) |
| `Ingestion:S3:SecretKey`             | `Ingestion__S3__SecretKey`      | K8s Secret (non-EKS only) |

## Consequences

- **+** Prod can ingest from S3 without code changes once ops applies the Terraform + Helm diff.
- **+** Dev exercises S3 code path via LocalStack — no divergence from prod.
- **+** Adding Azure Blob / GCS later = one new `ICloudDirectoryProvider` implementation, no dispatch changes.
- **−** `PipelineItemDocument` schema grows by two optional fields. Marten handles additively; no migration.
- **−** `AWSSDK.S3` was already in the csproj (3.7.415.4) — no new transitive risk.
- **−** LocalStack image pulls (~400 MB) on first `--profile localstack up`; devs opt in.

## Not in this ADR

- Azure Blob / GCS providers — the SDKs are in the csproj but no consumer. Add when first real ingest source is Azure/GCS.
- S3 Object Lambda / S3 Select — not needed for the list+get use case.
- Cross-region replication — bucket-level, not app-level.
- Lifecycle rules (delete-after-ingest) — ops policy, handled outside the app.
