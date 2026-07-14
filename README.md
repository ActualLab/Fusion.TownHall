# TownHall

TownHall is a live audience Q&A app ("Slido-lite"): anyone can create a town
hall (a room) and becomes its owner; participants post questions and upvote
others' questions; the question list sorts by votes, and a trending panel
highlights questions gaining votes right now. Owners moderate — resolve or
delete questions, start/stop the session. Participants also continuously
signal their mood on a 5-emoji scale, visualized as a big generated SVG face
plus a distribution chart.

The app exists to compare real-time frameworks: the same app will be
implemented several times (.NET + [Fusion](https://github.com/ActualLab/Fusion),
plain .NET, .NET + SignalR, possibly TypeScript and Elixir stacks) to measure
the code delta each framework produces. **The Fusion-based version is
implemented first**; the other versions will be derived from it later.
It's a Blazor app (WebAssembly + Server-Side Blazor) similar to the TodoApp
from [Fusion Samples](https://github.com/ActualLab/Fusion.Samples).

See [docs/TownHall-Phase1.md](docs/TownHall-Phase1.md) for the Phase 1 spec —
the Fusion-based implementation, where every view updates in real time via
Fusion's compute services and invalidation.

## Related repositories

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) — the real-time
  state synchronization framework this sample is built to showcase
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) —
  other Fusion samples, including the TodoApp this app's structure follows
