# SignalR vs Fusion — comparison

Built against: main `1b5d9a1` · signalr `d7d625a` · 2026-07-15
Framework delta: **78 files, +2157 / −2072 lines** (`git diff --stat main signalr`)

`main` is the reference **.NET + Fusion** implementation; `signalr` is the same app
on **.NET + SignalR streams, no Fusion** (only `ActualLab.Core` for `Moment`/hashing).
Both pass their suites against the same Postgres schema. Lenses below: **measured**
= observed live this build; **extrapolated** = derived from code (a live signalr
`--aspire` run would upgrade its perf numbers to measured).

---

## 1. Volume (numerical)

**Verdict: a wash on total LOC (+85 net on signalr), but the code lives in very
different places** — Fusion pushes ceremony into *contracts*, SignalR pushes it into
*client/reactive infrastructure it has to hand-write*.

Net line delta by area (`signalr − main`; negative = signalr is smaller):

| Area | +add | −del | net | who's smaller |
|------|-----:|-----:|----:|---------------|
| Contracts (`TownHall.Contracts`) | 111 | 282 | **−171** | signalr |
| UI components (Pages/Shared/Layout) | 181 | 273 | **−92** | signalr |
| Tests | 405 | 522 | **−117** | signalr |
| Db | 3 | 16 | **−13** | signalr |
| Host/Services | 636 | 611 | +25 | ~even |
| Host wiring (`Program.cs` etc.) | 215 | 134 | **+81** | Fusion |
| UI infra (`UI/Services`) | 449 | 76 | **+373** | Fusion |

Top files by churn: `RoomsService.cs` (357), `QuestionsService.cs` (295),
`Program.cs` (162), `TownHallClient.cs` (+149, new), `IRooms.cs` (−112 net).

**Where SignalR is lighter**
- **Contracts −171.** Fusion contracts carry a serializable `*_Command` record +
  attributes per write (`IRooms.cs` alone is −112: `Rooms_Create`, `Rooms_SetLive`,
  … each a `[MessagePackObject] … : ISessionCommand<Unit>` record). SignalR
  interfaces are plain async methods with implicit identity — no command records,
  no `[ComputeMethod]`/`[CommandHandler]` attributes.
- **UI components −92.** `StateComponent<T>` observes exactly one stream; components
  drop the per-read `Session`/`ComputeState` plumbing and the `CircuitHub.SessionResolver`
  hops.

**Where Fusion is lighter (SignalR pays it back as infra)**
- **UI infra +373 / Host services ~even but with 5 new files.** SignalR hand-writes
  the reactive plumbing Fusion ships in the box:
  `TownHallClient.cs` (149), `Clients.cs` (96), `ComponentBases.cs` (95) on the
  client; `ChangeTracker.cs` (96), `ServerService.cs` (61) on the server — **~497
  lines of framework-shaped glue** (one HubConnection with auto-reconnect + stream
  re-subscribe, a versioned per-scope change signal, a `StateComponent` base). In
  Fusion these are `AddFusion()` + `ComputedStateComponent`.
- **Host wiring +81.** SignalR adds the hub, a session-cookie middleware, the render-
  mode endpoint, and a Fusion-free render-mode switch.

Net: same app, similar size — Fusion trades **contract boilerplate** for a **free
reactive client**; SignalR trades **lean contracts/components** for **~500 lines of
reactive infrastructure you own**.

---

## 2. Cleanliness

**Verdict: SignalR reads cleaner in the leaves (contracts, components, commands);
Fusion reads cleaner in the middle (no bespoke client/transport layer to hold in
your head).**

- **Commands.** Fusion command handlers carry a dual-mode shape — the
  `if (Invalidation.IsActive) { _ = GetRoom(id); _ = ListRoomIds(); … return null!; }`
  branch that re-lists what to invalidate, plus `CreateOperationDbContext` — before
  the real body (`RoomsService.OnCreate`). SignalR's `CreateRoom` is one straight
  path: validate → `SaveAndNotify(db, "room:{id}")`. The invalidation-vs-notify
  ergonomics are the single biggest readability delta in the services.
- **Reads.** Fusion reads are ordinary cached async methods; the reactivity is
  invisible. SignalR reads are `IAsyncEnumerable` streams with an explicit self-wake
  (`(value, wake)` tuples for TTL/aging), which is more machinery in the method
  signature but keeps *time-based* re-evaluation local and legible.
- **Identity.** SignalR wins on ergonomics: identity is implicit (the hub binds the
  connection's session), so no method threads a `Session`. Fusion threads `Session`
  through every read/command.
- **Client.** Fusion has *no* client layer to read — components just inject the same
  interfaces. SignalR's `TownHallClient` (reconnect + transparent stream
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
- **Committed-but-errored writes.** Both were hardened: Fusion via the operation
  reprocessor; the signalr port added `SaveAndNotify` (notify in a `finally`) so a
  committed-but-throwing save still propagates.
- **Presence idle churn (Fusion, fixed this build).** Fusion's per-`Ttl` presence
  re-check used to cascade to stats/mood even when the set was unchanged; fixed with
  `ConsolidationDelay` + a value-equal `PresentSessions` (see `PresenceService`,
  `PresenceConsolidationTests`). SignalR sidesteps this structurally — presence
  streams self-wake and re-read only their own scope.
- **Reconnect.** Fusion's RPC handles reconnect/replay in-framework; SignalR
  re-implements it in `TownHallClient` (auto-reconnect + re-subscribe), verified live
  in the port but more surface to get right.

---

## 4. Performance

**Verdict: SignalR has the cheaper *write* path; Fusion has the cheaper *read* path
under concurrency (shared cache) and at idle.** *(Fusion = measured live on `main`
with `--aspire` this build; SignalR = extrapolated from code — run signalr with
`--aspire` to measure.)*

Method: drove `main` in the browser while watching the Aspire dashboard
(`ActualLab.Rpc`/`Npgsql`/`TownHall.Db` meters + traces), the `Executed DbCommand`
log, and the `_Operations` table.

**Reads**
- **Fusion (measured):** a compute method hits Postgres **once**, then serves from
  cache until invalidated — and the cache is **shared across viewers**. An idle but
  occupied room costs **~0 queries** after warm-up (verified: 0 stats/mood reads over
  a 65 s idle window post-consolidation-fix); the only idle DB traffic is the
  operation/event-log reader poll (60 s dev / 5 s prod).
- **SignalR (extrapolated):** each open stream **re-reads on every notify to its
  scope**, with **no cross-viewer sharing**. So a room with V viewers each holding S
  streams re-queries ≈ **V×S times per notify**, where Fusion re-computes ≈ **S times
  total** (shared) and only for actually-changed values. Read cost therefore grows
  with concurrency on SignalR and stays flat on Fusion.

**Writes**
- **Fusion (measured):** a persisted command appends an **operation-log row**
  (extra INSERT into `_Operations`) on top of the domain write — the price of
  cross-host invalidation. (Ephemeral commands like presence stay transient: verified
  `_Operations` empty at idle.)
- **SignalR (extrapolated):** `SaveAndNotify` is just `SaveChanges` + an in-memory
  version bump — **no operation-log INSERT**. Cheapest possible write, at the cost of
  the single-host limitation above.

**Rough per-action shape**

| Action | Fusion (measured) | SignalR (extrapolated) |
|--------|-------------------|------------------------|
| Cold room open | one query per compute method (room, stats, top-open, mood, presence, author names), then cached & shared | one query per stream, per viewer, re-run on each scope notify |
| Idle occupied room | ~0 queries (shared cache; log-reader poll only) | ~1 query per open stream per membership/vote/mood notify |
| Post / vote / mood | domain write **+ `_Operations` INSERT** | domain write, **no op-log row** |

**Bottom line.** Pick **Fusion** when reads dominate, viewers are concurrent, or you
run more than one host — its shared, invalidation-driven cache makes reads nearly
free and keeps every node consistent, at the cost of contract boilerplate and an
operation-log write per command. Pick **SignalR** for the leanest contracts/components
and the cheapest single-host writes, accepting ~500 lines of hand-written reactive
infrastructure and read cost that scales with concurrency.
