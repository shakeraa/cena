# Observability Topology Diagram

The full editable diagram is at [observability-topology.drawio](observability-topology.drawio) —
open in [draw.io](https://app.diagrams.net) (free, browser-based) or in
**Visio** (File → Open → select the `.drawio` file; Visio 2019+ imports
draw.io's mxGraph format directly), or use the VS Code "Draw.io Integration"
extension to view inline.

The Mermaid version below renders directly in GitHub / GitLab / VS Code
preview and is the canonical "read in PR" view:

```mermaid
flowchart LR
    subgraph App["Application tier (cena_default network)"]
        Emu[cena-emulator<br/>student simulator]
        NATS[cena-nats<br/>:4222 / :8222]
        Actor[cena-actor-host<br/>Proto.Cluster + Marten<br/>:5050 /metrics<br/>Jaeger: cena-learner-service]
        SAPI[cena-student-api<br/>:5050 /metrics]
        AAPI[cena-admin-api<br/>:5052 /metrics]
        SymPy[cena-sympy-sidecar<br/>NATS subscriber<br/>no HTTP]
        PG[(cena-postgres<br/>Marten event store)]
        Redis[(cena-redis)]
        ASPA[cena-admin-spa :5174]
        SSPA[cena-student-spa :5175]
    end

    subgraph Telemetry["Telemetry tier"]
        OTel[otel-collector<br/>OTLP :4317 / :4318<br/>Prom expose :8889<br/>PII-redaction]
        Prom[cena-prometheus :9090<br/>15s scrape, 5 jobs<br/>PRR-434 alert rules]
        AM[cena-alertmanager :9093]
        Jaeger[cena-jaeger :16686]
        Loki[cena-loki :3100<br/>⚠️ no .NET log pipeline]
        Graf[cena-grafana :3000<br/>6 dashboards<br/>DS: Prom, Loki, Jaeger]
    end

    subgraph Observer["Operator"]
        Card[Admin SPA<br/>Observability card]
        User((Browser))
    end

    Emu -->|publish learner events| NATS
    NATS --> Actor
    Actor -->|Marten append| PG
    Actor --> NATS
    NATS -->|cena.cas.verify.sympy| SymPy
    SAPI --> Actor
    AAPI --> Actor
    Actor --> Redis
    ASPA --> AAPI
    SSPA --> SAPI

    Actor -. OTLP traces+metrics .-> OTel
    SAPI -. OTLP .-> OTel
    AAPI -. OTLP .-> OTel
    OTel -. OTLP traces .-> Jaeger
    OTel -. logs (gap) .-> Loki

    Prom -->|scrape /metrics| Actor
    Prom -->|scrape /metrics| SAPI
    Prom -->|scrape /metrics| AAPI
    Prom -->|scrape :8889| OTel
    Prom -. alerts .-> AM

    Graf --> Prom
    Graf --> Loki
    Graf --> Jaeger

    User --> Card
    Card --> Graf
    Card --> Prom
    Card --> Jaeger

    classDef app fill:#d5e8d4,stroke:#82b366
    classDef otel fill:#ffe6cc,stroke:#d79b00
    classDef prom fill:#f8cecc,stroke:#b85450
    classDef trace fill:#e1d5e7,stroke:#9673a6
    classDef graf fill:#f5f5f5,stroke:#666666
    classDef gap fill:#e1d5e7,stroke:#9673a6,stroke-dasharray:5 5

    class Emu,NATS,Actor,SAPI,AAPI,SymPy,PG,Redis,ASPA,SSPA app
    class OTel otel
    class Prom,AM prom
    class Jaeger trace
    class Loki gap
    class Graf graf
```

## Reading the diagram

- **Solid arrows**: pull / scrape (Prometheus → service) and synchronous calls.
- **Dotted arrows**: push / export (services → OTel collector → Jaeger/Loki).
- **Two tiers**: the application tier (left/blue) carries real student traffic;
  the telemetry tier (right/yellow) is the read-only observability stack.
- **Two service-name namespaces**: actor-host registers itself as
  `cena-learner-service` in OpenTelemetry resource attributes (per
  `Program.cs` `.AddService(serviceName: ...)`) but the Prometheus job is
  named `cena-actor-host`. This is intentional — Jaeger groups by the OTel
  service name, Prometheus groups by scrape job.

## What flows where

| Signal type | Producer            | Transport                    | Sink         | UI                    |
|-------------|---------------------|------------------------------|--------------|-----------------------|
| Metrics     | ASP.NET services    | `/metrics` Prometheus pull   | Prometheus   | Grafana, :9090        |
| Metrics     | OTel SDK in .NET    | OTLP gRPC :4317              | OTel → Prom  | Grafana, :9090        |
| Traces      | OTel SDK in .NET    | OTLP gRPC :4317              | OTel → Jaeger| Jaeger UI :16686      |
| Logs        | (none — see gap)    | OTLP gRPC :4317              | OTel → Loki  | Grafana Explore       |
| Alerts      | Prometheus rules    | Alertmanager push            | Alertmanager | :9093                 |
