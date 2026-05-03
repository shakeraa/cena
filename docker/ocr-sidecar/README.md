# Cena OCR Sidecar

Python gRPC service exposing the heavy ML runners (Surya layout + Hebrew
text OCR, pix2tex math OCR) to the .NET `OcrCascadeService` via the
interface defined in [`app/ocr.proto`](app/ocr.proto).

Runs alongside the .NET API pods in Kubernetes. Not directly exposed to
end users.

## Why a sidecar

Surya and pix2tex have no .NET bindings and bring ~4 GB of torch + HF
model weight. Isolating them in their own container:

- Keeps the .NET API image small (< 500 MB) and rebuildable quickly
- Lets the ML deps scale independently under bursty OCR load
- Pins torch / transformers / surya-ocr versions without affecting other
  parts of the platform
- Matches the decomposition documented in ADR-0033

## Shape

```
docker/ocr-sidecar/
├── README.md                ← this file
├── Dockerfile               ← multi-arch (linux/arm64 + linux/amd64)
├── requirements.txt         ← pinned versions
├── .dockerignore
└── app/
    ├── ocr.proto            ← gRPC contract (shared with .NET via Grpc.Tools)
    ├── server.py            ← grpcio server entry point
    ├── surya_service.py     ← Surya layout + Hebrew text OCR
    ├── pix2tex_service.py   ← pix2tex math OCR
    ├── prewarm.py           ← init-container; downloads HF models once
    └── healthcheck.py       ← K8s readiness probe
```

## Build

```bash
# Multi-arch build (requires buildx)
docker buildx build --platform linux/arm64,linux/amd64 \
  -t cena/ocr-sidecar:dev -f docker/ocr-sidecar/Dockerfile docker/ocr-sidecar

# Local dev (single arch)
docker build -t cena/ocr-sidecar:dev docker/ocr-sidecar
```

## Run

```bash
# With a persistent model cache mounted:
docker run --rm -p 50051:50051 \
  -v cena-ocr-models:/hf-cache \
  -e HF_HOME=/hf-cache -e TRANSFORMERS_CACHE=/hf-cache \
  cena/ocr-sidecar:dev
```

Then from the .NET side:

```csharp
services.AddGrpcClient<OcrSidecar.OcrSidecarClient>(o =>
    o.Address = new Uri("http://ocr-sidecar:50051"));
```

## K8s / deployment

The canonical deployment pattern is:

1. **Init-container** `prewarm`: runs `prewarm.py` against a PV-mounted
   `/hf-cache`. Downloads Surya + pix2tex models once per PV lifecycle.
2. **Main container**: runs `server.py` with readiness probing
   `healthcheck.py`.
3. **Service**: ClusterIP on port 50051, selected by the API deployment.

`kubernetes/ocr-sidecar.yaml` (not yet shipped) will codify this.

## Apple Silicon caveats

- Docker Desktop on Mac does not pass MPS into containers. Local runs
  use CPU-only torch and are significantly slower. Development against
  the spike's native Python venv is preferred for latency-sensitive work.
- Production deploys to a Linux node with CUDA (or CPU-many-core if GPU
  is unavailable — see RDY-OCR-PORT for the latency budget discussion).

## Pinned dependencies

Intentional version pins — do not bump without running the benchmark
harness against the regression fixture set:

| Package | Pinned | Reason |
|---|---|---|
| `torch` | `2.3.*` | Known-good on both arm64 and amd64 wheels |
| `transformers` | `<4.45` | Required by `surya-ocr==0.5.*` — new transformers breaks `SuryaDecoderConfig.pad_token_id` |
| `surya-ocr` | `0.5.*` | Last known-good before the 0.6 API restructure |
| `pix2tex` | `>=0.1.4` | LatexOCR entry point |
| `grpcio` | `>=1.60` | Python 3.11 support |
