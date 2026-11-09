# GA module

Imported from `AcaTime.Common` at commit `8669726`:

- `AcaTime.Algorithm.Genetic`;
- `AcaTime.Algorithm.Default` (required project reference);
- `AcaTime.ScheduleCommon`;
- `AcaTime.ScriptModels`;
- `AcaTime.Algorithm.Genetic.Tests` (3 unit tests, all passing).

The production defaults are unchanged. The ITC adapter is opt-in and is
activated by the runner parameters (`Adapter=itc ExtraItcOperators=3`, etc.).
The build includes the original source warnings; they are not treated as
errors.
