# Session 08

**User:** 1. Rename the `fusion_townhall` DB to simply `townhall`. 2. Use the
same DB name in production; as for the persistent data path — what is better,
`/opt/apps/townhall-data/postgres` or the named-volume scheme BoardGames uses?
What's the right location on Unix? 3. Commit, push, and once the deploy lands,
remove the old DB from the prod VM and from this machine too.

**Fable5:** Renamed the DB to `townhall` everywhere (default connection
string, prod compose, tests now use `townhall_tests_*`, docs); kept a bind
mount over a named volume (explicit, easy to back up, immune to
`docker volume prune`) but moved it to the FHS-correct service-state location
`/var/lib/townhall/postgres` (`/opt` is for add-on software, not data);
verified locally, pushed, confirmed the prod deploy created the `townhall` DB
fresh at the new path, then removed the old `/opt/apps/townhall-data` dir on
the VM and dropped `fusion_townhall` from the local Postgres.
