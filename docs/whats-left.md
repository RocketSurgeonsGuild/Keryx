# What is left

Nov 26, 2026 · first written at the end of Phase 8 ([docs/handoff.md](handoff.md)); rewritten after the static-behavior rework and again
after the owner's second rework.

Phases 0 to 8 are implemented on `feature/v1` (draft pull request #1) for the current design: abstractions, handlers, the host, request,
stream and notification pipelines, cross-assembly discovery, the samples with the Native AOT job, the benchmark suite and the README.
Two reworks followed the first build, each decided by the owner on 2026-11-26 and recorded in
[open-questions.md](adr/open-questions.md) under **Decided**; the brief's phases describe the resulting design as what they build, so there
is no separate rework phase. This note lists the current state, what still needs a person, and what is left before a release.

## Done: the static-behavior rework

- **7d2fd01:** the C# 14 floor (KRX022 below it; both generators emit nothing there; `netstandard2.0` stays supported, OQ-52); the
  `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions` per TFM (OQ-8); KRX030 no longer skips request types
  declared in a referenced host (OQ-16); the host-only assembly attributes outside a host are warning KRX043 (OQ-54). (It also added
  `[assembly: ExternallyHandled]`, removed again in the second rework.)
- **f54dc49:** behaviors are `static` classes with the three kind attributes (`[RequestBehavior]`, `[StreamBehavior]`,
  `[NotificationBehavior]`) and static `Handle`/`HandleAsync` behavior methods, replacing the five behavior interfaces; an open behavior
  method constrains its message type parameter to the kind's message interface (KRX019); the pipeline context holds the behaviors'
  injected services and `AddKeryx()` registers no behaviors; the generic `RunsAfter<T>`/`RunsBefore<T>` and the closed forms of exclusion
  and edges are gone, and KRX020 is retired; stream hook behaviors and fused hook segments are removed; per-handler notification behaviors
  are removed (`[NotificationBehavior]` wraps the whole publish once, handlers are called directly, `[Behavior]` on a notification handler
  method is KRX042); the host reports KRX009 for a `[Behavior]` on a referenced notification type that no longer fits it (OQ-55) and
  KRX034 for unresolvable injected parameter types of referenced behavior methods (OQ-47).
- **a2f0ff6, 0db88ba, 73382c0:** the Phase 4 gate and the comparison benchmarks re-run for static behaviors, with the README rewritten;
  OQ-56 to OQ-58 decided (an empty `net11.0` dependency group, KRX034 per behavior kind, annotated response type parameters).

## Done: the second rework (owner decisions, 2026-11-26)

One commit per step, each with build and tests green:

- **d8ad015, requests are handled in their own assembly:** `[assembly: KeryxContracts]` and `[assembly: ExternallyHandled]` are removed
  (their designs are kept in [future-requirements.md](future-requirements.md)); KRX001 applies in every assembly, hosts included; a handler
  of a request declared in another assembly is the new error KRX023; host KRX030 covers referenced requests only. The sample order
  requests moved to `Keryx.Samples.Orders`; `Keryx.Samples.Contracts` keeps the notifications. The pre-commit `dotnet format` step is
  `dotnet format whitespace` only.
- **5afb535, a host is an MSBuild property:** `KeryxHostAttribute` is removed; `build/Keryx.targets` defaults `KeryxHost` to `true` for
  `OutputType` `Exe`/`WinExe` and `IsTestProject` (else `false`; the project's own value wins), and the generators and analyzers read
  `build_property.KeryxHost`; the package ships `buildTransitive/` (and keeps `buildMultiTargeting/`), and a pack-time check fails if any
  of the three build folders is missing; host diagnostics without a source location use `Location.None`; KRX031, KRX043 and
  `KeryxHandlerNotFoundException` name the property; `ReferenceScan` drops the referenced-host flag (`ProjectionDataVersion` 3).
- **4b0658f, test compilations go through the testing library:** every generator, analyzer and code-fix test compiles through
  `Rocket.Surgery.Extensions.Testing.SourceGenerators` 11.0.2, which can set compilation options and track incremental steps, so no
  interim harness is left (only `KnownTypeNamesTests` builds a Roslyn compilation by hand); jump-table dispatch is tested for requests,
  stream requests and notifications at and above the threshold, at run time, in the generated syntax and in the compiled IL.
- **eca51b5, shared services:** a pipeline context (request, stream, publish) has one `ServiceN` field per distinct service (type and
  key) its behavior methods inject, computed in the model; a transient shared by several behavior methods of a pipeline is one instance
  per scope and pipeline.
- **1fddbdd, generated code with `SyntaxFactory`:** `HandlerEmitter` and `HostEmitter` build their output with Roslyn's `SyntaxFactory`
  through `GeneratedSyntax` (one `NormalizeWhitespace` per compilation unit, then a layout pass and the header); links are one
  `readonly struct` per position with a primary constructor and target-typed `new(pipeline, request)`. Generic dispatch is written as
  `switch` statements with exact-type `when` guards, not the switch expressions decision 7 named (the commit title still says
  "switch-expression"): the expressions lost the arms' tail calls and measured slower (OQ-67).
- **No code change, decision 4:** `next` stays a struct implementing `INext<TResponse>`, `INext` or `IStreamNext<TItem>`; typed next
  delegates are recorded as rejected in the ADR's Alternatives considered.

Measured in the second rework (Apple M4 Max, macOS 27.2, BenchmarkDotNet 0.15.8; details in the open-questions entries):

- the Phase 4 gate prototype written with primary-constructor links: Tier 1 code byte for byte the same as before, 0 B at depths 1, 3
  and 8, depth 3 at 0.67x the nested calls ("Links with primary constructors");
- generic dispatch: switch statements compile to the same code as the earlier `if` chains; switch expressions lost every tail call of
  the linear `Send<int>` and cost up to about 0.3 ns per generic `Publish` and 0.3 to 1.8 ns per generic `Send` (OQ-67);
- generator time on a host of 300 requests: 66 to 384 ms for the host generator and 35 to 102 ms for the handler generator with the
  `SyntaxFactory` emitters, dominated by `NormalizeWhitespace` (OQ-66).

## Decisions for the owner

Nine open questions came up while implementing the second rework ([open-questions.md](adr/open-questions.md), **Open**); the code follows
each one's **Interim** line:

- **OQ-59:** with KRX023 in hosts, a host can exclude a referenced request handler but not provide its own; should a host's handler of a
  referenced request be allowed when the host excludes that request's referenced handler (test hosts)?
- **OQ-60:** generated manifests can no longer make two assemblies handle one request, so the host's cross-assembly KRX005 is only a
  safety net; keep it or remove it?
- **OQ-61:** app models whose `OutputType` is not `Exe`/`WinExe` (the .NET for Android SDK sets `Library` plus `AndroidApplication`) are
  not hosts by default; default `KeryxHost` to `true` for `AndroidApplication` and other app models, or keep the interim (set
  `<KeryxHost>true</KeryxHost>`) and document it?
- **OQ-62:** every `Exe`, `WinExe` or test project that runs the Keryx generators is a host whatever it targets, so one below `net10.0`
  (including a multi-targeting test project's older inner builds, and transitively through a `ProjectReference`) fails with KRX031;
  default `KeryxHost` to `true` only for `.NETCoreApp` 10.0 or later, or keep the interim (set `<KeryxHost>false</KeryxHost>`, per target
  framework)?
- **OQ-63:** a behavior's unkeyed `IServiceProvider` is held in a shared `ServiceN` field of the context; keep that, or give the context a
  fixed provider field?
- **OQ-64:** shared service fields are numbered by first use, so an edit that keeps the set but changes the order of first uses renumbers
  them; number by a stable sort of (type, key) instead?
- **OQ-65:** every pipeline with behaviors keeps a context, even one that resolves nothing, and every link takes both primary constructor
  parameters (CS9113 suppressed); drop such contexts and unread link parameters?
- **OQ-66:** the one `NormalizeWhitespace` per compilation unit makes the host generator about six times slower on a large host; accept
  it, or build the syntax with its trivia and skip normalization?
- **OQ-67:** keep switch statements for generic dispatch, or use the switch expressions as decided at their measured cost?

Next in the plan: the owner has answered OQ-65 to OQ-67 and asked for two more changes (request and stream request types sealed or part
of a closed hierarchy, with type-pattern dispatch; Verify snapshots of generic dispatch at and above the jump-table threshold). They are
scheduled for the step after this rework; until that step records them under **Decided** and folds them into the ADR, those three entries
stay open and the code follows their interim.

## Gates and checks

- **Phase 4 gate, instance behaviors:** passed on all three criteria (OQ-36). Criterion 2 (the behavior method and `next.InvokeAsync`
  called directly) was judged from a `DOTNET_JitDisasm` listing, accepted as equivalent evidence because BenchmarkDotNet's
  `DisassemblyDiagnoser` does not run on macOS.
- **Phase 4 gate, static design:** passed on all three criteria again (OQ-36): 0 B at depths 1, 3 and 8; every static behavior method
  and `next.InvokeAsync` inlined with no generic-virtual-method lookup (`DOTNET_JitDisasm`); depth 3 at 0.66x the nested-call baseline
  (1.08 ns against 1.63 ns), and 0.67x with the primary-constructor links. OQ-22 stays moot. The Linux `DisassemblyDiagnoser` evidence
  comes from the benchmarks workflow's `prototype-disassembly` job; its output is still to be added to OQ-36.
- **Phase checks:** the tests, allocation tests (on an optimized test host for `async` behaviors), incremental tests, IL and jump-table
  dispatch tests and the AOT job pass.
- **Benchmarks in CI:** `.github/workflows/benchmarks.yml` builds the suite, runs `--verify` and every benchmark with `--job Dry` on the
  pull request, and the `prototype-disassembly` job runs `--filter *PipelinePrototype*` on Linux with `DisassemblyDiagnoser` and uploads
  the `pipeline-prototype-disassembly` artifact.
- **Performance contract against the comparison run** (README, Benchmarks; measured with static behaviors on 2026-11-26): Keryx
  allocates 0 B per `Send` with a synchronous handler at 0, 1, 3 and 8 behaviors (typed and generic) and per sequential `Publish` to 1
  and 5 handlers; with a yielding `IRequest<T>` handler it allocates what the direct call allocates (the handler's state machine); a
  `Stream` with no behaviors allocates what the handler's iterator allocates, and with behaviors one iterator per stream behavior. The
  one generated async path, `UnitAdapter.ToUnit`, is checked by `SendUnitYieldValueTask` and `SendUnitYieldTask`: typed and `IMediator`
  both allocate 96 B, the same as the handler awaited directly, so the adapter adds 0 B in steady state. The numbers come from one
  machine (Apple M4 Max) with a 15-iteration job, not BenchmarkDotNet's default job; re-run on other hardware before quoting them. The
  second rework's dispatch comparison (OQ-67) found the switch statements within noise of the earlier code.
- **Publish baseline:** the direct call is non-async with an `async` fallback for handlers that do not complete synchronously; the
  README's Publish table shows 0.04 ns (inlined) and 0.67 ns for 1 and 5 handlers, against Keryx typed at 0.65 ns and 0.84 ns.

## Before a first release

- Decide OQ-59 to OQ-64 and apply the scheduled answers to OQ-65 to OQ-67 (above).
- Add the `DisassemblyDiagnoser` findings from the `prototype-disassembly` job to OQ-36.
- Mark pull request #1 ready for review after Phase 8 (the handoff's last step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks on the release candidate and update the README's numbers.

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior methods other than fully open or fully closed. Full type-parameter unification for behaviors and user-defined phases are
future work. Requests handled outside their own assembly (`[assembly: KeryxContracts]`, `[assembly: ExternallyHandled]`) are kept as
designs in [future-requirements.md](future-requirements.md).
