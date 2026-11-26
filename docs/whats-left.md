# What is left

Nov 26, 2026 · first written at the end of Phase 8 ([docs/handoff.md](handoff.md)); rewritten after the static-behavior rework.

Phases 0 to 7 are implemented for the static design on `feature/v1` (draft pull request #1): abstractions, handlers, the host, request,
stream and notification pipelines, cross-assembly discovery, and the samples with the Native AOT job. Phase 8 is done for the static
design too: the benchmark suite uses static behaviors, the README is rewritten, the Phase 4 gate and the comparison benchmarks were
re-run, and the benchmarks workflow has a Linux `PipelinePrototype` job (its first `DisassemblyDiagnoser` output is still to be
recorded). The design review of 2026-11-26 replaced the behavior model, and that rework is implemented in two commits; the brief's
phases now describe the static design as what they build, so there is no separate rework phase. This note lists what still needs a
person and what is left before a release.

## Done: the static-behavior rework

- **7d2fd01:** the C# 14 floor (KRX022 below it; both generators emit nothing there; `netstandard2.0` stays supported, OQ-52); the
  `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions` per TFM (OQ-8); `[assembly: ExternallyHandled]`
  with KRX030 and KRX036 (OQ-10; removed again in the second rework, below); KRX030 no longer skips request types declared in a referenced host (OQ-16); the host-only assembly
  attributes outside a host are warning KRX043 (OQ-54).
- **f54dc49:** behaviors are `static` classes with the three kind attributes (`[RequestBehavior]`, `[StreamBehavior]`,
  `[NotificationBehavior]`) and static `Handle`/`HandleAsync` behavior methods, replacing the five behavior interfaces; an open behavior
  method constrains its message type parameter to the kind's message interface (KRX019); the pipeline context holds the behaviors'
  injected services and `AddKeryx()` registers no behaviors; the generic `RunsAfter<T>`/`RunsBefore<T>` and the closed forms of exclusion
  and edges are gone, and KRX020 is retired; stream hook behaviors and fused hook segments are removed; per-handler notification behaviors
  are removed (`[NotificationBehavior]` wraps the whole publish once, handlers are called directly, `[Behavior]` on a notification handler
  method is KRX042); the host reports KRX009 for a `[Behavior]` on a referenced notification type that no longer fits it (OQ-55) and
  KRX034 for unresolvable injected parameter types of referenced behavior methods (OQ-47); `ProjectionDataVersion` is 2; the samples, the
  gate prototype and the Keryx comparison subject use static behaviors, and the hook-behavior stream variant of the comparison benchmarks
  is gone (OQ-51).
- **a2f0ff6:** the Phase 4 gate re-run on the static-behavior prototype and the comparison benchmarks re-run, with the README's
  behavior, lifetime and diagnostics sections and its comparison tables rewritten for static behaviors; the benchmarks workflow gains
  the Linux `prototype-disassembly` job (`--filter *PipelinePrototype*` with `DisassemblyDiagnoser`).

CI on f54dc49 is green (build, lint, the Native AOT publish and the benchmarks Dry job).

## In progress: the second rework (owner decisions, 2026-11-26)

- **Requests are handled in their own assembly:** `[assembly: KeryxContracts]` and `[assembly: ExternallyHandled]` are removed (designs
  kept in [future-requirements.md](future-requirements.md)); KRX001 applies in every assembly, hosts included; a handler of a request
  declared in another assembly is the new error KRX023; host KRX030 covers referenced requests only. The sample order requests moved to
  `Keryx.Samples.Orders`; `Keryx.Samples.Contracts` keeps the notifications. The pre-commit `dotnet format` step is whitespace only.

## Decisions for the owner

Two open questions came up while implementing the second rework ([open-questions.md](adr/open-questions.md), **Open**):

- **OQ-59:** with KRX023 in hosts, a host can exclude a referenced request handler but not provide its own; should a host's handler of a
  referenced request be allowed when the host excludes that request's referenced handler (test hosts)?
- **OQ-60:** generated manifests can no longer make two assemblies handle one request, so the host's cross-assembly KRX005 is only a
  safety net; keep it or remove it?

The three questions raised while implementing the first rework were decided by the owner on 2026-11-26 and are implemented;
[open-questions.md](adr/open-questions.md) records them under **Decided**, folded into the ADR and the
brief:

- **OQ-56:** the `net11.0` dependency group is empty: the .NET 11 shared framework carries
  `Microsoft.Extensions.DependencyInjection.Abstractions`, so `Keryx.csproj` lets package pruning drop it (`net10.0` and
  `netstandard2.0` keep the package dependency); a target in `Keryx.csproj` fails the pack if the packed groups change.
- **OQ-57:** KRX034 for a referenced behavior method that injects an unresolvable type is per kind, naming the method and its kind; the
  class stays in the pipelines of its other kinds, and KRX035 lists a global behavior per class and kind. A message constraint through a
  derived interface (`where TRequest : ICommand<TResponse>`) counts; one to an unrelated interface is KRX019.
- **OQ-58:** an open behavior method may write `TResponse?` (`TItem?`): matching substitutes the annotation and checks it with
  nullability, so it fits annotated, oblivious and value type responses and not a non-annotated reference one (skipped as a global
  behavior, KRX009 when attached). A closed method's `next` and return type are checked with nullability the same way.

Every other entry is decided and folded into the ADR.

## Gates and checks

- **Phase 4 gate, instance behaviors:** passed on all three criteria (OQ-36). Criterion 2 (the behavior method and `next.InvokeAsync`
  called directly) was judged from a `DOTNET_JitDisasm` listing, accepted as equivalent evidence because BenchmarkDotNet's
  `DisassemblyDiagnoser` does not run on macOS.
- **Phase 4 gate, static design:** passed on all three criteria again (OQ-36): 0 B at depths 1, 3 and 8; every static behavior method
  and `next.InvokeAsync` inlined with no generic-virtual-method lookup (`DOTNET_JitDisasm`); depth 3 at 0.66x the nested-call baseline
  (1.08 ns against 1.63 ns). OQ-22 stays moot. The Linux `DisassemblyDiagnoser` evidence comes from the benchmarks workflow's
  `prototype-disassembly` job; its first output is still to be added to OQ-36.
- **Phase checks:** the tests, allocation tests (now on an optimized test host for `async` behaviors), incremental tests and the AOT job
  pass.
- **Benchmarks in CI:** `.github/workflows/benchmarks.yml` builds the suite, runs `--verify` and every benchmark with `--job Dry` on the
  pull request, and the `prototype-disassembly` job runs `--filter *PipelinePrototype*` on Linux with `DisassemblyDiagnoser` and uploads
  the `pipeline-prototype-disassembly` artifact.
- **Performance contract against the comparison run** (README, Benchmarks; re-measured with static behaviors on
  2026-11-26): Keryx allocates 0 B per `Send` with a synchronous handler at 0, 1, 3 and 8 behaviors (typed and generic) and per
  sequential `Publish` to 1 and 5 handlers; with a yielding `IRequest<T>` handler it allocates what the direct call allocates (the
  handler's state machine); a `Stream` with no behaviors allocates what the handler's iterator allocates, and with behaviors one iterator
  per stream behavior (there are no hook behaviors). The one generated async path, `UnitAdapter.ToUnit`, is checked by
  `SendUnitYieldValueTask` and `SendUnitYieldTask` (an `IRequest` whose `async ValueTask`/`async Task` handler awaits `Task.Yield()`):
  typed and `IMediator` both allocate 96 B, the same as the handler awaited directly, so the adapter adds 0 B in steady state. The numbers
  come from one machine (Apple M4 Max) with a 15-iteration job, not BenchmarkDotNet's default job; re-run on other hardware before quoting
  them.
- **Publish baseline:** the direct call used to be an `async` method, which made it slower than Keryx's typed overload (ratio below 1).
  It is now non-async with an `async` fallback for handlers that do not complete synchronously, and the README's Publish table was
  re-run with static behaviors: 0.04 ns (inlined) and 0.67 ns for 1 and 5 handlers, against Keryx typed at 0.65 ns and 0.84 ns.

## Before a first release

- Add the `DisassemblyDiagnoser` findings from the first CI run of the `prototype-disassembly` job to OQ-36.
- Mark pull request #1 ready for review after Phase 8 (the handoff's last step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks on the release candidate and update the README's numbers.

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior methods other than fully open or fully closed. Full type-parameter unification for behaviors and user-defined phases are
future work.
