# syntax=docker/dockerfile:1.6
# =============================================================================
# Cena OCR Sidecar
#
# Multi-arch Python gRPC service wrapping Surya (layout + Hebrew OCR) and
# pix2tex (math OCR). Models are NOT baked into the image — they download
# into /hf-cache (mount a volume) on first run via prewarm.py.
#
# Per ADR-0033: pinned transformers<4.45 + surya-ocr==0.5.* to avoid the
# SuryaDecoderConfig.pad_token_id breakage in newer releases.
# =============================================================================

ARG PYTHON_VERSION=3.11

# ── Builder stage ────────────────────────────────────────────────────────────
FROM python:${PYTHON_VERSION}-slim AS builder

ENV PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PYTHONDONTWRITEBYTECODE=1

# System deps needed for building Pillow, opencv-python, torch extras
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        git \
        libgl1 \
        libglib2.0-0 \
        libsm6 \
        libxext6 \
        libxrender1 \
        poppler-utils \
        tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /build

COPY requirements.txt .

# Install into an isolated venv so the runtime stage copies a clean tree
RUN python -m venv /opt/venv && \
    /opt/venv/bin/pip install --upgrade pip wheel setuptools && \
    /opt/venv/bin/pip install -r requirements.txt

# ── Runtime stage ────────────────────────────────────────────────────────────
FROM python:${PYTHON_VERSION}-slim AS runtime

ENV PATH="/opt/venv/bin:${PATH}" \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    HF_HOME=/hf-cache \
    TRANSFORMERS_CACHE=/hf-cache \
    TOKENIZERS_PARALLELISM=false

# Minimal runtime system deps (tesseract + poppler for pdf2image)
RUN apt-get update && apt-get install -y --no-install-recommends \
        libgl1 \
        libglib2.0-0 \
        poppler-utils \
        tesseract-ocr \
        tesseract-ocr-heb \
        tesseract-ocr-eng \
        curl \
    && rm -rf /var/lib/apt/lists/*

# Copy the venv from the builder
COPY --from=builder /opt/venv /opt/venv

WORKDIR /app
COPY app/ /app/

# Generate gRPC stubs from ocr.proto. Without this, server.py imports
# `ocr_pb2_grpc` fails at startup and the OcrSidecar servicer never
# registers, so /healthcheck stays NOT_SERVING regardless of model state.
# grpcio-tools is already pinned in requirements.txt and lives in
# /opt/venv copied from the builder stage.
RUN python -m grpc_tools.protoc \
        -I/app \
        --python_out=/app \
        --grpc_python_out=/app \
        /app/ocr.proto

# Non-root user
RUN useradd --create-home --shell /bin/bash --uid 10001 ocr && \
    mkdir -p /hf-cache && chown -R ocr:ocr /app /hf-cache
USER ocr

EXPOSE 50051

HEALTHCHECK --interval=30s --timeout=8s --start-period=120s --retries=3 \
    CMD python /app/healthcheck.py || exit 1

ENTRYPOINT ["python", "/app/server.py"]
