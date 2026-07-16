---
allowed-tools: Bash
description: Rebase the framework version-branches onto main (files identical except the shared base, which is synced to main), then force-push them
---

# /rebase-branches

Re-base every **framework version-branch** onto the current `main` so that
`git diff main <branch>` shows **only the real-time-stack delta** — nothing else.
Each branch's own files come out **byte-identical** to what they were before; the
one exception is the **shared base** (agent/convention files), which is
synced to `main` so it's identical on every branch. Then force-push each branch.

## Background — why this exists

This app is implemented once per real-time stack to compare the code each
framework produces (see the "Version branches" section of `AGENTS.md`). `main`
is the reference **.NET + Fusion** version; every other stack lives on its own
branch (`signalr`, and more later). **These branches are never merged into
`main`.** When a shared, framework-agnostic feature is added to `main` (e.g.
backported from a version-branch), each version-branch must be re-based on top of
the new `main` so the branch-to-branch diff stays a *pure framework delta*.

## Branches this command manages

Rebase **every** branch listed here (skip one only if it is already based
directly on `main` **and** its shared base already matches `main`):

| Branch    | Stack                         |
|-----------|-------------------------------|
| `signalr` | .NET + SignalR (no Fusion)    |

When a new version-branch is created, add a row here so this command keeps it
in sync too.

## Shared base — identical on every branch

Some files are **not** part of any stack's implementation; they are project-wide
agent/convention files that must read the same on every branch. They are
maintained on `main` and mirrored outward. So the rule is **not** "retain
everything from the branch" — it's "retain everything **except** the shared base,
which must equal `main`". The shared base for this repo:

- `AGENTS.md`, `CLAUDE.md` — generated agent instructions (byte-identical across
  branches by design)
- `AGENTS-Source.md` (and `AGENTS-Suffix.md` if committed) — their source
- `CODING_STYLE.md` — project coding conventions
- `.claude/commands/` — shared slash commands (including this one)
- `docs/comparisons/` — the per-branch comparison reports written by
  `/compare-branches` (each branch carries the whole set)
- `docs/ai-sessions/` — **all** AI conversation logs (the top-level `main`/Fusion
  logs **and** every per-branch subfolder). These describe most of the shared
  architecture, so they must be present and identical on every branch — otherwise
  `git diff main <branch>` would show main's logs as "removed", which is noise, not
  a framework delta. `main` is the **canonical store**: a branch writes its own new
  logs under `docs/ai-sessions/<branch>/`, and those are **promoted to `main`**
  (copied into `main` and committed there) *before* rebasing, so the shared sync
  then mirrors the full set back and the branch delta shows **no logs at all**.

Everything else — `src/`, `tests/`, `README.md`, the rest of `docs/`, build/deploy
files — is **branch-owned** and kept as-is.
Extend the shared-base list above if you add another file that must be
branch-agnostic; never add a file that legitimately differs per stack.

Define the set once so the steps below can reuse it:

```bash
SHARED_BASE=(AGENTS.md CLAUDE.md AGENTS-Source.md AGENTS-Suffix.md CODING_STYLE.md .claude/commands docs/comparisons docs/ai-sessions)
# Keep only the paths that actually exist on main
SHARED_BASE=($(for p in "${SHARED_BASE[@]}"; do git cat-file -e "main:$p" 2>/dev/null && echo "$p"; done))
```

## Preconditions

- `main` already holds the intended commits (commit any backport/shared-base
  change to `main` **before** running this — the rebase needs `main` to be a
  real commit, and the sync copies from it).
- **Promote each branch's new AI logs to `main` first.** For every branch you're
  about to rebase, copy its new `docs/ai-sessions/<branch>/` files into `main` and
  commit them there (`git checkout <branch> -- docs/ai-sessions/<branch>` while on
  `main`, then commit). This makes `main` a superset of every branch's logs, so the
  shared-base sync below leaves the branch's `docs/ai-sessions/` byte-identical to
  `main` (zero log delta). Skip only if the branch added no new logs.
- The working tree is clean (`git status` empty). Stash or commit first.

## Procedure — per branch

The goal is "the branch's exact tree, with the shared base swapped to `main`'s,
re-parented onto `main`". Syncing the shared base **first** (as its own commit)
makes the *expected end result* explicit and checkable; the rebase then just
re-parents that exact tree. Squashing to a single commit is expected.

For each `$B` in the table (run from the repo root, with `SHARED_BASE` set):

```bash
ORIG_TIP=$(git rev-parse "$B")            # for the force-with-lease and diff proof

# 1. Sync the shared base main -> $B (its own commit), so $B == main for those files
git checkout "$B"
git checkout main -- "${SHARED_BASE[@]}"
git commit -m "Sync shared base with main" || echo "(shared base already in sync)"

# 2. This is now the EXPECTED end-result tree: branch content + main's shared base
EXPECTED_TREE=$(git rev-parse "$B^{tree}")

# 3. Re-parent that exact tree onto main (single squashed commit)
git reset --soft main                     # HEAD -> main; index+worktree still = EXPECTED_TREE
git commit -m "$B port, rebased onto main"

# 4. PROVE the result: tree equals the expected tree, and the shared base equals main
NEW_TREE=$(git rev-parse "$B^{tree}")
[ "$EXPECTED_TREE" = "$NEW_TREE" ] || { echo "TREE MISMATCH for $B"; exit 1; }
git diff --quiet main "$B" -- "${SHARED_BASE[@]}" || { echo "SHARED BASE != main for $B"; exit 1; }
echo "$B: tree $NEW_TREE == expected ✅  shared base == main ✅  parent $(git rev-parse --short main)"
```

`git reset --soft main` re-parents onto `main` without touching the index or
working tree, so the new commit's tree is exactly the post-sync branch tree — that
is what makes the result reproducible **and** resolves every non-shared conflict in
favor of the branch (there is no merge; the branch's content simply wins). Step 4
is the required proof: the rebased tree equals the expected tree **and** the shared
base matches `main`. **Do not force-push a branch whose checks did not pass.**

## Force-push — required, once each branch is proven

After a branch passes step 4, force-push it (its history was rewritten):

```bash
git push --force-with-lease="$B:$ORIG_TIP" origin "$B"
```

`--force-with-lease` against the recorded old tip aborts if someone else moved the
remote branch meanwhile — safer than a bare `--force`. Do this for **every**
branch this command rebased, while this command runs.

## Notes

- **`main` is not pushed by this command.** Pushing `main` may trigger a
  production deploy (the deploy timer polls `origin/main`); leave that to the
  user unless they ask. Force-pushing a version-branch carries the new `main`
  commits to the remote only as that branch's ancestors, which does not move
  `origin/main`.
- Finish by restoring the working tree to `main` (`git checkout main`) and
  reporting, per branch: old tip → new tip, tree-matches-expected ✅, shared base
  == main ✅, and pushed.
- Feature-parity tags (`<branch>-v1`) are fixed pointers — never moved here.
