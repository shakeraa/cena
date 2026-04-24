FROM python:3.12-slim

WORKDIR /app

RUN pip install --no-cache-dir \
      "nats-py==2.9.0" \
      "sympy==1.13.3"

COPY sympy_worker.py /app/sympy_worker.py

RUN groupadd --system --gid 10001 sympy \
 && useradd  --system --uid 10001 --gid sympy --shell /usr/sbin/nologin sympy \
 && chown -R sympy:sympy /app
USER sympy:sympy

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD python -c "import socket; s=socket.socket(); s.settimeout(2); s.connect(('cena-nats', 4222)); s.close()" || exit 1

ENV PYTHONUNBUFFERED=1
CMD ["python", "-u", "sympy_worker.py"]
