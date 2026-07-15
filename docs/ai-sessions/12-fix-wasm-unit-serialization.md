# Session 12 (main branch)

**User:** The prod (published, trimmed WASM) app fails: the RPC client can't
deserialize `System.Reactive.Unit` — `DefaultMessagePackResolver` throws an NRE from
`ActivatorExt.CreateInstance` building the `Unit` MessagePack formatter, so every
`Unit`-returning command fails on the client. The same problem was solved in
BoardGames, TodoApp, etc.

**Opus4.8:** Root cause: full trimming can't see the reflective hop that instantiates
`UnitMessagePackFormatter`, so it drops the formatter's ctor. Applied the same fix
BoardGames uses — a `CodeKeeper.Keep<UnitMessagePackFormatter>()` under
`if (CodeKeeper.AlwaysFalse)` in the WASM client's `Program.cs`, which the trimmer
can't prove unreachable, so it preserves the ctor. Verified it compiles and the
trimmed Release publish succeeds.
