# Project-specific Rules for ActualLab.Fusion.TownHall

**YOU MUST READ [CODING_STYLE.md](CODING_STYLE.md) before writing or
modifying any C# code.** It's not optional. This project
**deviates from standard .NET conventions** on several points (notably:
no `Async` suffix on async methods; no XML docs on members; mixed brace
style). Default instincts from elsewhere will produce code that gets
rejected. If you haven't opened that file yet in this session, stop and
read it now.

**You MUST NOT write a single comment, docstring, or XML doc** without
first reading [CODING_STYLE.md → "Regular comments, docstrings, XML
documentation comments"](CODING_STYLE.md#regular-comments-docstrings-xml-documentation-comments).

# Version branches

This app is implemented several times over — once per real-time stack — to
compare the code each framework produces. **Each version lives on its own
branch, named after the framework:**

- **`main`** — the .NET + [Fusion](https://github.com/ActualLab/Fusion)
  version. Implemented first; every other version is derived from it.
- **`signalr`** — the .NET + SignalR version, using **no Fusion at all**.
  The next one to build.

Branches for other stacks (plain .NET, and possibly TypeScript and Elixir
stacks) will be added to this list as they're built.

**Feature-parity tags.** A name like `<branch>-v1` (e.g. `main-v1`,
`signalr-v1`) marks the point where a branch reaches a given feature set. The
same `-v1` across all framework branches matches feature-wise, so those points
are directly comparable. These are **tags, not branches** — fixed pointers we
never move.

# Database & migrations

The app is PostgreSQL-only. The EF Core model (entities + `AppDbContext`)
lives in `src/TownHall.Db`; migrations live in `src/TownHall.Db/Migrations`
and are applied by `TownHall.Host` on startup.

**Any change to the DB model MUST come with a matching migration** in the
same commit:

```bash
dotnet ef migrations add <ChangeName> --project src/TownHall.Db
```

Local Postgres comes from the root `docker-compose.yml`
(`docker compose up -d`); the app and the tests expect it on
`localhost:5432` with `postgres`/`postgres` credentials.

# AI session logs

Every conversation with an AI agent in this repo must be logged to the
current session file in [docs/ai-sessions/](docs/ai-sessions/). Session files are named
`NN-description.md` (e.g. `01-init.md`); the current one is the file
with the highest `NN`.

**On a non-`main` branch, log into `docs/ai-sessions/<branch>/` instead**
(e.g. `docs/ai-sessions/signalr/01-init.md`). Each framework branch keeps its
session logs in its own subfolder, with `NN` numbering restarting there, so
merges from `main` never conflict over session files.

**One session file per commit.** A session file accumulates exchanges
until they are committed:

- When you start and the latest session file has no uncommitted changes
  (per `git status`), the previous session is done — create a new file
  with the next `NN`, initially named just `NN.md` (e.g. `03.md`).
- Right before committing, rename it to add a short kebab-case
  description suffix reflecting what was done (e.g. `03.md` →
  `03-basic-app.md`).
- If the latest session file has uncommitted changes, the commit hasn't
  happened yet — keep appending to that file.
- When in doubt about committing, or about appending vs. starting a new
  session file, ask the user what to do with the session log.

For every exchange, append:

- The user's message as `**User:**` ... — but clean it up first: fix
  grammar, remove filler words from voice-transcribed text ("uh",
  "so...", "kind of", etc.), and correct mistranscribed or imprecise
  terminology. Phrase it as you understood it; the result must be
  clear and readable.
- A very short summary of your response (ideally one sentence) as
  `**YourModelName:**` ... (e.g. `**Fable5:**` ...).

Use bold `**Name:**` prefixes — never `<Name>` angle-bracket markers, which
Markdown renders as (empty) HTML tags and drops. Session-level asides use a
`**Note:**` ... line the same way.
