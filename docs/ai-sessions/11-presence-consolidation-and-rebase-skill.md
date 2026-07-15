# Session 11 (main branch)

**User:** There's a logic bug: with no activity, the TownHall server constantly runs
DB commands (reading user names, etc.). At idle it should only hit the DB via the
operation/event-log reader's periodic checks. What's going on?

**Opus4.8:** Traced it to presence. `IPresence.GetPresentSessionIds` self-invalidates
every `Ttl` (30 s) to detect expiry; because Fusion propagates invalidation to
dependents unconditionally (confirmed in `Computed.cs`) and `string[]` compares by
reference, that re-check cascaded to `RoomStats.GetStats` and `Mood.GetSummary` (and,
on a room page, the question-author name reads) every cycle — even when the present
set was unchanged, re-hitting the DB.

**User:** You don't store presence in the DB in the SignalR version — don't store it
here either. My guess is it's this part; one option is a protected helper with a
consolidation delay (see the Fusion docs). There's likely a Fusion helper for
equatable lists/arrays; regardless, you need to override equality — maybe a custom
type/record. Use `ConsolidationDelay = 0.1`. And add a dedicated test for the scenario.

**Opus4.8:** Presence is already in-memory (no DB writes); the churn was the false
cascade. Restructured `PresenceService` into two compute methods: a raw
`GetPresentSessionsRaw` (reads the in-memory map, self-invalidates at the earliest
expiry) and `GetPresentSessions` carrying `[ComputeMethod(ConsolidationDelay = 0.1)]`
on top of it, returning a custom value-equal `PresentSessions` type (`ApiArray`/arrays
compare by reference, so a sequence-equality type is required). A re-check that yields
the same set is now swallowed, so a steady, occupied room never invalidates the
stats/mood reads. Added `PresenceConsolidationTests` asserting an unchanged re-check
doesn't invalidate a presence-dependent computed while a real join still does; 62/62
tests pass.

**User:** Create a `/rebase-branches` skill: list the branches meant to sit on top of
`main` and never merged (currently only `signalr`), explain the identical-files rebase,
and note each rebased branch must be force-pushed while the skill runs.

**Opus4.8:** Added `.claude/commands/rebase-branches.md` encoding the byte-identical
re-parent procedure (record tree → `reset --soft main` → commit → prove tree/​diff →
`--force-with-lease`).

**User:** Update it: identify the "shared base" (`AGENTS.md`, etc.) — files that must be
identical across all branches. So the rebase isn't "retain everything from the branch";
it's "retain everything except the shared base, which must equal `main`". Sync those
files `main -> branch` before the rebase so the expected end result stays checkable.

**Opus4.8:** Added a Shared base section (agent/convention files: `AGENTS.md`,
`CLAUDE.md`, `AGENTS-Source.md`, `CODING_STYLE.md`, `.claude/commands/`) and reworked
the procedure to sync the shared base from `main` first (its own commit, the checkable
"expected tree"), then re-parent onto `main`, proving the rebased tree equals the
expected tree and the shared base equals `main`.
