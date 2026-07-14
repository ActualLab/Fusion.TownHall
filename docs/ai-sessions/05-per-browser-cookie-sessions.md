# Session 05

**User:** Are sessions per-tab or per-browser? If per-tab, switch to per-browser — I think you ignored my earlier ask on this.

**Opus4.8:** They were per-tab (the session GUID lived in `sessionStorage`). As a first cut I moved it to `localStorage` (per-browser), then reverted that per the correction below.

**User:** No — handle sessions the same way as the TodoApp sample: store them in cookies, and the WASM app must use the default Session.

**Opus4.8:** Adopted the TodoApp pattern. The session now comes from the `FusionAuth.SessionId` cookie that the already-registered `UseFusionSession()` middleware resolves/creates; `_HostPage` passes that id to `App`, which sets `SessionResolver.Session = new Session(id)` for server render and `Session.Default` for WASM (the server RPC layer — `RpcDefaultSessionReplacer` + the session-bound WS connection factory from `AddWebServer()`, both already wired up — substitutes the cookie session for `Session.Default` on every call). Removed the `townHall.getSessionId` sessionStorage JS. Verified live in two browsers: chrome1 shows the same user across two tabs (per-browser), chrome2 is a distinct user; WASM renders real data with an HttpOnly cookie (`document.cookie` empty).

**User:** You can test with chrome1/chrome2. If you need more than two users, add a `?session=...` URL parameter that makes a tab ignore its cookie-based session and use that one instead (and in that case propagate it explicitly to WASM).

**Opus4.8:** Added the override: `_HostPage` reads `?session=<id>` (min 8 chars, per the `Session` ctor rule) and, when present, uses it as the session and sets `PropagateSessionToWasm=true` so `App`'s WASM branch sends `new Session(id)` explicitly instead of `Session.Default` (the RPC replacer passes concrete valid sessions through). Verified in WASM: `?session=aliceoverride01` renders "CHEERFUL MARMOT" instead of the cookie's "KEEN YAK".

**User:** That `?session=` parameter must never work in production — gate it behind an environment check that disables it there entirely.

**Opus4.8:** Gated it: `_HostPage` computes the override as `Env.IsProduction() ? "" : Query["session"]`, so in Production the override is dropped and the cookie session is always used. Verified with a throwaway Production-env instance on port 5199 (separate from the dev loop): `?session=aliceoverride01` left the identity as the cookie's "SNAZZY HERON" — the override is ignored in prod — while the same URL in the dev instance honored it. 60 tests still green; left uncommitted.
