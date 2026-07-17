# Session 19

**User:** Push.

**Fable5:** Pushed `main` (`3ab1b74..453eb45`: invariants docs + unified action names).

**User:** I still see rebase-branches.md in the PR diff.

**Fable5:** The refs were clean (merge-base = main tip, zero shared-base files in `git diff main signalr`); GitHub's PR #1 had a stale cached base (`3ab1b74`) because signalr was force-pushed before main. Fixing it needed a close/reopen, which the session's `gh` token couldn't do (403).

**User:** How do I auth GitHub (for gh)? Can you find this token in github.com/settings/personal-access-tokens in chrome1?

**Fable5:** `gh` runs on a fine-grained PAT from `GH_TOKEN`; drove the user's Chrome via the chrome-devtools MCP bridge and identified it as **GITHUB_SHARED** — resource owner **@Actual-Chat**, so no permission edit can ever grant it write access to ActualLab repos (fine-grained PATs are locked to their resource owner). Offered close/reopen via the browser instead.

**User:** OK, do it.

**Fable5:** Clicked Close → Reopen on PR #1 through the user's Chrome; GitHub recomputed the merge base to `453eb45` — the PR diff dropped to 91 files with only `Directory.*.props` outside `src/`/`tests/`.

**User:** /compare-branches — and /rebase-branches after the report is updated.

**Fable5:** Refreshed `docs/comparisons/signalr.md` against main `453eb45` · signalr `8416524`: re-measured Volume (91 files, +2300/−2009; contracts −218, UI components −102 for signalr; UI infra +434, wiring +117, ~800 lines of hand-written reactive glue for Fusion) and Cleanliness (fixed the now-wrong "no command records" claim — both branches keep command records by design; names now unified), updated Robustness (36 authored/68 executed Fusion vs 37/37 signalr), carried Performance over from the 2026-07-15 measured run (hot paths unchanged); then re-ran the rebase procedure and force-pushed `signalr`.
