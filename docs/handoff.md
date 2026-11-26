# Keryx implementation brief

Nov 25, 2026 · @David

This brief replaces the original "mediator library" handoff prompt. It is written so an agent with no other context can implement any
single phase from this file plus the ADR.

## Read first

1. [docs/adr/0001-keryx-mediator.md](adr/0001-keryx-mediator.md): the authoritative design. If this brief and the ADR disagree, the ADR
   wins.
2. [CONTEXT.md](../CONTEXT.md): the glossary. Use its terms.
3. [docs/adr/open-questions.md](adr/open-questions.md): decided points and their history, and the open ones with their interim
   behavior. Add new questions under Open with an interim behavior.

If the ADR is ambiguous, or you believe part of it is wrong, do not deviate silently. Add an entry to `open-questions.md` with the
question, the interim behavior you are using and a proposal, continue with the safest option, and say so in your phase summary.

## Summary of the design

- Library name **Keryx**; namespace `Keryx`; generated-code-only types in `Keryx.Internal`; diagnostics `KRX001`+.
- One NuGet package `Keryx`: `Keryx.dll` (`netstandard2.0;net10.0;net11.0`) plus `Keryx.Analyzers.dll` (`netstandard2.0`; generators,
  analyzers and code fixes in one assembly) and `System.Text.Json.dll` under `analyzers/dotnet/cs`. `Keryx.dll` depends on
  `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.x for `net10.0`, 11.0.x for `net11.0`, 8.0.x for `netstandard2.0`).
  `build/Keryx.props` defaults `LangVersion` to `latest`; Keryx needs C# 11 or later in every project that references it (KRX022).
- Handlers are `private static Handle`/`HandleAsync` methods on `partial` classes (`public` outside a host). The generator emits the other
  half: a constructor for injected dependencies, a public `InvokeAsync` (handler only, no behaviors) and a `[KeryxHandler]` manifest
  attribute.
- `[assembly: KeryxHost]` (net10.0+) gets an internal scoped `KeryxMediator : IMediator` (`ISender` + `IPublisher`) with typed overloads,
  exact-type dispatch with `Unsafe.As`, and an internal `AddKeryx()` that registers everything with generated factory lambdas.
- Behaviors are `static` classes marked with kind attributes (`[RequestBehavior]`, `[StreamBehavior]`, `[NotificationBehavior]`); for each
  kind the class declares one `static` `Handle`/`HandleAsync` behavior method, fully open (its message type parameter constrained to the
  kind's message interface) or fully closed, called directly by the generated pipeline. Attached per target, per assembly or globally;
  ordered by `Phase` (with `Caching = 450`), `Order`, `RunsAfter`/`RunsBefore` (`typeof` form, naming the class), type name. No behavior
  instance exists: pipelines are flat link structs over a per-scope pipeline context that holds the handler and the behaviors' injected
  services.
- Streams: a `[StreamBehavior]` wraps the stream (Immediate.Handlers `StreamingBehavior` model with a struct `IStreamNext<TItem>` link:
  get the inner stream from `next`, re-yield it with `await foreach`).
- Notifications: zero or more handlers, polymorphic, `[HandlerOrder]`, sequential by default or `[PublishStrategy(Parallel)]`, run all
  then `AggregateException`, notification behaviors (`[NotificationBehavior]`, once per publish); handlers are called without
  per-handler pipelines.
- Cross-assembly: the host reads `[KeryxHandler]` types, global behavior classes and request types from references with
  `Specular.Scanning.Sources`, caching the projected manifest per MVID in Specular's ScanCache. A host marks request types handled
  outside it with `[assembly: ExternallyHandled(typeof(X))]`.

## Repository layout

Follow the current Specular layout (`RocketSurgeonsGuild/Specular`, `origin/main`, commit 92746cd). A shallow clone may exist at
`/private/tmp/claude-501/-Users-david-Development-RocketSurgeonsGuild-Keryx/017970f3-2a4b-4b8c-befb-9898fc1dffd8/scratchpad/specular-src`;
if not, clone `https://github.com/RocketSurgeonsGuild/Specular` (depth 1) into a scratch directory. Do **not** use the Indago-era Specular
worktree branch (Nuke, husky); it is outdated.

```
global.json                     .NET 11 SDK 11.0.100, rollForward latestFeature, MTP test runner, Rocket.Surgery.Sdk msbuild-sdk
Directory.Build.props/.targets  Rocket.Surgery.Sdk import, package metadata, TreatWarningsAsErrors=true (everywhere, always)
Directory.Packages.props        CPM, every package pinned exactly
NuGet.config, GitVersion.yml, GitReleaseManager.yaml, .editorconfig, LICENSE, Readme.md
.config/mise.toml, .config/hk.pkl
build/Build.cs                  ModularPipelines file-based build, `mise run build`
.github/workflows/              hand-written (build, lint, release-drafting), modelled on Specular's
Keryx.slnx
src/Keryx/                      Keryx.dll + packaging; packs Keryx.Analyzers (its GetKeryxPackageFiles target) into analyzers/dotnet/cs
src/Keryx/build/Keryx.props      LangVersion default (latest), CompilerVisibleProperty RootNamespace;TargetFrameworkIdentifier;
                                TargetFrameworkVersion, packed to build/
src/Keryx.Analyzers/            generators + DiagnosticAnalyzers + CodeFixProviders (CodeFixes/; Workspaces, RS1038 suppressed),
                                AnalyzerReleases.*.md
test/Keryx.Tests/               TUnit, TUnit.Assertions.Should, Verify.TUnit, Rocket.Surgery.Extensions.Testing.SourceGenerators
test/Keryx.Benchmarks/          BenchmarkDotNet; Prototype/ for the Phase 4 spike; comparison libraries in their own projects
samples/hosts/, samples/libraries/
docs/adr/, CONTEXT.md
```

Do not copy Specular's docs site, multi-Roslyn `*.supports` projects, Uno/MAUI/Blazor samples, icons or spec-kit files.

## Engineering rules

- `IIncrementalGenerator` only. Pipeline models are value-equatable records with equatable arrays (reuse Specular's `EquatableArray` once
  the package is referenced). No `Compilation`, `ISymbol`, `SyntaxNode` or `Location` in cached models. Every step has a tracking name.
- Emit generated sources with `RegisterSourceOutput`, never `RegisterImplementationSourceOutput`.
- The analyzers never reference `Keryx.dll`. Keryx types are found by metadata name through one `KnownTypeNames` class, covered by a test.
- Code fixes live in `Keryx.Analyzers` (its `CodeFixes` folder). Never touch a code-fix type, or any Workspaces type, from a generator or
  analyzer code path: the command-line compiler does not ship Workspaces (ADR, Package and assemblies).
- Read `[BehaviorOrder]`, `[RunsAfter]`, `[RunsBefore]` and `[GlobalBehavior]` from the behavior class and its behavior methods through
  one helper: a method's attribute overrides the class's for that method's kind, and `typeof(X)` names the class (ADR, Ordering).
- Two generators: `HandlerGenerator` (every project; never reports diagnostics, silently skips malformed input) and `HostGenerator`
  (only with `[assembly: KeryxHost]`; reports the host-only rules, carrying an equatable `LocationInfo` instead of `Location`).
  Per-assembly rules come from `DiagnosticAnalyzer`s; KRX001, same-assembly KRX005 and in-source KRX008 are compilation-end diagnostics
  (tagged `CompilationEnd`).
- Generated code is deterministic, starts with `// <auto-generated/>`, enables nullable, uses `global::` names, and is AOT and trimming
  safe. `[GeneratedCode("Keryx", version)]` goes on generated members of user partial types, never on the partial declaration; type level
  only on types Keryx owns. Hint names are the sanitised fully qualified metadata name of the handler class, a hash of the exact name,
  and `.Keryx.g.cs` (ADR, Incremental generation rules). No runtime reflection anywhere, including DI activation.
- Warnings are errors everywhere. Fix warnings; suppress only with a justification comment.
- Performance: meet the **Performance contract** in the ADR. Prove it with allocation tests (`GC.GetAllocatedBytesForCurrentThread` over a
  synchronous region after warm-up) and BenchmarkDotNet `[MemoryDiagnoser]` numbers.
- Tests: generator output as Verify snapshots; analyzer and code-fix tests through `Rocket.Surgery.Extensions.Testing.SourceGenerators`;
  runtime behavior tests compile and run generated code.

## Git workflow

- The ADR, this brief, `CONTEXT.md` and `open-questions.md` are the initial commit on `main`, pushed to
  `git@github.com:RocketSurgeonsGuild/Keryx.git`.
- All phases happen on one feature branch, one commit per phase, each commit passing `mise run build` (build + tests). Commits go through
  the hk hook; never `--no-verify`. Formatting changes from the hook belong in the phase commit.
- After Phase 0, push the branch and open a **draft** PR to `main`, so CI runs on every later phase. Mark it ready after the last phase
  (Phase 9). Never merge it.
- Continue from one phase to the next without waiting. A failed Phase 4 gate does not stop work (decided 2026-11-26): record the numbers,
  commit Phase 4 and continue.
- After each phase, give a short summary: what was built, test status, decisions made, entries added to `open-questions.md`.

## Phases

The order is fixed. Phase 3 should build the host on a handler model (request type, response type, handler class, kind, injected
parameters, attached behaviors) that Phase 7 can fill from manifests without rewriting Phase 3's emitter.

### Phase 0: scaffold

Solution, all projects (empty), Directory.Build/Packages props, global.json, mise/hk config, `build/Build.cs`, GitHub workflows, `.editorconfig`,
`AnalyzerReleases.Shipped.md`/`Unshipped.md`. `mise run build` succeeds with one trivial test per test project. Packing produces a `Keryx`
package with the analyzer assembly in `analyzers/dotnet/cs`.

**Done when:** clean clone → `mise run build` passes locally; the draft PR's CI passes.

### Phase 1: abstractions

Every public type in the ADR's Core types, Behaviors, Ordering, Notifications and Attribute reference sections, plus `Keryx.Internal`
(`KeryxHandlerAttribute`, `KeryxManifestAttribute`, `HandlerKind`, `UnitAdapter`), for `netstandard2.0;net10.0;net11.0`. XML docs on
every public member. `UnitAdapter.ToUnit` allocates nothing when the source has already completed.

_Note (2026-11-26): the behavior interfaces and generic `RunsAfter<T>`/`RunsBefore<T>` built here are replaced in Phase 9._

**Done when:** the abstractions compile for all three TFMs; unit tests cover `Unit` equality and `UnitAdapter` (including a zero-allocation
test on net10+); the `KnownTypeNames` test exists (it may start with an empty list).

### Phase 2: handlers, diagnostics, code fixes

Handler-candidate selection, handler partial generation (constructor, `InvokeAsync`, return adaptation, keyed/default parameter copying,
static invoker, `[KeryxHandler]` and `[assembly: KeryxManifest(FormatVersion = 1)]`). Analyzer rules KRX001–KRX007, KRX011–KRX018,
KRX021 and KRX022, and the shape checks for notification handlers (KRX040, KRX041). KRX022 (language version below C# 11) is reported
once per compilation from the parse options; with it, the handler generator emits nothing for that project (no manifest, no partials),
so the only error the user sees is KRX022. Code fixes for KRX001 (both actions), KRX002, KRX004 and KRX014, in
`src/Keryx.Analyzers/CodeFixes`.

**Done when:** snapshot tests cover every handler shape (nested/top-level, static/instance, every return type, keyed and default
parameters, notifications including polymorphic parameters); every diagnostic has a positive and a negative test (KRX022: C# 10 is
reported, C# 11 and `latest` are not); every code fix has a
before/after test; the incremental test (ADR, Incremental generation rules) passes for the handler pipeline.

### Phase 3: host, request/response, no behaviors

`[assembly: KeryxHost]`, KRX031 (via `build/Keryx.props` and the `System.Runtime` fallback), `KeryxMediator` (scoped, lazy pipeline
contexts created with `Interlocked.CompareExchange`), typed overloads (`IRequest` overloads return `ValueTask<Unit>`), generic `Send`
with exact-type dispatch, `Unsafe.As` result reinterpretation, `FrozenDictionary` above the threshold, `KeryxHandlerNotFoundException`,
`AddKeryx()` with factory registrations (including value-type and keyed optional parameters), host namespace from `RootNamespace` with
the sanitised assembly-name fallback, KRX030 for the host's own request types. Same-assembly handlers only. Like the handler generator,
`HostGenerator` emits nothing when the parse options are below C# 11, so KRX022 stays the only error a host with an old `LangVersion` sees.

**Done when:** runtime tests send through typed and generic paths (value-type and reference-type responses, `IRequest`, struct requests);
allocation tests show 0 B after warm-up for synchronous handlers (class requests on both paths; struct requests on the typed path); a
concurrency test covers concurrent first use in one scope; a micro-benchmark compares lazy pipeline contexts against
`GetRequiredService` per call and sets the dispatch threshold (record the numbers in `open-questions.md` OQ-15); a host with
`LangVersion` 10 reports KRX022 and generates no host sources.

### Phase 4: behaviors

First, the gate: a **hand-written** prototype of a depth-1/3/8 struct pipeline (flat links, concrete behavior calls) in
`test/Keryx.Benchmarks/Prototype`, measured against the ADR's Phase 4 gate criteria. If it fails, do not stop (decided 2026-11-26): record
the measured numbers and the `DisassemblyDiagnoser` findings in a new `open-questions.md` entry, report them in the phase summary, and
carry on with the struct-based pipeline as designed; commit Phase 4 and continue to Phase 5. Do not switch to the hook fallback on your
own.

_Note (2026-11-26): this phase built interface-based instance behaviors, KRX020 and both ordering forms; Phase 9 replaces them with
static behaviors and the `typeof` form only._

Then: behavior shapes (KRX019, KRX020), three scopes (per target, `[assembly: Behavior]`, `[GlobalBehavior]` from the host's own
compilation), constraint evaluation (ADR, Matching), KRX009, KRX042, ordering with KRX008, `ExcludeBehavior`, KRX035, KRX036 for
`ExcludeBehavior`, closed concrete registrations and generated flat link structs for request/response pipelines. Ordering reads
`RunsAfter`/`RunsBefore` in both forms, `[RunsAfter(typeof(T))]` and the generic `[RunsAfter<T>]` (likewise `RunsBefore`), through one
helper, so the sort never sees which form was written.

**Done when:** snapshot tests for pipelines of depth 0–8; ordering tests for every sort rule and every KRX008 case, each run with the
`typeof` form, the generic form and the two forms mixed on one class, with identical results; constraint tests for
each constraint kind; allocation tests at depths 1, 3 and 8 show 0 B; gate numbers recorded in the phase summary and, when the gate
failed, in the new `open-questions.md` gate-results entry. A failed gate does not block this line: if criterion 1 (0 B at depths 1, 3
and 8) failed, follow `open-questions.md` OQ-22 for the 0 B allocation tests.

### Phase 5: streaming

Stream dispatch (typed and generic), no-behavior pass-through (the handler's enumerable returned as is), `IStreamPipelineBehavior` wrap
behaviors called on the concrete type through flat `IStreamNext<TItem>` link structs, `IStreamHookBehavior` hooks fused into one generated
iterator per run of adjacent hook behaviors with the ADR's hook semantics, both kinds sorted in one list, the token rules (two tokens
linked once, at the outermost element), synchronous validation of null and unknown requests. Follow `open-questions.md` OQ-21 for
exceptions raised outside a fused segment.

_Note (2026-11-26): hook behaviors and fused hook segments are removed in Phase 9; stream behaviors become static `[StreamBehavior]`s._

**Done when:**

- runtime tests cover wrap behaviors (re-yield, filter, map, calling `next` twice, not calling it), hook order, item dropping, early
  dispose, exceptions from the handler, from hooks and from wrap behaviors, cancellation through both tokens with a hook segment and with
  a wrap behavior outermost, and wrap behaviors at the start, middle and end of a run of hooks (segment splitting);
- a non-iterator wrap behavior inside a fused segment that throws synchronously from `Handle`: the segment's hooks run `OnErrorAsync`
  (inner → outer) and the exception is rethrown;
- an outermost non-iterator wrap behavior that throws in `Handle`: the exception comes from the `Stream()` call itself;
- an exception thrown by an outer wrap behavior, and one thrown by a hook of an outer segment, makes the inner segment's hooks run
  `OnCompleteAsync` (not `OnErrorAsync`) and still reaches the consumer (OQ-21);
- ordering tests where wrap and hook behaviors interleave by `Phase`, `Order` and `RunsAfter`/`RunsBefore`, with snapshots of the
  resulting segment split;
- KRX019 analyzer tests, positive (a class implementing both `IStreamPipelineBehavior<,>` and `IStreamHookBehavior<,>`; an `abstract`
  wrap behavior; a `virtual`, `abstract` or `override` `Handle<TNext>`; a `virtual` hook method) and negative (a sealed class implementing
  exactly one of the two);
- scope tests (per target, `[assembly: Behavior]`, `[GlobalBehavior]`) and constraint-matching tests for open generic behaviors of each
  stream kind, substituted with `TRequest`/`TItem`;
- generator snapshot tests of wrap-only, hook-only and mixed stream pipelines: the `StreamLink_k` structs, one segment iterator per run
  of adjacent hooks, and `Handle` called on the concrete type;
- allocation tests show 0 extra with no behaviors, one iterator per wrap behavior plus one per fused hook segment per `Stream` call, and
  0 per item; the two-token test runs with (a) a hook segment outermost and (b) a wrap behavior outermost over an inner fused hook
  segment, each asserting exactly one linked `CancellationTokenSource` in total when two distinct cancellable tokens are supplied, and
  none when one token is `default` or the two are equal.

### Phase 6: notifications

Publish dispatch (typed and generic), handler lists with polymorphic expansion and `[HandlerOrder]`, sequential and parallel strategies,
run-all-then-`AggregateException` (also for one failure and on cancellation), `INotificationPipelineBehavior` (per publish, also with zero
handlers) and `INotificationHandlerPipelineBehavior` (per handler) with all three scopes, boxing-free generic `Publish` for struct
notifications, KRX037. Parallel publish exactly as the ADR's Publish strategy section defines it. Follow `open-questions.md` interim
behavior for OQ-3, OQ-4, OQ-5, OQ-17 and OQ-18.

_Note (2026-11-26): in Phase 9, `INotificationPipelineBehavior` becomes the static `[NotificationBehavior]` and
`INotificationHandlerPipelineBehavior` (per-handler pipelines) is removed._

**Done when:** tests cover zero/one/many handlers, polymorphic handlers, ordering, both strategies, failures in several handlers,
cancellation, and both behavior kinds; allocation tests show 0 B for a sequential publish and for a parallel publish with synchronous
handlers.

### Phase 7: cross-assembly discovery

Reference `Specular.Scanning.Sources` 0.4.3-beta.2 (exact, `PrivateAssets=all`) plus `System.Text.Json` and
`System.Collections.Immutable` as it requires; ship `System.Text.Json.dll` beside the analyzer; let Specular's pack helper add its cache
targets to the package. One `context.Scan<KeryxReferenceModel>("Keryx", requests, transform)` call over the three scans in the ADR
(an `IncrementalValuesProvider<ScanRequest>`, results read by position); projection cached in ScanCache with a source-generated
`JsonSerializerContext` and the `ProjectionDataVersion` constant. Merge own and referenced handlers into the Phase 3 model. Host rules
KRX005 (cross-assembly), KRX030 (referenced types, skipping types declared in another host), KRX032, KRX033, KRX034, `ExcludeHandler`
with KRX036 for `ExcludeHandler`. Samples: `samples/hosts/` (an ASP.NET Core or console host) and `samples/libraries/`
(a contracts library with `[assembly: KeryxContracts]` and a handler library with handlers, a global behavior and notification
handlers). CI job: publish the sample host with `PublishAot=true`, fail on IL2xxx/IL3xxx, run it.

_Note (2026-11-26): KRX030 no longer skips request types declared in another host (OQ-16); Phase 9 changes it._

**Done when:** the samples build and run end to end; tests cover a host with project and metadata references, a missing reference
(KRX034), a newer format version (KRX032), a Keryx reference without a manifest (KRX033), duplicate handlers (KRX005), exclusion
(including KRX036 for `ExcludeHandler`), and a test host that references an app host;
the incremental test shows downstream steps cached after an unrelated edit and `Modified` after a manifest change; the AOT job passes.

### Phase 8: benchmarks, README, notes

The benchmark suite in the ADR's Benchmarks section (baseline, Keryx, Mediator 3.0.2 scoped and singleton, MediatR 15.x without a key,
Immediate.Handlers 4.4.0, ZeroAlloc.Mediator 7.0.1), each library in its own project. README: install, minimum SDK (.NET 10), handler
rules, behaviors, streaming, notifications, hosts, contracts assemblies, testing handlers through `InvokeAsync`, diagnostics table with
help links, benchmark results from a local run with machine details. A short "what is left" note in `docs/`.

_Note (2026-11-26): the Keryx hook-behavior stream variant of the comparison benchmarks is dropped in Phase 9, and the PR stays a
draft until Phase 9._

**Done when:** benchmarks run locally and in CI with `--job Dry`; the README is complete.

### Phase 9: static behaviors

Rework the behaviors built in Phases 4 to 6 into the static-behavior design the ADR now describes (open-questions "Static behaviors",
"Stream hook behaviors removed", "One notification behavior kind" and "Message constraint on open behavior methods", 2026-11-26), and
fold in the other decisions of that day (OQ-8 to OQ-54; OQ-55 is open, so implement its interim). Read the ADR's Behaviors,
Ordering, Streaming, Notifications, Host control and Performance contract sections first; they are written for this design.

- **Public API.** Remove `IRequestPipelineBehavior`, `IStreamPipelineBehavior`, `IStreamHookBehavior`, `INotificationPipelineBehavior`,
  `INotificationHandlerPipelineBehavior` and the generic `RunsAfterAttribute<T>`/`RunsBeforeAttribute<T>`. Keep `INext<T>`,
  `IStreamNext<T>`, `INext` and `Phase`. Add the three kind attributes `RequestBehaviorAttribute`, `StreamBehaviorAttribute` and
  `NotificationBehaviorAttribute` (no per-handler notification kind) and the host attribute `ExternallyHandledAttribute`
  (`[assembly: ExternallyHandled(typeof(X))]`, `AllowMultiple`). `GlobalBehavior`, `BehaviorOrder`, `RunsAfter` and `RunsBefore` also
  target methods. Update the public API snapshot and `KnownTypeNames`.
- **Package.** `Keryx.csproj` references `Microsoft.Extensions.DependencyInjection.Abstractions` per TFM (10.0.x for `net10.0`, 11.0.x
  for `net11.0`, 8.0.x for `netstandard2.0`), pinned in `Directory.Packages.props` (OQ-8).
- **Behavior discovery and shapes.** A behavior is a non-generic `static` class with kind attributes, declaring one `static`
  `Handle`/`HandleAsync` behavior method per kind, fully open or fully closed, with the message, `next` and an optional `CancellationToken`
  found by type and every other parameter injected. A method's kind follows from its signature (`next` constraint and message type). An open
  method's message type parameter must be constrained to the kind's message interface (`TRequest : IRequest<TResponse>`,
  `TRequest : IStreamRequest<TItem>`, `TNotification : INotification`); without it the method is KRX019. KRX019 becomes the static-behavior
  shape rule; KRX013 and KRX018 cover behavior method parameters; KRX016 covers behavior classes and methods (public outside a host, at
  least `internal` and not file-local in a host). Retire KRX020 (descriptor, analyzer and `AnalyzerReleases.Unshipped.md`) the way KRX010
  was retired, and update the `AnalyzerReleases.Unshipped.md` descriptions of KRX019 (static-behavior shape), KRX034 (injected
  behavior-method parameter types instead of behavior constructor parameters) and KRX036 (`ExternallyHandled`). KRX042 covers
  `[GlobalBehavior]` on a class without a kind attribute or a method that is not a behavior method, and `[Behavior]` on a notification
  handler method (no effect). Add KRX043 (warning, analyzer): `[assembly: ExternallyHandled]`, `[assembly: ExcludeHandler]` or
  `[assembly: ExcludeBehavior]` in an assembly without `[assembly: KeryxHost]` (OQ-54), with its descriptor, help link and
  `AnalyzerReleases.Unshipped.md` entry; update the KRX042 description there too.
- **Matching and ordering.** `typeof(X)` names the class and resolves to its method of the target's kind; constraints are checked on the
  method's type parameters (`IMethodSymbol.Construct` does not validate them). `ExcludeBehavior` removes a class from every kind.
  The closed forms of exclusion and edges are gone. A method's `[BehaviorOrder]`, `[RunsAfter]`/`[RunsBefore]` list and
  `[GlobalBehavior]` override the class's for that kind. KRX008 is reported once per pair of behavior classes, and once per kind where
  a method overrides the class's ordering.
- **Generation.** The pipeline context resolves each behavior method's injected services (with the handler parameter rules, keyed and
  optional forms included) in its constructor and holds them beside the handler; links call the static method with explicit type
  arguments and the arguments in declared order. `AddKeryx()` registers no behaviors.
- **Streams.** Remove hook behaviors: fused hook segments, hook semantics, their emitter code and tests. Stream behaviors are
  `[StreamBehavior]` wrap behaviors only.
- **Notifications.** `[NotificationBehavior]` replaces `INotificationPipelineBehavior`: it wraps the whole publish once, also with
  zero handlers, and is attached with `[Behavior]` on the notification type, `[assembly: Behavior]` in the notification type's assembly
  or `[GlobalBehavior]`. Remove per-handler notification behaviors entirely: `INotificationHandlerPipelineBehavior`, the per-handler
  pipelines and links the host emits for notification handlers, attaching behaviors from a notification handler method or from
  `[assembly: Behavior]` in the handler's assembly, the polymorphic-handler skipping of attached behaviors, and their tests. Handlers
  in a publish are called directly under both strategies. The handler generator writes no `Behaviors` for a notification handler, and
  the analyzer reports KRX042 for `[Behavior]` on one (OQ-40 is moot).
- **Host.** `ExternallyHandled` turns KRX030 off for the named type; a type the host handles or that is not a request or stream request
  is KRX036 (OQ-10). Outside a host, `ExternallyHandled`, `ExcludeHandler` and `ExcludeBehavior` have no effect and are KRX043
  (OQ-54). KRX030 no longer skips request types declared in a referenced host (OQ-16). The scan also finds behavior classes whose
  `[GlobalBehavior]` is on a method only, and KRX035 is reported for them too. KRX034 covers the injected parameter types of a
  referenced behavior method instead of behavior constructor parameters (OQ-47). A `[Behavior]` on a referenced notification type that
  does not fit the type as the host resolves it follows OQ-55's interim (skipped silently) until OQ-55 is decided. Bump
  `ProjectionDataVersion` for the changed projection.
- **Samples, benchmarks and README.** Rewrite the sample behaviors as static behaviors, and update `samples/hosts/README.md` and
  `samples/libraries/README.md`, which name the generic `LoggingBehavior<,>`, to the new behavior class names and descriptions. Rewrite
  the prototype in `test/Keryx.Benchmarks/Prototype` and the Keryx comparison subject with static behavior methods; drop the Keryx
  hook-behavior stream variant (OQ-51). The benchmarks workflow also runs `--filter *PipelinePrototype*` on a Linux runner with
  `DisassemblyDiagnoser` (OQ-36). Every open behavior method in the samples, the prototype and the comparison subject constrains its
  message type parameter (KRX019). The samples and benchmarks use no notification behaviors today; if any are added, they are
  `[NotificationBehavior]`s (attached to the notification type, per assembly or globally), never per handler. Rewrite the README to the
  ADR: the quick start's `AddKeryx()` sentence (it no longer registers behaviors), behaviors (its table of one interface per pipeline
  becomes the three kind attributes, with `[NotificationBehavior]` wrapping the whole publish and no row for
  `INotificationHandlerPipelineBehavior`; examples with the message constraint; the Attaching bullet's per-target scope becomes a
  request or stream handler method, or the notification type for a `[NotificationBehavior]`, and `[Behavior]` on a notification handler
  method is KRX042), streaming (no hook behaviors), notifications (the bullet that says `INotificationPipelineBehavior` wraps the whole
  publish and `INotificationHandlerPipelineBehavior` wraps each handler becomes: `[NotificationBehavior]` wraps the whole publish, also
  with zero handlers, and handlers have no behaviors of their own), hosts (the `AddKeryx()` bullet registers no behavior types, and the
  scoped-mediator paragraph creates only handler instances, never behavior instances; a host referenced by another host no longer keeps
  its requests out of KRX030; the host-only attributes outside a host are KRX043), the diagnostics table (KRX020 retired; KRX009,
  KRX013, KRX016, KRX018, KRX019, KRX030, KRX034, KRX036 and KRX042 rewritten for static behaviors, the message constraint,
  `[Behavior]` on a notification handler and `ExternallyHandled`; KRX043 added), the benchmark notes about hook behaviors, and the DI
  dependency; then grep it for the removed names (`PipelineBehavior`, `IStreamHookBehavior`, `PublishBehavior`,
  `NotificationHandlerBehavior`, per-handler, per-publish, `RunsAfter<`, hook). Update `docs/whats-left.md`.

**Done when:**

- the five behavior interfaces and the generic ordering attributes are gone from `src/Keryx`, the public API snapshot and
  `KnownTypeNames`; the three kind attributes (`RequestBehaviorAttribute`, `StreamBehaviorAttribute`, `NotificationBehaviorAttribute`)
  and `ExternallyHandledAttribute` exist, no `PublishBehaviorAttribute` or `NotificationHandlerBehaviorAttribute` exists, and the
  `KnownTypeNames` test passes;
- the packed `Keryx` package lists `Microsoft.Extensions.DependencyInjection.Abstractions` in each TFM's dependency group with the
  version above;
- KRX019 has a positive test for each shape case (class not `static`, generic class, kind attribute without a method, two methods of one
  kind, partially open method, extra type parameter, `next` without the `struct` constraint, wrong return type, closed method whose `next`
  does not carry the request's response, a method whose `next` constraint and message type belong to different kinds, and an open method of
  each kind whose message type parameter lacks the kind's message constraint) and negative tests (one class with all three kinds; `Handle`
  and `HandleAsync` side by side; an open method of each kind with the message constraint plus further constraints; a closed method, which
  needs no message constraint); KRX013, KRX016 and KRX018 have positive and negative tests on behavior methods; KRX020 is no longer reported
  anywhere;
- KRX009 (analyzer), KRX035 (including a global behavior marked only on a method), KRX036 for `ExcludeBehavior` and KRX042 (the
  `[GlobalBehavior]` cases and `[Behavior]` on a notification handler method, exact-type and polymorphic) have positive and negative
  tests against static behaviors, and `[assembly: Behavior(typeof(X))]` naming a non-behavior reports nothing;
- KRX043 is reported for each of `[assembly: ExternallyHandled]`, `[assembly: ExcludeHandler]` and `[assembly: ExcludeBehavior]` in
  a library and in a contracts assembly, and not in a host;
- snapshot tests of request, stream and publish pipelines at depths 0, 1, 3 and 8 show static calls with explicit type arguments,
  injected services held by the pipeline context, and no behavior registrations in `AddKeryx()`; publish snapshots show the handlers
  called directly, with no per-handler pipelines or links, under both strategies;
- matching tests cover open and closed methods of every kind, each constraint kind and nullable annotations; ordering tests cover every
  sort rule and every KRX008 case with `typeof` edges, a method-level `[BehaviorOrder]` and `[RunsAfter]` overriding the class's for one
  kind, a two-kind class whose method overrides `[RunsAfter]` (KRX008 reported per kind), and `[GlobalBehavior]` on a method only;
  `ExcludeBehavior` removes a class from pipelines of every kind; the closed-form `ExcludeBehavior`/`RunsAfter` handling and its tests
  are removed;
- the Phase 5 wrap-behavior tests still pass against `[StreamBehavior]`s (re-yield, filter, map, calling `next` twice, not calling it,
  exceptions, a synchronous throw from an outermost non-iterator behavior, cancellation through both tokens with a stream behavior
  outermost); no hook code, test or benchmark variant is left (including `test/benchmark-subjects`);
- the Phase 6 per-publish behavior tests pass against `[NotificationBehavior]`s (all three scopes, zero handlers, both strategies, an
  exception propagating unwrapped, a synchronous throw from the outermost behavior); an `[assembly: Behavior]` naming a
  `[NotificationBehavior]` applies in the notification type's assembly, and in an assembly that declares notification handlers but not
  the notification type it changes no publish pipeline; no per-handler notification behavior code, test,
  snapshot or attribute is left (`git grep` finds no `INotificationHandlerPipelineBehavior`, `NotificationHandlerBehavior` or
  `PublishBehavior` outside `docs/`), and `[Behavior]` on a notification handler method changes no generated code;
- host tests: `ExternallyHandled` silences KRX030 for the named type only, and is KRX036 on a handled type and on a non-request; a test
  host that references an app host reports KRX030 for an app request whose handler is internal and nothing for one whose handler is
  public; a referenced global behavior marked only on a method is found; KRX034 reports a missing injected parameter type of a
  referenced behavior method;
- allocation tests show 0 B per `Send` at depths 1, 3 and 8 (typed and generic) and per sequential publish, with behaviors that take
  injected services and with `async` behaviors whose work completes synchronously, on an optimized (Release) test host; stream
  allocation tests show one iterator per stream behavior, 0 per item, and exactly one linked `CancellationTokenSource` in the two-token
  case;
- the Phase 4 gate is re-run with the static prototype and its three criteria are recorded in the phase summary and in a new
  `open-questions.md` entry (a failed gate does not stop work, as in Phase 4); the comparison benchmarks are re-run locally and the
  README's tables updated; `Keryx.Benchmarks --verify` passes;
- `.github/workflows/benchmarks.yml` has a Linux job that runs `--filter *PipelinePrototype*` with `DisassemblyDiagnoser`; its output
  shows the static behavior method and `next.InvokeAsync` called directly (criterion 2), and is recorded in the new gate entry in
  `open-questions.md`;
- the README's behavior, streaming, notification and host sections show only static behaviors with the three kind attributes, open
  methods with the message constraint, and no interfaces, hooks or per-handler notification behaviors (every hit of the grep for the
  removed names, per-handler and per-publish included, is checked against the ADR); its diagnostics table matches the ADR (KRX020
  retired; KRX009, KRX013, KRX016, KRX018, KRX019, KRX030, KRX034, KRX036 and KRX042 text updated; KRX043 added); it mentions the
  `Microsoft.Extensions.DependencyInjection.Abstractions` dependency; `docs/whats-left.md` is updated;
- the samples build and run end to end, the AOT job passes, and the incremental test still passes;
- `ProjectionDataVersion` is bumped and the incremental test shows the cache invalidated;
- `mise run build` passes and the draft PR's CI is green;
- the PR is marked ready for review (the Git workflow's last step).
