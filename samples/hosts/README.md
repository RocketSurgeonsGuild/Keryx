# Sample hosts

Applications (executables that target `net10.0` and `net11.0`, so Keryx makes them hosts: their `KeryxHost` MSBuild property defaults
to `true`) that compose the sample libraries (ADR 0001, Cross-assembly discovery).

- `Keryx.Samples.Host`: a console host over `Keryx.Samples.Orders` and `Keryx.Samples.Contracts`. Its generated `KeryxMediator` and
  `AddKeryx()` compose the library's handlers, its global `Logging` behavior and its notification handlers with the host's own `Ping`
  request and handler, its `OrderShipped` notification and its own handler of the contracts' `OrderPlaced` (`PrintPlacedOrder`; a host
  may handle a notification declared anywhere, while a request is handled in its own assembly). It runs a short scenario (send through
  the generic and the typed entry points, stream, publish, a validation failure) and exits with a non-zero code when anything is off.

Run it with `dotnet run --project samples/hosts/Keryx.Samples.Host -f net10.0`. The Native AOT check (`.github/workflows/aot.yml`)
publishes every host here for `net10.0` and `net11.0`, fails on any IL2xxx/IL3xxx warning, and runs the native binary. A host sets
`PublishAot` in its project file: as a global property it would also reach the `netstandard2.0` contracts library (NETSDK1207).
