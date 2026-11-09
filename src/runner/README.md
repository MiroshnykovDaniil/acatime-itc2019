# Runner module

Imported from `itc2019-benchmark/runner`. The runner hosts the GA locally and
does not require the AcaTime web API, database or production configuration.

Build and run:

```bash
dotnet build src/AcaTime.ItcArtifact.sln -c Release
dotnet run --project src/runner -c Release -- \
  <api-scheduledata.json> <outDir> genetic key=value ...
```

The exact experiment configurations and seeds still need to be curated into
`experiments/`. The certified result files already carry the runner output
JSON and parameters used for each reported run.
