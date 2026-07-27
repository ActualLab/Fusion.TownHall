# Session 21

**User:** Fusion's own `CODING_STYLE.md` just gained a `#### Method order within
a section` subsection under `### Member Ordering` (Fusion commit `42a035fed`).
Read it there verbatim, then replicate the same rule here — adapting heading
levels, tone and line width to this document, and swapping the Fusion-internal
example for something meaningful in this repo if the original doesn't read well
here. Then commit the pending Fusion 14.1.73 upgrade separately from the doc
change and push to `main`; build `TownHall.slnx` first.

**Opus5:** This repo's `### Member Ordering` section is a near-copy of Fusion's,
so the subsection went in at the identical spot — right after item 12 of the
member list, before "For typical RPC API (interface)" — with prose reflowed to
this file's ~76-char width. The example was swapped: Fusion quotes
`ConsolidatingComputed<T>` / `AreOutputsEqual`, which means nothing here, so it
now quotes `MoodFaceSvg` (`Render` first, the pure `LerpColor` helper last),
which demonstrates the same caller-above-callee and utilities-last points with
real code from `src/TownHall.UI`. The pending upgrade was committed separately:
`ActualLabFusionVersion` 14.1.47 → 14.1.73 plus the removal of the
`CodeKeeper.Keep<UnitMessagePackFormatter>()` guard from `TownHall.UI/Program.cs`
— `ActualLab.Core` retains that formatter itself as of 14.1.73, so the local
workaround (and its two usings) is dead weight. `dotnet build TownHall.slnx`
succeeds with 0 warnings and 0 errors.
