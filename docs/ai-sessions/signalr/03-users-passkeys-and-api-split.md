# Session 03

**User:** Port the same improvements the Fusion (`main`) branch just got to the
`signalr` branch — including the frontend/backend API split, real users + passkey
(WebAuthn) sign-in, guests being read-only, re-keying domain data to `UserId`, a
name prompt on passkey creation, and anonymous question posting. Log this message
here. When done, run `/rebase-branches` to rebase `signalr` onto `main`, test
everything with chrome1/chrome2, and commit + push the branch once it's confirmed
feature-wise equal to `main`.

**Opus 4.8:** Implemented the SignalR-stack equivalents of the whole feature set:
a real user model + passkeys; a **frontend** (hub, session-bound) vs **backend**
(id-based, local) service split — the per-connection `ServerService` frontends resolve
the connection's session to a user, enforce sign-in/ownership, and assemble the
session-specific view facets, delegating writes + session-agnostic reads to new
`*Backend` singletons; guest read-only gating; `UserId`-keyed domain data;
name-on-passkey-create; and anonymous posting via a per-(user, room) pseudonym.

**Note:** New/changed pieces: `User`/`UserFull` + `AnonId` + `NameGenerator` moved to
`TownHall.Contracts`; `IUsers`/`IAuth` interfaces (replacing `IParticipants`), `Lobby`
stream + `IsSignedIn`/`AnonName` on `RoomView`, `anonymous` flag on `PostQuestion`; DB
gains `Users`/`PasskeyCredentials`/`SessionUsers` and re-keys owners/votes/moods/authorship
to `UserId` (copied main's `UsersAndPasskeys` migration for shared-schema parity);
`ChangeTracker` + `ServerService.Stream` gained multi-scope waiting so session-aware
streams re-read on a new `session:{id}` scope (sign-in/out/rename); passkeys via Fido2NetLib
(`AuthService` + `PasskeyChallengeStore` + `passkey.js`/`PasskeyClient`); `UserMenu`, guest
gating and the anon checkbox in the UI. Fixed a session bug surfaced by sign-in: on the
first request the `th_sess` cookie is only in the *response*, so the server-render pass read
an empty session id and diverged from the hub's cookie session — the middleware now exposes
the resolved id via `HttpContext.Items` and `_HostPage` reads it. All **37 tests pass**;
verified in the browser via CDP virtual authenticators (named create → sign-out → sign-in
persists across reload; anonymous post shows the pseudonym, never the real name; two-user
Server+WASM run with live cross-user question/vote propagation and a 2-participant audience).
Then rebased onto `main` via `/rebase-branches` and force-pushed.
