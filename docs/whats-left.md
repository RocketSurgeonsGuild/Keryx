# What is left

Nov 27, 2026 · first written at the end of Phase 8 ([docs/handoff.md](handoff.md)); rewritten after the static-behavior rework, after the
owner's second rework, and again after its follow-up (owner decisions 14 to 18); updated for owner decisions 19 to 22 and 23 to 28
(2026-11-27), and checked against the code by the docs sweep after OQ-59 to OQ-64; updated for owner decision 30 (real-world benchmark
scenarios, 2026-11-27) and decisions 29 and 31 (the NativeAOT benchmark run and its CI job, 2026-11-27); updated for decision 32 (the
README's numbers from a full local run under the JIT and NativeAOT, 2026-11-27) and decision 35 (transient `IMediator`, `ISender`
and `IPublisher` forwarders, OQ-74); updated for decisions 33, 34 and 36 (the real-world setup rules in the ADR, the verification probe
compiled out of the benchmark builds, `MessageName<T>`; OQ-72, OQ-73 and OQ-75); updated for decision 37 (the second full benchmark
run, at 3575a50; OQ-77); updated for decisions 38 and 39 (OQ-76 and OQ-77 decided: the `ValidateOnBuild` trade-off accepted and
documented, a fixed invocation count for the direct call's NativeAOT `Publish` to one handler); updated for owner decisions 46 and 47,
with 48 (2026-11-28: a transient mediator over a scoped state, one field per pipeline context; the size-bounded layout is OQ-78).

Phases 0 to 8 are implemented on `feature/v1` (draft pull request #1) for the current design: abstractions, handlers, the host, request,
stream and notification pipelines, cross-assembly discovery, the samples with the Native AOT job, the benchmark suite and the README.
Two reworks followed the first build, each decided by the owner on 2026-11-26 and recorded in
[open-questions.md](adr/open-questions.md) under **Decided**, and a follow-up to the second applied the owner's later decisions of the same
day; the owner's decisions 19 to 22 of 2026-11-27 answered the questions the follow-up raised, and decisions 23 to 28 of the same day
answered OQ-59 to OQ-64, the questions the second rework had left open. The brief's phases describe the resulting design as what they
build, so there is no separate rework phase. This note lists the current state, every commit since the second rework, what still needs
a person, and what is left before a release.

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
  `OutputType` `Exe`/`WinExe` and `IsTestProject` (else `false`; the project's own value wins; decisions 25 and 26, below, added
  `AndroidApplication` and the .NET 10 condition), and the generators and analyzers read
  `build_property.KeryxHost`; the package ships `buildTransitive/` (and keeps `buildMultiTargeting/`), and a pack-time check fails if any
  of the three build folders is missing; host diagnostics without a source location use `Location.None`; KRX031, KRX043 and
  `KeryxHandlerNotFoundException` name the property; `ReferenceScan` drops the referenced-host flag (`ProjectionDataVersion` 3).
- **4b0658f, test compilations go through the testing library:** every generator, analyzer and code-fix test compiles through
  `Rocket.Surgery.Extensions.Testing.SourceGenerators` 11.0.2, which can set compilation options and track incremental steps, so no
  interim harness is left (only `KnownTypeNamesTests` builds a Roslyn compilation by hand); jump-table dispatch is tested for requests,
  stream requests and notifications at and above the threshold, at run time, in the generated syntax and in the compiled IL.
- **eca51b5, shared services:** a pipeline context (request, stream, publish) has one `ServiceN` field per distinct service (type and
  key) its behavior methods inject, computed in the model; a transient shared by several behavior methods of a pipeline is one instance
  per scope and pipeline. (Decisions 27 and 28, below, moved the unkeyed `IServiceProvider` to its own field and numbered the services
  by type and key.)
- **1fddbdd, generated code with `SyntaxFactory`:** `HandlerEmitter` and `HostEmitter` built their output with Roslyn's `SyntaxFactory`
  through `GeneratedSyntax` (one `NormalizeWhitespace` per compilation unit, a layout pass and the header; 38c8f74 below built the
  whitespace in, and 100bfe1 replaced the `SyntaxFactory` emitters with text emitters that write the same bytes); links are one
  `readonly struct` per position with a primary constructor and target-typed `new(pipeline, request)`. The commit title says
  "switch-expression dispatch", but the code it committed does not use switch expressions: generic dispatch was written as `switch`
  statements with exact-type `when` guards, one `return` per section, because the switch expressions decision 7 named lost the arms' tail
  calls and measured slower (OQ-67, then open). 6e7465a below replaced the guards of `Send` and `Stream` with type patterns.
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
- **38c8f74, generated syntax with its whitespace (decision 16 for OQ-66):** `GeneratedSyntax` built every token with its spaces and every
  line from the depth its container gave it, so `Render` wrote the tree as built: no `NormalizeWhitespace` and no layout pass. Type names
  were parsed once per file and constant attribute lists built once (all of it removed by 100bfe1 below). Every snapshot is byte for byte
  unchanged (a temporary two-way render of 7,472 generated files found no difference); two shapes no snapshot covers now read `int[,]` and
  `Dictionary<,>` where normalization wrote `int[, ]` and `Dictionary<, >`, and `GeneratedSyntaxTests` pinned each layout rule against the
  helpers. OQ-66 is decided; OQ-71 (generator time still above the target) was raised, and decision 21 below answered it.
- **e8f8655, docs:** whats-left.md rewritten around the follow-up's commits and measurements; the README, ADR and brief noted that
  context-free pipelines (OQ-65), built-in whitespace (OQ-66), KRX024/KRX038 and the type-pattern dispatch changed their earlier text, and
  the Orders sample's requests are listed as sealed.

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

## Done: owner decisions 19 to 22 (2026-11-27)

Each code change is one commit, with build and tests green:

- **100bfe1, text emitters (decision 21 for OQ-71; decision 19 recorded):** every generator writes its source as text through `CodeWriter`
  (`src/Keryx.Analyzers/CodeWriter.cs`, the writer removed in 1fddbdd, recovered and extended with the layout rules the `SyntaxFactory`
  helpers had: blank lines between members, after a statement that ends with a block, the break after `=>`, one constructor parameter per
  line, switch sections, directives at column 0). `GeneratedSyntax` is removed and `GeneratedSyntaxTests` became `CodeWriterTests`. No
  snapshot changed; every source the test suite generates (734 distinct, on both target frameworks, plus three hosts of keywords, unusual
  type shapes, escaped keys and literals, and every kind above the jump-table threshold, built for the check only), the large host's 393
  sources and the samples' and benchmark host's 168 are byte for byte what `SyntaxFactory` wrote. Decision 5 ("Generated code is built with
  SyntaxFactory") and OQ-66's implementation are marked superseded.
- **OQ-69 (decision 19, recorded in 100bfe1):** accepted with no code change: a closed request base from a referenced assembly is
  treated, and named in KRX003, as abstract; the attribute lookup stays in case a compiler returns it.
- **febb1a6, publish pipelines that resolve nothing (decision 20 for OQ-70):** a publish pipeline whose handlers' invokers are all static
  and whose behaviors inject nothing has no context: its `RunHandlers` (and `RunHandlersAsync`) are static members of
  `private static class __NotificationN`, called as `__NotificationN.RunHandlers(notification, cancellationToken)`, with no
  `__notificationN` field and no `__CreateNotificationN`. The notification jump-table snapshots changed accordingly; new snapshots cover
  such a pipeline sequential and parallel, with and without behaviors, and the allocation tests add six 0 B rows. Measured in interleaved
  runs: typed `PublishTo1` and `PublishTo5` about 0.3 ns faster (linear and above the threshold), generic `PublishTo5` 3.03 to 2.74 ns,
  the other generic rows within noise, 0 B everywhere; generic `Publish<__Canon>` keeps its 2 and 35 tail calls and its code shrinks (392
  to 300 bytes linear, 2,856 to 1,468 above the threshold). Raw results in the session scratchpad, `oq70-before/` and `oq70-after/`.
- **Generic `Stream` at depth 0 (decision 22):** the 20 to 60 ns (3 to 9%) slowdown of generic `Stream` with no stream behaviors measured
  after the type patterns (6e7465a; cause not verified, code placement suspected) is tracked for the release and is on the list below; it is
  not investigated now. The docs sweep after the text emitters and OQ-70 recorded it and checked the ADR, the brief, open-questions.md,
  CONTEXT.md, the README, the samples' READMEs and the analyzer release notes against the code.

## Done: owner decisions 23 to 28 (2026-11-27)

One commit per pair of decisions, in the order they landed, each with build and tests green and each folding its decisions into the
ADR, the brief, open-questions.md, CONTEXT.md where a term changed, the README and this note:

- **7a94d0d, `KeryxHost` only where a host can run (decision 25 for OQ-61, decision 26 for OQ-62):** `build/Keryx.targets` (which
  `buildTransitive/` and `buildMultiTargeting/` import) defaults `KeryxHost` to `true` only when the project is an `Exe`/`WinExe`, a test
  project or an Android app (`AndroidApplication`) **and** targets `.NETCoreApp` 10.0 or later; a value the project sets still wins, so
  KRX031 now means an explicit `<KeryxHost>true</KeryxHost>` below `net10.0`, and its message says to target `net10.0` or stop setting the
  property. The README no longer tells `net8.0` or multi-targeting projects to set `false`. `KeryxHostPropertyTests` import the package's
  files from `obj/*.nuget.g.props`/`.targets`, where restore puts them, and gain rows for `net8.0`, `net48`, `net10.0` and `net11.0`
  executables and test projects, explicit values, Android apps (`net10.0-android`, `net8.0-android`, `AndroidApplication` set as the Android
  SDK leaves it, so the rows do not need the workload) and each inner and the outer build of a `net8.0;net10.0` test project. Checked by
  hand with the packed package: a `net8.0` console app referencing a `netstandard2.0` Keryx library builds (it did not before: KRX031); a
  `net8.0;net10.0` test project referencing it builds, with `AddKeryx()` generated in its `net10.0` build only; a `net10.0` app runs; a
  `net8.0` app with `<KeryxHost>true</KeryxHost>` gets KRX031.
- **98bcacd, `ExcludeHandler` names only notification handlers (decision 23 for OQ-59):** naming a request or stream request handler,
  referenced or the host's own, is the new host error KRX039 (the next free ID in the host range; KRX036 is a warning about an exclusion
  without effect, and this one is an error), reported on the attribute; the handler is not excluded and stays dispatched, and the exclusion
  is not also KRX036. There is no exception to KRX023 for hosts: a test swaps what a request handler does through the services it injects
  (ADR, Host control; README, Testing handlers). The tests that excluded request handlers now assert KRX039 (referenced request and stream
  request handlers, the host's own, a referenced one beside the host's KRX023 replacement, and one the host cannot resolve, which is KRX034
  since it is no longer skipped); the exclusion of a notification handler the host cannot resolve keeps its test.
- **98bcacd, no host KRX005 (decision 24 for OQ-60):** the host's cross-assembly KRX005, the `DuplicateHandlerInHost` descriptor and its
  hand-written-manifest test are removed; KRX005 is the analyzer's rule within one assembly. Duplicates inside one referenced assembly still
  leave the request undispatched, with no host diagnostic (the test says so).
- **4df2f3d, a fixed `IServiceProvider` field (decision 27 for OQ-63):** a pipeline context (request, stream, publish) whose behavior
  methods inject the unkeyed `IServiceProvider` holds it in a dedicated `ServiceProvider` field, assigned the provider the context is built
  with and never resolved, outside the numbered `ServiceN` fields; a context without such a behavior has no field, and a behavior that
  injects only the provider gives a static-handler pipeline a context. The model carries it as `ContextServiceProvider` (and slot
  `PipelineBehaviors.ServiceProviderSlot`); `ContextServiceModel` lost `IsServiceProvider`. A handler's injected `IServiceProvider` was
  already the provider directly (its factory passes `serviceProvider`); a keyed `IServiceProvider` is an ordinary keyed service and
  `IServiceScope` has no special case. Tests: a snapshot of request, stream and publish pipelines whose two behaviors each inject the
  provider (required and optional), a keyed provider beside the unkeyed one, a handler that injects both, and at run time every behavior and
  handler provider is the scope's; the allocation test's injecting behaviors also take the provider and still allocate 0 B.
- **4df2f3d, stable service numbering (decision 28 for OQ-64):** `PipelineBehaviors.ShareServices` numbers a context's services by an
  ordinal sort of (service type, then key: none first, then the key's literal text) instead of first use, so any edit that keeps the set
  keeps every name. `SharedServiceTests` replaces the renumbering test with one that asserts unchanged `ContextServices` and fields for a
  set-preserving reorder, and adds a test of the order itself. Snapshots that changed: the keyed `IClock` keys (`"earth"` before `"utc"`),
  the all-optional consumers (sorted by type) and the required-consumer one (the provider moved to `ServiceProvider`).
- **Docs sweep after OQ-59 to OQ-64:** the ADR, the brief, open-questions.md, CONTEXT.md, the README, the samples' READMEs,
  future-requirements.md and the analyzer release notes checked against the code after 7a94d0d, 98bcacd and 4df2f3d. The ADR's Alternatives
  considered gained the two designs OQ-63 and OQ-64 replaced (the provider as a numbered service, first-use numbering); the brief's rework
  summary names the three commits; future-requirements.md no longer says a host can exclude a request handler; the sample host README says
  the host default needs `net10.0` or later. No code or snapshot changed.

## Done: the full benchmark run (owner decision 32, 2026-11-27)

No code changed: the README's Benchmarks section (and these notes) were rewritten from one full local run of the comparison suite at
1652e50, built from a clean `git archive` copy with nothing else running, with the README's settings (IterationCount=15,
IterationTime=200ms, LaunchCount=1, WarmupCount=5): under the JIT (238 benchmarks, 25 min 29 s) and right after it under NativeAOT (215
benchmarks, MediatR's 23 n/a, 21 min 58 s), both exit 0, after `--verify` passed in both builds (238; 215 and 23 n/a). Every existing
table is refreshed, the real-world scenarios have three tables, and the NativeAOT section shows the NativeAOT times beside the JIT times.
The reports are in the session scratchpad (`bench-final/jit`, `bench-final/aot`); the allocations are the exact bytes per call from the
logs' `// GC:` lines, since the CSV reports round a column to KB once every row in it is 1 KB or more. The run found two open questions
(below): OQ-74, Keryx's `IMediator` rows with a scope per request pay for the forwarding registration's scope entry (360 B and 800 B per
publish scope, confirmed with a probe), and OQ-75, the benchmark behaviors' `typeof(T).Name` allocates under NativeAOT (96 B per behavior
row) while the direct call uses `nameof`. Generic `Stream` at depth 0 (decision 22) did not show its slowdown (open-questions, "Generic
`Stream` at depth 0 is tracked for the release").

## Done: transient `IMediator`, `ISender` and `IPublisher` forwarders (owner decision 35, 2026-11-27)

OQ-74 is decided: `AddKeryx()` registers the three interfaces as transient factories that forward to the scoped `KeryxMediator`
(`TryAddTransient<IMediator>(static serviceProvider => serviceProvider.GetRequiredService<KeryxMediator>())`), so a scope still has one
mediator and resolving an interface adds no entry to it. The 34 Verify snapshots that contain `AddKeryx()` change in its doc summary and
the three registration lines only. `HostRuntimeTests` checks the lifetimes, one instance per scope behind all four service types,
different instances across scopes and scope validation from the root; a new allocation test asserts that a new scope resolving
`IMediator`, `ISender`, `IPublisher` or all four, or resolving one of them and then sending or publishing through a handler with a scoped
service, allocates the same as the same work starting from `KeryxMediator` (with the scoped forwarders, all four allocated 736 B per scope
against 376 B and every send and publish row 808 B against 448 B; a single interface resolved alone does not cross the scope's capacity
boundary and matches either way). A new test pins the trade-off found while doing it, OQ-76 (decided: decision 38 accepts it):
`ValidateOnBuild` no longer reports
a singleton that injects one of the interfaces, which fails when first resolved instead. 1,576 tests pass (788 per target framework).
Measured in decision 37's run (below): the `IMediator` rows with a scope per request now allocate what the typed rows allocate.

## Done: benchmark probe compiled out, `MessageName<T>`, real-world setup rules (owner decisions 33, 34 and 36, 2026-11-27)

- **OQ-72** (decision 33): the real-world setup is confirmed as implemented, including the behavior-scenario baseline that does the
  behaviors' work inline, the singleton SQLite connection, the separate notification-behavior pair and the Mediator parallel project; the
  rules are in the ADR's Benchmarks section ("Real-world setup rules"), beside OQ-51's. No code changed for it.
- **OQ-73** (decision 34): the builds BenchmarkDotNet makes (JIT and NativeAOT) have no verification probe. `Program.cs` sets
  `KeryxBenchmarksProbe=false` in its environment before BenchmarkDotNet builds; under it `Keryx.Benchmarks.Common` defines
  `KERYX_BENCHMARKS_NO_PROBE`, `Probe.Enabled` is a `const` `false` and `Probe.Hit()` is empty. `--verify` builds without it (JIT and the
  NativeAOT publish) and keeps counting; `Probe.EnsureCounting()` fails a verify run of a probe-less build. Checked with `--keepFiles`:
  BenchmarkDotNet's JIT build has `Enabled` as a literal and a one-`ret` `Hit`; its NativeAOT build has no `Probe` static constructor or
  static-base helper, and the baseline's `PingHandler.Handle` is 80 B of code against 128 B in the `--verify` publish, which keeps the
  class-initialization check, the field load and the branch. Enforced on every run since: `ComparisonBenchmark.Setup()` (the shared
  `[GlobalSetup]`) throws unless `Probe.IsCompiledOut()`, so the CI Dry jobs (JIT and NativeAOT) fail if the probe comes back; `--verify`
  calls `CreateSubjects()` instead.
- **OQ-75** (decision 36): `MessageName<T>.Value` in `Keryx.Benchmarks.Common` is the name every real-world logging behavior (Keryx,
  Mediator scoped, MediatR, Immediate.Handlers, ZeroAlloc.Mediator) and the baseline (with its concrete types) pass to `RequestLog`.
- `--verify`: 238 passed under the JIT (net10.0 and net11.0), 215 passed and 23 n/a in the NativeAOT publish; full Dry runs pass (JIT 271
  benchmarks, NativeAOT 242); 1,576 tests pass; no snapshot changed. Measured in decision 37's run (below).

## Done: the second full benchmark run (decision 37, 2026-11-27)

No code changed: the README's Benchmarks tables and notes were refreshed from a full local run of the comparison suite at 3575a50 (after
decisions 34 to 36), built from a clean `git archive` copy, with the README's settings, under the JIT and then NativeAOT, after
`--verify` passed in both builds (238; 215 and 23 n/a). Another session on the machine built, tested and benchmarked during the run, so
the five JIT classes it overlapped were run again on their own and the NativeAOT run went one class at a time, each waiting until no
other build, test or benchmark was busy and repeated when one started during it; every kept run had none (open-questions, "Full
benchmark run", second run). What it shows: Keryx's `IMediator` rows with a scope per request allocate what its typed rows allocate
(OQ-74; 560 B and 1,336 B for the per-request publishes, against 920 B and 2,136 B before); under NativeAOT, without the probe, Keryx's
typed `Send` measures 1.40 to 1.41 ns at every depth and the direct call 1.34 to 1.35 ns (OQ-73); the NativeAOT behavior rows allocate
what they allocate under the JIT (OQ-75). It raised OQ-77: BenchmarkDotNet 0.15.8 cannot measure the direct call's NativeAOT `Publish`
to one handler with the job (its pilot reads 0 ns and throws), so that cell was from a run of that benchmark alone with a fixed
invocation count (decided since: decision 39, below, puts the count in the runner's config). The reports are in the session scratchpad
(`bench-final-2/jit`, `bench-final-2/aot`); a script checks that every README table row matches the generated tables.

## Done: OQ-76 and OQ-77 decided (owner decisions 38 and 39, 2026-11-27)

- **OQ-76** (decision 38): the trade-off of the transient forwarders is accepted, as MediatR's is: `ValidateOnBuild` does not report a
  singleton that injects `IMediator`, `ISender` or `IPublisher`, and with `ValidateScopes` it fails when first resolved. No code changed
  and no generator warning; the caveat stays in the ADR (Generated host, DI registration) and the README (Hosts), and
  `HostRuntimeTests.A_Singleton_Taking_A_Mediator_Interface_Passes_Build_Validation_And_Fails_When_Resolved` pins it.
- **OQ-77** (decision 39): `Program.cs`'s config gives the NativeAOT job of `PublishTo1.Baseline` a fixed invocation count,
  160,000,000 calls per iteration, which skips BenchmarkDotNet's pilot (`Comparison/FixedInvocationCount.cs`). BenchmarkDotNet 0.15.8
  has no per-case job hook, so it is an orderer, through which every benchmark case passes before it is built; it replaces that one
  case and computes logical group keys and the summary order from the original cases, so the row stays in its class's group and is its
  baseline. It leaves JIT jobs, jobs that are not `Throughput` (the Dry job) and jobs with `--invocationCount` given as they are.
  Proved: `PublishTo1` under NativeAOT with the README's settings, waiting for a quiet machine and watched throughout (no other build,
  test or benchmark ran), exit 0, 1 min 47 s; the Baseline row measured 0.0427 ns (0 B, ratio 1.02 against itself; the other rows'
  ratios against it), with 15 iterations of 160,000,000 calls each taking 187.6 to 190.5 ms (5.7 to 8.6 ms after the overhead), and
  the other eight rows on the usual pilot-chosen counts. Dry runs of `PublishTo1` under the JIT and NativeAOT still run one invocation
  per benchmark. The README's NativeAOT cell is now 0.04 ns (0.06 ns before). Build, 1,576 tests and `--verify` (238, net10.0 and
  net11.0) pass. The reports are in the session scratchpad (`oq77/aot-measured-1`, `oq77/dry-jit`, `oq77/dry-aot`).

## Done: a transient mediator over a scoped state (owner decisions 46 and 47, with 48, 2026-11-28)

`KeryxMediator` is a transient 32 B object (the provider and a lazily resolved `__state`); `AddKeryx()` registers it and `IMediator`,
`ISender` and `IPublisher` as `sp => new KeryxMediator(sp)`, and the pipeline contexts live in a scoped `KeryxMediator.__KeryxScopeState`,
one `object?` field per pipeline with a context, which every mediator of the scope shares and the first call that needs a context
resolves. Mediator identity is per resolve, contexts are per scope; a singleton that captures a mediator fails at its first context
use under `ValidateScopes` (decision 35 and OQ-74 superseded in part, decision 38 and OQ-76 changed in shape). Decision 47's slot cache
(four write-once slots and an overflow) was built and measured: 40 B less per scope with a context, but 2 to 4 ns slower on the
long-lived generic `Send` with behaviors in three interleaved rounds, so the per-field layout stays and the bound is OQ-78 (open).
Measured (JIT, the README's job, against a run of the code before it): `RealWorldPingScopePerRequest` 448 to 192 B, 55.4 to 29.9 ns
typed and 67.4 to 34.8 ns through `IMediator` (the direct call 368 B, ZeroAlloc.Mediator 208 B); scopes that use one context 24 B more
and about 7 to 13 ns slower (the state's resolve); the long-lived rows unchanged within noise. `HostRuntimeTests`,
`HostAllocationTests` and `HostConcurrencyTests` pin the new guarantees (several mediators and several pipelines racing in one scope,
requests, streams and publishes, each state field holding its own pipeline's context); 41 snapshots change in the mediator and
`AddKeryx()` only, and one is new. Build, 1,582 tests and `--verify` (238 under the JIT, net10.0 and net11.0, on both variants and on
the final tree) pass. Details, numbers and how the runs were kept quiet: open-questions, "The mediator is transient over a scoped state"; the reports are
in the session scratchpad (`rework-scope/state/`).

## Decisions for the owner

**OQ-78** is open ([open-questions.md](adr/open-questions.md)): the scope state has one field per pipeline with a context (8 B each, in
every scope that uses a context) because decision 47's slot cache measured slower on the long-lived path; the proposal keeps the fields
up to a threshold and measures a lighter slot read or chunked pages above it. Until it is decided, decision 41's 25/50/100-handler
benchmarks would add 8 B per new pipeline with a context to every Keryx scope-per-request row of the shared benchmark host.

OQ-76 (decision 38: the `ValidateOnBuild` trade-off
accepted, no generator warning) and OQ-77 (decision 39: a fixed invocation count for the direct call's NativeAOT `Publish` to one
handler) are decided (above), as are OQ-74 (decision 35), OQ-72 (decision 33), OQ-73 (decision 34) and OQ-75 (decision 36), and
decision 37's run is done.

OQ-65, OQ-66 and OQ-67 (decision 16), OQ-68 (decision 18), OQ-69 (decision 19), OQ-70 (decision 20), OQ-71 (decision 21), OQ-59
(decision 23: `ExcludeHandler` names only notification handlers, a request handler is KRX039), OQ-60 (decision 24: no host KRX005), OQ-61
(decision 25), OQ-62 (decision 26), OQ-63 (decision 27: a fixed `ServiceProvider` field) and OQ-64 (decision 28: stable service
numbering) are decided and applied; decision 22 keeps the generic `Stream` depth-0 slowdown on the release list below.

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
  pull request; the `native-aot` job (decision 31) installs clang and zlib1g-dev, publishes the suite with NativeAOT, runs `--verify` in
  that binary and runs every benchmark but the JIT-only prototype with `--job Dry --runtimes nativeaot10.0`; and the
  `prototype-disassembly` job runs
  `--filter *PipelinePrototype*` on Linux with `DisassemblyDiagnoser` and uploads the `pipeline-prototype-disassembly` artifact.
- **Real-world benchmark scenarios** (owner decision 30, 2026-11-27): `RealWorldPing`, `RealWorldPingScopePerRequest`,
  `RealWorldBehaviors`, `RealWorldBehaviorsScopePerRequest` and `RealWorldSqlite` run a plain handler, a logging and a validation
  behavior around a handler with a scoped repository, and a SQLite query, from the long-lived scope and with a scope per request; for
  decision 30d, `RealWorldPublish{1,5}ScopePerRequest`, `RealWorldPublishBehavior{1,5}ScopePerRequest` (the notification logging
  behavior, Keryx only), `RealWorldPublishAsync{1,5}` (handlers awaiting the SQLite query, sequential) and `RealWorldPublishParallel{1,5}`
  (each library's parallel strategy) publish a `Pinged` notification to 1 and 5 handlers. 104 benchmarks in all, for every subject that
  has an API for them. `--verify` checks their `Pong` and counts handler, behavior, validator, repository, query and log calls (238
  comparison benchmarks pass on net10.0 and net11.0), and the Dry job, which already runs every benchmark, covers them (a Dry run of
  `*RealWorld*` runs 104). Their per-library setup and n/a cells are in the ADR's Benchmarks section (decision 33, OQ-72). Measured in the
  full runs of decisions 32 and 37 (the README's numbers are decision 37's).
- **NativeAOT benchmark run** (owner decisions 29 and 31, 2026-11-27; open-questions, "NativeAOT benchmark run"): the comparison
  suite runs with BenchmarkDotNet's NativeAOT toolchain (`--runtimes nativeaot10.0`, on its own: side by side with the JIT the
  NativeAOT rows get no ratios) with the same scenarios and subjects. Built and run
  under NativeAOT, MediatR 15 is n/a in every scenario (`AddMediatR` throws at startup with the suite's behaviors; without behaviors the
  container cannot construct MediatR's `Mediator`), and every other subject runs every scenario it runs under the JIT;
  `Comparison/NativeAot.cs` holds the reason, `Subjects` skips MediatR's containers there and the runner's config leaves its benchmarks
  out of NativeAOT jobs. `--verify` runs in the suite published with NativeAOT (`-p:KeryxBenchmarksNativeAot=true`): 215 benchmarks
  pass and 23 are listed n/a; its scenarios are listed (no assembly scan, no trim warnings), and a benchmark run fails when one is missing
  from the list. No trim or AOT warning in Keryx's own code, none suppressed; the benchmark and subject projects build with the trim, AOT
  and single-file analyzers. The Phase 4 prototype is JIT-only (left out of NativeAOT jobs: BenchmarkDotNet rejects its Linux
  `DisassemblyDiagnoser` there). Local Dry runs of every benchmark pass under NativeAOT (242) and the JIT (271), and the NativeAOT job's
  steps pass on Linux (linux-arm64 container: `--verify` 215 passed, 23 n/a; Dry run 242, exit 0). Measured in decision 37's
  run (README, NativeAOT), without the probe (decision 34, OQ-73); one benchmark, the direct call's `Publish` to one handler, has a
  fixed invocation count in its NativeAOT job because BenchmarkDotNet's pilot cannot measure it, set by the runner's config, so a full
  NativeAOT run needs no manual step (decision 39, OQ-77).
- **Performance contract against the comparison run** (README, Benchmarks; decision 37's run, 2026-11-27, at 3575a50): Keryx
  allocates 0 B per `Send` with a synchronous handler at 0, 1, 3 and 8 behaviors (typed and generic) and per sequential `Publish` to 1
  and 5 handlers; with a yielding `IRequest<T>` handler it allocates what the direct call allocates (the handler's state machine); a
  `Stream` with no behaviors allocates what the handler's iterator allocates, and with behaviors one iterator per stream behavior. The
  one generated async path, `UnitAdapter.ToUnit`, is checked by `SendUnitYieldValueTask` and `SendUnitYieldTask`: typed and `IMediator`
  both allocate 96 B, the same as the handler awaited directly, so the adapter adds 0 B in steady state. The numbers come from one
  machine (Apple M4 Max) with a 15-iteration job, not BenchmarkDotNet's default job; re-run on other hardware before quoting them. The
  tables include the follow-up: generic `Send` with a synchronous handler at 2.69 to 3.37 ns (2.44 to 2.74 ns in decision 32's run,
  3.22 to 3.51 ns before the type patterns), three stream behaviors at 648 B (OQ-65), and the typed `Publish` at 0.44 and 0.48 ns
  (OQ-70). The same allocations hold under NativeAOT, the real-world behavior rows included (the benchmark behaviors' type names added
  96 B there in decision 32's run; read once per type since decision 36, OQ-75), except in the SQLite and async-handler rows, 16 B less
  per query, the same change as the direct call's. With a scope per request, decision 37's run measured Keryx's typed overload at 80 B
  more than the direct call for a plain `Ping` (the scoped `KeryxMediator`), and its `IMediator` rows allocating what the typed rows
  allocate (decision 32's run, with the scoped forwarders decision 35 replaced, measured 360 B and 800 B more for the publishes; OQ-74).
  Since owner decision 46, with a scope per request the typed overload and `IMediator` allocate 192 B for `Ping`, 176 B less than the
  direct call's 368 B (ZeroAlloc.Mediator 208 B), because a scope that uses no pipeline context resolves no scope state; scopes that use
  a context allocate 24 B more than in decision 37's run (1,432 B for the behaviors, 584 B and 1,360 B for the publishes to 1 and 5
  handlers: the 96 B state replaces the 104 B scoped mediator, plus the 32 B transient mediator). Open-questions, "The mediator is
  transient over a scoped state".
- **Publish baseline:** the direct call is non-async with an `async` fallback for handlers that do not complete synchronously; the
  README's Publish table shows 0.01 ns (inlined) and 0.53 ns for 1 and 5 handlers, against Keryx typed at 0.44 ns and 0.48 ns; under
  NativeAOT 0.04 ns (a fixed invocation count, decision 39, OQ-77; from the run that proved it, 0.06 ns in decision 37's run) and 0.67
  ns against 0.68 ns both.

## Before a first release

- Add the `DisassemblyDiagnoser` findings from the `prototype-disassembly` job to OQ-36.
- Mark pull request #1 ready for review after Phase 8 (the handoff's last step, left to the owner).
- Publish to nuget.org: the build workflow pushes packages on a `v*` tag through trusted publishing (`NUGET_USER` secret, `nuget`
  environment). Then set `PackageValidationBaselineVersion` (Directory.Build.props) to the released version.
- Add `icons/keryx-icon-512.png`; the package picks it up when the file exists.
- Keryx depends on a prerelease, `Specular.Scanning.Sources` 0.4.3-beta.2 (pinned exactly); move to a stable version when one ships.
- Re-run the comparison benchmarks (JIT and NativeAOT) on the release candidate and update the README's numbers (its tables are from
  decision 37's run at 3575a50; Keryx's scope-per-request cells predate decision 46, whose numbers are in the README's notes; NativeAOT
  was not measured for decision 46).
- Generic `Stream` at depth 0 (owner decision 22, tracked for the release, not investigated now): after the type patterns (6e7465a)
  `StreamDepth0` through `IMediator` measured 20 to 60 ns (3 to 9%) slower than before, while its typed row, `StreamDepth3` and the
  generic `Stream<int>` dispatch code did not get worse (open-questions, "Requests are sealed"); code placement is the unverified
  suspect. The full runs of decisions 32 and 37 did not show it (`IMediator` 637.8 and 639.2 ns, typed 631.9 and 648.2 ns, direct call
  632.5 and 645.8 ns), but neither was a before-and-after comparison. Check it on the release candidate and decide then whether it
  needs work.
- Build one `net10.0-android` app that references the packed Keryx (decision 25): `KeryxHostPropertyTests` evaluate the targets with
  `AndroidApplication` and `OutputType` set as the Android SDK leaves them, and the SDK's import order was checked in a preprocessed
  project (OQ-61), but no full Android app build with Keryx has run.

## Out of scope for v1

From the ADR's non-goals: runtime-registered handlers, generic request types (use a generic abstract base with closed subtypes), and
behavior methods other than fully open or fully closed. Full type-parameter unification for behaviors and user-defined phases are
future work. Requests handled outside their own assembly (`[assembly: KeryxContracts]`, `[assembly: ExternallyHandled]`) are kept as
designs in [future-requirements.md](future-requirements.md).
