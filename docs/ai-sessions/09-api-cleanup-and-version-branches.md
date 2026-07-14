# Session 09

**User:** Let's clean up the API a bit. First, methods returning lists of IDs shouldn't carry an `Ids` suffix — the signature already makes clear what they return. Also, apply an invalidation delay of 0.5s to methods returning potentially large lists that update frequently.

**Opus4.8:** Renamed `IQuestions.ListOpenIds`→`ListOpen`, `GetTopOpenIds`→`GetTopOpen`, `ListResolvedIds`→`ListResolved`, and `IRooms.ListActiveIds`→`ListActive` (interfaces, impls, UI, tests, and a stale `Models.cs` comment); added `[ComputeMethod(InvalidationDelay = 0.5)]` to the frequently-updating large-list reads `ListOpen`, `GetTopOpen`, and `IRoomStats.GetTrending`.

**User:** The server loop is running — can you briefly test the live version?

**Opus4.8:** Couldn't reach `localhost:5136` from inside Docker (host-networking limit), so drove the live app through host Chrome: created a room, resumed it, posted/voted/resolved a question, and confirmed all renamed list methods work end-to-end (RECENT/TOP/RESOLVED/TRENDING tabs, `ListActive` home list) with no console errors.

**User:** Some list-returning API methods still don't follow the `ListXxx` pattern — rename those too before resuming the previous task.

**Opus4.8:** Renamed `GetTopOpen`→`ListTopOpen` and `GetTrending`→`ListTrending` across contracts, services, UI, and tests (internal helper `GetRoomTrending` left as-is).

**User:** Add one test for a method with the invalidation delay, using the test host client to validate the delay isn't double the intended one. Then commit and push, and create + push a `v1/fusion` branch at the same commit.

**Opus4.8:** Added `InvalidationTests.ListOpenInvalidationDelayIsAppliedOnceForClient`, which reads `ListOpen` via the RPC client container and asserts the client-observed invalidation lands at ~0.5s, not the ~1s a doubled delay would give (grounded in Fusion's `ComputedOptions`: a client replica takes `InvalidationDelay` from `ReplicaMethodAttribute` only). Builds clean; couldn't run the suite here (Postgres unreachable from Docker).

**User:** Don't create the `v1/fusion` branch yet — we'll keep working. In AGENTS-Source.md and the README, document that versions live on per-framework branches: `main` = Fusion, `signalr` = SignalR (no Fusion), with more to be listed later.

**Opus4.8:** Added a `Version branches` section to AGENTS-Source.md and a `Framework versions` section to the README; regenerated `AGENTS.md`/`CLAUDE.md` via `ai update-md`.

**User:** Also note that `<branch>-v1`-style names are feature-parity **tags** (fixed pointers, never moved), and that AI sessions on a non-`main` branch must live in `docs/ai-sessions/<branch>/` so merges from `main` don't conflict.

**Opus4.8:** Documented the feature-parity tags in both AGENTS-Source.md and the README, and the per-branch session-log rule in AGENTS-Source.md; regenerated the docs and committed/pushed everything.
