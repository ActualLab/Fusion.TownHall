# Session 06

**User:** In the README, add a link to the AI session log folder and explain
that the app is generated and kept updated with Claude Code, and that the
full interaction log (with Claude's responses summarized to a bare minimum)
is there.

**Fable5:** Added a paragraph to README.md (after the Phase 1 spec link)
linking to `docs/ai-sessions/` and explaining the app is generated and
maintained with Claude Code, with the full interaction log kept there.

**User:** Add PostgreSQL support the same way TodoApp does it, but also
generate a migrations project and make sure migrations run with PostgreSQL;
add a note to AGENTS-Source.md that migrations must be updated on DB
changes. Make PostgreSQL the default — in fact, get rid of SQLite
completely. Use the same default login/password as TodoApp, but a different
database. Check the Docker Compose file in Fusion Samples and replicate its
Postgres part here, to document how to start the sample locally (start
Docker Compose first, then run the sample with the default credentials).
Also tweak our production VM to run PostgreSQL in Docker — the same version
as in Docker Compose, but persisting the databases to a local folder (not
ephemeral), with the same login/password, and not exposed in any way
(accessible only via SSH). Once all of that is verified, migrate the sample
to Postgres completely, verify locally, make sure Postgres runs on the
production VM, then commit and push. Finally, describe the Postgres setup
for local runs in the README: run Docker Compose before starting the sample.
*(Also: /server-loop is running, so the changes can be live-tested.)*

**Fable5:** Extracted the EF model into a new `TownHall.Db` project with an
`Initial` migration and design-time factory, switched Host and tests to
PostgreSQL-only (`UseNpgsql` + `UseNpgsqlHintFormatter` +
`AddNpgsqlOperationLogWatcher`, `MigrateAsync` on start, default
`postgres/postgres @ localhost:5432`, DB `fusion_townhall`), removed SQLite,
added a root `docker-compose.yml` (Postgres 18, replicating the Fusion
Samples db service), added `townhall-db` to the prod compose (no published
ports, data bind-mounted to `/opt/apps/townhall-data/postgres`), and updated
README, deploy/README, and AGENTS-Source.md (migrations-on-DB-changes rule).
Local verification surfaced two fixes: EF assembly-version unification
(TownHall.Db compiled against EF 10.0.1 while the Host resolved 10.0.0, which
silently broke migration discovery — fixed by referencing
`Microsoft.EntityFrameworkCore.Relational` non-privately) and stamping times
at Postgres microsecond precision (`Moment.ToDbPrecision()`), since 100ns
ticks don't round-trip. All 60 tests pass against Postgres; the UI flow
(create room → start → post → vote → mood) verified via server-loop +
Chrome, and the rows checked directly in the `fusion_townhall` database.
