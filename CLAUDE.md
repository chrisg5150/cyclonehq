# CycloneHQ

Read `knowledgebase.md` first. It covers the architecture, data contract, sources, engine tuning, commands and gotchas.

- .NET 10 solution: `CycloneHQ.slnx`. Build with `dotnet build CycloneHQ.slnx` and test with `dotnet test CycloneHQ.slnx`.
- `src/CycloneHQ.Core/Models/Contract.cs` is the data contract between Sync and the app: add fields, never rename or remove them.
- Don't hand-edit `src/CycloneHQ.App/wwwroot/data/`; regenerate it with `dotnet run --project src/CycloneHQ.Sync -- --out src/CycloneHQ.App/wwwroot/data`.
- Test UI changes at 412x915 (Android), in dark and light mode.
