# Session 14

**User:** Implement two features. (1) A real user type stored in the database. Guests are
ephemeral, read-only observers (represented by `null` / `User.Guest`); signing in with a
**passkey** (WebAuthn) creates and links a real user to the session. Guests can't act and
don't report presence. (2) Split the API into a **frontend** API (takes `Session`, resolves
the user, checks permissions, then forwards) and a **backend** API (ids only, does the work),
à la ActualChat — services mostly use the backend API; the frontend API is what's exposed via
RPC. Structure as Abstractions / Api / BackendApi projects. Re-key all domain data to `UserId`.
Use NuGet packages + JS for passkeys as needed.

**Opus 4.8:** Explored the codebase and ActualChat's frontend/backend conventions, planned,
and got approval. Confirmed via questions: re-key everything to `UserId`; guests are read-only
(and require sign-in for *any* action, including presence) and are modeled as `null`/`User.Guest`;
no anon→user migration; passkeys are usernameless/discoverable with a generated default name;
use `User`/`UserFull` like ActualChat.

**User:** You can use any useful NuGet packages to get passkey auth working, and add JavaScript
if needed.

**Opus 4.8:** Used **Fido2NetLib** (`Fido2` + `Fido2.AspNet` 4.0.1) for the WebAuthn ceremonies,
plus a `wwwroot/js/passkey.js` module that drives `navigator.credentials`.

**User:** When done (tests added and passing, browser tests via chrome1, etc.), commit and push.
If you can't fully automate real passkeys in the browser (PIN prompts, etc.), add temp APIs to
test without real passkeys — I'll test the real thing myself when you ask. Research ready-made
solutions for fake passkeys and use the best option.

**Opus 4.8:** Best ready-made solution: Chrome's **CDP WebAuthn virtual authenticator**
(`WebAuthn.addVirtualAuthenticator`), driven via Playwright — fully automates real passkey
create/get with no PIN. Also added a **Development-only** `/dev/signin` endpoint as a fallback.

**User:** Guests shouldn't report presence either.

**Opus 4.8:** Made presence require sign-in and re-keyed it to `UserId` (present = signed-in users).

**Note:** Delivered end to end. Structure is now `TownHall.Abstractions` (models incl.
`User`/`UserFull`), `TownHall.Api` (frontend `IUsers`/`IAuth`/`IRooms`/… + `X_Verb`
session commands), `TownHall.Backend` (`IUsersBackend`/… : `IBackendService`, id-based, +
`XBackend_Verb` commands). Frontend services resolve `Session→UserId`, enforce sign-in/ownership,
then delegate to the local backend via `Commander.Call(cmd, isOutermost: true)`. New DB tables
`Users`/`PasskeyCredentials`/`SessionUsers`; `RoomOwners`/`Votes`/`Moods`/`Question.AuthorId`
re-keyed to `UserId` (migration `UsersAndPasskeys`). Passkeys via Fido2NetLib + `IAuth` +
`PasskeyChallengeStore` + `passkey.js`/`PasskeyClient`; UI got a `UserMenu` (sign in / create
passkey / rename / sign out) and guest gating on every action. All **66 tests pass** (server +
RPC-client). Verified in a real browser via a CDP virtual authenticator: register → sign out →
sign in reuse the same discoverable passkey; and a two-user (Server + WASM) run showed live
cross-user question/vote propagation with a correct 2-participant audience. Fixed one real bug
found during verification: the `UserMenu` used `ActivatorContent`+`MudButton`, which swallowed
the open click — switched to MudMenu `Label`/`StartIcon`.
