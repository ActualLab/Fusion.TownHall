# Session 22

**User:** Update Fusion to the latest release in every sibling project that
depends on it, working on them in parallel. Once updated, commit and push
every repo where Fusion was bumped.

**Opus5:** Bumped `<ActualLabFusionVersion>` in `Directory.Packages.props` from
14.3.4 to 14.3.8 — a one-line diff feeding all six `ActualLab.*` package
references, with no code changes required since the release carries no breaking
changes and no API surface change. 14.3.8 contains a single shipping fix: a
`DbContext` returned to EF's pool with a stale connection open count no longer
breaks the commands that later rent it, a failure that previously surfaced as
*"The instance of DbConnection is currently in use"* on unrelated write
commands. `dotnet build TownHall.slnx` succeeds with 0 errors across all 9
projects; the 2 warnings are the pre-existing `IL2111` trimmer warnings from
Blazor's generated `App_razor.g.cs`, not Fusion. The .NET 10 pinning is
untouched — `Directory.Build.props` still targets `net10.0`. Verified against
`project.assets.json` that every `ActualLab.*` reference genuinely resolves to
14.3.8 rather than falling back to the cached 14.3.4.

**Note:** the build ran under SDK `11.0.100-preview.6.26359.118` even though
`global.json` pins `10.0.100` with `allowPrerelease: false`, producing nine
`NETSDK1057` preview notices. Output still targets `net10.0` and builds cleanly.
This predates the bump and was left alone, but it's worth a separate look.
