# TownHall — Implementation Task (Phase 1)

## What this is

TownHall is a live audience Q&A app ("Slido-lite"): anyone can create a town
hall (a room) and becomes its owner; participants who open it post questions
and upvote others' questions; the question list sorts by votes; a trending
panel highlights questions gaining votes right now; owners moderate — they
resolve questions (with an optional note on how they were answered) or delete
them, and they start/stop the session. Participants also continuously signal
their mood on a 5-emoji scale, visualized as a big generated face plus a
distribution chart.

The app exists to compare real-time frameworks. The same app will be
implemented several times (.NET + [Fusion](https://github.com/ActualLab/Fusion),
plain .NET, .NET + SignalR, possibly TypeScript and Elixir stacks) to
measure the code delta each framework produces. **This task is Phase 1:
the Fusion-based implementation — it is built first**, and the other
versions will be derived from it later (strategy TBD — do not prepare for
them). Freshness comes from Fusion itself: reads are compute services,
writes invalidate them, and every client sees changes in real time — no
manual refresh, no polling loops.

Because this codebase is a comparison reference, code size and readability
are success criteria. Prefer the smallest clear implementation. No
speculative abstractions, no layers that exist "for later."

## Core concepts

- **Session = user.** There is no authentication and no user entity. A
  session corresponds to one browser window. The client generates a GUID on
  first load, keeps it in `sessionStorage` (NOT `localStorage`, NOT a cookie —
  those are shared across tabs, and multiple tabs must behave as multiple
  users for demo purposes), and uses it as its Fusion `Session` id
  (via `ISessionResolver`), so every RPC call carries it implicitly.
- **Display name.** Each session has a display name used as the author
  snapshot on questions it posts. On first load the server assigns a
  generated name (scheme below). The user can change it at any time via an
  inline editor in the header.
- **Room = town hall.** Code says `Room`; UI copy says "town hall."
- **Owner = capability URL, not auth.** Every room is created with a secret
  `OwnerToken`. The **owner URL** (`/room/{id}/own/{token}`) marks the
  visiting session as an owner and immediately redirects to the regular room
  URL, so the token never lingers in the address bar. The room creator's
  session is marked owner automatically at creation. Any number of sessions
  can be owners — sending someone the owner URL delegates moderation. There
  is no way to revoke ownership (out of scope).
- **Owner powers:** start/stop the room, resolve questions (with an optional
  resolution note), delete questions. Owners can also do everything
  participants can, including posting and voting.

### Generated names

Format: `Adjective Animal` — e.g. "Punctual Otter", "Skeptical Marmot",
"Radiant Pelican". Ship two hardcoded word lists (~40 adjectives, ~40
animals; pick fun, family-friendly words), select uniformly at random.
No uniqueness guarantee needed.

## Room lifecycle

```
                 OnStart                    now >= ClosesAt
   Stopped  <------------->  Live  ----------------------->  Ended
  (default)      OnStop                    (terminal)
```

- A room is created **Stopped** and shows "hasn't started yet / paused" copy.
- Owners toggle Stopped <-> Live freely with `OnStart`/`OnStop`.
- `ClosesAt` (creation time + chosen duration) is a hard deadline: once
  `now >= ClosesAt` the room is **Ended** regardless of the toggle, terminally.
  Status is DERIVED at read time from the stored toggle + `ClosesAt`; there
  are no background timers — the computed status invalidates itself when
  `ClosesAt` passes, so the change still propagates instantly.
- Gating:
  - Posting questions, voting, and setting mood require **Live**.
  - Resolving/deleting questions and Start/Stop work while Stopped or Live
    (moderation is allowed during pauses), but not once Ended.
  - Reading everything is always allowed, in every status.
- The home page lists Stopped and Live rooms (with a status badge); Ended
  rooms disappear from the list but their URLs keep working read-only.

## Questions: statuses, resolution, deletion

- A question is **Open** or **Resolved**. Resolved questions move to the
  "Resolved" tab and carry an optional owner-written resolution note
  ("answered live", "see the wiki page X", ...). Resolving again overwrites
  the note; there is no un-resolve.
- **Deletion is hard**: the question, its votes, and its resolution vanish.
  Consequently, per-room question `Index` values are unique and monotonic
  but MAY have gaps after deletions — nothing may assume gaplessness.
- Votes on a question are frozen for ranking purposes once it's resolved
  (voting requires Open status), but the accumulated count is still shown on
  the resolved card.

## Mood tracker

- 5-level scale, rendered as five emoji buttons, always visible on the room
  page while the room is Live: 1 😞, 2 🙁, 3 😐, 4 🙂, 5 😄.
- Clicking any of them at any time sets **your current mood** for this room
  (one value per session per room; clicking another level overwrites; no
  explicit clear). Rejected unless the room is Live.
- **A mood only counts while its session is present** (same 30 s presence TTL
  as the audience count). Someone who closes the tab drops out of the mood
  aggregate within ~30 s. Their stored level is retained and counts again if
  they return.
- Visualization (top of the mood panel): one large programmatically
  generated **SVG face** reflecting the average mood, above a **distribution
  bar chart** (5 vertical bars, one per level, labeled with the emoji and
  the count).

### SVG face (crude v1 — will be polished separately later)

Generate the SVG in code (a small Razor component / C# helper), driven by
`avg` in [1..5]. Let `t = (avg - 1) / 4` in [0..1]. Crude v1 spec:

- viewBox `0 0 200 200`; face = circle r=80 centered (100,100).
- Face fill: linear interpolation between `#8fa8c9` (gloomy blue-gray, t=0)
  and `#ffd54f` (sunny yellow, t=1) in RGB space.
- Eyes: two filled circles r=7 at (72,82) and (128,82).
- Mouth: quadratic Bézier `M 62 132 Q 100 {cy} 138 132` with
  `cy = 132 + (avg - 3) * 19` — frown at avg=1 (cy≈94), straight at 3,
  smile at avg=5 (cy≈170); stroke width 6, round caps, no fill.
- No mood votes yet (or nobody present): gray face `#cfcfcf`, straight
  mouth, and a "no mood signals yet" caption.
- Keep the mapping in one pure function `(double avg) -> string svg` so it
  can be iterated on in isolation later; do not over-engineer beyond the
  above.

## API contract

The contract below is canonical for all phases; other implementations change
attributes and the `Session` type, not shapes. For the Fusion version:

- `Session` is ActualLab.Fusion's `Session`; the client assigns it per
  browser tab (the `sessionStorage` GUID above) via `ISessionResolver`.
- Interfaces live in `TownHall.Contracts`, referenced by both server and
  client.
- The server implements them as Fusion compute services: interfaces extend
  `IComputeService`, reads are `[ComputeMethod]`s, commands are
  `[CommandHandler]`s taking fully serializable command records (per
  CODING_STYLE conventions). Fusion RPC over WebSocket is the transport —
  no hand-written HTTP endpoints or HTTP clients.
- The Blazor client consumes the same interfaces via `fusion.AddClient<T>()`.
- The shapes below are shown without Fusion & serialization attributes;
  the implementation adds them.
- The one exception to "session never in a URL": the owner-claim page URL
  contains the owner TOKEN (not the session). That page exists precisely to
  convert the token into session state and then hide it.

```csharp
namespace TownHall;

// Session = ActualLab.Fusion.Session

// ============================================================ Model

public enum RoomStatus
{
    Stopped = 0,  // default at creation; also the paused state
    Live = 1,
    Ended = 2,    // now >= ClosesAt; terminal
}

public sealed record Room(
    string Id,          // short, URL-friendly, server-generated, e.g. "th-7f3a"
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset ClosesAt,
    RoomStatus Status   // DERIVED at read time (stored toggle + ClosesAt);
                        // never stored; auto-invalidated at ClosesAt
);
// The owner token is intentionally NOT part of Room — it is only ever
// returned by IRooms.GetOwnerToken, and only to owners.

public enum QuestionStatus { Open = 0, Resolved = 1 }

public sealed record Question(
    string RoomId,
    long Index,         // per-room, unique, monotonic; gaps possible after
                        // deletions; the question's id within its room
    string AuthorName,  // snapshot of the poster's name at post time; immutable
    string Text,
    DateTimeOffset PostedAt
);
// Question records are immutable after creation. Mutable facets live behind
// their own reads: vote count -> GetVoteCount, resolution -> GetResolution.
// This split is part of the contract (it matters for invalidation
// granularity in later phases) — keep it even though Phase 1 could cheat.

public sealed record Resolution(
    string Note,        // "" if the owner resolved without a note
    DateTimeOffset ResolvedAt
);

public sealed record ParticipantInfo(string Name);

public sealed record TrendingQuestion(
    string RoomId,
    long QuestionIndex,
    int RecentVoteCount // active votes with CastAt within the trailing 5 min
);

public sealed record RoomStats(
    int OpenQuestionCount,
    int ResolvedQuestionCount,
    long TotalVoteCount,   // active votes across all non-deleted questions
    int AudienceCount      // same number IPresence.GetAudienceCount returns
);

public sealed record MoodSummary(
    ImmutableArray<int> Counts, // length 5; Counts[i] = present sessions at level i+1
    int VoterCount,             // sum of Counts
    double? Average             // null when VoterCount == 0
);

// ============================================================ Services

public interface IParticipants
{
    Task<ParticipantInfo> GetOwn(Session session, CancellationToken ct = default);
    // First call for an unknown session creates it with a generated name.

    Task OnSetName(Participants_SetName command, CancellationToken ct = default);
    // Trimmed length 1..30; rejects otherwise. Does NOT rewrite AuthorName
    // snapshots on already-posted questions.
}

public sealed record Participants_SetName(Session Session, string Name);

public interface IRooms
{
    Task<Room?> Get(Session session, string roomId, CancellationToken ct = default);

    Task<ImmutableArray<string>> ListActiveIds(Session session, CancellationToken ct = default);
    // Stopped + Live rooms (i.e. not Ended), newest first.

    Task<bool> IsOwner(Session session, string roomId, CancellationToken ct = default);
    // Drives owner-only UI affordances. Non-owners simply see false.

    Task<string?> GetOwnerToken(Session session, string roomId, CancellationToken ct = default);
    // The room's owner token — returned ONLY if the session is an owner;
    // null otherwise. Used to display/copy the shareable owner URL.

    Task<Room> OnCreate(Rooms_Create command, CancellationToken ct = default);
    // Title: trimmed 1..80 chars. Duration: one of the values the UI offers;
    // server clamps to [5 min, 24 h]. ClosesAt = server now + Duration.
    // Marks the creating session as an owner. Room starts Stopped.

    Task OnClaimOwnership(Rooms_ClaimOwnership command, CancellationToken ct = default);
    // Validates the token; on match, marks the session as an owner of the
    // room. Idempotent. Rejects on unknown room or wrong token.

    Task OnSetLive(Rooms_SetLive command, CancellationToken ct = default);
    // Owner-only. Live=true => start, Live=false => stop. Idempotent.
    // Rejects if session is not an owner or the room is Ended.
}

public sealed record Rooms_Create(Session Session, string Title, TimeSpan Duration);
public sealed record Rooms_ClaimOwnership(Session Session, string RoomId, string OwnerToken);
public sealed record Rooms_SetLive(Session Session, string RoomId, bool Live);

public interface IQuestions
{
    // ---- Reads
    Task<Question?> Get(Session session, string roomId, long index, CancellationToken ct = default);
    // Returns deleted questions as null, like never-existed ones.

    Task<ImmutableArray<long>> ListOpenIds(Session session, string roomId, CancellationToken ct = default);
    // Open questions, newest first ("Recent" tab).

    Task<ImmutableArray<long>> GetTopOpenIds(Session session, string roomId, int limit, CancellationToken ct = default);
    // Open questions sorted by active vote count desc, ties older-first
    // ("Top" tab).

    Task<ImmutableArray<long>> ListResolvedIds(Session session, string roomId, CancellationToken ct = default);
    // Resolved questions, most recently resolved first ("Resolved" tab).

    Task<Resolution?> GetResolution(Session session, string roomId, long index, CancellationToken ct = default);
    // null for Open questions.

    Task<int> GetVoteCount(Session session, string roomId, long index, CancellationToken ct = default);

    Task<bool> HasOwnVote(Session session, string roomId, long index, CancellationToken ct = default);

    // ---- Participant commands
    Task<Question> OnPost(Questions_Post command, CancellationToken ct = default);
    // Requires room Live. Text: trimmed 1..500 chars.

    Task OnVote(Questions_Vote command, CancellationToken ct = default);
    // Requires room Live and question Open. Sets (Value=true) or clears
    // (Value=false) this session's vote. Idempotent: re-setting an existing
    // vote refreshes its CastAt; clearing a non-existent vote is a no-op.
    // One vote per (session, question). Voting for your own question is
    // allowed. Owners vote like everyone else.

    // ---- Owner commands (reject unless session is an owner of the room;
    //      allowed while Stopped or Live, rejected once Ended)
    Task OnResolve(Questions_Resolve command, CancellationToken ct = default);
    // Marks Open -> Resolved with an optional note (trimmed 0..500 chars).
    // Resolving an already-Resolved question overwrites the note. No un-resolve.

    Task OnDelete(Questions_Delete command, CancellationToken ct = default);
    // Hard delete: question, its votes, and its resolution disappear.
    // Idempotent (deleting a missing question is a no-op).
}

public sealed record Questions_Post(Session Session, string RoomId, string Text);
public sealed record Questions_Vote(Session Session, string RoomId, long QuestionIndex, bool Value);
public sealed record Questions_Resolve(Session Session, string RoomId, long QuestionIndex, string Note);
public sealed record Questions_Delete(Session Session, string RoomId, long QuestionIndex);

public interface IRoomStats
{
    Task<ImmutableArray<TrendingQuestion>> GetTrending(Session session, string roomId, int limit, CancellationToken ct = default);
    // OPEN questions ranked by RecentVoteCount desc (ties: higher total votes
    // first), entries with RecentVoteCount == 0 excluded. "Recent" = active
    // votes whose CastAt is within the trailing 5-minute window at read time.

    Task<RoomStats> GetStats(Session session, string roomId, CancellationToken ct = default);
}

public interface IPresence
{
    Task<int> GetAudienceCount(Session session, string roomId, CancellationToken ct = default);
    // Sessions whose last OnWatch for this room is <= 30 s old.

    Task OnWatch(Presence_Watch command, CancellationToken ct = default);
    // Heartbeat. Client sends it every 15 s while a room page is open,
    // and once immediately on opening the page — regardless of room status.
}

public sealed record Presence_Watch(Session Session, string RoomId);

public interface IMood
{
    Task<MoodSummary> GetSummary(Session session, string roomId, CancellationToken ct = default);
    // Aggregates ONLY presently-present sessions (presence TTL above) that
    // have a stored mood for this room.

    Task<int?> GetOwn(Session session, string roomId, CancellationToken ct = default);
    // This session's stored level (1..5) or null — drives button highlight.

    Task OnSetMood(Mood_Set command, CancellationToken ct = default);
    // Requires room Live. Level in 1..5. Overwrites the previous value.
}

public sealed record Mood_Set(Session Session, string RoomId, int Level);
```

Command failures (validation, not-owner, wrong lifecycle state, missing
entities) are thrown as exceptions server-side; Fusion RPC re-throws them
on the client, where they surface as a toast/snackbar message.

## Storage

Sqlite via EF Core — the setup is already in place: `AppDbContext`
(`DbContextBase`), Fusion's EF operations framework (operation/event logs +
file-system log watcher), `EnsureCreated` on start, no migrations. Add
simple tables for domain records (rooms, owners, questions, votes,
resolutions, moods); ordering/aggregation is computed on read — Fusion's
computed caching makes repeated reads cheap, and writes invalidate exactly
the affected compute methods. Purely ephemeral state (presence heartbeats)
may live in an in-memory singleton instead of the DB — keep it simple.
Seed nothing (empty lobby on first run is correct); optionally add a
`--demo-seed` flag that creates one Live room with a handful of questions,
votes, and moods for screenshots.

Must be retained per record: vote `CastAt` per active (session, question)
vote (trending depends on it); owner session ids per room; per-room stored
lifecycle toggle; mood level per (session, room); last heartbeat per
(session, room).

## UI (Blazor)

Stack: ASP.NET Core host + Blazor with WASM and Server render modes
(Fusion's render-mode infrastructure; Auto by default), .NET 10, MudBlazor
for components; project structure follows the TodoApp sample from
Fusion Samples. It should look tidy in a side-by-side multi-window demo,
nothing more.

### Header (all pages)

- App name "TownHall" (links home).
- Session name: shown as text, click to edit inline (input + save/cancel),
  backed by `IParticipants`.
- No refresh controls: Fusion keeps every view current in real time —
  that's the signature element of this version.

### Home page `/`

- "Create a town hall" form: Title input + Duration select (15 min, 1 h,
  4 h, 8 h) + Create button.
- **Post-create panel** (shown once, right after creation, instead of
  immediately navigating away): "Your town hall is ready" with BOTH links,
  each with a copy button —
  - Participant link: `/room/{id}` — "share this with your audience";
  - Owner link: `/room/{id}/own/{token}` — "keep this private; share it only
    with co-moderators", visually de-emphasized/collapsed by default;
  - and an "Enter your town hall" button → `/room/{id}`.
- List of active town halls: Title, status badge (Stopped/Live), time
  remaining (coarse: "42 min left"), open question count, audience count.
  Each row links to `/room/{id}`. Empty state: friendly one-liner.

### Owner-claim page `/room/{id}/own/{token}`

- On load: sends `Rooms_ClaimOwnership`, then IMMEDIATELY navigates to
  `/room/{id}` with `replace: true` (no history entry — the token must not
  be reachable via Back). On failure (bad token/room): error message with a
  link home, no redirect loop.

### Room page `/room/{id}`

- Title + status. Stopped: "hasn't started yet — hold tight" banner (or
  "paused" if it has questions already; a single neutral phrasing like
  "This town hall is paused" covering both is fine). Live: countdown to
  ClosesAt ticking client-side every second (pure client-side rendering of
  already-known ClosesAt — this does not violate the no-real-time rule).
  Ended: "This town hall has ended" banner. Posting/voting/mood controls are
  disabled unless Live (server enforces regardless).
- Audience count ("N here") and stats line (open/resolved counts, total
  votes) from `IRoomStats.GetStats`.
- **Owner bar** (rendered only when `IsOwner` is true): a `Start`/`Stop`
  toggle button, and a collapsed "Owner link" disclosure that reveals the
  owner URL (from `GetOwnerToken`) with a copy button.
- Question composer: textarea + Post button, 500-char counter. Visible but
  disabled when not Live.
- Tabs: **Top** (`GetTopOpenIds`), **Recent** (`ListOpenIds`), **Resolved**
  (`ListResolvedIds`, with count in the tab label).
  - Open-question card: text, author name, relative age, vote count, vote
    toggle button reflecting `HasOwnVote` (filled = voted). For owners, two
    extra small actions: `Resolve` (opens a one-field inline form/dialog for
    the optional note, with Resolve/Cancel) and `Delete` (with a confirm).
  - Resolved-question card: same base info, vote count frozen, plus the
    resolution note (if any) and "resolved Xm ago". Owners still see
    `Delete` and may re-`Resolve` to edit the note.
- **Mood panel** (sidebar on wide screens, section on narrow, above
  Trending):
  - The big SVG face (spec above), sized ~160 px.
  - The distribution chart: 5 vertical bars labeled 😞🙁😐🙂😄 with counts;
    bar heights relative to the max count; all-zero state renders flat
    baseline. Plain divs or inline SVG — no chart library.
  - The 5 emoji buttons; the session's current level (from `GetOwn`)
    highlighted. Disabled unless Live.
- Trending panel below the mood panel: top 5 from `GetTrending`, each
  showing "+N in last 5 min" and linking/scrolling to the question.
- Unknown room id → "Not found" message with a link home.

### Update semantics (exact)

- Every view renders from Fusion computed state (`ComputedStateComponent`);
  when the server invalidates a dependency, the state recomputes and the
  UI re-renders automatically (default update delay ~0.25 s).
- Commands go through the same service interfaces; no post-command refresh
  logic is needed — invalidation covers this window and everyone else's.
- Presence heartbeat runs on its own 15 s timer whenever a room page is
  open, plus once on page open.

## Project layout

```
TownHall.slnx
src/
  TownHall.Contracts/   # model records, command records, service interfaces
  TownHall.Host/        # ASP.NET Core host: Fusion server + DB + service
                        # implementations, name generator; serves the UI
  TownHall.UI/          # Blazor UI (WASM + Server render modes): pages,
                        # header, SVG face component
```

One `dotnet run` (on TownHall.Host) starts everything. README with: what
this is (two paragraphs, including the multi-phase comparison purpose), how
to run, and a "try it" script: open two browser windows, create a room in
one, start it, join from the other, post/vote/set moods, watch every window
update in real time.

## Non-goals (Phase 1)

- No authentication or accounts. The owner token is a capability URL, not a
  login; no revocation, no owner lists in the UI.
- No moderation beyond resolve/delete (no bans, no rate limiting, no
  profanity filtering).
- No migrations — `EnsureCreated` is enough; deleting the Sqlite file on
  schema changes is fine.
- No transports beside Fusion RPC (no SignalR, no custom WebSockets/SSE);
  no server-side background timers (room Ended state, presence expiry, and
  mood-aggregate membership are evaluated lazily at read time, with
  auto-invalidation where a deadline is known).
- No pagination (rooms and questions are demo-scale; unbounded lists fine).
- No localization, no mobile-first polish (must merely not break on a
  phone).
- No unit test suite. One smoke test project is acceptable if it stays
  under ~100 lines; otherwise skip.
- SVG face stays at the crude v1 spec — no eyebrows, animation, or easing;
  it will be polished in a separate effort.

## Acceptance walkthrough

1. `dotnet run` → open `http://localhost:xxxx` in two separate browser
   windows (W1, W2). Each shows a different generated name in the header.
2. W1 creates "All-hands Q&A" (1 h) → sees the post-create panel with both
   links; copies the participant link; enters the room. The room is Stopped;
   composer, vote, and mood controls are disabled. W1 sees the Owner bar.
3. W2 opens the participant link → sees the Stopped banner, no Owner bar.
   Audience shows 2 within ~15 s in both windows.
4. W1 presses Start → room goes Live. W2 sees it within a second — no
   refresh, that's Fusion at work.
5. W2 posts a question → it appears in both windows in real time.
6. Voting: W1 votes the question up (button fills, count +1); clicking again
   clears it. Top tab ordering follows vote counts; Trending shows
   recently-voted questions and drops them ~5 min after voting stops.
7. Moods: W1 clicks 😄, W2 clicks 🙁 → face and distribution reflect
   avg=3 (two bars, straight-ish mouth) in both windows immediately.
   W2 closes the tab → within ~30 s, VoterCount drops to 1 and the face
   turns happy.
8. Owner moderation: W1 resolves W2's question with note "answered live" →
   it moves to the Resolved tab with the note; votes frozen. W1 deletes
   another question → it disappears everywhere; its index is never reused.
   In W2 (non-owner), resolve/delete controls never render, and forged
   POSTs to owner endpoints return 403.
9. Owner delegation: open W3, paste the owner URL → it redirects to the
   plain room URL (token gone from the address bar, no Back-button trace),
   and W3 now sees the Owner bar.
10. W1 presses Stop → controls disable everywhere immediately; W1
    resolves one more question while Stopped (allowed), presses Start again.
11. Create a room with a short duration (temporarily allow 5 min via the
    clamp, or edit the select for testing) → at the deadline, the room shows
    as Ended everywhere, all write controls disabled including owner
    Start/Stop, and it disappears from the home page list.
12. Rename in W1 header → W1's subsequent posts carry the new name; earlier
    posts keep the old snapshot.
13. Closing and reopening a tab yields a fresh session (new name) —
    expected, since sessions live in `sessionStorage`.
