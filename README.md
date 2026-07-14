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

## How to run

```bash
dotnet run --project src/TownHall.Host
```

Then open http://localhost:5136. Tests (`dotnet test`) cover every API method
twice — once against the server DI container, once over Fusion RPC against a
test host on a random port.

## Try it

1. Open http://localhost:5136 in two separate browser windows (W1, W2) —
   each gets its own generated name (sessions are per tab).
2. In W1, create a town hall and enter it; you'll see the owner bar.
   Copy the participant link and open it in W2.
3. W1 presses Start — W2 sees the room go Live within a second, no refresh.
4. Post a question in W2, vote on it in W1: the lists, vote counts, and the
   Trending panel update everywhere in real time.
5. Click mood emojis in both windows — the SVG face and the distribution
   chart follow the average instantly; close W2 and watch its mood drop out
   of the aggregate within ~30 s.
6. In W1, resolve the question with a note (it moves to the Resolved tab)
   or delete it; toggle Private to hide the room from the home-page list.
7. Paste the owner link (from the owner bar) into a third window to hand
   moderation rights to another session.

## Related repositories

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) — the real-time
  state synchronization framework this sample is built to showcase
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) —
  other Fusion samples, including the TodoApp this app's structure follows
