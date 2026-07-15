---
allowed-tools: Bash, Read, Edit, Write, Glob, Grep
description: Produce/update a per-version-branch report comparing each framework stack to the Fusion (main) branch across volume, cleanliness, robustness, and performance
---

# /compare-branches

For every **framework version-branch**, produce (or update) a report that compares
it to the reference **Fusion `main`** branch and highlights the **biggest wins for
each side** — where one stack is *cleaner*, *smaller*, *more robust*, or *cheaper*
than the other. The reports live on `main` and are part of the **shared base**, so
every branch carries the same set of reports.

## Branches this command compares

The **same set** as `/rebase-branches` — compare every version-branch to `main`:

| Branch    | Stack                         |
|-----------|-------------------------------|
| `signalr` | .NET + SignalR (no Fusion)    |

When `/rebase-branches` gains a branch, add it here too.

## Where the reports live (shared base)

- One report per version-branch: **`docs/comparisons/<branch>.md`**
  (e.g. `docs/comparisons/signalr.md`).
- These files are **shared base**: they must be identical on every branch, so add
  `docs/comparisons/` to the shared-base set in `/rebase-branches`. They are
  written on `main` and mirrored outward by the next `/rebase-branches` run.

## Preconditions

- The version-branches are already rebased onto the current `main` (run
  `/rebase-branches` first), so `git diff main <branch>` is a **pure framework
  delta** and the numbers mean something.
- **Performance lens only:** the `server-loop` must be running **with `--aspire`**
  (Aspire dashboard on `:18888`, app on `:5136`), because the perf numbers come
  from live browser use paired with Aspire/Postgres observation. Check with
  `/server-loop`. If it is **not** running with `--aspire`:
  - On the **host OS** (`AC_OS` unset or `Windows`/`macOS`/`Linux`): start it
    yourself — `pwsh -NoProfile -File server-loop.ps1 --aspire` (background).
  - In **Docker/WSL** (`AC_OS` = `Linux in Docker` / `Linux on WSL`): you can't
    start the host loop — **ask the user to start it** (`server-loop.cmd --aspire`),
    or run the branch's host yourself on a spare port (`dotnet run` the AppHost /
    Host) and read Postgres directly.
- Ideally observe **each** branch live (its own `--aspire` run) so both sides are
  measured. Where only one branch can run at a time, measure it live and derive the
  other from code — and **mark every number as measured or extrapolated** in the
  report.

## Report structure — `docs/comparisons/<branch>.md`

Start with a header pinning what the report was built against, so a later run can
tell whether anything material changed:

```
# <Branch> vs Fusion — comparison
Built against: main <main-short-sha> · <branch> <branch-short-sha> · <YYYY-MM-DD>
Framework delta: <N> files, +<adds> / -<dels> lines (git diff --stat main <branch>)
```

Then four lenses. For each, lead with a one-line **verdict** (who wins and by how
much), then the evidence. Keep it concrete — cite files and real numbers.

### 1. Volume (numerical)
The objective slice. From `git diff --numstat main <branch>` and
`git diff --stat main <branch>`:
- Total added/removed lines, and the **biggest deltas by area** (contracts, host
  services, UI components, host wiring, tests).
- For each big delta, say **which stack has more code and why** — e.g. "signalr UI
  components are ~X% lighter because `StateComponent<T>` drops the per-read
  `ComputeState`/`Session` plumbing"; "Fusion carries more command boilerplate:
  `[ComputeMethod]`/`[CommandHandler]` + `ISessionCommand<Unit>` records +
  `Invalidation.IsActive` branches".
- A small table of the top ~8 files by absolute line delta.

### 2. Cleanliness
The subjective-but-defensible slice: where the code **reads** cleaner/simpler in
each stack, with concrete before/after snippets or file references. Typical axes:
per-method identity plumbing, invalidation vs. notify ergonomics, view-model
folding, reconnect handling, render-mode wiring.

### 3. Robustness
Reliability & scalability deltas — where one stack is **more correct or scales
further**. Typical axes: multi-host correctness (Fusion's operation-log +
reprocessor vs. signalr's in-process `ChangeTracker`/`PresenceStore` = single-host),
convergence guarantees, committed-but-errored writes, cache-consistency under load,
reconnect/replay. Be honest about where each is weaker.

### 4. Performance
Per-API-method cost, **backed by live browser use + Aspire/Postgres observation**
(not a load test — estimates you can defend). Method:
- Drive the app in the browser (open a room, post, vote, set mood, reconnect) and
  watch the **Aspire dashboard** (`ActualLab.Rpc`/`Npgsql`/`TownHall.Db` meters,
  traces) and the **`Executed DbCommand`** log / `_Operations` table.
- For each representative read and command, record **how many DB queries it costs**
  and when it re-runs, then explain the structural reason:
  - Fusion: compute methods **cache**; a read hits the DB once, then only on
    invalidation; commands append to the operation log.
  - SignalR: streams **re-read on every notify** (no compute cache); commands
    `SaveAndNotify`. Cheaper write path, more read re-execution.
- Give a rough **queries-per-action** estimate per stack and call out the biggest
  gaps (e.g. "an idle occupied room costs ~0 queries on Fusion after warm-up but
  ~1 per notify per open stream on signalr").

Close with a short **Bottom line** paragraph: the 2–3 headline wins for each stack.

## Updating a report

Reports are meant to be refreshed, not regenerated from scratch each time:
- Re-read the pinned `Built against:` hashes. If `git diff --stat <pinned-branch-sha> <branch>`
  (and the same for main) shows only **small** changes in the areas a lens covers,
  you may **keep that lens's numbers** and just bump the hashes — in particular the
  expensive **Performance** lens can be skipped when neither side's runtime surface
  moved materially. Note in the report which lenses were re-measured vs. carried over.
- Always refresh the **Volume** lens (it's cheap: pure `git diff`).
- If a lens is intentionally carried over, say so explicitly so the reader knows the
  number predates the current hashes.

## Finishing

- Write/update every branch's `docs/comparisons/<branch>.md` on `main`, commit them.
- They're shared base, so run `/rebase-branches` afterward (or note it) to mirror the
  reports onto the version-branches.
- Report per branch: built-against hashes, which lenses were (re)measured, and the
  headline verdict.
