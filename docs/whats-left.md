# What is left

Nov 26, 2026 · written at the end of Phase 8 ([docs/handoff.md](handoff.md)); updated the same day after the design review that
decided every open question.

Phases 0 to 8 are implemented on `feature/v1` (draft pull request #1): abstractions, handlers, the host, request, stream and
notification pipelines, cross-assembly discovery, the samples with the Native AOT job, and the benchmark suite. The design review
changed the behavior model, so one more phase is planned. This note lists the next work and what still needs a person.

## Next: Phase 9, static behaviors

[Phase 9 in the brief](handoff.md#phase-9-static-behaviors) reworks the code to the design the ADR now describes (a summary; the
brief has the full list):

- behaviors become `static` classes with the three kind attributes (`[RequestBehavior]`, `[StreamBehavior]`,
  `[NotificationBehavior]`) and static `Handle`/`HandleAsync` behavior methods, replacing the five behavior interfaces; an open
  behavior method constrains its message type parameter to the kind's message interface (KRX019); the pipeline context holds the
  behaviors' injected services, and nothing is registered for a behavior;
- per-handler notification behaviors are removed: `[NotificationBehavior]` wraps the whole publish once, handlers are called without
  per-handler pipelines, and `[Behavior]` on a notification handler method is KRX042;
- stream hook behaviors and fused hook segments are removed;
- the generic `RunsAfter<T>`/`RunsBefore<T>` and the closed forms of exclusion and edges are removed; KRX020 is retired, and KRX019,
  KRX016, KRX013, KRX018, KRX009, KRX042 and KRX034 (OQ-47) are reworked for behavior methods;
- `[assembly: ExternallyHandled(typeof(X))]` with KRX030 and KRX036 (OQ-10), and KRX030 no longer skips request types declared in a
  referenced host (OQ-16); the host-only assembly attributes outside a host are the new warning KRX043 (OQ-54);
- the `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions` (OQ-8);
- the samples, benchmarks and README are updated, the Phase 4 gate is re-run with a static prototype (and on Linux in CI with
  `DisassemblyDiagnoser`, OQ-36), and the comparison benchmarks are re-run without the hook-behavior variant.

## Decisions

One question is open: **OQ-55** (what the host does when a `[Behavior]` on a referenced notification type no longer fits the type as the
host resolves it; until it is decided the behavior is skipped silently). Every other question in
[open-questions.md](adr/open-questions.md) was decided on 2026-11-26 and folded into the ADR:
OQ-8, OQ-9, OQ-10, OQ-15, OQ-16, OQ-17, OQ-36 and OQ-41 by an explicit choice, OQ-11 by the static-behavior design, OQ-21, OQ-38
and OQ-39 as moot (hook behaviors removed), OQ-22 as moot until the gate is re-run, OQ-37 rewritten for static behaviors, and every
other entry up to OQ-51 as proposed.

The owner's second round of decisions the same day settled the rest:

- only one notification behavior kind remains, `[NotificationBehavior]` (was `[PublishBehavior]`); per-handler notification
  behaviors are removed, which makes OQ-40, OQ-53 and the per-handler part of OQ-43 moot;
- an open behavior method must constrain its message type parameter to the kind's message interface (KRX019);
- **OQ-52:** the floor rises to C# 14 (KRX022 below it); `netstandard2.0` targets stay supported;
- **OQ-54:** KRX043 warns about `ExternallyHandled`, `ExcludeHandler` and `ExcludeBehavior` in an assembly that is not a host.

## Gates and checks

- **Phase 4 gate:** passed on all three criteria with the instance behaviors (OQ-36). Criterion 2 (the behavior `Handle` and
  `next.InvokeAsync` called directly) was judged from a `DOTNET_JitDisasm` listing, which is accepted as equivalent evidence, because
  BenchmarkDotNet's `DisassemblyDiagnoser` does not run on macOS. Phase 9 re-runs the gate with static behavior methods and adds a Linux
  run with the diagnoser to the benchmarks CI job.
- **No other gate failed.** The phase checks (tests, allocation tests, incremental tests, the AOT job) pass.
- **Benchmarks in CI:** `.github/workflows/benchmarks.yml` (build, `--verify`, every benchmark with `--job Dry`) runs on the pull request
  and passed its first CI run. Phase 9 adds the Linux `PipelinePrototype` job with `DisassemblyDiagnoser`.
- **Performance contract against the comparison run** (README, Benchmarks; measured with the instance behaviors, and re-measured in
  Phase 9): Keryx allocates 0 B per `Send` with a synchronous handler
  at 0, 1, 3 and 8 behaviors (typed and generic) and per sequential `Publish` to 1 and 5 handlers; with a yielding `IRequest<T>` handler
  it allocates what the direct call allocates (the handler's state machine); a `Stream` with no behaviors allocates what the handler's
  iterator allocates, and with behaviors one iterator per wrap behavior or per fused hook segment (after Phase 9, one iterator per
  stream behavior). The one generated async path, `UnitAdapter.ToUnit`, is checked by `SendUnitYieldValueTask` and
  `SendUnitYieldTask` (an `IRequest` whose `async ValueTask`/`async Task` handler awaits `Task.Yield()`): typed and `IMediator` both
  allocate 96 B, the same as the handler awaited directly, so the adapter adds 0 B in steady state. The numbers come from one machine
  (Apple M4 Max) with a 15-iteration job, not BenchmarkDotNet's default job; re-run on other hardware before quoting them.
- **Publish baseline:** the direct call used to be an `async` method, which made it slower than Keryx's typed overload (ratio below 1).
  It is now non-async with an `async` fallback for handlers that do not complete synchronously, and the README's Publish table was
  re-run: 0.00 ns (inlined) and 0.64 ns for 1 and 5 handlers, against Keryx typed at 0.68 ns and 0.74 ns.

## Before a first release

- Mark pull request #1 ready for review after Phase 9 (the handoff's last step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks on the release candidate and update the README's numbers.

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior methods other than fully open or fully closed. Full type-parameter unification for behaviors and user-defined phases are
future work.
