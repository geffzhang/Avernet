# .NET 10 + Orleans Python Services Migration Design

## Status

Approved on 2026-08-08.

## Summary

Replace all Python services in OCB with implementations built on .NET 10,
ASP.NET Core, and Orleans while preserving the frontend and the Rust BCS.
The replacement is a clean implementation inside Avernet: the sibling
`openclaw.net/src` codebase is a design and implementation reference, not a
runtime, source, project, or NuGet dependency.

The migration is an overall replacement program rather than a long-term
strangler deployment. Engineering work remains staged by module so each
boundary can be tested before the final system-wide cutover.

## Goals

- Replace Python in `backend`, `engine`, `baas`, `gateway`, and `bcsfuse`.
- Replace FastAPI and Uvicorn with ASP.NET Core and Kestrel.
- Use Orleans for stateful identity, lifecycle, and concurrency coordination.
- Preserve existing public HTTP, WebSocket, SSE, Service API, and Plugin API
  contracts unless a separately reviewed contract change is approved.
- Preserve the repository microkernel architecture and its CI enforcement.
- Start production as a new system without importing existing user or runtime
  records.
- Use PostgreSQL for .NET business data and Orleans infrastructure.
- Use MinIO for files and large objects.
- Support SonnetDB for single-node vector search and Qdrant for clustered
  deployments, selected at startup.
- Keep Rust BCS on SQLite as a transitional compatibility service that can be
  replaced by Orleans in a later project.

## Non-Goals

- Rewriting the Rust BCS in this project.
- Migrating historical MySQL, SQLite, FAISS, or Qdrant data.
- Rewriting the TypeScript frontend.
- Rewriting OpenClaw, its BCN plugin, or the Node-based Claude Code gateway.
- Depending on `OpenClaw.*` assemblies or copying unrelated openclaw.net
  features such as Canvas, Dashboard, or Payments.
- Preserving MySQL as part of the target platform.
- Making BCS horizontally scalable during its transitional SQLite phase.

## Constraints and Sources of Truth

- `docs/arch/arch.rules.md` remains binding.
- `docs/arch/ci.enforce.md` remains binding.
- `docs/arch/context-boundary-format.md` remains the module-boundary model.
- `docs/arch/protocol-contract-tests.md` remains the Plugin API conformance
  model.
- `docs/arch/service-skills-layout-wire-contract.md` remains the Skills layout
  wire contract.
- Existing OpenAPI, JSON Schema, WebSocket frame, SSE, and BCS protocol
  definitions govern compatibility.
- `openclaw.net/src` provides implementation references for .NET patterns,
  especially channels, scheduling, composition, plugins, Skills, and tests.

## Feasibility Decision

The migration is technically feasible and carries high engineering risk. The
main risk is behavioral parity across roughly 2,200 Python files and the
existing test suite, not availability of .NET libraries.

A reasonable delivery estimate is:

- 8-12 experienced engineers: 18-24 months.
- 4-6 experienced engineers: 24-36 months.

The project should not commit to a six-to-twelve-month full replacement.

## Target Architecture

### Runtime topology

The target separates delivery, coordination, process execution, and durable
infrastructure:

```text
Frontend / external clients
          |
          | HTTP / WebSocket / SSE
          v
Ocb.Gateway (ASP.NET Core, Orleans client)
          |
          v
Ocb.Silo (Orleans grains and application coordination)
          |
          +--> Ocb.Runtime.Worker --> OpenClaw / Claude Code processes
          +--> Rust BCS over versioned HTTP/WebSocket contracts
          +--> PostgreSQL
          +--> MinIO
          +--> SonnetDB or Qdrant
          +--> Redis when explicitly configured

Ocb.Scheduler (TickerQ)
          |
          +--> PostgreSQL lease/outbox --> Orleans/application pipeline
```

Singlebox may place these hosts in one container, but the ownership boundaries
remain separate. Cluster deployment uses independently scalable Gateway, Silo,
Runtime Worker, and Scheduler processes.

### Solution layout

```text
src/dotnet/
  Ocb.slnx
  Directory.Build.props
  Directory.Packages.props
  src/
    Ocb.Contracts/
    Ocb.Core/
    Ocb.PluginApi/
    Ocb.Channels/
    Ocb.Scheduling/
    Ocb.GrainContracts/
    Ocb.Grains/
    Ocb.Gateway/
    Ocb.Runtime.Worker/
    Ocb.Backend/
    Ocb.Baas/
    Ocb.Fusion/
    Ocb.Infrastructure.PostgreSql/
    Ocb.Infrastructure.Minio/
    Ocb.Infrastructure.Vector/
    Ocb.Bcs.Client/
    Ocb.Plugins.*/
    Ocb.Silo.Host/
    Ocb.Scheduler.Host/
  tests/
    Ocb.Architecture.Tests/
    Ocb.Contracts.Tests/
    Ocb.Core.Tests/
    Ocb.Grains.Tests/
    Ocb.Infrastructure.Tests/
    Ocb.EndToEnd.Tests/
```

All projects target `net10.0`, enable nullable reference types, use implicit
usings, and treat warnings as errors. Package versions are pinned centrally.
Only stable package releases verified with .NET 10 are permitted.

NativeAOT is not an initial acceptance requirement. The openclaw.net Gateway
uses NativeAOT, but Orleans and new infrastructure providers must not inherit
`PublishAot=true` without an explicit compatibility test.

## Architectural Boundaries

### Service APIs and Plugin APIs

- Service APIs define what consumers call in the OCB core.
- Plugin APIs define capabilities the core calls on infrastructure providers.
- The two contract categories remain separate projects, documentation, and
  conformance suites.
- Core and contracts do not reference ASP.NET Core, EF Core, Orleans
  implementations, MinIO, SonnetDB, Qdrant, or concrete plugins.
- Concrete implementations are selected only in composition roots.

### Orleans boundary

Orleans is an application coordination mechanism, not the domain or plugin
architecture itself.

Use a Grain only when an object has a stable identity and at least one of:

- mutable durable state;
- serial concurrency requirements;
- lifecycle activation/deactivation behavior;
- distributed coordination behavior.

Examples include `BotGrain`, `SessionGrain`, `DeviceGrain`,
`FusionJobGrain`, and `ConnectionDirectoryGrain`.

Do not make HTTP forwarding, cryptography, vector queries, database access,
file transfer, or process handles into Grains. Grain implementations call
Service APIs and Plugin APIs and remain thin coordinators.

### Context boundaries

Every boundary-significant C# module has machine-readable metadata equivalent
to the existing Context Boundary format: purpose, provided contracts, consumed
contracts, allowed internal dependencies, and change impact. Architecture tests
enforce the dependency graph.

## Python Service Mapping

| Existing service | Target ownership |
| --- | --- |
| `gateway` | `Ocb.Gateway`, ASP.NET Core endpoints, auth, routing, rate limits, schema catalog, telemetry |
| `engine` | `Ocb.Runtime.Worker`, runtime API, OpenClaw/Claude Code anti-corruption layer, process and workspace lifecycle |
| `backend` | `Ocb.Backend` services plus Bot, Session, Skill activation, and asset Grains |
| `baas` | `Ocb.Baas` services plus Device and Template Grains and sandbox/provider plugins |
| `bcsfuse` | `Ocb.Fusion` services plus Worker, Fusion Job, and Group Profile Grains |

The BaaS migration includes sandbox provisioning, Docker/Kubernetes providers,
transparent `invoke-http`, API gateway behavior, QPM controls, SSE, publication,
runtime queues, device TTL, billing controls, and third-party integration
boundaries. These capabilities must not be reduced to only Device and Template
CRUD.

## ASP.NET Core Replacement

| Python capability | Target implementation |
| --- | --- |
| FastAPI routers | ASP.NET Core endpoint groups or controllers selected consistently per module |
| Uvicorn | Kestrel |
| Pydantic request/response models | C# records and source-generated `System.Text.Json` contexts |
| Pydantic validation | endpoint filters plus explicit validators |
| Injector/dependency-injector | `Microsoft.Extensions.DependencyInjection` |
| FastAPI middleware | ASP.NET Core middleware |
| `HTTPException` | domain errors mapped by `IExceptionHandler` |
| FastAPI lifespan | Generic Host lifecycle and `IHostedService` |
| `httpx`/`aiohttp` | typed clients from `IHttpClientFactory` |
| Python async queues | `System.Threading.Channels` |
| SQLAlchemy | EF Core with Npgsql |

Transport adapters parse authentication and protocol details, call a Service
API, and map domain outcomes back to the existing wire contract. They do not
own business policy.

## openclaw.net Reference Policy

The migration may study and adapt patterns from `openclaw.net/src`, but Avernet
owns independent implementations and namespaces.

Reference areas include:

- `OpenClaw.Core`: abstractions, models, security, and pipeline patterns;
- `OpenClaw.Channels`: `IChannelAdapter`, `WebSocketChannel`, and
  `CronChannel` patterns;
- `OpenClaw.Gateway`: endpoint validation, composition, and inbound workers;
- `OpenClaw.PluginKit`: plugin discovery and lifecycle patterns;
- `OpenClaw.SkillKit`: Skill models and loading patterns;
- `OpenClaw.Testing`: test infrastructure patterns.

No `OpenClaw.*` ProjectReference or NuGet PackageReference is allowed in the
target solution. Adapted code must use OCB contracts, naming, configuration,
error behavior, tenancy, and tests. Third-party dependencies used by both
codebases remain normal NuGet dependencies.

## WebSocket and SSE Design

### WebSocket channel

`Ocb.Channels.WebSocketChannel` follows the openclaw.net raw ASP.NET Core
WebSocket channel model rather than SignalR. It supports:

- raw text and JSON envelope modes;
- complete message assembly across receive fragments;
- total and per-IP connection limits;
- per-connection message rate limits;
- serialized sends per connection;
- streaming response envelopes;
- authentication identity association;
- Origin validation;
- clean handling of disconnects and concurrent sends.

External wire formats remain the existing OCB formats. openclaw.net-specific
Canvas envelopes are not added unless a separate contract change requires
them.

### Cluster routing

The WebSocket object stays in the Gateway process that accepted it. It is never
stored in Grain state.

`ConnectionDirectoryGrain` records the owning Gateway instance, connection
identifier, tenant, user, session, and lease expiry. Orleans Streams route
outbound notifications to the owning Gateway, which sends through its local
`WebSocketChannel` connection table. Disconnect and lease expiry remove stale
routes.

SSE uses ASP.NET Core streaming responses and follows the same authentication,
backpressure, cancellation, and correlation rules.

## Scheduling and Reliable Work

TickerQ replaces APScheduler and the earlier Quartz.NET proposal. The initial
version aligns with openclaw.net's split:

- TickerQ triggers a periodic scheduling function;
- `CronScheduler` evaluates configured jobs, time zones, and overlap rules;
- due work is written to the application message pipeline;
- application workers or Grains perform the actual work.

Cluster deployments must not execute the same scan independently on every
host. `Ocb.Scheduler.Host` acquires a PostgreSQL lease before each scheduling
cycle. Every emitted job carries an idempotency key based on schedule identity
and occurrence time.

Use the following mechanisms for distinct semantics:

- TickerQ: user and system Cron schedules;
- Orleans Reminders: durable Grain lifecycle reminders;
- Orleans Timers: short-lived activation-local timing;
- PostgreSQL outbox and worker: reliable asynchronous commands, retries, and
  dead-letter state.

## Runtime Worker and OpenClaw Integration

`Ocb.Runtime.Worker` owns process and filesystem resources that cannot live in
Grain state:

- OpenClaw and Claude Code gateway processes;
- workspace creation and cleanup;
- port allocation;
- process health, restart, cancellation, and shutdown;
- stdout/stderr log capture;
- engine WebSocket and HTTP anti-corruption adapters;
- materialization of activated Skills.

`BotRuntimeGrain` coordinates desired state and assigns work to a Runtime
Worker. Runtime Workers report observed state and use leases so another worker
can recover an abandoned assignment. Grain state contains identifiers and
desired/observed status, never process handles or local paths as authoritative
business state.

## Tenancy, Identity, and Security

- Tenant-scoped Grain keys include `(tenant_id, entity_id)`.
- Tenant identity is passed explicitly in a serializable `CallerContext`; it is
  not inferred from ambient process state.
- Gateway authentication validates existing JWT and signed principal semantics,
  including `X-Avernet-Principal` where required by the current contract.
- Orleans call filters enforce caller and tenant consistency at the Grain
  boundary.
- PostgreSQL queries include explicit tenant predicates and database-level
  constraints where practical.
- API keys, model credentials, MinIO credentials, BCS secrets, and SM4 keys are
  resolved through a Secret Plugin and never stored in Grain state.
- Logs, traces, metrics, exceptions, and health endpoints do not reveal secrets
  or sensitive message contents.
- Dependency, container, and secret scanning are required CI gates.

## Data and Persistence

### PostgreSQL

PostgreSQL replaces MySQL for all migrated Python services. The new system does
not preserve the previous MySQL schema.

Separate schemas and roles provide ownership boundaries:

- `ocb_business`: EF Core business entities;
- `ocb_orleans`: membership, reminders, and Grain persistence;
- `ocb_jobs`: outbox, job leases, retries, and dead letters.

EF Core migrations and Orleans infrastructure migrations are separate release
steps. Application startup validates schema compatibility but does not
silently rewrite production schemas.

### Rust BCS SQLite

Rust BCS remains a single-instance stateful service with a private SQLite file,
WAL, busy timeout, persistent volume, and periodic backup. No .NET component
accesses its database directly.

All coordination access uses `Ocb.Bcs.Client` and the existing versioned
HTTP/WebSocket contracts. The client implements `IBcsClient`, allowing a later
Orleans implementation to replace Rust BCS without changing consumers. BCS
SQLite data will not be migrated in that later project; the future replacement
also starts fresh unless separately specified.

### MinIO

MinIO stores files, Skill packages, and large objects. PostgreSQL stores object
metadata and ownership.

The object workflow supports multipart upload, checksum validation, size
limits, tenant-prefixed keys, temporary object publication, signed URL expiry,
range reads, retention, deletion compensation, and an optional malware
scanning Plugin API. A failed metadata transaction leaves only a temporary
object eligible for background cleanup.

### Redis

Redis is optional and may provide cache, distributed rate counters, or other
explicitly non-authoritative acceleration. Correctness and durable state do not
depend on Redis.

## Vector Storage

`Ocb.Fusion` depends on contracts rather than a concrete vector engine:

- `IVectorStore`: collection-independent upsert, delete, get, Top-K search,
  distance, and metadata filtering;
- `IHybridSearchStore`: hybrid text/vector capabilities;
- `IVectorStoreAdministration`: collection/index creation and health.

Implementations:

- `SonnetDbVectorStore`: singlebox and edge deployments;
- `QdrantVectorStore`: clustered deployments.

Deployment selects exactly one provider at startup. Cluster profile rejects
SonnetDB configuration. There is no permanent dual write. Both providers run
the same conformance suite covering dimensions, distance behavior, payload
types, filters, deterministic tie handling, Top-K limits, hybrid capability
declaration, and error semantics.

Embedding and reranker providers remain separate Plugin APIs. Vector records
carry model and dimension metadata so incompatible embedding changes fail
closed rather than corrupting an index.

## SM4 and Cryptography

The target Crypto Plugin supports the BaaS SM4 use cases through
BugFree.Security where its behavior matches the contract, with
BouncyCastle.Cryptography available as the lower-level implementation and
compatibility fallback. The dependency version aligns with the verified
openclaw.net baseline when possible.

Because production starts without historical ciphertext, the target format
does not need to decrypt previous gmssl records. New known-answer, round-trip,
invalid-key, padding, and tamper tests define the target contract. SM2 is not a
current requirement.

## Skills Management and Delivery

The existing three source categories remain distinct:

- `git://`: repository-managed public content;
- `local://`: uploaded user content;
- `center://`: governed Skill Center content.

PostgreSQL stores metadata, publication, activation, and immutable manifest
records. MinIO stores versioned Skill content and packages. The Runtime Worker
materializes only the Skills activated for a Bot into its workspace.

The service Skills manifest remains engine-agnostic and preserves
`skills-pool-p3-v1`. Backend emits the existing layout variables; runtime image
logic maps the manifest to physical paths. A Bot never receives a bridge or
mount to an entire Skills content store.

Runtime activation and recovery are explicit workflows:

1. resolve immutable Skill versions;
2. download and verify content;
3. materialize into a temporary workspace;
4. atomically publish the active view;
5. report observed activation state;
6. reconcile again after Runtime Worker restart or reassignment.

Failures never replace a previously active workspace with a partial view. A
transient download failure is retried three times with exponential backoff and
jitter; an integrity failure is not retried. Resolution, verification, or
materialization failure removes temporary content, preserves the previous
active view, and records desired-versus-observed failure state. An atomic
publish failure rolls back the active pointer to the previous view. If there
is no previous view, Bot activation fails closed with an actionable error.
Runtime Worker restart or reassignment runs the complete reconciliation again
from authoritative desired state.

The openclaw.net SkillKit is a design reference for models and loading, but OCB
publication, source governance, and layout contracts remain authoritative.

## Configuration and Profiles

Configuration uses strongly typed options with startup validation. Unknown
keys, missing required values, and invalid provider/profile combinations fail
startup. Raw environment reads are allowed only in configuration loading,
composition roots, and tests.

Required profiles:

| Profile | Topology |
| --- | --- |
| `singlebox` | Single Gateway/Silo/Worker, one Scheduler Host with TickerQ, PostgreSQL, MinIO, SonnetDB, Rust BCS with SQLite |
| `cluster` | Multiple Gateways/Silos/Workers, one active Scheduler Host with TickerQ and PostgreSQL lease, PostgreSQL, MinIO, Qdrant, Rust BCS with SQLite |
| `test` | Orleans TestCluster plus in-memory plugins or Testcontainers |

Provider capability validation occurs before serving traffic. Production URLs,
tokens, and private endpoints are never hardcoded.

## Deployment and Packaging

Singlebox provides one user-facing container workflow while retaining process
boundaries internally. The image includes the .NET runtime, Rust BCS binary,
Node/OpenClaw runtime, BCN plugin, and built frontend. PostgreSQL, MinIO, and
SonnetDB run as declared local dependencies rather than hidden embedded state.

Cluster deployment uses separate images for Gateway, Silo, Runtime Worker,
Scheduler, and BCS. Qdrant, PostgreSQL, MinIO, and optional Redis are external
services. BCS remains one replica while it owns SQLite.

Builds support x64 and arm64, official public package sources, and the existing
China mirror switch. Community artifacts contain no corporate package,
endpoint, credential, or source dependency.

## Observability

OpenTelemetry covers ASP.NET Core, outbound HTTP, Orleans calls, PostgreSQL,
Runtime Worker operations, MinIO, and vector clients. Correlation fields
include tenant, Bot, session, request, task, and connection identifiers where
policy permits.

Required operational metrics include:

- request and WebSocket latency/error rates;
- live and rejected WebSocket connections;
- Orleans activation, call, and reminder health;
- scheduler lag, duplicate suppression, queue depth, and dead letters;
- Runtime Worker assignments, process restarts, and orphan recovery;
- PostgreSQL pool and migration status;
- MinIO transfer and cleanup failures;
- vector indexing/query latency and provider health.

## Testing and CI

### Test layers

- xUnit domain tests without ASP.NET Core or Orleans hosts;
- Orleans TestCluster tests for activation, concurrency, persistence, Streams,
  Reminders, and call filters;
- `WebApplicationFactory` tests for HTTP, WebSocket, SSE, auth, errors, and JSON;
- Testcontainers integration tests for PostgreSQL, MinIO, SonnetDB, Qdrant,
  Redis, and BCS where applicable;
- Service API and Plugin API conformance suites;
- vector provider conformance suites;
- Docker Compose end-to-end user stories;
- NBomber performance and long-connection tests;
- fault tests for process loss, Silo loss, network timeout, duplicate delivery,
  and storage recovery.

### Required gates

- formatting, compilation, nullable analysis, and analyzers;
- architecture dependency tests;
- forbidden framework and environment access checks;
- strict configuration-schema tests;
- protocol and provider conformance tests;
- public endpoint coverage;
- source-generated serialization compatibility tests for contracts and Grain
  state;
- changed-line coverage at least as strict as the replaced module gate;
- existing Rust BCS and frontend CI unchanged;
- security and license scanning.

Contract parity tests exercise the Python baseline and .NET implementation
against the same request/frame corpus before cutover. They compare status,
headers where contractual, JSON shape and values, message sequencing, and
failure behavior.

## Delivery Plan

The overall replacement is implemented in dependency order:

1. .NET solution, contracts, architecture tests, and infrastructure profiles.
2. Protocol corpus and Python/.NET parity harness.
3. Gateway and channel delivery surfaces.
4. Runtime Worker and Engine anti-corruption layer.
5. BaaS capabilities and providers.
6. Fusion services with SonnetDB and Qdrant conformance.
7. Backend domains, tenancy, assets, and Skills.
8. Full integration, performance, recovery, and security validation.
9. Maintenance-window cutover and observation.
10. Python removal after acceptance.

Modules may be developed in parallel, but production does not enter a
long-lived mixed Python/.NET topology. Python remains the behavioral baseline
until the complete replacement passes acceptance.

## Cutover and Rollback

Production cutover starts a new system:

1. freeze writes and enter a maintenance window;
2. apply PostgreSQL business and Orleans migrations;
3. create MinIO buckets and lifecycle rules;
4. initialize the selected vector provider and indexes;
5. initialize fresh BCS SQLite state;
6. load only required system seed/configuration data;
7. start Scheduler, Silo, Runtime Worker, Gateway, BCS, and frontend;
8. run readiness and critical user-story checks;
9. open traffic.

There is no historical data import or reverse synchronization. Before traffic
opens, rollback restores the Python deployment. After the new system accepts
durable writes, rollback to Python is data-lossy and is not an automatic
option. The formal go-live decision therefore marks the cutover as
irreversible; subsequent recovery uses the new platform's backups and fixes.

## Acceptance Criteria

- No target service imports or requires the migrated Python packages.
- All existing contractual HTTP, WebSocket, and SSE behaviors pass parity
  tests or have approved versioned changes.
- All Service API and Plugin API implementations pass conformance tests.
- Singlebox passes with SonnetDB; cluster passes with Qdrant.
- PostgreSQL, MinIO, vector provider, and BCS SQLite backup/restore exercises
  pass.
- Multi-Silo restart, Grain reactivation, Streams, Reminders, scheduler lease,
  and Runtime Worker reassignment tests pass.
- Critical frontend-to-backend and .NET-to-Rust-BCS user stories pass.
- Peak-load P95 and P99 latency and throughput meet the approved Python
  baseline; resource regressions have documented acceptance.
- Security review confirms tenant isolation, secret handling, authorization,
  upload controls, and dependency posture.
- Updated singlebox, Docker, deployment, API, and contributor documentation is
  complete.

## Principal Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Hidden Python behavior and test volume | Parity corpus, contract-first implementation, staged module verification |
| Orleans overuse | Thin Grain rule and architecture tests |
| Duplicate scheduling in cluster | Dedicated TickerQ host, PostgreSQL lease, occurrence idempotency |
| WebSocket ownership across replicas | Local socket ownership, Connection Directory Grain, Orleans Streams |
| Runtime process loss | Worker leases, observed state, reassignment and workspace reconciliation |
| Skills leakage between Bots | Immutable manifests, tenant keys, per-Bot materialization, no full-store mounts |
| Vector provider semantic drift | Shared conformance suite and capability validation |
| BCS SQLite availability | Single-instance transitional status, WAL, persistent volume, backup |
| Third-party library maturity | Plugin wrappers, pinned versions, contract tests, replaceable implementations |
| Irreversible cutover after writes | Maintenance window, explicit go-live gate, new-platform recovery plan |
| Long dual-maintenance period | Feature-freeze policy for migrated surfaces and tracked parity propagation |

## Future BCS Replacement

The future project replaces `IBcsClient`'s Rust implementation with Orleans
Grains for coordination, routing, groups, messages, and related state. This
design deliberately prevents consumers from depending on BCS SQLite, process
layout, or Rust implementation details, so the later replacement is a
composition-root and contract-conformance change rather than another broad
application rewrite.
