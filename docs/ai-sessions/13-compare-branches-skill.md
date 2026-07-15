# Session 13 (main branch)

**User:** Push main (rebase my changes onto origin first). Also create a
`/compare-branches` skill that uses the same branch set as `/rebase-branches` and
produces, on `main` (kept in the shared set), a report per version-branch comparing
it to the Fusion branch — the biggest wins for each stack. Four lenses: volume
(count added/removed lines, biggest deltas — e.g. lighter UI here, more command/
attribute boilerplate there), cleanliness, robustness (reliability/scalability), and
performance (play with the app in the browser + watch Postgres via Aspire, extrapolate
per-API-method cost — assume the server-loop runs with `--aspire`; if not, start it on
the host or ask me). Reports should be updatable and pin commit hashes so a later run
can tell if the deltas are big enough to re-run the browser/Aspire perf pass. Sequence:
commit the skill to main, pull+rebase, run `/rebase-branches` again, then run
`/compare-branches`.

**Opus4.8:** Added `.claude/commands/compare-branches.md` documenting the four-lens
methodology (volume/cleanliness/robustness/performance), the `docs/comparisons/<branch>.md`
report location, the pinned-hashes + selective-refresh update policy, and the
server-loop-`--aspire` precondition (start on host / ask in Docker). Added
`docs/comparisons/` to the `/rebase-branches` shared base so the reports mirror onto
every branch. Then pushed main and re-ran `/rebase-branches` before generating the
first report.
