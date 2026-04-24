"""K8s readiness/liveness probe.

Calls the sidecar's own Health RPC over localhost. Exits 0 if SERVING or
DEGRADED, non-zero otherwise so the pod is marked not-ready until models
are loaded.
"""
from __future__ import annotations

import os
import sys

import grpc
from grpc_health.v1 import health_pb2, health_pb2_grpc


def main() -> int:
    port = int(os.environ.get("OCR_SIDECAR_PORT", "50051"))
    channel = grpc.insecure_channel(f"localhost:{port}")
    stub = health_pb2_grpc.HealthStub(channel)
    try:
        resp = stub.Check(
            health_pb2.HealthCheckRequest(service=""),
            timeout=5.0,
        )
    except grpc.RpcError as e:
        print(f"[healthcheck] RPC failed: {e.code().name}", file=sys.stderr)
        return 1

    if resp.status == health_pb2.HealthCheckResponse.SERVING:
        return 0
    print(f"[healthcheck] status={resp.status}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
