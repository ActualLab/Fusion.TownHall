# Session 16

**User:** The Api-level (frontend) commands should implement Fusion's
`IDelegatingCommand`, which lets us drop the useless `if (Invalidation.IsActive)`
blocks from their handlers. Also confirm whether the `AnonId` class is still needed.

**Opus 4.8:** `AnonId` stays — it's used by `QuestionsBackend` (pseudonymous author id),
`UsersBackend.Get` (`anon-` id → generated name), and `RoomPage` (pseudonym preview).
Marked all 18 Api-level command records `IDelegatingCommand` (they only resolve the
session, check permissions, and forward to the backend commands, which own the real
work + invalidation), and removed the 17 now-dead `if (Invalidation.IsActive)` guard
blocks from the frontend handlers. The `*Backend` command handlers keep their guards.
All 68 tests pass.
