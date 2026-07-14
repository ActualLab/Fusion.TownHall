# Session 07

**User:** For the prod VM, make sure it properly behaves on its own restart —
PostgreSQL starts there, and all hosted (deployed) apps start as well.

**Fable5:** Audited boot behavior (Docker service and townhall-deploy.timer
enabled, all 7 containers `restart: unless-stopped`) and verified it for real
with a VM reboot: Postgres and all hosted apps came back on their own —
townhall.actuallab.net, blazor-samples, and todoapp all HTTP 200, both DB
containers healthy, townhall-app restart count 0, and the townhall DB data
survived (migration check reports "already up to date").

**User:** Also, let's update all used NuGet packages in this sample to their
latest versions.

**Fable5:** Bumped ASP.NET Core/Blazor and EF Core floors to 10.0.9, pinned
Npgsql.EntityFrameworkCore.PostgreSQL to 10.0.3 (was resolving 10.0.0),
Microsoft.NET.Test.Sdk 17.13.0 → 18.8.0, xunit.runner.visualstudio 2.8.2 →
3.1.5; ActualLab.Fusion 13.0.28, MudBlazor 9.7.0, and xunit 2.9.3 were
already latest. All 60 tests pass; verified via server-loop, then committed
and pushed.
