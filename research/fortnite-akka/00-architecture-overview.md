# Fortnite Backend Architecture Overview

> Research date: 2026-03-27
> Relevance: Fortnite ran Akka on JVM; Cena runs Proto.Actor on .NET 9. Same actor model lineage (Roger Alsing created both Akka.NET and Proto.Actor).

---

## Technology Stack

| Layer              | Fortnite                          | Cena                                |
|--------------------|-----------------------------------|-------------------------------------|
| **Actor Framework** | Akka (JVM, Scala/Java)           | Proto.Actor (.NET 9, C#)           |
| **Primary DB**     | MongoDB (9 shards)                | PostgreSQL + Marten (event-sourced)|
| **Caching**        | Memcached                         | Redis                               |
| **Messaging**      | XMPP (custom, 101-node mesh)     | NATS JetStream                      |
| **Container Orch** | Kubernetes (EKS) + Nomad          | TBD                                 |
| **Cloud**          | AWS (12 DCs, 24 AZs)             | TBD                                 |
| **Analytics**      | Kinesis (5K shards) + EMR Hadoop  | TBD                                 |
| **Languages**      | Java, Scala, Go, C++              | C#, TypeScript (polyglot planned)  |

## Overall Design

Fortnite uses a **control plane / data plane** separation:

- **Control Plane** = "MCP" (Matchmaking Control Plane, named after Tron). Handles player profiles, statistics, inventory, matchmaking, configuration. Java/Scala/Akka on Kubernetes.
- **Data Plane** = Unreal Engine dedicated servers on EC2. 16 game servers per c4.8xlarge instance.
- **Analytics Plane** = Kinesis + Spark + DynamoDB (real-time) + EMR Hadoop (batch). 125M events/minute.

### Cena Parallel

- **Control Plane** = Actor system (StudentActor, LearningSessionActor, etc.) managing learner state, session orchestration, pedagogy decisions
- **Data Plane** = LLM calls (Kimi/Sonnet/Opus) -- where the actual learning interaction happens
- **Analytics Plane** = Not yet built; Fortnite's Kinesis pipeline shows this becomes critical at scale

## Scale Numbers (Peak)

| Metric                     | Fortnite                         |
|----------------------------|----------------------------------|
| Registered players         | 350M+                            |
| Peak concurrent users      | 3.4M (February 2018)            |
| MCP throughput             | 124K req/sec                     |
| DB reads                   | 318K reads/sec                   |
| DB writes                  | 132K writes/sec                  |
| Auth throughput            | 100K req/sec                     |
| XMPP connections           | 3M+ persistent                   |
| XMPP messages              | ~600K msg/sec                    |
| Analytics events           | 125M events/min                  |

## Backend Service Inventory (16+ services)

1. **Account Service** -- auth, authorization, account management
2. **MCP Service** -- profiles, inventory, loadouts, matchmaking (the core)
3. **Friends Service** -- social graph
4. **Party Service** -- multiplayer party management
5. **Persona Service** -- display names, identity resolution
6. **XMPP Service** -- presence, messaging, party chat
7. **Fulfillment Service** -- purchases, item distribution
8. **Stats Proxy Service** -- player statistics
9. **Lightswitch Service** -- feature flags, service availability
10. **FN-Content API** -- cosmetics, game content
11. **FN-Discovery Service** -- content recommendations
12. **FN-Hotconfig** -- dynamic configuration, feature toggles
13. **Nelly Service** -- messaging/communication
14. **Tag Management** -- content categorization
15. **User Search** -- player discovery
16. **EGS Platform** -- Epic Games Store integration

### Cena Service Mapping

| Fortnite Service     | Cena Equivalent                    | Status    |
|---------------------|------------------------------------|-----------|
| MCP (profiles)      | StudentActor (event-sourced)       | Built     |
| Stats Proxy         | BKT/HLR services                   | Built     |
| Lightswitch         | Feature flags / methodology switch | Built     |
| XMPP (presence)     | NATS + OutreachScheduler           | Built     |
| Fulfillment         | Curriculum content delivery        | Planned   |
| Discovery           | Adaptive item selection            | Partial   |
| Hotconfig           | Dynamic config per student         | Planned   |

## Sources

- [Epic Games KubeCon 2018 - Paul Sharpe](https://www.serverwatch.com/server-news/how-epic-games-uses-kubernetes-to-power-fortnite-application-servers/)
- [Akka 200K Developers Press Release (confirms Fortnite)](https://www.globenewswire.com/news-release/2019/04/23/1807741/0/en/Akka-Community-Reaches-200-Thousand-Developers-As-Cloud-Native-s-Most-Powerful-Platform-for-Concurrency.html)
- [Fortnite Postmortem - 3.4M CCU](https://www.fortnite.com/news/postmortem-of-service-outage-at-3-4m-ccu?lang=en-US)
- [Fortnite Data Analytics Pipeline (Datanami)](https://www.datanami.com/2018/07/31/inside-fortnites-massive-data-analytics-pipeline/)
- [Fortnite API Documentation (Community)](https://github.com/LeleDerGrasshalmi/FortniteEndpointsDocumentation)
