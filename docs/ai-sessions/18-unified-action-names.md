# Session 18

**User:** The /rebase-branches skill should be in the shared set — I shouldn't see it in the branch diff.

**Fable5:** It already is (`.claude/commands` is in `SHARED_BASE`); verified the current `main`↔`signalr` diff contains zero shared-base files — only `src/`, `tests/`, and the two branch-owned `Directory.*.props` remain.

**User:** On names like `Delete` vs `OnDelete` — let's use the same names on both branches, e.g. `Delete`. And why is there no `ListTopOpen` in signalr?

**Fable5:** Renamed all of `main`'s `On`-prefixed command handlers to the plain action names (`OnPost`→`Post`, `OnCreate`→`Create`, ..., in interfaces, services, UI, and test names), updated CODING_STYLE.md (command handlers now use no `On` prefix in this repo — a deliberate deviation from the broader ActualLab convention) and the parity docs; 68/68 tests pass. `ListTopOpen` is a genuine framework delta: signalr's `ListOpen` stream already carries every question's live vote count, so the UI sorts the "Top" tab client-side; on `main` reads return id lists, so the server must provide the vote-sorted list.

**User:** For `IRooms` — think about how to make the two branches' versions more similar to each other.

**Fable5:** With writes now identical, aligned signalr's read names to main's vocabulary: `RoomView(roomId)`→`Get(roomId)` (the per-room page read, pairing with main's `Get`), `Lobby`→`GetLobby`, `RoomCard`→`GetCard`, and reordered reads to main's order; the remaining delta (main's `IsOwner` vs signalr's bundled views) is inherent to fine-grained compute reads vs streamed view models. Also proposed (not implemented): sharing the view-model records (`RoomView`/`LobbyView`/`RoomCard`) with main's UI components, which currently define private near-identical `Model` records.

**User:** Is `GetOwnUserId` necessary, given there is `GetOwn`?

**Fable5:** No — removed it from `main`'s `IUsers` (UI sign-in checks and tests now use `GetOwn`; services keep private `GetOwnUserId` helpers over the backend), which also makes `IUsers` converge across branches; then re-ran the rebase procedure and force-pushed `signalr`.
