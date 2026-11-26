# What is left

Nov 26, 2026 · written at the end of Phase 8 ([docs/handoff.md](handoff.md)).

Phases 0 to 8 are implemented on `feature/v1` (draft pull request #1): abstractions, handlers, the host, request, stream and
notification pipelines, cross-assembly discovery, the samples with the Native AOT job, and the benchmark suite. This note lists what
still needs a person.

## Decisions

The ADR is authoritative, and these are the points it does not settle yet. Each has an interim behavior in
[open-questions.md](adr/open-questions.md) that the code follows; deciding one means folding the answer into the ADR and moving the entry
to **Decided**.

- **Needs a choice between options:** OQ-8 (the `Keryx` package takes no DI dependency; hosts reference
  `Microsoft.Extensions.DependencyInjection.Abstractions` themselves), OQ-9 (analyzer folder `analyzers/dotnet/cs` and the Roslyn 5.0
  pin), OQ-10 (KRX030 can only be lowered globally), OQ-11 (exactly one public behavior constructor), OQ-16 (a host referenced by another
  host).
- **ADR text that the interim behavior contradicts or extends:** OQ-15 (the dispatch threshold is 32 request types, not the ADR's 16,
  from the Phase 3 measurements), OQ-36 (the Phase 4 gate's disassembly criterion judged from a `DOTNET_JitDisasm` listing on macOS),
  OQ-51 (how the comparison benchmarks set up each library).
- **Confirmations** of behavior the ADR implies but does not spell out: OQ-3, OQ-4, OQ-5, OQ-13, OQ-14, OQ-17, OQ-18, OQ-19, OQ-21 to
  OQ-35, OQ-37 to OQ-50.

## Gates and checks

- **Phase 4 gate:** passed on all three criteria (OQ-36). Criterion 2 (the behavior `Handle` and `next.InvokeAsync` called directly) was
  judged from a `DOTNET_JitDisasm` listing, because BenchmarkDotNet's `DisassemblyDiagnoser` does not run on macOS. Re-run
  `--filter *PipelinePrototype*` on Linux or Windows once to get the diagnoser's own output.
- **No other gate failed.** The phase checks (tests, allocation tests, incremental tests, the AOT job) pass.
- **Benchmarks in CI:** `.github/workflows/benchmarks.yml` (build, `--verify`, every benchmark with `--job Dry`) is new in Phase 8 and has
  run locally only; its first CI run happens on the next push.
- **Performance contract against the comparison run** (README, Benchmarks): Keryx allocates 0 B per `Send` with a synchronous handler
  at 0, 1, 3 and 8 behaviors (typed and generic) and per sequential `Publish` to 1 and 5 handlers; with a yielding `IRequest<T>` handler
  it allocates what the direct call allocates (the handler's state machine); a `Stream` with no behaviors allocates what the handler's
  iterator allocates, and with behaviors one iterator per wrap behavior or per fused hook segment. The one generated async path,
  `UnitAdapter.ToUnit`, is checked by `SendUnitYieldValueTask` and `SendUnitYieldTask` (an `IRequest` whose `async ValueTask`/`async
  Task` handler awaits `Task.Yield()`): typed and `IMediator` both allocate 96 B, the same as the handler awaited directly, so the
  adapter adds 0 B in steady state. The numbers come from one machine (Apple M4 Max) with a 15-iteration job, not BenchmarkDotNet's
  default job; re-run on other hardware before quoting them.
- **Publish baseline:** the direct call used to be an `async` method, which made it slower than Keryx's typed overload (ratio below 1).
  It is now non-async with an `async` fallback for handlers that do not complete synchronously, and the README's Publish table was
  re-run: 0.00 ns (inlined) and 0.64 ns for 1 and 5 handlers, against Keryx typed at 0.68 ns and 0.74 ns.

## Before a first release

- Mark pull request #1 ready for review (the handoff's last Phase 8 step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks on the release candidate and update the README's numbers.

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior shapes other than open-with-matching-arity or closed. Full type-parameter unification for behaviors and user-defined phases are
future work.
