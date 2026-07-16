# Session 15

**User:** Two follow-up changes. (1) On passkey creation, also ask the user to provide a name.
(2) In each town hall, add a "Post anonymously" checkbox on the composer. When checked, the
question is attributed to a name derived from a hash of the user id + room id — a random name
per (user, room). Show that expected name next to the checkbox, e.g. "Post anonymously as …".

**Opus 4.8:** (1) `UserMenu` now prompts for a name (inline field) before the passkey ceremony;
`IAuth.GetRegistrationOptions(session, name)` embeds it in the WebAuthn options and uses it as
the account name (random fallback if blank). (2) Added `AnonId.Of(userId, roomId)` +
`NameGenerator` (moved to `TownHall.Abstractions` so the UI and server share it). `Questions_Post`
/ `QuestionsBackend_Post` gained an `Anonymous` flag; when set, the backend stores
`AnonId.Of(user, room)` as the question's `AuthorId` (the real user id isn't stored), and
`UsersBackend.Get` resolves an `anon-…` id to a generated pseudonym with no DB row. The composer
shows a "Post anonymously as {pseudonym}" checkbox (pseudonym computed in `RoomPage` from the
signed-in user + room). All **68 tests pass**; verified in the browser via CDP virtual
authenticators: registering with a chosen name ("Alex Tester") and, in a two-user run, an
anonymous post by "Bob Real" showed up for the owner attributed to the pseudonym
("Breezy Meerkat"), never the real name.
