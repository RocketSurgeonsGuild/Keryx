# Keryx

A source-generated mediator for .NET. Handlers are plain `private static` methods on `public partial` classes, and a Roslyn
generator emits all dispatch and pipeline code at compile time: no reflection, no delegate chains, no per-call allocations from
the mediator itself, and safe for Native AOT and trimming.

> **Status:** early development. The design is recorded in [docs/adr](docs/adr); nothing is published yet.

## Requirements

- The .NET 10 SDK (Roslyn 5.0) or later, whatever framework your project targets. The generator does not load in older compilers.
- `Keryx` targets `netstandard2.0`, `net10.0` and `net11.0`. Projects that host the mediator (`[assembly: KeryxHost]`) target
  `net10.0` or later.

## Building

```bash
mise run build
```

or, without mise:

```bash
dotnet build Keryx.slnx
dotnet test --solution Keryx.slnx
```

## License

[MIT](LICENSE)
