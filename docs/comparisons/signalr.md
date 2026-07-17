# SignalR vs Fusion — comparison

Built against: main `453eb45` · signalr `8416524` · 2026-07-16
Framework delta: **91 files, +2300 / −2009 lines** (`git diff --stat main signalr`)

`main` is the reference **.NET + Fusion** implementation; `signalr` is the same app
on **.NET + SignalR streams, no Fusion** (only `ActualLab.Core` for `Moment`/hashing).
Both pass their suites against the same Postgres schema, and since this build the
two codebases are **name-aligned**: API method names, command records, member order,
and comments are identical wherever the framework doesn't force a difference, so the
diff below is a pure framework delta. Lenses: **Volume** and **Cleanliness**
re-measured this build; **Robustness** updated (test counts, file names);
**Performance** carried over from the 2026-07-15 measured run — the hot paths
(reads, writes, presence) haven't changed since, only names/comments/auth UX.

---

## 1. Volume (numerical)

**Verdict: signalr is +291 net lines — the app logic is a wash, and the whole gap
is the reactive infrastructure SignalR has to hand-write.** Fusion pushes ceremony
into *contracts*; SignalR pushes it into *client/server reactive glue it owns*.

Net line delta by area (`signalr − main`; negative = signalr is smaller):

| Area | +add | −del | net | verdict |
|------|-----:|-----:|----:|---------|
| Contracts (`TownHall.Api` + `TownHall.Backend`) | 112 | 330 | **−218** | signalr smaller |
| UI components (Pages/Shared/Layout) | 166 | 268 | **−102** | signalr smaller |
| Models (`TownHall.Abstractions`) | 53 | 20 | **+33** | signalr adds view-model records |
| UI infra (`UI/Services`, startup) | 479 | 45 | **+434** | Fusion smaller |
| Host wiring (`Program.cs`, hub, filters) | 261 | 144 | **+117** | Fusion smaller |
| Host/Services | 850 | 703 | **+147** | Fusion smaller — but see note |
| Db | 5 | 12 | −7 | **~even** (both plain EF, same schema) |
| Tests | 362 | 459 | **−97** | leaner signalr harness — see note |

Top files by churn (add+del): `RoomsBackend.cs` (224), `Host/Program.cs` (204),
`QuestionsBackend.cs` (192), `TownHallClient.cs` (+150, new), `RoomsService.cs`
(146), `QuestionsTests.cs` (143), `UsersBackend.cs` (143), `ChangeTracker.cs`
(+140, new).

**The domain logic is genuinely even.** The Host/Services +147 is *not* the
room/question/vote logic — it's the server-side reactive glue that lives there as
new files: `ChangeTracker.cs` (140), `ServerService.cs` (85), `PresenceStore.cs`
(46), `ServerShared.cs` (34), `BackendService.cs` (26), plus small session/telemetry
helpers — **~360 lines of infrastructure**. Subtract those and the domain services
come out slightly *smaller* than Fusion's (no `Invalidation.IsActive` branches, no
operation-DbContext plumbing). The DB layer (−7) is within noise; both run the same
schema and migrations.

**Where SignalR is lighter**
- **Contracts −218.** Both branches keep one command record per write (a deliberate
  shared invariant — see AGENTS.md), but Fusion's records each carry
  `[MessagePackObject]`, a `Session` field, and an `ISessionCommand<T>`/
  `IDelegatingCommand` base, and every interface method carries
  `[ComputeMethod]`/`[CommandHandler]`. SignalR's records are the bare
  `(RoomId, ...)` payload and its interfaces are attribute-free (identity is bound
  by the hub, so no method threads a `Session`).
- **UI components −102.** `StateComponent<T>` observes exactly one stream;
  components drop the per-read `Session`/`ComputeState` plumbing and the
  `CircuitHub.SessionResolver` hops. The +33 in Abstractions is the flip side:
  signalr bundles what a component needs into view-model records
  (`RoomView`, `RoomCard`, `LobbyView`, `QuestionView`, `MoodView`) where Fusion
  components compose fine-grained reads.

**Where Fusion is lighter (SignalR pays it back as infra)**
- **UI infra +434 and ~360 lines of the Host/Services delta.** SignalR hand-writes
  what Fusion ships in the box: `TownHallClient.cs` (150, one HubConnection with
  auto-reconnect + stream re-subscribe), `Clients.cs` (115, per-interface proxies),
  `ComponentBases.cs` (95, the `StateComponent` base) on the client;
  `ChangeTracker.cs` + `ServerService.cs` + `PresenceStore.cs` on the server —
  **~800 lines of framework-shaped glue** total. In Fusion these are `AddFusion()`
  + `ComputedStateComponent`.
- **Host wiring +117.** SignalR adds the hub (`TownHallHub.cs`, one forwarding
  method per API method), `ErrorHubFilter`, a session-cookie middleware, and the
  render-mode endpoint.

Net: same app, similar size — Fusion trades **contract/attribute boilerplate** for
a **free reactive client**; SignalR trades **lean contracts/components** for
**~800 lines of reactive infrastructure you own**.

---

## 2. Cleanliness

**Verdict: SignalR reads cleaner in the leaves (contracts, components, commands);
Fusion reads cleaner in the middle (no bespoke client/transport layer to hold in
your head).** With names now unified (`Post`, `Create`, `SetLive`, `ListOpen`, ...
on both branches), a file-by-file diff shows exactly the framework, nothing else.

- **Commands.** Fusion command handlers carry a dual-mode shape — the
  `if (Invalidation.IsActive) { ... re-list what to invalidate ...; return null!; }`
  branch plus `CreateOperationDbContext` — before the real body
  (`RoomsService.Create`). SignalR's `Create` is one straight path: validate →
  `SaveAndNotify(dbContext, "room:{id}")`. The invalidation-vs-notify ergonomics
  are the single biggest readability delta in the services.
- **Reads.** Fusion reads are ordinary cached async methods; the reactivity is
  invisible. SignalR reads are `IAsyncEnumerable` streams with an explicit
  self-wake (`(value, wake)` tuples for TTL/aging), which is more machinery in the
  method signature but keeps *time-based* re-evaluation local and legible.
- **Identity.** SignalR wins on ergonomics: identity is implicit (the hub binds the
  connection's session), so no method threads a `Session` and command records carry
  only their payload. Fusion threads `Session` through every read and every command
  record.
- **Client.** Fusion has *no* client layer to read — components inject the same
  interfaces everywhere. SignalR's `TownHallClient` (reconnect + transparent stream
  re-subscription) is well-written but is a whole subsystem a reader must learn.

---

## 3. Robustness

**Verdict: Fusion is materially more robust at scale and across hosts; SignalR is
simpler but single-host and re-reads more eagerly.**

- **Multi-host correctness — Fusion wins decisively.** Fusion propagates
  invalidations through a transactional **operation log** (+ operation reprocessor),
  so a write on one host invalidates caches on *every* host. SignalR's
  `ChangeTracker` and `PresenceStore` are **in-process** — correct on one host,
  but a second host wouldn't see the first's notifications (the signalr port
  documents this and points at Postgres `LISTEN/NOTIFY` as the real fix).
- **Cross-viewer sharing — Fusion wins.** A Fusion computed is shared by key across
  all viewers: a change re-computes **once**, shared. SignalR streams are
  **per-subscription**: the same read for N viewers re-runs N times per notify.
- **Committed-but-errored writes.** Both are hardened: Fusion via the operation
  reprocessor; the signalr port's `SaveAndNotify` notifies in a `finally`, so a
  committed-but-throwing save still propagates.
- **Presence idle churn (Fusion).** Fusion's per-`Ttl` presence re-check used to
  cascade to stats/mood even when the set was unchanged; fixed with
  `ConsolidationDelay` + a value-equal `PresentUsers` (see `PresenceBackend`,
  `PresenceConsolidationTests`). SignalR sidesteps this structurally — presence
  streams self-wake and re-read only their own scope.
- **Reconnect.** Fusion's RPC handles reconnect/replay in-framework; SignalR
  re-implements it in `TownHallClient` (auto-reconnect + re-subscribe), verified
  live in the port but more surface to get right.
- **Test coverage — Fusion wins.** Authored effort is even (**36** Fusion vs **37**
  signalr `[Fact]`/`[Theory]` methods), but Fusion **executes 68 vs signalr's 37**:
  every shared test runs against both the in-process server container *and* the
  RPC-client container, so Fusion's suite also exercises the client/RPC transport
  for free. signalr's `TestBase` has a single hub-client access point, so each test
  runs once (its `PropagationTests` cover stream re-yield specifically).

---

## 4. Performance

**Verdict: SignalR has the cheaper *write* path; Fusion has the cheaper *read* path
under concurrency (shared cache) and at idle.** *(Carried over from the 2026-07-15
run: Fusion measured live with `--aspire` on that build; SignalR extrapolated from
code. The hot paths are unchanged since — only names, comments, and sign-in UX
moved — so the numbers stand. A live signalr `--aspire` run would upgrade its
numbers to measured.)*

Method: drove `main` in the browser while watching the Aspire dashboard
(`ActualLab.Rpc`/`Npgsql`/`TownHall.Db` meters + traces), the `Executed DbCommand`
log, and the `_Operations` table.

**Reads**
- **Fusion (measured):** a compute method hits Postgres **once**, then serves from
  cache until invalidated — and the cache is **shared across viewers**. An idle but
  occupied room costs **~0 queries** after warm-up (verified: 0 stats/mood reads
  over a 65 s idle window post-consolidation-fix); the only idle DB traffic is the
  operation/event-log reader poll (60 s dev / 5 s prod).
- **SignalR (extrapolated):** each open stream **re-reads on every notify to its
  scope**, with **no cross-viewer sharing**. So a room with V viewers each holding
  S streams re-queries ≈ **V×S times per notify**, where Fusion re-computes ≈
  **S times total** (shared) and only for actually-changed values. Read cost
  therefore grows with concurrency on SignalR and stays flat on Fusion.

**Writes**
- **Fusion (measured):** a persisted command appends an **operation-log row**
  (extra INSERT into `_Operations`) on top of the domain write — the price of
  cross-host invalidation. (Ephemeral commands like presence stay transient:
  verified `_Operations` empty at idle.)
- **SignalR (extrapolated):** `SaveAndNotify` is just `SaveChanges` + an in-memory
  version bump — **no operation-log INSERT**. Cheapest possible write, at the cost
  of the single-host limitation above.

**Rough per-action shape**

| Action | Fusion (measured) | SignalR (extrapolated) |
|--------|-------------------|------------------------|
| Cold room open | one query per compute method (room, stats, top-open, mood, presence, author names), then cached & shared | one query per stream, per viewer, re-run on each scope notify |
| Idle occupied room | ~0 queries (shared cache; log-reader poll only) | ~1 query per open stream per membership/vote/mood notify |
| Post / vote / mood | domain write **+ `_Operations` INSERT** | domain write, **no op-log row** |

**Bottom line.** Pick **Fusion** when reads dominate, viewers are concurrent, or you
run more than one host — its shared, invalidation-driven cache makes reads nearly
free and keeps every node consistent, at the cost of contract boilerplate and an
operation-log write per command. Pick **SignalR** for the leanest
contracts/components and the cheapest single-host writes, accepting ~800 lines of
hand-written reactive infrastructure and read cost that scales with concurrency.
