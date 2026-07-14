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

# AI session logs

Every conversation with an AI agent in this repo must be logged to the
current session file in [docs/ai-sessions/](docs/ai-sessions/). Session files are named
`NN-description.md` (e.g. `01-init.md`); the current one is the file
with the highest `NN`. Start a new file (next `NN`, short kebab-case
description) when a new session or topic begins.

For every exchange, append:

- The user's message as `<User> ...` — but clean it up first: fix
  grammar, remove filler words from voice-transcribed text ("uh",
  "so...", "kind of", etc.), and correct mistranscribed or imprecise
  terminology. Phrase it as you understood it; the result must be
  clear and readable.
- A very short summary of your response (ideally one sentence) as
  `<YourModelName> ...` (e.g. `<Fable5> ...`).
