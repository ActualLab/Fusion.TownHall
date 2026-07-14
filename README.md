# TownHall

TownHall is a live audience Q&A app ("Slido-lite"): anyone can create a town
hall (a room) and becomes its owner; participants post questions and upvote
others' questions; the question list sorts by votes, and a trending panel
highlights questions gaining votes right now. Owners moderate — resolve or
delete questions, start/stop the session. Participants also continuously
signal their mood on a 5-emoji scale, visualized as a big generated SVG face
plus a distribution chart.

The app exists to compare real-time frameworks: the same app will be
implemented several times (plain .NET, .NET + [Fusion](https://github.com/ActualLab/Fusion),
.NET + SignalR, possibly TypeScript and Elixir stacks) to measure the code
delta each framework produces. It's a Blazor app (WebAssembly + Server-Side
Blazor) similar to the TodoApp from [Fusion Samples](https://github.com/ActualLab/Fusion.Samples).

See [docs/TownHall-Phase1.md](docs/TownHall-Phase1.md) for the Phase 1 spec —
the plain, non-real-time baseline, where freshness comes exclusively from the
client re-fetching state (manual or auto-refresh).

## Related repositories

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) — the real-time
  state synchronization framework this sample is built to showcase
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) —
  other Fusion samples, including the TodoApp this app's structure follows
