# What is left

Nov 27, 2026 · first written at the end of Phase 8 ([docs/handoff.md](handoff.md)); rewritten after the static-behavior rework, after the
owner's second rework, and again after its follow-up (owner decisions 14 to 18); updated for owner decisions 19 to 21 (2026-11-27).

Phases 0 to 8 are implemented on `feature/v1` (draft pull request #1) for the current design: abstractions, handlers, the host, request,
stream and notification pipelines, cross-assembly discovery, the samples with the Native AOT job, the benchmark suite and the README.
Two reworks followed the first build, each decided by the owner on 2026-11-26 and recorded in
[open-questions.md](adr/open-questions.md) under **Decided**, and a follow-up to the second applied the owner's later decisions of the same
day; the brief's phases describe the resulting design as what they build, so there is no separate rework phase. This note lists the
current state, what still needs a person, and what is left before a release.

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
  through `GeneratedSyntax` (at the time with one `NormalizeWhitespace` per compilation unit, a layout pass and the header; 38c8f74 below
  built the whitespace in); links are one `readonly struct` per position with a primary constructor and target-typed
  `new(pipeline, request)`. The commit title says "switch-expression dispatch", but the code it committed does not use switch expressions:
  generic dispatch was written as `switch` statements with exact-type `when` guards, one `return` per section, because the switch
  expressions decision 7 named lost the arms' tail calls and measured slower (OQ-67, then open). 6e7465a below replaced the guards of
  `Send` and `Stream` with type patterns.
- **02fb58e, docs:** decision 4 recorded with no code change (`next` stays a struct implementing `INext<TResponse>`, `INext` or
  `IStreamNext<TItem>`; typed next delegates are rejected in the ADR's Alternatives considered), and the docs aligned with the commits
  above.

## Done: the follow-up (owner decisions 14 to 18, 2026-11-26)

Applied on 2026-11-27, one commit per step, each with build and tests green and each folding its decisions into the ADR, the brief,
open-questions.md, CONTEXT.md and the README:

- **2e8e4e1, dispatch snapshots (decision 15):** Verify snapshots of the generated mediator for each kind at and one above the jump-table
  threshold (32 and 33 messages), and of a host with every kind above it, committed on the output of the time so that 6e7465a shows its
  dispatch change as a snapshot diff.
- **6e7465a, requests are sealed (decision 14; decision 16 for OQ-67; decision 18 for OQ-68):** a request or stream request class or
  record that is neither sealed nor abstract (or C# 15 `closed`) is the new error KRX024, with the code fix _Make sealed_ (not offered for a
  type that declares its own `virtual` or `protected` members); a handler of an abstract or closed request base is KRX003; notifications are
  not affected. Generic `Send` and `Stream` are switch statements with a type pattern per sealed request type
  (`case global::X r: return __Dispatch0<TResponse>(r, cancellationToken);`, `default: throw __HandlerNotFound(request.GetType());`), the
  index switch above the threshold stays keyed on `GetType()`, typed overloads of request types have no runtime type check, and
  `Publish<T>` keeps exact-type guards and ends `default: return default;`. A struct request's section has no pattern variable and its arm
  unboxes, so every arm stays a tail call. OQ-67 is decided (switch statements, not switch expressions), and so is OQ-68: a request or
  stream request type that is not sealed (KRX024 suppressed) is never composed, and a referenced one is the new host error KRX038. OQ-69
  (a closed base from a referenced assembly) was raised, and decision 19 below answered it.
- **bd2029b, pipelines that resolve nothing get no context (decision 16 for OQ-65):** a request or stream pipeline whose handler's invoker
  is static and whose behaviors inject nothing, and a publish pipeline without handlers whose behaviors inject nothing, have no pipeline
  context: the entry calls the outermost behavior method directly and the links sit in a `private static class`. Every link takes only
  what it reads or passes on (`(pipeline, request)`, `(request)`, or nothing), so the mediator no longer suppresses CS9113 and its header
  pragma is the obsolete-type one alone. OQ-65 is decided; OQ-70 (a publish pipeline with handlers that resolves nothing keeps its
  context) was raised, and decision 20 below answered it.
- **38c8f74, generated syntax with its whitespace (decision 16 for OQ-66):** `GeneratedSyntax` builds every token with its spaces and every
  line from the depth its container gives it, so `Render` writes the tree as built: no `NormalizeWhitespace` and no layout pass. Type
  names are parsed once per file and constant attribute lists built once. Every snapshot is byte for byte unchanged (a temporary two-way
  render of 7,472 generated files found no difference); two shapes no snapshot covers now read `int[,]` and `Dictionary<,>` where
  normalization wrote `int[, ]` and `Dictionary<, >`, and `GeneratedSyntaxTests` pins each layout rule against the helpers. OQ-66 is
  decided; OQ-71 (generator time still above the target) was raised, and decision 21 below answered it.

Measured in the second rework and its follow-up (Apple M4 Max, macOS 27.2, .NET SDK 11.0.100, BenchmarkDotNet 0.15.8 default job,
net10.0; JIT code with `DOTNET_TieredCompilation=0` and `DOTNET_JitDisasm`; details in the open-questions entries named, raw results in
the session scratchpad of the follow-up, `followup-*/`, which is not kept in the repository):

- the Phase 4 gate prototype written with primary-constructor links: Tier 1 code byte for byte the same as before, 0 B at depths 1, 3
  and 8, depth 3 at 0.67x the nested calls ("Links with primary constructors"); unchanged by OQ-65, because every link of the prototype
  reads or passes on its context;
- generic dispatch (OQ-67): switch statements compile to the same code as the earlier `if` chains; switch expressions lost every tail
  call of the linear `Send<int>` (10 to none, 43 to one above the threshold) and cost up to about 0.3 ns per generic `Publish` and 0.3 to
  1.8 ns per generic `Send`;
- type patterns on sealed requests ("Requests are sealed"): every tail call kept (10, 43, 2 and 35 for linear and above-threshold `Send`
  and `Publish`); the linear `Send<int>` loads the method table once and compares it with each type, 512 bytes instead of 784; in
  interleaved reruns generic `Send` with a synchronous handler measured 0.4 to 0.7 ns faster at every depth, and generic `Stream` at depth
  0 20 to 60 ns slower (3 to 9%), for a reason not found in its code (the benchmark method's code is the same; code placement is the
  suspect, not verified); everything else within noise, allocations unchanged;
- pipelines without a context (OQ-65): `StreamDepth3` allocates 648 B instead of 680 B (typed and generic); generic `Send`, `Stream` and
  `Publish` keep every tail call, the linear generic `Stream<int>` shrinks from 352 to 308 bytes, and the synchronous `Send` rows
  measured within noise or slightly faster;
- generator time on a host of 300 requests with five behaviors each, 30 stream requests and 30 notifications (`ReportAnalyzer`, Release,
  median of ten builds): 66 ms for the host generator and 35 ms for the handler generator with the text emitters, 384 ms and 102 ms with
  the normalized `SyntaxFactory` emitters (OQ-66); with the whitespace built in, 109 ms and 48 ms on a warm compiler server (372 ms and
  93 ms before, measured the same way). Re-measured on 2026-11-27 with ten extra warm-up builds (OQ-71): 97 ms and 46 ms with
  `SyntaxFactory` and explicit trivia, 58 ms and 33 ms with the text emitters before 1fddbdd, 51 ms and 29 ms with the text emitters of
  decision 21 (raw results in the session scratchpad, `text-before/` and `text-after/`).

## Done: owner decisions 19 to 21 (2026-11-27)

- **Text emitters (decision 21 for OQ-71):** every generator writes its source as text through `CodeWriter`
  (`src/Keryx.Analyzers/CodeWriter.cs`, the writer removed in 1fddbdd, recovered and extended with the layout rules the `SyntaxFactory`
  helpers had: blank lines between members, after a statement that ends with a block, the break after `=>`, one constructor parameter
  per line, switch sections, directives at column 0). `GeneratedSyntax` is removed and `GeneratedSyntaxTests` became `CodeWriterTests`.
  No snapshot changed; every source the test suite generates (734 distinct, on both target frameworks, plus three hosts of keywords,
  unusual type shapes, escaped keys and literals, and every kind above the jump-table threshold, built for the check only), the large
  host's 393 sources and the samples' and benchmark host's 168 are byte for byte what `SyntaxFactory` wrote. Decision 5 ("Generated code
  is built with SyntaxFactory") and OQ-66's implementation are marked superseded.
- **OQ-69 (decision 19):** accepted with no code change: a closed request base from a referenced assembly is treated, and named in
  KRX003, as abstract; the attribute lookup stays in case a compiler returns it.
- **Publish pipelines that resolve nothing (decision 20 for OQ-70):** a publish pipeline whose handlers' invokers are all static and
  whose behaviors inject nothing has no context: its `RunHandlers` (and `RunHandlersAsync`) are static members of
  `private static class __NotificationN`, called as `__NotificationN.RunHandlers(notification, cancellationToken)`, with no
  `__notificationN` field and no `__CreateNotificationN`. The notification jump-table snapshots changed accordingly; new snapshots cover
  such a pipeline sequential and parallel, with and without behaviors, and the allocation tests add six 0 B rows. Measured in interleaved
  runs: typed `PublishTo1` and `PublishTo5` about 0.3 ns faster (linear and above the threshold), generic `PublishTo5` 3.03 to 2.74 ns,
  the other generic rows within noise, 0 B everywhere; generic `Publish<__Canon>` keeps its 2 and 35 tail calls and its code shrinks
  (392 to 300 bytes linear, 2,856 to 1,468 above the threshold). Raw results in the session scratchpad, `oq70-before/` and `oq70-after/`.

## Decisions for the owner

Six questions are open ([open-questions.md](adr/open-questions.md), **Open**): OQ-59 to OQ-64 from the second rework. The code follows
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

OQ-65, OQ-66 and OQ-67 (decision 16), OQ-68 (decision 18), OQ-69 (decision 19), OQ-70 (decision 20) and OQ-71 (decision 21) are
decided and applied.

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
  tables predate the follow-up: the dispatch comparison (OQ-67) found the switch statements within noise of the earlier code, the type
  patterns made generic `Send` slightly faster and generic `Stream` at depth 0 slightly slower, and three stream behaviors now allocate
  648 B instead of the table's 680 B (the README notes it).
- **Publish baseline:** the direct call is non-async with an `async` fallback for handlers that do not complete synchronously; the
  README's Publish table shows 0.04 ns (inlined) and 0.67 ns for 1 and 5 handlers, against Keryx typed at 0.65 ns and 0.84 ns.

## Before a first release

- Decide OQ-59 to OQ-64.
- Add the `DisassemblyDiagnoser` findings from the `prototype-disassembly` job to OQ-36.
- Mark pull request #1 ready for review after Phase 8 (the handoff's last step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks on the release candidate and update the README's numbers (its tables are from before the follow-up);
  check whether generic `Stream` at depth 0 is still 20 to 60 ns slower than before the type patterns ("Requests are sealed").

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior methods other than fully open or fully closed. Full type-parameter unification for behaviors and user-defined phases are
future work. Requests handled outside their own assembly (`[assembly: KeryxContracts]`, `[assembly: ExternallyHandled]`) are kept as
designs in [future-requirements.md](future-requirements.md).
