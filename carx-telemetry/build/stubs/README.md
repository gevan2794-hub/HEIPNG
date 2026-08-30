# Reference stubs — compile checking only

BepInEx publishes its assemblies on its own NuGet feed and SimHub ships its assemblies
only inside the installer, so neither can be restored in every environment (CI, a
container, a machine without SimHub installed). These projects declare just enough of
each API surface for the real source to compile against.

**They are not a shim, a mock, or a runtime substitute.** Every method throws. Their only
job is to let `dotnet build` type-check the real code so that syntax errors, typos and
wrong member names get caught without a game install.

A stub build proves the code is internally consistent. It does **not** prove the
signatures match the real libraries — if upstream renamed a method, the stub build stays
green and the real one fails. Always do a real build before shipping:

```
dotnet build src/CarX.Telemetry.Mod    -c Release -p:GameFlavor=Mono
dotnet build src/CarX.Telemetry.SimHub -c Release -p:SimHubPath="C:\Program Files (x86)\SimHub"
```

Stub builds are opt-in via `-p:UseStubs=true`, so a normal build can never pick them up
by accident.
