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
  `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.x for `net10.0`, 8.0.x for `netstandard2.0`); `net11.0` gets it from the
  shared framework, so that dependency group is empty (OQ-56).
  `build/Keryx.props` defaults `LangVersion` to `latest`; Keryx needs C# 14 or later in every project that references it (KRX022, OQ-52);
  `netstandard2.0` targets stay supported.
- Handlers are `private static Handle`/`HandleAsync` methods on `partial` classes (`public` outside a host). The generator emits the other
  half: a constructor for injected dependencies, a public `InvokeAsync` (handler only, no behaviors) and a `[KeryxHandler]` manifest
  attribute.
- A host (net10.0+; the `KeryxHost` MSBuild property, `true` by default for executables and test projects) gets an internal scoped `KeryxMediator : IMediator` (`ISender` + `IPublisher`) with typed overloads,
  exact-type dispatch (type patterns on the sealed request types, KRX024) with `Unsafe.As`, and an internal `AddKeryx()` that registers handlers and the mediator with generated factory
  lambdas (nothing for behaviors).
- Behaviors are `static` classes marked with kind attributes (`[RequestBehavior]`, `[StreamBehavior]`, `[NotificationBehavior]`); for each
  kind the class declares one `static` `Handle`/`HandleAsync` behavior method, fully open (its message type parameter constrained to the
  kind's message interface) or fully closed, called directly by the generated pipeline. Attached per target, per assembly or globally;
  ordered by `Phase` (with `Caching = 450`), `Order`, `RunsAfter`/`RunsBefore` (`typeof` form only, naming the class), type name. No
  behavior instance exists: pipelines are flat link structs over a per-scope pipeline context that holds the handler and the behaviors'
  injected services, each distinct service (type and key) once. Generated code is built with `SyntaxFactory`.
- Streams: a `[StreamBehavior]` wraps the stream (Immediate.Handlers `StreamingBehavior` model with a struct `IStreamNext<TItem>` link:
  get the inner stream from `next`, re-yield it with `await foreach`). There are no hook behaviors.
- Notifications: zero or more handlers, polymorphic, `[HandlerOrder]`, sequential by default or `[PublishStrategy(Parallel)]`, run all
  then `AggregateException`, notification behaviors (`[NotificationBehavior]`, once per publish, also with zero handlers); handlers are
  called directly, without per-handler pipelines, and `[Behavior]` on a notification handler method is KRX042.
- Cross-assembly: the host reads `[KeryxHandler]` types, global behavior classes and request types from references with
  `Specular.Scanning.Sources`, caching the projected manifest per MVID in Specular's ScanCache. A request's handler lives in the
  request's own assembly (KRX001 everywhere, hosts included; KRX023 for a handler of another assembly's request); notifications may be
  handled anywhere. The host-only assembly attributes outside a host are KRX043. `KeryxContracts` and `ExternallyHandled` were removed in
  the second rework; their designs are in `docs/future-requirements.md`.

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
.github/workflows/              hand-written (build, lint, release-drafting, aot, benchmarks), modelled on Specular's
Keryx.slnx
src/Keryx/                      Keryx.dll + packaging; packs Keryx.Analyzers (its GetKeryxPackageFiles target) into analyzers/dotnet/cs
src/Keryx/build/Keryx.props      LangVersion default (latest), CompilerVisibleProperty KeryxHost;RootNamespace;
                                TargetFrameworkIdentifier;TargetFrameworkVersion, packed to build/
src/Keryx/build/Keryx.targets    KeryxHost default (Exe/WinExe or IsTestProject: true, else false; the project's value wins), the
                                Specular.Scanning scan cache wiring; buildTransitive/ and buildMultiTargeting/ import build/
src/Keryx.Analyzers/            generators + DiagnosticAnalyzers + CodeFixProviders (CodeFixes/; Workspaces, RS1038 suppressed),
                                AnalyzerReleases.*.md
test/Keryx.Tests/               TUnit, TUnit.Assertions.Should, Verify.TUnit, Rocket.Surgery.Extensions.Testing.SourceGenerators
test/Keryx.Benchmarks/          BenchmarkDotNet; Prototype/ for the Phase 4 gate prototype, Comparison/ for the comparison suite
test/benchmark-subjects/        one small project per compared library (Keryx host, baseline, Mediator, MediatR, ...)
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
- Behaviors are never instantiated and never registered in DI. The generated pipeline context resolves each behavior method's injected
  services (with the handler parameter rules) in its constructor, and links call the static method with explicit type arguments.
- Two generators: `HandlerGenerator` (every project; never reports diagnostics, silently skips malformed input) and `HostGenerator`
  (only when `build_property.KeryxHost` is `true`; reports the host-only rules, carrying an equatable `LocationInfo` instead of `Location`).
  Per-assembly rules come from `DiagnosticAnalyzer`s; KRX001, same-assembly KRX005 and in-source KRX008 are compilation-end diagnostics
  (tagged `CompilationEnd`). Both generators emit nothing when the parse options are below C# 14
  (`KeryxSymbols.MinimumLanguageVersion`), so KRX022 is the only error such a project sees.
- Generated code is deterministic, starts with `// <auto-generated/>`, enables nullable, uses `global::` names, and is AOT and trimming
  safe. `[GeneratedCode("Keryx", version)]` goes on generated members of user partial types, never on the partial declaration; type level
  only on types Keryx owns. Hint names are the sanitised fully qualified metadata name of the handler class, a hash of the exact name,
  and `.Keryx.g.cs` (ADR, Incremental generation rules). No runtime reflection anywhere, including DI activation.
- Generated sources are built with Roslyn's `SyntaxFactory` through the shared helpers in `GeneratedSyntax` (owner decision 5 of the second
  rework; Dovetail's `Helpers.cs`, `Support/ExportDovetails.cs` and `Support/ImportDovetails.cs` are the reference), never as concatenated
  text: syntax only inside `RegisterSourceOutput`, one `NormalizeWhitespace` per compilation unit followed by `GeneratedSyntax.Render`'s
  layout pass (blank lines between members that are not both fields, no space in `T? )` or `T? []`), the header trivia
  (`// <auto-generated/>`, `#nullable enable`, the obsolete-type `#pragma`, which suppresses nothing else) added last, and no stray `;`
  after a type declaration. The `[GeneratedCode]` attributes and the `#nullable disable warnings` islands of oblivious fits (OQ-58) are part
  of the tree.
- Warnings are errors everywhere. Fix warnings; suppress only with a justification comment.
- Performance: meet the **Performance contract** in the ADR. Prove it with allocation tests (`GC.GetAllocatedBytesForCurrentThread` over a
  synchronous region after warm-up; tests of `async` behaviors compile the test host optimized) and BenchmarkDotNet `[MemoryDiagnoser]`
  numbers.
- Tests: generator output as Verify snapshots; analyzer and code-fix tests through `Rocket.Surgery.Extensions.Testing.SourceGenerators`;
  runtime behavior tests compile and run generated code. Test compilations use C# 14 or later. Every test compilation goes through the
  testing library (decided 2026-11-26), the way Dovetail and Specular use it: the shared `GeneratorTestContextBuilder` in a load context
  of the test's own; referenced libraries built with the library itself (`CompileLibraryAsync`, each with a distinct project name, then
  `AddCompilationReferences`), loaded into that context, where a host resolves them by name; compilation options through its builder
  (`WithNullableContext`, `WithAllowUnsafe`, `WithOptimizationLevel`, `WithDiagnosticOption`, and `dotnet_diagnostic.*.severity` as
  global or per-file options); `build_property.*` options through named builder helpers (`BuildProperties`); incremental tests through
  `WithIncrementalTracking()` and `RerunAsync`; `UseParameters` for every snapshotted argument row; one snapshot shape per kind of
  output (host-generator output is `{ Mediator, AddKeryx }` through `HostTest.VerifyHost`, handler-generator, analyzer and code-fix
  results are `Verify(result)`). Only `KnownTypeNamesTests` builds a Roslyn compilation by hand.

## Git workflow

- The ADR, this brief, `CONTEXT.md` and `open-questions.md` are the initial commit on `main`, pushed to
  `git@github.com:RocketSurgeonsGuild/Keryx.git`.
- All phases happen on one feature branch, one commit per phase, each commit passing `mise run build` (build + tests). Commits go through
  the hk hook; never `--no-verify`. Formatting changes from the hook belong in the phase commit. The hook's `dotnet-format` step runs
  `dotnet format whitespace` only (decided 2026-11-26): the style and analyzer passes rewrote code on commit, and the build's analyzers
  (warnings as errors) enforce style instead.
- After Phase 0, push the branch and open a **draft** PR to `main`, so CI runs on every later phase. Mark it ready for review after the
  last phase (Phase 8); that step is left to the owner. Never merge it.
- Continue from one phase to the next without waiting. A failed Phase 4 gate does not stop work (decided 2026-11-26): record the numbers,
  commit Phase 4 and continue.
- After each phase, give a short summary: what was built, test status, decisions made, entries added to `open-questions.md`.

Phases 0 to 8 were first built with interface-based behaviors. The design review of 2026-11-26 replaced them with static behaviors, and
that rework landed as two further commits on the branch (7d2fd01: the C# 14 floor, the DI abstractions dependency, `ExternallyHandled`
and KRX043; f54dc49: static behaviors, hook and per-handler behaviors removed). The owner's second rework followed, one commit per step:
d8ad015 (requests handled in their own assembly, KRX023; `KeryxContracts` and `ExternallyHandled` removed; the sample order requests moved
next to their handlers; whitespace-only pre-commit formatting), 5afb535 (the `KeryxHost` MSBuild property replaces `KeryxHostAttribute`),
4b0658f (every test compilation through the testing library 11.0.2; jump-table dispatch tests), eca51b5 (one context field per shared
service) and 1fddbdd (`SyntaxFactory` emitters, switch-statement dispatch, primary-constructor links). The phases below describe the
resulting design as what each phase builds.

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

- **Behaviors.** The link interfaces `INext<T>`, `INext` and `IStreamNext<T>`, the `Phase` enum, and the three kind attributes
  `RequestBehaviorAttribute`, `StreamBehaviorAttribute` and `NotificationBehaviorAttribute`. There are no behavior interfaces and no
  per-handler notification kind. `next` is a struct type argument constrained to a link interface, never a delegate: typed next
  delegates were rejected (owner decision 4 of the second rework; ADR, Alternatives considered) because a delegate call is indirect and
  not inlined, and a delegate bound to a pipeline context is allocated per scope. `GlobalBehavior`, `BehaviorOrder`, `RunsAfter` and
  `RunsBefore` target classes and methods; `RunsAfter`/`RunsBefore` have only the `typeof` form (a static class cannot be a type
  argument).
- **Host attributes.** `ExcludeBehavior` and `ExcludeHandler`. There is no `KeryxContracts` or `ExternallyHandled`
  (removed in the second rework, 2026-11-26; see `docs/future-requirements.md`), and no `KeryxHost` attribute: a host is the `KeryxHost`
  MSBuild property (Phase 3).
- **Package.** `Keryx.csproj` references `Microsoft.Extensions.DependencyInjection.Abstractions` per TFM (10.0.x for `net10.0`, 11.0.x
  for `net11.0`, 8.0.x for `netstandard2.0`), pinned in `Directory.Packages.props` (OQ-8). The .NET 11 shared framework carries the
  assembly, so the SDK's package pruning drops the reference for `net11.0` and nothing keeps it (OQ-56). The comment in
  `src/Keryx/build/Keryx.props` gives the C# 14 floor as the reason for the `LangVersion` default.

**Done when:** the abstractions compile for all three TFMs; unit tests cover `Unit` equality and `UnitAdapter` (including a zero-allocation
test on net10+); the public API snapshot lists the three kind attributes and no behavior interface, `KeryxContractsAttribute`,
`ExternallyHandledAttribute`, `KeryxHostAttribute`, generic `RunsAfter<T>`/`RunsBefore<T>`, `PublishBehaviorAttribute` or
`NotificationHandlerBehaviorAttribute`; the `KnownTypeNames` test passes; the packed `Keryx` package lists
`Microsoft.Extensions.DependencyInjection.Abstractions` in the `net10.0` and `netstandard2.0` dependency groups with the versions
above, and its `net11.0` dependency group is empty (OQ-56); a target in `Keryx.csproj` checks that in the packed nuspec after every pack.

### Phase 2: handlers, diagnostics, code fixes

Handler-candidate selection, handler partial generation (constructor, `InvokeAsync`, return adaptation, keyed/default parameter copying,
static invoker, `[KeryxHandler]` and `[assembly: KeryxManifest(FormatVersion = 1)]`). Analyzer rules KRX001–KRX007, KRX011–KRX018,
KRX021, KRX022, KRX023 and KRX024, and the shape checks for notification handlers (KRX040, KRX041). KRX001 applies in every assembly, hosts
included; KRX023 (a handler of a request or stream request declared in another assembly, decided in the second rework) is reported on
the handler's request parameter, and the generators skip that handler like any handler error. KRX024 (owner decision 14 of the second
rework) is reported by `MessageTypeAnalyzer` on a request or stream request class or record that is neither sealed nor abstract (or
closed: the `closed` modifier's text on the declaring syntax, `IsClosedTypeAttribute` in metadata, Roslyn 5.0 APIs only), in libraries and
hosts; a handler of an abstract or closed request base is KRX003. KRX022 (language version below C# 14, OQ-52) is
reported once per compilation from the parse options; with it, the handler generator emits nothing for that project (no manifest, no
partials), so the only error the user sees is KRX022. Code fixes for KRX001 (add a handler stub, also in a host), KRX002, KRX004, KRX014 and KRX024 (make the type `sealed`, keeping
`partial` and the other modifiers), in `src/Keryx.Analyzers/CodeFixes`.

**Done when:** snapshot tests cover every handler shape (nested/top-level, static/instance, every return type, keyed and default
parameters, notifications including polymorphic parameters); every diagnostic has a positive and a negative test (KRX022: C# 13 is
reported, C# 14 and `latest` are not, in a `netstandard2.0` project as well); every code fix has a before/after test; the incremental test
(ADR, Incremental generation rules) passes for the handler pipeline.

### Phase 3: host, request/response, no behaviors

The `KeryxHost` MSBuild property (ADR, Opting in): `build/Keryx.targets` defaults it after the project body (`true` for `OutputType`
`Exe`/`WinExe` and for `IsTestProject`, else `false`; a value the project sets wins), `build/Keryx.props` makes it a
`CompilerVisibleProperty`, `buildTransitive/` imports `build/` for transitive consumers, and every generator and analyzer reads
`build_property.KeryxHost` (unset is not a host; tests set it with the `WithKeryxHost()` builder helper). Host diagnostics without a source
location are reported with `Location.None`. KRX031 (via `build/Keryx.props` and the `System.Runtime` fallback), `KeryxMediator` (scoped,
lazy pipeline contexts created with `Interlocked.CompareExchange`), typed overloads (`IRequest` overloads return `ValueTask<Unit>`), generic
`Send` with exact-type dispatch (a switch statement with a type pattern per sealed request type, `case global::X r: return
__Dispatch0<TResponse>(r, cancellationToken);`, ending `default: throw __HandlerNotFound(request.GetType());`, and above the threshold a switch on
the index of `request.GetType()`; switch statements with one `return` per section, never switch expressions, which lose the arms' tail
calls, owner decision 16 and OQ-67; a struct request's section has no designation, `case global::S: return __Dispatch1<TResponse>(request,
cancellationToken);`, and its arm takes `IRequest<TResponse>` and unboxes, so `Send` holds no struct local and every arm stays a tail call;
a request or stream request type that is not sealed, reachable only with KRX024 suppressed, is never composed: no section, no typed overload,
so sending it throws not-found, owner decision 18 and OQ-68; typed overloads of sealed requests have no runtime type check), `Unsafe.As` result reinterpretation, `FrozenDictionary` above
the threshold, `KeryxHandlerNotFoundException`, `AddKeryx()` with factory registrations (including value-type and keyed optional
parameters), host namespace from `RootNamespace` with the sanitised assembly-name fallback. The host's own request types are KRX001's (the
analyzer runs in hosts too), so the host generator does not collect them; KRX030 is Phase 7's, for referenced request types. Same-assembly
handlers only. Like the handler generator, `HostGenerator` emits nothing when the parse options are below C# 14, so KRX022 stays the only
error a host with an old `LangVersion` sees.

**Done when:** runtime tests send through typed and generic paths (value-type and reference-type responses, `IRequest`, struct requests);
allocation tests show 0 B after warm-up for synchronous handlers (class requests on both paths; struct requests on the typed path); a
concurrency test covers concurrent first use in one scope; a micro-benchmark compares lazy pipeline contexts against
`GetRequiredService` per call and sets the dispatch threshold (record the numbers in `open-questions.md` OQ-15); generic `Send` is
tested on both sides of the threshold (decided 2026-11-26): the generated shape (type compares at the threshold, an index lookup
governing a `switch` above it), the compiled IL (a `switch` opcode above the threshold and none at or below it, read with
System.Reflection.Metadata), every request dispatched to its own handler above it, and a type derived from a handled request, with no
handler of its own, not dispatched to the base handler, as an unknown type is not (for notifications; a request or stream request type
that is not sealed, the only one a derived type can come from, is not dispatched at all: `KeryxHandlerNotFoundException` for it while its
sealed siblings still dispatch); Verify snapshots of the generated mediator per kind at and one above the
threshold; the JIT code of the generic `Send`, `Stream` and `Publish<T>` (`DOTNET_JitDisasm`, `Keryx.Benchmarks --verify`) keeps every
arm's tail call and tests each sealed type with one method-table compare; a host with
`LangVersion` 13 reports KRX022 and generates no host sources; MSBuild evaluation tests (`BuildFiles/KeryxHostPropertyTests`) cover the
`KeryxHost` defaults through `build/` and `buildTransitive/`, the project's own value, and the compiler-visible property; `dotnet pack`
ships `build/`, `buildMultiTargeting/` and `buildTransitive/` (the `_KeryxCheckPackedBuildFiles` target in `Keryx.csproj` fails the pack
otherwise).

### Phase 4: behaviors

First, the gate: a **hand-written** prototype of a depth-1/3/8 struct pipeline (flat links over a pipeline context, static behavior
methods called directly) in `test/Keryx.Benchmarks/Prototype`, measured against the ADR's Phase 4 gate criteria. Where
`DisassemblyDiagnoser` does not run (macOS), judge criterion 2 from `Keryx.Benchmarks --prototype-disasm` with `DOTNET_JitDisasm` (OQ-36);
the Linux benchmarks job (Phase 8) adds the `DisassemblyDiagnoser` output. If the gate fails, do not stop (decided 2026-11-26): record the
measured numbers and the disassembly findings in `open-questions.md`, report them in the phase summary, and carry on with the struct-based
pipeline as designed. Do not switch to the hook fallback on your own.

Then request behaviors, as the ADR's Behaviors, Ordering and Performance contract sections describe them:

- **Discovery and shapes.** A behavior is a non-generic `static` class with kind attributes, declaring one `static` `Handle`/`HandleAsync`
  behavior method per kind, fully open or fully closed, with the message, `next` and an optional `CancellationToken` found by type and every
  other parameter injected. A method's kind follows from its signature (`next` constraint and message type). An open method's message
  type parameter must be constrained to the kind's message interface (`TRequest : IRequest<TResponse>`, `TRequest : IStreamRequest<TItem>`,
  `TNotification : INotification`), directly or through an interface that derives from it (`TRequest : ICommand<TResponse>`,
  `TNotification : IDomainEvent`; OQ-57); a constraint to an unrelated interface is KRX019. The response or item type parameter may be
  written annotated, `TResponse?` (`TItem?`), in `next`, the return type and the message constraint (OQ-58). KRX019 is the
  static-behavior shape rule (ADR, Behavior methods). KRX013 and KRX018 cover behavior method parameters; KRX016 covers behavior classes and methods (public outside a host, at
  least `internal` and not file-local in a host). KRX020 is retired the way KRX010 was (no descriptor, analyzer or release entry). The
  `AnalyzerReleases.Unshipped.md` descriptions of KRX019 and KRX042 match the ADR's rows.
- **Scopes and matching.** Three scopes (per target, `[assembly: Behavior]`, `[GlobalBehavior]` on the class or on a behavior method, from
  the host's own compilation). `typeof(X)` names the class and resolves to its method of the target's kind; constraints are checked on the
  method's type parameters after substitution (`IMethodSymbol.Construct` does not validate them), with the nullability of their type
  arguments; closed methods match exactly, with nullable annotations in `next` and the return type (ADR, Matching). An open method that writes `TResponse?` has the
  annotated type substituted there, and `next` and the return type must carry exactly the target's response with its nullability: it fits
  an annotated, oblivious or value type response and not a non-annotated reference one; `struct`-constrained, it fits only a nullable
  value type (OQ-58). A fit through an oblivious response is called with nullable warnings disabled, so generated code never warns.
  KRX009 for an attached behavior that does not fit its target. KRX042 for `[Behavior]` or
  `[GlobalBehavior]` where it has no effect, including `[GlobalBehavior]` on a class without a kind attribute or on a method that is not a
  behavior method.
- **Ordering.** `Phase`, `Order`, `typeof` `RunsAfter`/`RunsBefore` edges and the class name, read through the one helper (a method's
  `[BehaviorOrder]`, `[RunsAfter]`/`[RunsBefore]` list and `[GlobalBehavior]` override the class's for that kind). KRX008 is reported once
  per pair of behavior classes, and once per kind where a method overrides the class's ordering.
- **Host control.** `ExcludeBehavior` removes a class from pipelines of every kind; KRX035 lists every applied global, once per class and
  kind it makes global (OQ-57); KRX036 for an
  `ExcludeBehavior` that names nothing in any pipeline.
- **Generation.** Flat link structs for request/response pipelines. The pipeline context resolves each behavior method's injected services
  (keyed and optional forms included) in its constructor and holds them beside the handler; links call the static method with explicit type
  arguments and the arguments in declared order. `AddKeryx()` registers no behaviors. Links follow owner decision 11 of the second rework
  (ADR, Generated pipeline shape): one non-generic `readonly struct` per position with a primary constructor
  (`LinkK(__PipelineN pipeline, TRequest request)`, `notification` for a publish pipeline), an expression-bodied `InvokeAsync`, a
  target-typed `new(...)` at every behavior call and at the entry (`TNext` stays an explicit type argument), a one-line `<summary>` naming
  where `next` goes, and `global::` names without `using` aliases. Owner decision 16 (OQ-65): only a pipeline that resolves something has
  a context. A request or stream pipeline whose handler's invoker is static and whose behaviors inject nothing, and a publish pipeline
  without handlers whose behaviors inject nothing, have no context field, no `__CreatePipelineN` and no `Volatile.Read` at the entry, and
  their links are nested in a `private static class` of the same name. Every link takes only the parameters it reads or passes on
  (`(pipeline, request)`, `(request)`, or nothing for the last link of a publish pipeline without handlers, called as `new()`), so no link
  raises CS9113 and the mediator's header suppresses nothing but the obsolete-type warnings. A publish pipeline with handlers keeps its
  context even when it resolves nothing (OQ-70, open). The gate prototype is written the same way and `--prototype-disasm` must still show
  0 B and every link inlined.
- **Shared services** (owner decision 9 of the second rework; ADR, Injected parameters). Every pipeline context (request, stream, publish)
  resolves each distinct service (type and key) once, into one field (`Service0`, `Service1`, ... in order of first use), whatever the
  number of behavior parameters that inject it. The model computes this (`PipelineBehaviors.ShareServices`: the pipeline model's
  `ContextServices`, and each behavior's `ServiceSlots` indexing into it); the emitter only writes it. A required consumer makes the
  service required for all; when every consumer is optional it is resolved once into a nullable field and each call site applies its own
  default. A transient shared by several behavior methods is one instance per scope and pipeline.

**Done when:**

- snapshot tests for request pipelines of depth 0–8 show static calls with explicit type arguments, injected services held by the pipeline
  context, and no behavior registrations in `AddKeryx()`;
- KRX019 has a positive test for each shape case (class not `static`, generic class, kind attribute without a method, two methods of one
  kind, partially open method, extra type parameter, `next` without the `struct` constraint, wrong return type, closed method whose `next`
  does not carry the request's response, a method whose `next` constraint and message type belong to different kinds, and an open method of
  each kind whose message type parameter lacks the kind's message constraint or is constrained to an unrelated interface) and negative
  tests (one class with all three kinds; `Handle` and `HandleAsync` side by side; an open method of each kind with the message constraint
  plus further constraints; a closed method, which needs no message constraint); a message constraint through a derived interface of
  each kind is accepted, matched to the targets of that interface only, and its generated code compiles and runs (OQ-57); an open method
  that writes `TResponse?` (`TItem?`) is not KRX019 and has snapshot and compile-and-run tests for `string?`, an oblivious `string`, `int`
  and `int?`, with the non-annotated `string` case skipped as a global behavior and KRX009 as an attached one, for requests and streams
  (OQ-58), and partially annotated methods (only the message constraint, only `next` and the return type, `class`-constrained) and
  closed methods with an annotated `next` or return type compile without warnings for every fit; KRX013, KRX016 and KRX018 have positive and negative tests on behavior methods; KRX020 is not reported
  anywhere;
- KRX009, KRX035 (including a global behavior marked only on a method, and one class global for three kinds listed once per kind), KRX036 for `ExcludeBehavior` and KRX042 (the `[GlobalBehavior]`
  cases) have positive and negative tests, and `[assembly: Behavior(typeof(X))]` naming a non-behavior reports nothing;
- matching tests cover open and closed methods of every kind, each constraint kind and nullable annotations; ordering tests cover every
  sort rule and every KRX008 case with `typeof` edges, a method-level `[BehaviorOrder]` and `[RunsAfter]` overriding the class's for one
  kind, a two-kind class whose method overrides `[RunsAfter]` (KRX008 reported per kind), and `[GlobalBehavior]` on a method only;
  `ExcludeBehavior` removes a class from pipelines of every kind;
- allocation tests show 0 B per `Send` at depths 1, 3 and 8 (typed and generic), with behaviors that take injected services and with
  `async` behaviors whose work completes synchronously, on an optimized (Release) test host;
- `PipelineGeneratorTests.Should_Give_No_Context_To_A_Pipeline_That_Resolves_Nothing` snapshots request, stream and publish pipelines
  that resolve nothing (no context, links in a static class) beside ones that resolve a service, with every link taking only what it
  reads or passes on, and the host compiles without warnings with no CS9113 suppression (OQ-65);
- `Behaviors/SharedServiceTests`: a snapshot of request, stream and publish pipelines whose four behaviors each inject `IClock` shows one
  field per context; keyed and unkeyed services of one type stay separate; a required consumer makes a service required for optional
  consumers too; consumers that are all optional keep their own defaults (generated code and at run time); every behavior of a pipeline
  receives the same instance of a transient, and a required and an optional `IServiceProvider` both receive the scope's provider; and
  adding a behavior that injects only a service the pipeline already shares, without changing the order of first uses, leaves the
  pipeline's context services (model and fields) unchanged, while an edit that keeps the set but changes that order renumbers the fields
  (`open-questions.md` OQ-64, open);
- the gate's three criteria are recorded in the phase summary and in `open-questions.md`. A failed gate does not block this line: if
  criterion 1 (0 B at depths 1, 3 and 8) failed, follow `open-questions.md` OQ-22 for the 0 B allocation tests.

### Phase 5: streaming

Stream dispatch (typed and generic), no-behavior pass-through (the handler's enumerable returned as is), `[StreamBehavior]` behavior
methods that wrap the stream, called directly through flat `IStreamNext<TItem>` link structs (`StreamLinkK`) with their injected services
read from the pipeline context (each distinct service once, shared as Phase 4's "Shared services" describes), the token rules (two tokens linked once, at the outermost element), synchronous validation of null and
unknown requests. Stream behaviors use the same scopes, matching, ordering and diagnostics as request behaviors. There are no hook
behaviors and no fused segments.

**Done when:**

- runtime tests cover stream behaviors that re-yield, filter, map, call `next` twice and do not call it, exceptions from the handler and
  from stream behaviors, a synchronous throw from an outermost non-iterator stream behavior (it comes from the `Stream()` call itself),
  and cancellation through both tokens with a stream behavior outermost;
- KRX019 tests cover the stream-kind shape cases (Phase 4 list);
- scope tests (per target, `[assembly: Behavior]`, `[GlobalBehavior]`) and constraint-matching tests for open stream behavior methods,
  substituted with `TRequest`/`TItem`, and ordering tests by `Phase`, `Order` and `RunsAfter`/`RunsBefore`;
- generator snapshot tests of stream pipelines at depths 0, 1, 3 and 8: the `StreamLinkK` structs and each behavior method called
  directly with explicit type arguments;
- allocation tests show 0 extra with no behaviors, one iterator per stream behavior per `Stream` call, and 0 per item; the two-token test
  asserts exactly one linked `CancellationTokenSource` in total with a stream behavior outermost when two distinct cancellable tokens are
  supplied, and none when one token is `default` or the two are equal;
- generic `Stream` is tested on both sides of the dispatch threshold, as generic `Send` is (Phase 3): the generated shape and the
  compiled IL at the threshold and one above, every stream request streaming from its own handler above it, and an unknown type or a
  type derived from a handled stream request throwing `KeryxHandlerNotFoundException` when `Stream` is called.

### Phase 6: notifications

Publish dispatch (typed and generic; generic `Publish` keeps exact-type `when` guards, `case var _ when notificationType ==
typeof(X):`, ending `default: return default;`, because notification types need not be sealed and a type pattern would send an unknown
derived notification to its base type's pipeline), handler lists with polymorphic expansion and `[HandlerOrder]`, sequential and parallel strategies,
run-all-then-`AggregateException` (also for one failure and on cancellation), boxing-free generic `Publish` for struct notifications,
KRX037. Parallel publish exactly as the ADR's Publish strategy section defines it. `open-questions.md` OQ-3, OQ-4, OQ-5, OQ-17 and OQ-18
record the decisions this phase follows.

`[NotificationBehavior]` wraps the whole publish once, also with zero handlers, uses `INext` and returns `ValueTask`; it is attached with
`[Behavior]` on the notification type, `[assembly: Behavior]` in the notification type's assembly or `[GlobalBehavior]`, and its
`TNotification` is the concrete published type; the publish pipeline context shares its behaviors' injected services as Phase 4's "Shared
services" describes. Handlers in a publish are called directly under both strategies: there are no per-handler
pipelines or links. The handler generator writes no `Behaviors` for a notification handler, and the analyzer reports KRX042 for
`[Behavior]` on a notification handler method.

**Done when:** tests cover zero/one/many handlers, polymorphic handlers, ordering, both strategies, failures in several handlers and
cancellation; notification behavior tests cover all three scopes, zero handlers, both strategies, an exception propagating unwrapped and a
synchronous throw from the outermost behavior; an `[assembly: Behavior]` naming a `[NotificationBehavior]` applies in the notification
type's assembly, and in an assembly that declares notification handlers but not the notification type it changes no publish pipeline;
KRX042 has positive tests for `[Behavior]` on a notification handler method (exact-type and polymorphic), and it changes no generated
code; snapshot tests of publish pipelines at depths 0, 1, 3 and 8 show the handlers called directly under both strategies; allocation tests
show 0 B for a sequential publish (with behaviors that take injected services) and for a parallel publish with synchronous handlers;
generic `Publish<TNotification>` is tested on both sides of the dispatch threshold, as generic `Send` is (Phase 3): the generated shape and
the compiled IL at the threshold and one above, every notification type reaching its own handlers above it, and an unknown type or a type
derived from a handled notification type (unknown to the host) running nothing.

### Phase 7: cross-assembly discovery

Reference `Specular.Scanning.Sources` 0.4.3-beta.2 (exact, `PrivateAssets=all`) plus `System.Text.Json` and
`System.Collections.Immutable` as it requires; ship `System.Text.Json.dll` beside the analyzer; let Specular's pack helper add its cache
targets to the package. One `context.Scan<KeryxReferenceModel>("Keryx", requests, transform)` call over the three scans in the ADR
(an `IncrementalValuesProvider<ScanRequest>`, results read by position); projection cached in ScanCache with a source-generated
`JsonSerializerContext` and the `ProjectionDataVersion` constant, bumped whenever the projection changes. Merge own and referenced handlers
into the Phase 3 model.

- The scan finds global behavior classes whose `[GlobalBehavior]` is on the class or only on a behavior method; KRX035 lists both.
- Host rules KRX005 (cross-assembly), KRX030 for referenced request types, including those declared in another host (OQ-16), KRX032,
  KRX033, and KRX034, which also covers the injected parameter types of a referenced behavior method (OQ-47), per kind: KRX034 names the
  behavior method and its kind, and the class stays in the pipelines of the kinds whose methods resolve (OQ-57). `ExcludeHandler` with KRX036 for `ExcludeHandler`.
- A request's handler lives in the request's own assembly (second rework): KRX030 is a safety net for referenced request types (an
  excluded handler, an internal handler of a referenced host); a host's handler of a referenced request is KRX023, so `ExcludeHandler`
  cannot replace a referenced request handler (OQ-59, open); the host's cross-assembly KRX005 stays as a safety net (OQ-60, open).
  There is no `ExternallyHandled`. Outside a host, `ExcludeHandler` and `ExcludeBehavior` have no effect and the analyzer reports
  warning KRX043 on each (OQ-54). KRX023 and KRX043 have descriptors, help links and `AnalyzerReleases.Unshipped.md` entries, and the
  `AnalyzerReleases.Unshipped.md` descriptions of KRX034 and KRX036 match the ADR's rows.
- A referenced request or stream request type that is not sealed (its library suppressed KRX024) is the host error KRX038, without a
  location, naming the type and its assembly; the host composes no part of it (no dispatch section, no typed overload; sending it throws
  `KeryxHandlerNotFoundException`), and it counts as handled, so there is no KRX030 on top. The host's own type that is not sealed is its
  KRX024, not KRX038, and is not composed either (owner decision 18, OQ-68).
- A `[Behavior]` on a referenced notification type that does not fit the type as the host resolves it is KRX009 without a location,
  naming the notification type and the behavior; the behavior is skipped and the rest of the publish pipeline kept
  (OQ-55).
- Samples: `samples/hosts/` (an ASP.NET Core or console host) and `samples/libraries/` (a contracts library with the order
  notifications, targeting `netstandard2.0`, and a handler library with the order requests next to their handlers, a global static
  behavior and notification handlers). Every open behavior method in the samples constrains its message type parameter; `samples/hosts/README.md` and
  `samples/libraries/README.md` name the static behavior classes. CI job: publish the sample host with `PublishAot=true`, fail on
  IL2xxx/IL3xxx, run it.

**Done when:** the samples build and run end to end; tests cover a host with project and metadata references, a missing reference
(KRX034), a missing injected parameter type of a referenced behavior method (KRX034; a two-kind class whose other kind still runs and is
the only kind KRX035 lists, as a global, attached and `[assembly: Behavior]` behavior, reported once), a referenced global behavior that writes `TResponse?` (fits and runs for the same targets as in source), a newer format version (KRX032), a Keryx reference
without a manifest (KRX033), duplicate handlers (KRX005), exclusion (including KRX036 for `ExcludeHandler`), a referenced global behavior
marked only on a method, KRX023 for a host's handler of a referenced request (also when the host excludes the referenced handler), KRX030
for a referenced request without a handler, KRX038 for a referenced request and stream request type that is not sealed (not for the
host's own type, an abstract or closed base, a sealed type or a notification; at run time not dispatched while a sealed sibling is),
KRX043 for `ExcludeHandler` and `ExcludeBehavior` in a library (and not in a host), host
KRX009 for a
`[Behavior]` on a referenced notification type that no longer fits (the other publish behaviors still run; a fitting one reports nothing),
and a test host that references an app host (KRX030 for an app request whose handler is internal, nothing for one whose handler is
public); the incremental test shows downstream steps cached after an unrelated edit and `Modified` after a manifest change, and a
`ProjectionDataVersion` bump invalidates the cache; the AOT job passes.

### Phase 8: benchmarks, README, notes

The benchmark suite in the ADR's Benchmarks section (baseline, Keryx, Mediator 3.0.2 scoped and singleton, MediatR 15.x without a key,
Immediate.Handlers 4.4.0, ZeroAlloc.Mediator 7.0.1), each library in its own project under `test/benchmark-subjects`. The Keryx subject
uses static behavior methods with the message constraint; there is no hook-behavior stream variant (OQ-51). The benchmarks workflow runs
the suite with `--verify` and `--job Dry`, and a Linux job runs `--filter *PipelinePrototype*` with `DisassemblyDiagnoser` (OQ-36), whose
output is the CI evidence for gate criterion 2.

README, written to the ADR: install, the `Microsoft.Extensions.DependencyInjection.Abstractions` dependency, minimum SDK (.NET 10) and
C# 14, handler rules, quick start (`AddKeryx()` registers no behaviors), behaviors (the three kind attributes, `[NotificationBehavior]`
wrapping the whole publish, examples with the message constraint, attaching per target on a request or stream handler method or on the
notification type), streaming (stream behaviors only), notifications (handlers have no behaviors of their own), hosts (the scoped mediator
creates only handler instances; a host referenced by another host keeps the handlers it wants composed public, enforced by KRX030;
KRX043), where requests are handled (their own assembly; KRX023) and what contracts assemblies hold (notifications), testing handlers
through `InvokeAsync`, a diagnostics table matching the ADR with help links (KRX020 retired, KRX023 and KRX043 included), and benchmark results from a local run with machine details. A short "what is left" note in
`docs/whats-left.md`.

**Done when:** benchmarks run locally and in CI with `--job Dry`, and `Keryx.Benchmarks --verify` passes; the Linux `PipelinePrototype`
job runs, and its output (the static behavior method and `next.InvokeAsync` called directly) is recorded with the gate result in
`open-questions.md`; the README is complete, its tables come from a local run, and every hit of a grep of `Readme.md` for the removed
names (`PipelineBehavior`, `IStreamHookBehavior`, `PublishBehavior`, `NotificationHandlerBehavior`, per-handler, per-publish,
`RunsAfter<`, hook) is checked against the ADR; `git grep` in `src/`, `test/` (including `test/benchmark-subjects`) and `samples/` finds no
`IRequestPipelineBehavior`, `IStreamHookBehavior`, `INotificationPipelineBehavior`, `INotificationHandlerPipelineBehavior`,
`NotificationHandlerBehavior`, `PublishBehavior` or generic `RunsAfter<T>`/`RunsBefore<T>` (the other libraries' subjects keep those
libraries' own `IPipelineBehavior` and `IStreamPipelineBehavior`), and no hook-behavior code, test or benchmark variant is left; the
`AnalyzerReleases.Unshipped.md` descriptions match the README's diagnostics table and the ADR; `docs/whats-left.md` is current;
`mise run build` passes and the draft PR's CI is green. Marking the PR ready for review is left to the owner.
