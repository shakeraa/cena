# OCR Sidecar Runbook (RDY-OCR-SIDECAR-CONTAINER)

This runbook covers day-2 operations for the Cena OCR sidecar that hosts
Surya (Layer 1 layout + Hebrew OCR) and pix2tex (Layer 2b math OCR) behind
the gRPC contract at `docker/ocr-sidecar/app/ocr.proto`.

## Deploy

```
# Image is built via .github/workflows/ocr-sidecar.yml on main.
# Apply manifests:
kubectl apply -k k8s/ocr-sidecar
```

Two replicas by default, HPA scales to 10 on CPU > 70%. First pod takes
~2-5 minutes to download model weights to the `hf-cache` emptyDir.

## Health

- `healthcheck.py` opens a gRPC channel to localhost:50051 and calls the
  health service. Startup probe has a 7.5-minute grace window; thereafter
  liveness checks every 30s.
- From the .NET side, observability metrics (`ocr.cascade.fallbacks_fired`
  tagged `fallback=gemini_vision` or `mathpix`) going up means the cascade
  is falling back because the sidecar is slow or down.

## Common failures

### Sidecar refuses connections on boot
Most common cause: HF model download blocked by network policy. Check
egress NetworkPolicy permits 443/TCP to the HF CDN. Log line to look for
in the sidecar:

```
HTTPSConnectionPool(host='huggingface.co', port=443): Max retries exceeded
```

### Surya 0.5 decoder config error
If you see `SuryaDecoderConfig.pad_token_id` in logs, the pinned
`transformers<4.45` was not applied. Rebuild with the committed
`requirements.txt` untouched.

### OOMKilled
Default limit is 6Gi. Large bagrut PDFs with many figures can push into
that. First mitigation: scale horizontally (HPA handles it). If a single
request OOMs, bump `resources.limits.memory` to 8Gi and update the
sidecar image tag to track the capacity change.

## Observability

- Prometheus alerts in `config/prometheus/ocr-alerts.yml` include
  `OcrFallbackOverReliance` which fires when the sidecar underperforms.
- Per-layer latency histograms in OcrMetrics (layer tags `layer_1_layout`
  and `layer_2b_math`) are the first thing to look at when the sidecar
  misbehaves.

## Rollback

```
kubectl rollout undo deployment/cena-ocr-sidecar -n cena-platform
```

A Pod Disruption Budget (`pdb.yaml`) keeps minAvailable=1 throughout.
