# Cena Platform — Mutual TLS for Inter-Service Communication Contract

**Layer:** Infrastructure / Security | **Runtime:** .NET 9 (gRPC) + Python 3.12 (gRPC)
**Status:** BLOCKER — inter-service gRPC is plaintext; no transport security

---

## 1. Scope

All gRPC communication between internal services requires mutual TLS (mTLS) in staging and production environments.

### Service Communication Matrix

| Source | Destination | Protocol | mTLS Required |
|--------|-------------|----------|---------------|
| .NET Actor Cluster | Python LLM ACL | gRPC | Yes (staging/prod) |
| .NET Actor Cluster | .NET Actor Cluster (remote nodes) | gRPC (Proto.Actor) | Yes (staging/prod) |
| .NET Actor Cluster | NATS (Synadia Cloud) | NATS protocol | TLS (server-side, Synadia enforces) |
| API Gateway | .NET Actor Cluster | gRPC | Yes (staging/prod) |
| Python LLM ACL | LLM Providers (Anthropic, Moonshot) | HTTPS | TLS (standard, not mTLS) |

---

## 2. Certificate Authority

| Parameter | Value |
|-----------|-------|
| CA Provider | AWS Private CA (ACM Private Certificate Authority) |
| CA Type | Subordinate CA under Cena root CA |
| Key Algorithm | RSA 2048 or ECDSA P-256 |
| CA Validity | 10 years |
| Service Cert Validity | 90 days |
| CRL Distribution | ACM-managed, checked on connection |

### Certificate Hierarchy

```
Cena Root CA (offline, HSM-protected)
└── Cena Services Subordinate CA (ACM PCA)
    ├── actor-cluster.cena.internal (server + client cert)
    ├── llm-acl.cena.internal (server + client cert)
    ├── api-gateway.cena.internal (server + client cert)
    └── admin-service.cena.internal (server + client cert)
```

---

## 3. Certificate Issuance

### Per-Service Certificate

Each service receives a certificate with:

| Field | Value |
|-------|-------|
| Subject CN | `{service-name}.cena.internal` |
| Subject Alternative Names (SAN) | DNS: `{service-name}.cena.internal`, `{service-name}.{namespace}.svc.cluster.local` |
| Key Usage | Digital Signature, Key Encipherment |
| Extended Key Usage | TLS Web Server Authentication, TLS Web Client Authentication |
| Validity | 90 days from issuance |

### Issuance Flow

1. Service deployment triggers certificate request via ACM PCA API.
2. ACM PCA issues certificate signed by subordinate CA.
3. Certificate + private key stored in AWS Secrets Manager.
4. Service loads cert on startup from Secrets Manager (not from filesystem).
5. Private key never leaves the service process memory.

---

## 4. Automatic Rotation

| Parameter | Value |
|-----------|-------|
| Rotation Period | Every 90 days (certificate validity) |
| Rotation Trigger | AWS Secrets Manager automatic rotation Lambda |
| Grace Period | New cert issued 7 days before old cert expires |
| Rollover Strategy | Dual-cert: service accepts both old and new cert during overlap |

### Rotation Flow

1. Day 83: Rotation Lambda requests new certificate from ACM PCA.
2. Day 83: New cert stored in Secrets Manager alongside old cert.
3. Day 83: Service picks up new cert on next health check cycle (every 5 min).
4. Day 83-90: Service presents new cert; peers accept both old and new (trust both).
5. Day 90: Old cert expires. Only new cert active.

### Failure Handling

- If rotation Lambda fails: CloudWatch alarm, PagerDuty alert.
- If service cannot load new cert: fall back to old cert (still valid for 7 days).
- If cert is expired and no rotation occurred: service refuses to start, alert fires.

---

## 5. mTLS Handshake

### gRPC Server (.NET Actor Cluster)

```
Server Configuration:
  - Listen on port 5001 (gRPC)
  - Load server certificate from Secrets Manager
  - Require client certificate (ClientCertificateMode.RequireCertificate)
  - Validate client cert chain against Cena subordinate CA
  - Reject connections with untrusted or expired client certs
```

### gRPC Client (Python LLM ACL -> .NET, or vice versa)

```
Client Configuration:
  - Load client certificate + private key from Secrets Manager
  - Set trusted CA bundle to Cena subordinate CA cert
  - Verify server certificate CN matches expected service name
  - TLS 1.3 minimum (TLS 1.2 acceptable as fallback)
```

### Cipher Suites (TLS 1.3)

```
TLS_AES_256_GCM_SHA384
TLS_AES_128_GCM_SHA256
TLS_CHACHA20_POLY1305_SHA256
```

---

## 6. Development Mode

| Environment | mTLS | Configuration |
|-------------|------|---------------|
| `local` | Disabled | Plaintext gRPC (`GRPC_TLS_ENABLED=false`) |
| `dev` | Optional | Self-signed certs via `dotnet dev-certs` |
| `staging` | Required | ACM PCA certs (same as prod) |
| `production` | Required | ACM PCA certs |

### Dev Self-Signed Certs

```bash
# Generate self-signed CA for local development
openssl req -x509 -newkey rsa:2048 -keyout ca-key.pem -out ca-cert.pem \
  -days 365 -nodes -subj "/CN=Cena Dev CA"

# Generate service cert signed by dev CA
openssl req -newkey rsa:2048 -keyout svc-key.pem -out svc-csr.pem \
  -nodes -subj "/CN=actor-cluster.cena.internal"
openssl x509 -req -in svc-csr.pem -CA ca-cert.pem -CAkey ca-key.pem \
  -out svc-cert.pem -days 90
```

### Environment Flag

```
GRPC_TLS_ENABLED=true|false    # Master switch for mTLS
GRPC_TLS_CERT_SOURCE=acm|file  # ACM Secrets Manager vs local file
```

---

## 7. NATS TLS

NATS connections to Synadia Cloud use server-side TLS (not mTLS).

| Parameter | Value |
|-----------|-------|
| Connection | `tls://connect.ngs.global:4222` |
| Auth | NATS JWT + NKey credentials (see nats-auth.md) |
| TLS Mode | Server-side TLS (Synadia enforces) |
| Client cert | Not required (auth via JWT) |
| CA Bundle | Synadia's public CA (or system CA bundle) |

---

## 8. Proto.Actor Remote TLS

Proto.Actor cluster nodes communicate over gRPC for actor-to-actor messaging.

| Parameter | Value |
|-----------|-------|
| Transport | gRPC with TLS |
| Certificate | Same service cert as the actor-cluster service |
| Peer validation | mTLS — each node validates the other's cert |
| Cluster discovery | Consul with TLS-verified node addresses |

### Configuration

```
Proto.Actor Remote:
  - AdvertisedHost: {pod-ip}.cena.internal
  - Port: 5002
  - ServerCredentials: SslServerCredentials(cert, key, ca, requireClientCert: true)
  - ClientCredentials: SslCredentials(ca, keyCertPair)
```

---

## 9. Certificate Monitoring

| Metric | Alert Threshold |
|--------|-----------------|
| Days until cert expiry | < 14 days: warning, < 3 days: critical |
| mTLS handshake failure rate | > 1% in 5 minutes |
| Certificate rotation success | Any failure triggers PagerDuty |
| TLS version used | Alert if TLS < 1.2 detected |

### Health Check

Each service exposes a `/health/tls` endpoint returning:

```json
{
  "tls_enabled": true,
  "cert_subject": "actor-cluster.cena.internal",
  "cert_expires_at": "2026-06-24T00:00:00Z",
  "cert_days_remaining": 89,
  "ca_subject": "Cena Services Subordinate CA",
  "tls_version": "TLSv1.3",
  "last_rotation": "2026-03-26T02:00:00Z"
}
```

---

## 10. Security Hardening

- Private keys are stored in Secrets Manager, never on disk or in container images.
- Certificate PEM files are loaded into memory at startup, not persisted.
- OCSP stapling enabled where supported for real-time revocation checking.
- Minimum TLS version: 1.2 (prefer 1.3).
- Disable client-initiated renegotiation.
- Log all TLS handshake failures with peer IP and failure reason (for intrusion detection).
