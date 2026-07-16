# Session 01 (signalr branch)

**User:** Read the last `main` session, then create the `signalr` branch and keep
this session's log there. The goal: build a full implementation of the same Town
Hall app on **SignalR, with no Fusion at all**, by modifying the existing sources
on this branch. Don't commit until it's verified working (the `/server-loop` is
running for live testing). The four reads with an invalidation delay don't need
delay parity — ignore the delays. Useless tests can be dropped, and the API can
change freely. Spin up a `main` worktree for reference if needed.

**Opus4.8:** Created the `signalr` branch, read the whole app (contracts, host
services, EF layer, Blazor UI, tests), then asked two high-impact questions before
writing code: the render-mode topology, and whether to keep the `ActualLab.Core`
utility dependency.

**User:** Use a thin abstraction layer: the app talks to interfaces whose reactive
reads return `IAsyncEnumerable<T>`, with two implementations of the same API — the
server services (real logic), and a client that routes each call to the SignalR
hub. It must handle connection drops/reconnects gracefully. Keep `ActualLab.Core`
(for `Moment` etc.).

**Opus4.8:** Reshaped the design around that: reactive reads are server-to-client
SignalR **streams** (`IAsyncEnumerable`), so no groups/broadcasts are needed — a
per-scope `ChangeTracker` (versioned signal) wakes each open stream, which re-reads
and yields; time-based transitions (room ending, presence TTL, trending aging)
become per-stream self-wake timers, a faithful port of Fusion's scheduled
invalidation. Client-side, a `TownHallClient` wraps one `HubConnection`
(`WithAutomaticReconnect` + infinite retry) and transparently re-establishes streams
across drops; a `StateComponent<T>` base re-renders on each yielded value. Folded
the fine-grained reads into view models (`RoomListItem`, `RoomView`, `QuestionView`,
`TrendingQuestion`, `MoodView`) so every component subscribes to exactly one stream.

**User:** For the DB, keep the `DbOperation`/`DbEvent` tables identical (copy the
types from Fusion or make EF ignore them) — I want every framework branch to run on
the same DB.

**Opus4.8:** Restored the original migration verbatim (identical schema, incl. the
now-unused `_Operations`/`_Events` tables) and left them out of the lean EF model,
suppressing EF Core 10's `PendingModelChangesWarning` so `MigrateAsync` no-ops on the
shared DB. Then finished the whole conversion — Fusion removed everywhere; server
services rewritten on plain EF + `ChangeTracker`; `TownHallHub` (streaming reads +
one-shot commands) with cookie/`?session=` identity and a `HubException`-mapping
filter; WASM-only Blazor host (no render-mode switch); all pages/components moved to
`StateComponent`; tests rewritten against the services (dropped the Fusion-specific
invalidation test and the RPC-client variants). Verified end-to-end: solution builds,
**29/29 tests pass** against Postgres, and a two-browser live run showed cross-client
reactivity (B sees A's question, A sees B's vote — live) with zero console errors,
plus a full server-restart proving graceful reconnect (banner → reconnect → streams
and commands resume). Not committed yet, per instructions.

**User:** Live-test it with two real browsers (chrome1/chrome2), making sure every
update propagates — and I hope you tested that in the suite too.

**Opus4.8:** Drove chrome1 (owner) + chrome2 (participant) through lobby/presence/
status/question/vote propagation, all live; and added `PropagationTests.cs` (4 tests
that subscribe to a stream, mutate from another session, and assert the stream
re-yields) — suite now **33/33**.

**User:** Is convergence guaranteed, and where isn't it? And how do you propagate a
notification when a commit reports an error but actually committed (e.g. connection
drops on the commit ACK)?

**Opus4.8:** Found and fixed the gap: commands notified only after a *successful*
SaveChanges, so a committed-but-errored save skipped the notify and could strand
streams. Added `ServerService.SaveAndNotify` which notifies in a `finally` (a spurious
notify is an idempotent re-read, so over-notify is safe; under-notify strands viewers);
routed all 15 writes through it. Documented the remaining non-guarantees honestly:
multi-server (in-process `ChangeTracker`/`PresenceStore` — the real fix is transactional
Postgres `LISTEN/NOTIFY`), scope-by-convention, and per-yield (non-snapshot) composite
reads. 33/33 still pass.

**User:** Did you test an update arriving during a client's disconnect, and what
happens to the UI on reconnect — is every non-static value re-requested?

**Opus4.8:** Verified with the two browsers: took A offline (DevTools), posted from B,
brought A back — A converged to include B's post (fresh re-read, not event replay).
Confirmed reconnect is not a reload: every reactive value is re-fetched by
re-subscribing its stream; local UI state is preserved. Also saw A's presence expire
off B's audience via the TTL self-wake while A was offline.

**User:** I don't see the WASM/Server/Auto render-mode switch — add something like
Fusion's (you may reference its assemblies just for that).

**Opus4.8:** Referencing `ActualLab.Fusion.Server` would drag the whole framework back
in, so I reimplemented the ~60-line switch Fusion-free: `RenderModeDef` (Auto/Server/
WASM), a `/render-mode/{key}` cookie endpoint, `RenderModeSwitch` in the app bar. Made
the app run in all three modes via the thin abstraction — the interfaces resolve to the
**server-direct services** (bound to a per-circuit `CircuitSession`) in the server
container and to the **hub clients** in the WASM container, both driven by the one
`ChangeTracker`. Live-verified: switched chrome1 to Server (created/entered/resumed a
room, presence + owner tools all working) and chrome2 to WASM, then a Server-mode write
propagated live to the WASM client — proving one reactivity core bridges both transports.
33/33 tests still pass. Still uncommitted.
