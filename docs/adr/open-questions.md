# Open questions

This file collects questions raised while writing or implementing [ADR 0001](0001-keryx-mediator.md). Each entry states the gap, what the
implementation does until it is decided (the safest option), and a proposed answer. The ADR stays authoritative. When an entry is
decided, fold the answer into the ADR and move the entry to **Decided** with the date.

Implementers: add new entries at the end of **Open**, tell the user, and continue with the stated interim behavior. Do not change the ADR
on your own. Every open entry has an **Interim** line; that is what the code does until the entry is decided.

## Open

### OQ-3: Assembly scope for notification behaviors

"Per assembly" was defined for request handlers only. The ADR applies `[assembly: Behavior]` to per-handler notification behaviors in the
**handler's** assembly and to per-publish notification behaviors in the **notification type's** assembly.

- **Interim:** as the ADR says (handler's assembly for per-handler behaviors, notification type's assembly for per-publish behaviors).
- **Proposal:** confirm.

### OQ-4: Notification behavior signature

The ADR adds a non-generic `INext` (`ValueTask InvokeAsync(CancellationToken)`) for notification behaviors, rather than reusing
`INext<Unit>`, so notification behaviors return plain `ValueTask`.

- **Interim:** non-generic `INext`, as in the ADR.
- **Proposal:** confirm. The alternative (`INext<Unit>`, `ValueTask<Unit>`) reuses one interface but makes every notification behavior
  return `Unit`.

### OQ-5: `IPublisher.Publish` shape

The ADR uses `ValueTask Publish<TNotification>(TNotification, CancellationToken) where TNotification : INotification`. A non-generic
`Publish(INotification)` overload was not added.

- **Interim:** generic form only.
- **Proposal:** keep only the generic form. With a value-type `TNotification`, the generated dispatch compares `typeof(TNotification)`
  (folded by the JIT) and converts with `Unsafe.As`, so a struct notification passed by its own type is never boxed by dispatch. A
  non-generic overload would box every struct notification. Base-typed calls (`Publish<IDomainEvent>(e)`) still work through the
  `GetType()` path for reference-type `TNotification`.

### OQ-8: `Microsoft.Extensions.DependencyInjection.Abstractions` dependency

Generated host code uses `IServiceCollection`, `IServiceProvider` extensions and keyed-service APIs. The ADR does not say whether the
`Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions`.

- **Interim:** `Keryx` takes no DI dependency. Hosts reference it themselves (every ASP.NET Core and generic host app already does).
- **Proposal:** add a host error when the host compilation cannot resolve `Microsoft.Extensions.DependencyInjection.IServiceCollection`, or
  add the dependency to the package (per-TFM versions, as Specular's CPM does). Needs a decision.

### OQ-9: Analyzer packing folder and Roslyn pin

The analyzers compile against `Microsoft.CodeAnalysis.CSharp` 5.0.0, while the Specular template pins 5.12.0 in CPM for everything else
(tests need the newer Roslyn through `Rocket.Surgery.Extensions.Testing.SourceGenerators`).

- **Interim:** analyzer projects use 5.0.0; tests use the newer version. Pack to `analyzers/dotnet/cs`. No multi-Roslyn `*.supports`
  projects in v1.
- **Proposal:** confirm, or pack to `analyzers/dotnet/roslyn5.0/cs` as Specular does.

### OQ-10: Per-assembly relief for KRX030

KRX030 (request visible to the host with no handler) can only be lowered globally through `.editorconfig`. A host that references one
contracts assembly handled out of process, and another it does handle, cannot keep the error for the second.

- **Interim:** global severity only.
- **Proposal:** add `[assembly: KeryxIgnoreUnhandledFrom(typeof(MarkerInThatAssembly))]` on the host. Needs a decision.

### OQ-11: Behavior constructors

The ADR requires exactly one public constructor on each behavior (KRX020), because the host emits explicit factories. Behaviors with
several constructors (for example one for tests) are rejected.

- **Interim:** exactly one public constructor (KRX020).
- **Proposal:** confirm, or pick the constructor with the most parameters as MS.DI does.

### OQ-13: Code-fix additions

Not in the original ADR, added while writing it:

- KRX001 offers a second action, "Mark this assembly as a contracts assembly". KRX001 is never reported in a host, so this action never
  appears there.
- KRX002 has a code fix that makes the method `private static`.

- **Interim:** both code fixes are implemented.
- **Proposal:** confirm.

### OQ-14: Host namespace and target framework source

Generated host types go in the host's root namespace, read from `RootNamespace` through `CompilerVisibleProperty`. KRX031 needs the
target framework, which a `Compilation` does not carry. Both come from `build/Keryx.props` in the package, which exposes
`RootNamespace;TargetFrameworkIdentifier;TargetFrameworkVersion`.

- **Interim:** as the ADR says. Namespace falls back to the sanitised assembly name; KRX031 falls back to the referenced `System.Runtime`
  version when the properties are absent.
- **Proposal:** confirm; an explicit `[assembly: KeryxHost(Namespace = "...")]` override can be added later if needed.

### OQ-15: Dispatch threshold

The ADR switches from a linear type-check chain to a `FrozenDictionary` lookup above 16 request types. The number is a guess.

- **Interim:** 32 (`HostEmitter.DispatchThreshold`), set from the Phase 3 benchmark below.
- **Proposal:** change the ADR's "more than 16 request types" to 32, and add the dispatch-shape rule below to its Generic entry points
  section.

Phase 3 measurements (2026-11-26; BenchmarkDotNet 0.15.8 `--job short`, .NET 10.0.15 Arm64 RyuJIT, Apple M4 Max, macOS 27.2, default
runtime settings with Dynamic PGO on; `test/Keryx.Benchmarks/Host`). All rows allocate 0 B.

| Request types | Linear, last type | Linear, middle type | FrozenDictionary + switch, last | middle  |
| ------------- | ----------------- | ------------------- | ------------------------------- | ------- |
| 8             | 1.86 ns           | 2.25 ns             | 4.50 ns                         | 4.48 ns |
| 16            | 2.94 ns           | 2.12 ns             | 4.59 ns                         | 4.58 ns |
| 24            | 3.91 ns           | 2.51 ns             | 4.52 ns                         | 4.80 ns |
| 32            | 5.10 ns           | 3.04 ns             | 4.54 ns                         | 4.56 ns |
| 48            | 7.03 ns           | 3.97 ns             | 4.74 ns                         | 4.80 ns |
| 64            | 8.92 ns           | 5.02 ns             | 4.49 ns                         | 4.49 ns |

The lookup is flat at about 4.5 ns; the chain costs about 0.13 ns per compare. The worst case crosses over between 24 and 32 types, the
average near 56. At 32 the chain's worst case is 0.6 ns slower than the lookup and its average 1.5 ns faster, hence 32.

Lazy pipeline context against resolving per call (`PipelineContextBenchmarks`, same machine and job): `Volatile.Read` of the scope's context
measured below BenchmarkDotNet's resolution (reported as 0 ns, like the direct handler call), against 10.76 ns for
`GetRequiredService<THandler>()` on a scoped registration per call; both 0 B.

Dispatch shape found while measuring: a first version wrote every arm inline in the generic `Send` (`var response = ...;
return Unsafe.As<ValueTask<X>, ValueTask<TResponse>>(ref response);`) and kept the `FrozenDictionary` lookup's `out` local in the same
method. Under Dynamic PGO that measured about 45 ns per call for every count, both shapes, whichever arm was taken (about 1.5 to 8 ns with
`DOTNET_TieredPGO=0`): the Tier1 code zeroed a 1 KB frame (one `ValueTask` per arm) in the prolog of every call and could not tail-call the
arms. The generator therefore emits each arm as its own non-inlined `__Dispatch{n}<TResponse>` method holding the `Unsafe.As`, and the
lookup as a non-inlined `__RequestIndex(Type)` returning -1 for an unknown type, so generic `Send` holds no address-taken locals; the
table above is for that shape.

### OQ-16: A host that references another host

A test project that is a host and references the app host sees the app's public request types, but the app host may keep its handlers
`internal`, and internal handlers carry no manifest. Without a rule, the test host would get KRX030 for every such request.

- **Interim:** KRX030 skips request types declared in an assembly that carries `[assembly: KeryxHost]`. The referencing host dispatches
  such a request only if the referenced host's handler is public; otherwise it throws `KeryxHandlerNotFoundException` at run time.
- **Proposal:** keep the interim. Alternative: require a host that is referenced by another host to keep its handlers public, with an
  analyzer warning for internal handlers in a host that grants `InternalsVisibleTo`.

### OQ-17: Struct notifications delivered to interface-typed handlers

A polymorphic handler typed as an interface or `INotification` cannot receive a struct notification without boxing it. The performance
contract promised 0 extra allocations for a sequential publish, which cannot hold for this case without dropping polymorphic delivery of
struct notifications.

- **Interim:** box once per `Publish` and reuse that box for every interface-typed handler; Info KRX037 reports it; the performance
  contract lists it as a documented exception.
- **Proposal:** keep the interim. Alternative: make "struct notification with an interface-typed handler" an error, which narrows the
  polymorphic-handler decision.

### OQ-18: Per-publish behaviors when a notification has zero handlers

The ADR said both "zero handlers: `Publish` completes immediately" and "`INotificationPipelineBehavior` wraps the whole `Publish` call". The
verification pass picked one reading.

- **Interim:** for a notification type the host knows, the per-publish pipeline runs even with zero handlers. `Publish` completes
  immediately only when there are neither handlers nor per-publish behaviors, or when the runtime type is unknown.
- **Proposal:** confirm (logging and outbox behaviors then see every publish).

### OQ-19: Shapes of the attributes the ADR lists only in the Attribute reference table

The ADR gives exact declarations for `BehaviorOrder`, `RunsAfter`/`RunsBefore`, `PublishStrategy`, `HandlerOrder`, `KeryxHandler` and
`KeryxManifest`, but only a table row (targets, "multiple") for `KeryxHost`, `KeryxContracts`, `Behavior`, `GlobalBehavior`,
`ExcludeBehavior` and `ExcludeHandler`. Their property names and `AttributeUsage.Inherited` are unstated, and Phase 1 fixes them in the
public API snapshot.

- **Interim:** sealed attributes with a primary constructor, as the declared ones are. `Behavior(Type)` and `ExcludeBehavior(Type)` expose
  `BehaviorType` (as `RunsAfter`/`RunsBefore` do); `ExcludeHandler(Type)` exposes `HandlerType`. `KeryxHost`, `KeryxContracts` and
  `GlobalBehavior` have no members. `AllowMultiple` follows the table ("multiple" → `true`, otherwise `false`); `Inherited` keeps the
  default (`true`) everywhere, like the attributes the ADR declares. The generator reads attributes through Roslyn, which ignores
  `Inherited`, so it has no effect on Keryx itself.
- **Proposal:** confirm and copy the declarations into the ADR's Attribute reference. Alternative: `Inherited = false` on the class-level
  attributes (`GlobalBehavior`, `BehaviorOrder`, `RunsAfter`/`RunsBefore`, `PublishStrategy`), so reflection-based tooling does not
  report a derived class as a global behavior or a parallel notification when the generator does not treat it as one.

### OQ-21: Hook behaviors and exceptions raised outside their fused segment

With the wrap model (decided 2026-11-26, below), a wrap behavior splits hook behaviors into separate fused segments, each its own
iterator. An exception raised outside a segment, in an outer wrap behavior or in a hook of an outer segment, never passes through the
inner segment's iterator: the outer code stops enumerating and disposes it. Before the wrap model, all hooks shared one iterator and
`OnErrorAsync` saw every exception from any hook.

- **Interim:** a segment's `OnErrorAsync` observes exceptions from its inner stream (inner behaviors and the handler) and from its own
  hooks. An exception raised outside the segment reaches it as an early dispose, so its hooks run `OnCompleteAsync`. This is what nested
  iterators do on their own and costs nothing extra. Within one segment, the previously decided semantics are unchanged.
- **Proposal:** confirm. The alternative (inner hooks also see outer exceptions through `OnErrorAsync`) would need shared state between
  segments per `Stream` call, an extra allocation the stream contract does not allow.

### OQ-22: Phase 4 0 B allocation tests after a failed allocation gate

The Phase 4 "Done when" requires allocation tests at depths 1, 3 and 8 to show 0 B, and the Performance contract promises 0 B for a
synchronous `Send` at any depth. The decision "Phase 4 gate does not stop work" (below) says a failed gate does not block Phase 4. If the
gate fails on criterion 1 (0 B at depths 1, 3 and 8), the generated pipeline allocates the same way, those tests fail, and with warnings
as errors and CI Phase 4 cannot be committed. How the tests behave in that case is not decided.

- **Interim:** keep the 0 B assertions, but mark the failing ones skipped, with a skip reason that points to the Phase 4 gate-results
  entry in this file (which records the measured bytes). Do not loosen the assertions to the measured value and do not delete them. If
  the gate passes criterion 1, nothing is skipped.
- **Proposal:** re-enable the skipped tests once the allocation is fixed, and keep the Performance contract at 0 B. Alternative: record
  the measured allocation as a documented exception in the Performance contract and assert that value instead.

### OQ-23: Hint name of a nested handler class

The ADR's hint-name rule replaces `+` (and any character outside `[A-Za-z0-9_.]`) in the fully qualified metadata name with `_`, but its
example names `MyApp.Orders.Create.Keryx.g.cs`, which matches the rule only when `Orders` is a namespace. For the nested-class grouping the
ADR recommends (`public static partial class Orders { public partial class Create { ... } }`), the rule gives
`MyApp.Orders_Create.Keryx.g.cs`.

- **Interim:** follow the rule: `MyApp.Orders+Create` becomes `MyApp.Orders_Create.Keryx.g.cs`. The assembly manifest goes to the fixed
  hint name `KeryxManifest.g.cs`, which cannot collide with a handler hint name because it does not end in `.Keryx.g.cs`. OQ-28 adds
  a hash before `.Keryx.g.cs` (`MyApp.Orders_Create.<hash>.Keryx.g.cs`) so that sanitised names cannot collide.
- **Proposal:** confirm the rule and change the example to a namespace-qualified class, or add a nested example.

### OQ-24: Types the generated public constructor and invoker mention

KRX016 lists request, response, notification and behavior types. The generated constructor also takes every injected parameter type, and
the generated invoker takes a polymorphic notification handler's base type or interface. Outside a host the handler class is public, so an
internal injected type makes the generated public constructor fail to compile (CS0051), and a host could not construct the handler anyway.
Inside a host a public handler class may use internal request or service types, and the ADR says nothing about its generated members.

- **Interim:** outside a host, KRX016 also covers injected parameter types and polymorphic notification parameter types (reported on the
  parameter) and the behavior types attached with `[Behavior]` on the handler method (reported on the attribute); the handler generator
  skips such a handler. In a host, a public handler class whose request, response or injected types are internal gets `internal`
  generated members and no `[KeryxHandler]` attribute, like the host's internal handlers; a handler whose signature uses a type narrower
  than `internal` (its request, response or an injected type) is skipped, and KRX016 reports that type in the host ("must be at least
  internal", on the parameter or the return type), so the skipped handler is never silent. Its request still counts as handled, so it does
  not also get KRX030. Generated member accessibility follows the signature alone (`public` only when every type it mentions is
  effectively public), so a `protected internal` handler class with internal signature types also gets `internal` members rather than
  public ones that fail with CS0051.
- **Proposal:** confirm and extend the KRX016 row of the ADR ("... and every type the generated constructor and invoker mention; in a
  host, every type a handler's signature mentions is at least `internal`").

### OQ-25: Where KRX021 is reported

KRX021 says a struct request is boxed when sent through `ISender`, but not where the diagnostic goes: on the struct's declaration (every
struct request, sent or not) or where it is boxed.

- **Interim:** at the call site: the request argument of an `ISender.Send`/`ISender.Stream` call (including through `IMediator`) whose
  argument is a value type converted to the interface. A struct request that is never sent through `ISender` reports nothing.
- **Proposal:** confirm. The alternative (Info on every struct request declaration) also fires in hosts that only use the typed overloads.

### OQ-26: Handler candidate and handler class edge cases

The handler rules leave a few shapes open. Phase 2 implements these, all on the side that reports or skips rather than generating code
that does not compile:

- **Abstract methods** named `Handle`/`HandleAsync` (an interface's own members, abstract class members) are not handler candidates, like
  interface implementations and overrides: they declare a contract and have no body to call.
- **Interfaces and file-local types.** KRX004 also reports a handler class that is an interface, and any file-local (`file`) type in the
  containing chain: Keryx cannot generate the other half of a file-local type from another file.
- **Generic containing types.** A handler class nested in a generic type is reported as "has type parameters" (KRX012), like a generic
  handler class, because the host cannot construct it.
- **`IRequest<Unit>` written directly** counts as `IRequest`: its handler may also return `ValueTask`, `Task` or synchronous `void`.

- **Interim:** as listed.
- **Proposal:** confirm and fold into the ADR's Handlers section.

### OQ-27: KRX022 as a compilation-end diagnostic

The ADR names KRX001, same-assembly KRX005 and in-source KRX008 as compilation-end diagnostics. KRX022 is "reported once per compilation"
from the parse options, which an analyzer can only do from a compilation action, and RS1037 then requires the `CompilationEnd` tag. In the
IDE it therefore appears only with full-solution analysis or on build, like KRX001.

- **Interim:** KRX022 is reported from `RegisterCompilationAction` with `Location.None` and carries `CompilationEnd`. Every other Keryx
  analyzer and both generators stay silent below C# 11, so KRX022 is the only Keryx error there.
- **Proposal:** confirm and list KRX022 with the compilation-end diagnostics in the ADR. The alternative (reporting it from a syntax-tree
  action on one chosen tree) shows it in the IDE, but on an arbitrary file.

### OQ-28: Hint names that the sanitised metadata name merges

The ADR's hint-name rule (see also OQ-23) is not injective. In one namespace a nested `Orders.Cancel` (`Sample.Orders+Cancel`) and a
top-level `Orders_Cancel` both become `Sample.Orders_Cancel.Keryx.g.cs`, because `+` and `_` both map to `_`. Roslyn also compares hint
names case-insensitively, so classes `Ping` and `ping` collide. `AddSource` then throws, the generator fails with CS8785, and no handler in
the compilation gets its partial.

- **Interim:** the hint name is the sanitised metadata name, then 8 lowercase hex digits of the 32-bit FNV-1a hash of the UTF-8 bytes of
  the exact (ordinal) metadata name, then `.Keryx.g.cs`: `Sample.Orders_Cancel.1a2b3c4d.Keryx.g.cs`. The sanitised prefix keeps the name
  readable; the hash keeps names apart that differ only in `+`/`_`, other replaced characters, or case. The manifest keeps
  `KeryxManifest.g.cs`.
- **Proposal:** amend the ADR rule (Incremental generation rules) to append the hash, and change its example. Escaping alone (`_` as
  `__`, `+` as `_p`) would not be enough, because it still collides on case.

### OQ-29: Accessibility floor of a handler class in a host

The ADR says only that "a host may keep handlers `internal` for its own mediator". A handler class, or a type containing it, that is
`private`, `protected` or `private protected` cannot be reached by the host's generated `KeryxMediator`/`AddKeryx` (Phase 3), which live
outside the class. Nothing reported it, and the handler generator still generated its partial.

- **Interim:** KRX004 is also reported in a host when the handler class or any containing type is narrower than `internal` (with the
  "make public" code fix), and the handler generator skips such a handler in a host. `protected internal` counts as `internal`.
- **Proposal:** fold into the KRX004 row: "... is not `public` outside a host, or is narrower than `internal` in a host".

### OQ-30: Injected parameter types a field cannot hold

KRX013 lists `ref`, `in`, `out`, `params` and a second `CancellationToken`. An injected service is stored in a `private readonly` field of
the handler class, which a ref struct (`Span<T>`, `ReadOnlySpan<T>`, `TypedReference`, any `ref struct`), a pointer or a function
pointer cannot be. Without a rule the generated half fails to compile (CS8345, CS0214), which goes against reporting or skipping
(OQ-26).

- **Interim:** report KRX013 on such a parameter ("is a ref struct; an injected service is stored in a field of the handler class") and
  skip the handler. A parameter that breaks KRX013 for any reason is not counted as an injected service, so a static handler class does not
  also get KRX014 (whose "remove static" fix would not help) and the base-constructor part of KRX007 does not apply.
- **Proposal:** extend the KRX013 row: "Handler parameter is `ref`, `in`, `out` or `params`, an injected parameter is a ref struct or a
  pointer, or there is more than one `CancellationToken`."

### OQ-31: A typed overload called with a derived request

Typed overloads "skip type dispatch" (ADR, Mediator), and "a handler for a base request never receives a derived request" (ADR,
Requests and notifications). The two conflict for a request class that is not sealed: `Animal` and `Dog : Animal` are both requests, and
`mediator.Send(animal)` with an `Animal`-typed variable that holds a `Dog` binds to the `Send(Animal)` overload at compile time, which would
hand the `Dog` to `Animal`'s handler.

- **Interim:** the typed overload of a request class that is not sealed compares `request.GetType()` with its own type (a method-table
  compare, no allocation) and, when they differ, sends through the generic entry point, which dispatches on the exact runtime type (or
  throws `KeryxHandlerNotFoundException`). Sealed classes and structs skip the check.
- **Proposal:** confirm and add one sentence to the ADR's Mediator section ("A typed overload for a request class that is not sealed checks
  the runtime type first").

### OQ-32: What the host generator emits when KRX031 is reported

KRX031 reports a host that targets a framework older than `net10.0`. The ADR does not say whether the host generator still emits the
mediator and `AddKeryx()`, which use `Unsafe.As`, `FrozenDictionary` and `ArgumentNullException.ThrowIfNull` and so fail to compile there.

- **Interim:** like KRX022, the host generator emits nothing in such a host and reports KRX031 on `[assembly: KeryxHost]` (KRX030 is still
  reported), so the user sees KRX031 instead of compiler errors in generated files. The handler generator is unaffected.
- **Proposal:** confirm and add "the host generator emits nothing there" to the KRX031 row.

### OQ-33: Host namespace sanitisation for keywords and empty segments

The ADR's fallback rule (characters not valid in an identifier become `_`, a segment that starts with a digit gets a `_` prefix) leaves
two cases that still give an invalid namespace: a segment that is a C# keyword (`Contoso.namespace`, from an assembly named that way) and
an empty segment (`My..App`). It also does not say whether `RootNamespace` itself is sanitised.

- **Interim:** the same sanitisation runs on `RootNamespace` and on the assembly-name fallback (a valid namespace is unchanged); an empty
  segment becomes `_` and a keyword segment is escaped with `@` (`Contoso.@namespace`).
- **Proposal:** confirm and add both cases to the rule in the ADR's Opting in section.

### OQ-34: Handler classes a host cannot construct with `new`

A handler class with injected parameters is constructed by the host's generated factory (`new Handler(...)` in `AddKeryx()`). Two shapes
pass every rule and still break that factory with a compiler error in generated code: an `abstract` handler class (CS0144) and a handler
class that has, or inherits, a `required` field or property (CS9035, because the generated constructor does not set it). The handler
partial alone compiles, so outside a host nothing shows until a host composes the handler.

- **Interim:** KRX007 also reports a handler class with injected parameters that is abstract, or that it or a base type declares
  `required` members ("has injected parameters, so a host constructs it, but it is abstract" / "... but it has required members ..."), and
  the handler generator skips it. The host then counts its request as handled by a malformed handler, so no KRX030 is added.
  `[SetsRequiredMembers]` is not put on the generated constructor, which does not set them. A static invoker (no injected parameters) is
  never constructed, so neither case applies to it.
- **Proposal:** extend the KRX007 row: "..., or has injected parameters and is abstract, declares or inherits `required` members, or has a
  base type without an accessible parameterless instance constructor."

### OQ-35: Where host diagnostics are reported, and KRX030 severity in `.editorconfig`

The ADR has host diagnostics turned back into a `Location` with `Location.Create` from the equatable `LocationInfo`, and says KRX030's
severity "can be changed in `.editorconfig`". `Location.Create(path, span, lineSpan)` gives a location without a syntax tree, so the
compiler applies only global options to it: `dotnet_diagnostic.KRX030.severity` under `[*.cs]` in an `.editorconfig` had no effect (only an
`is_global` config did).

- **Interim:** the host generator computes its diagnostics from its cached models (still carrying `LocationInfo`) in a step of their own,
  and only the output that reports them is combined with the compilation, to find the syntax tree by file path and report with
  `Location.Create(tree, span)`. Per-tree `.editorconfig` severity then applies to KRX030 (and KRX031). The source output does not read the
  compilation and stays cached on unrelated edits; the diagnostics output reruns on every compilation, which costs one dictionary of the
  compilation's trees, built only when there is something to report.
- **Proposal:** amend the Diagnostics section: "turned back into a `Location` on the compilation's syntax tree when reporting (in an output
  that combines the cached diagnostics with the compilation)".

### OQ-36: Phase 4 gate disassembly where `DisassemblyDiagnoser` is not available

The Phase 4 gate's second criterion reads "`DisassemblyDiagnoser` output shows the behavior `Handle` called directly ... and
`next.InvokeAsync` called directly". BenchmarkDotNet 0.15.8 supports `DisassemblyDiagnoser` only on Windows and Linux; on macOS its
validation rejects the run ("Only Windows and Linux are supported in DisassemblyDiagnoser without Mono").

- **Interim:** `PipelinePrototypeBenchmarks` adds the diagnoser through a config only on Windows and Linux. Elsewhere the same pipelines are
  run to Tier 1 by `Keryx.Benchmarks --prototype-disasm` (an unoptimized delegate loop, so the benchmark methods tier up on their own as under
  BenchmarkDotNet) with `DOTNET_JitDisasm` set, and the gate is judged from that listing. The gate **passed** on all three criteria
  (re-run 2026-11-26 after a review found the first baseline handicapped; BenchmarkDotNet 0.15.8 default job, .NET 10.0.15 Arm64 RyuJIT,
  Apple M4 Max, macOS 27.2, Dynamic PGO on; `test/Keryx.Benchmarks/Prototype`, `--filter *PipelinePrototype*`):

    | Depth | Nested-call baseline | Struct pipeline | Ratio | Allocated (both) |
    | ----- | -------------------- | --------------- | ----- | ---------------- |
    | 1     | 0.52 ns              | 0.67 ns         | 1.28  | 0 B              |
    | 3     | 1.65 ns              | 1.09 ns         | 0.66  | 0 B              |
    | 8     | 2.09 ns              | 2.09 ns         | 1.00  | 0 B              |

    Baseline and measurement: each depth has its own nested-call chain (`NestedCallDepth1`, `NestedCallDepth3`, `NestedCallDepth8`) of
    sealed, non-generic classes, each behavior holding the next one (or the handler) in a readonly field and calling it directly, with no
    runtime depth check and no extra arguments. Every benchmark runs 1,000 sends per invocation (`OperationsPerInvoke = 1000`), so the
    depth-1 times are above BenchmarkDotNet's resolution; the first run's 0.00 ns at depth 1 measured nothing.

    Disassembly (Tier 1, Dynamic PGO): `SendDepth3` and `SendDepth8` inline every behavior's `Handle<TLink>` and every
    `Link_k.InvokeAsync` down to the handler's invoker; the only call left is the cold `CreateDepthN()` that builds the pipeline context. No
    generic-virtual-method lookup and no runtime generic dictionary lookup appears, although each behavior is closed over a reference-type
    request and so runs as shared canonical code (`Behavior1<__Canon, int>.Handle<Link1>`): the struct link type argument is exact. The
    baselines inline completely too, so neither side makes a call per send. The remaining difference is the shape of the loads: the struct
    pipeline reads every behavior from the pipeline context (independent loads), while a nested-call chain reaches each behavior through the
    previous one (a dependent load per level), the likely reason the struct pipeline is faster at depth 3. At depth 1 the struct pipeline pays the
    lazy pipeline-context read (a `Volatile.Read` and a null check) that the baseline does not have; depth 1 is not a gate criterion.
    Criterion 1 (0 B at depths 1, 3 and 8) also holds for the generated pipelines (`PipelineAllocationTests`), so OQ-22 does not apply.

- **Proposal:** accept a `DOTNET_JitDisasm` listing as equivalent evidence for criterion 2, and when the gate is re-run in CI, run it on a
  Linux runner so `DisassemblyDiagnoser` output is available too.

### OQ-37: Behavior edge cases

The ADR's behavior rules leave some shapes open. Phase 4 implements these, each on the side that reports or skips rather than generating
code that does not compile:

- **Struct behaviors** implementing a behavior interface are KRX019: a behavior is resolved as a scoped service and held by the pipeline
  context, so it is a class.
- **An explicit interface implementation** of `Handle` (or a hook method) is KRX019: generated code calls the method on the concrete type,
  where an explicit implementation is not accessible. A method inherited from a non-virtual base class implementation is fine.
- **An abstract behavior class** reports KRX019 only, not also KRX020 for its protected constructors.
- **Behavior constructor parameters** follow the handler parameter rules where they can break the generated factory: a `ref`/`in`/`out`,
  `params`, pointer or ref struct parameter is KRX020 ("has a constructor parameter 'x' that ..."), and a `[FromKeyedServices]` key that is not
  a constant (or the parameterless form) is KRX018. The host skips such a behavior.
- **Behaviors in a host narrower than `internal`** (a `private` nested behavior attached to a method, or a `private` nested
  `[GlobalBehavior]`) are KRX016 ("must be at least internal"), like OQ-24 for handlers, and the host skips them.
- **Nullable value types and interface constraints:** a constraint type that is an interface never holds for `Nullable<T>` (CS0313),
  although `ClassifyConversion` reports a boxing conversion from `T?` to `T`'s interfaces.
- **Annotated reference types and type constraints:** a type constraint that is not annotated (`where TResponse : IEquatable<TResponse>`)
  does not hold for an annotated argument (`string?`), because the compiler reports CS8631, an error in generated code, although
  `ClassifyConversion` ignores nullability. An oblivious constraint accepts anything; a constraint that is a type parameter takes its
  argument's annotation.
- **`new()` and `required` members:** a type with `required` members (declared on it or a base type) does not satisfy `new()` (CS9040)
  unless its parameterless constructor carries `[SetsRequiredMembers]`.
- **Constraint types nested in generic types** (`Outer<TResponse>.IMarker`, `Outer<TResponse>.Inner<TRequest>`) are substituted through
  their containing type.
- **The handler manifest attribute** (`[KeryxHandler(..., Behaviors = ...)]`) is written under `#nullable disable`: a `typeof` operand drops
  nullable annotations, so a closed behavior over `string?` would be named over `string` and break a nullable constraint (CS8631).
  Attribute metadata keeps no annotations either way.
- **Closed behaviors and nullability:** "identical" type arguments include nullable annotations when both sides are annotated
  (`IRequestPipelineBehavior<Find, string>` does not fit a request that responds with `string?`), because the link implements
  `INext<string?>` and the mismatch would be a nullability warning, and so an error, in generated code. Oblivious annotations match
  anything.
- **KRX008 in the analyzer** compares behavior classes: an edge to one closing of a generic in-source behavior counts as an edge to the
  class for contradictions and cycles. The host still sorts per pipeline, where a closed edge matches only its closing (ADR, Ordering).
  The host reports a contradiction once per pair of behavior classes, whichever side declares it, and a cycle once per set of behavior
  classes; a cycle is a strongly connected component of the edges left unsorted, so a behavior that only waits on a cycle is not named
  in it, and a cycle of in-source behaviors stays the analyzer's even when a referenced behavior waits on it.
- **KRX009 for a polymorphic notification handler** (parameter typed as a base type or interface) checks only the behavior kind; the
  constraints are checked against each concrete published type when Phase 6 composes notification pipelines.
- **`[assembly: Behavior(typeof(X))]` naming a type that is not a behavior** reports nothing (it matches no target, like any assembly
  behavior that does not fit).
- **KRX035** lists every `[GlobalBehavior]` the behavior rules accept and no `ExcludeBehavior` removes entirely, whether or not it matches a
  target (an exclusion of one closing of an open global leaves it listed). It is reported on the `[GlobalBehavior]` attribute.
- **KRX036 for `ExcludeBehavior`** is not reported for an exclusion that names a notification behavior until Phase 6 composes
  notification pipelines. Stream pipelines are composed already (their behaviors count as used), although the host dispatches streams
  only from Phase 5. (Phase 6 composes notification pipelines, so KRX036 now covers notification behavior exclusions too.)
- **Allocation tests of `async` behaviors** compile the test host with optimizations on: a Debug build compiles an `async` method's state
  machine to a class, allocated on every call even when it completes synchronously. That allocation is the behavior's own (ADR, Performance
  contract: "provided the behaviors' own work completes synchronously"); Keryx's generated pipeline allocates nothing in either build.

- **Interim:** as listed.
- **Proposal:** confirm and fold into the ADR's Behaviors and Ordering sections and the KRX016, KRX019, KRX020, KRX035 and KRX036 rows.

### OQ-38: Terminal hook calls in a fused segment that throw, and disposing the inner stream

The ADR's hook semantics say when `OnCompleteAsync` and `OnErrorAsync` run, but not what happens when one of them throws, whether the
remaining (outer) hooks of the segment still get their terminal call, or where the segment disposes the enumerator of its inner stream
(the guarded region lists `next.InvokeAsync(ct)`, `GetAsyncEnumerator(ct)`, `MoveNextAsync`, `Current` and the hook calls, not
`DisposeAsync`).

- **Interim:** the terminal pass nests like `using` statements, so every hook whose `OnStartAsync` completed gets exactly one terminal
  call even when an inner one throws. Its kind is decided once, before the pass: `OnErrorAsync` when the guarded region threw, otherwise
  `OnCompleteAsync`; an exception thrown during the pass does not change the kind for the outer hooks. An exception from `OnCompleteAsync`
  or `OnErrorAsync` propagates with C# `finally` semantics (it replaces the exception being rethrown, and the last one thrown wins). The
  inner enumerator is disposed before any terminal hook call: on normal completion inside the guarded region (so an exception from that
  `DisposeAsync` reaches `OnErrorAsync`), and on early dispose or after a fault first in the terminal pass (an exception from it then
  propagates like a terminal hook's, and the hooks still get their call).
- **Proposal:** confirm and add to the ADR's Hook semantics. Alternatives: switch the outer hooks to `OnErrorAsync` once a terminal call
  throws, or collect terminal-call exceptions into an `AggregateException`; both need more generated state.

### OQ-39: `ConfigureAwait` in fused hook segments

A fused hook segment awaits the hooks' `OnStartAsync`, `OnCompleteAsync` and `OnErrorAsync` and the inner enumerator's `MoveNextAsync` and
`DisposeAsync`. The ADR does not say whether these awaits capture the caller's synchronization context.

- **Interim:** every await in a generated segment uses `ConfigureAwait(false)`, as library code usually does: hooks and inner elements may
  continue on a thread-pool thread after an asynchronous hook or item. Wrap behaviors, which users write, await however they choose, and
  the consumer's own `await foreach` still resumes on its context.
- **Proposal:** confirm and state it in the ADR's Streaming section. The alternative (capturing the context) keeps thread-affine hooks on
  the caller's context but costs a context switch per asynchronous step in UI and legacy ASP.NET hosts.

### OQ-40: Attached behaviors whose constraints a polymorphic handler's concrete type does not satisfy

`TNotification` of a per-handler behavior is always the concrete published type, so a behavior attached with `[Behavior]` to a
polymorphic handler (`Handle(IDomainEvent e)`) is closed once per concrete type the handler receives. The ADR says an attached behavior
that does not fit its target is KRX009, but a polymorphic handler has several targets, and a constrained behavior (`where T : IAudited`)
may fit some of them and not others. OQ-37 already leaves the constraints out of the analyzer's KRX009 for such a handler.

- **Interim:** the host skips the behavior for each concrete type it does not fit, silently, and applies it to the ones it fits. No
  diagnostic, also when it fits none of them.
- **Proposal:** keep skipping per concrete type, and have the host report KRX009 (or a new warning) only when an attached behavior fits
  none of the concrete types the handler receives, which is almost certainly a mistake.

### OQ-41: `ConfigureAwait` in generated publish code

The sequential strategy's slow path awaits each handler pipeline after the first one that did not complete synchronously, and the
parallel strategy awaits `Task.WhenAll`. The ADR does not say whether these awaits capture the caller's synchronization context, so
whether later handlers run on it.

- **Interim:** every await in generated publish code uses `ConfigureAwait(false)`, as in fused hook segments (OQ-39): after a handler
  yields, the next handlers of a sequential publish may run on a thread-pool thread. Handlers and behaviors, which users write, await
  however they choose, and the caller's own `await Publish(...)` still resumes on its context.
- **Proposal:** confirm together with OQ-39 and state it in the ADR's Notifications section. The alternative keeps thread-affine handlers
  on the caller's context at the cost of a context switch per yielding handler.

### OQ-42: Which notification types the host knows before cross-assembly discovery

Polymorphic expansion needs every concrete notification type the host knows. Phase 6 reads the host's own source; Phase 7 adds the
referenced assemblies through the scan. Until then a handler in the host can still take a notification type declared in a referenced
assembly (typically a contracts assembly).

- **Interim:** the host knows (a) every concrete notification type declared in its own source that generated code can reach (at least
  `internal`; narrower ones are skipped like OQ-24 handlers), and (b) the parameter type of each of its own notification handlers when
  that is itself a concrete notification, wherever it is declared. A referenced type known this way gets its per-publish behaviors from
  `[Behavior]` on the type, the `[assembly: Behavior]`s of its own assembly read from metadata (OQ-3) and the host's globals. Referenced
  types reach polymorphic handlers only through (b) until Phase 7. A typed `Publish` overload is generated for every known type, also for
  one that has neither handlers nor per-publish behaviors (it completes immediately).
- **Proposal:** keep (a) and (b) and add the scanned referenced notification types in Phase 7, as the ADR's third scan describes.

### OQ-43: Exceptions from per-publish behaviors, and derived notifications of unknown types

The ADR says `Publish` throws one `AggregateException` when any handler fails, and that a type the host does not know runs no handlers.
It does not say what a per-publish behavior's own exception looks like, or what happens to a notification whose runtime type derives from
a known type but is itself unknown.

- **Interim:** handler failures, including failures of per-handler behaviors (they are part of the handler's pipeline), are collected into
  the `AggregateException`. A per-publish behavior wraps the whole publish, so an exception it throws (or lets through from `next`, which
  is already the `AggregateException`) propagates as it is, not wrapped again; a synchronous throw from the outermost per-publish behavior
  throws from `Publish` itself, like a request behavior's from `Send`. A notification whose runtime type is unknown runs nothing, even
  when it derives from a known type with handlers: the typed overload of a non-sealed type checks the runtime type and redispatches, as
  typed `Send` does for requests (OQ-31).
- **Proposal:** confirm and add both to the ADR's Notifications section.

### OQ-44: File-local types in a host

A file-local (`file`) type, or any type nested in one, has declared accessibility `internal` or wider, but generated code in another file
cannot name it. OQ-42 generates a typed `Publish` overload for every notification type declared in the host's source, so a
`file sealed record Hidden : INotification;` made the generated mediator fail to compile (CS0234) with no Keryx diagnostic. Behaviors
(global, assembly or attached) that are file-local hit the same problem. Handler classes do not: KRX004 reports them (OQ-26).

- **Interim:** a file-local type anywhere in the containing chain counts as narrower than `internal`. A file-local notification type is
  unknown to the host (no overload, no dispatch, polymorphic handlers do not receive it), like a `private` nested one (OQ-42), and a
  file-local behavior is skipped by the host and reported where the host already reports a behavior narrower than `internal` (KRX016
  with "at least internal", for `[GlobalBehavior]` and attached behaviors).
- **Proposal:** confirm, and say in the ADR that "narrower than `internal`" includes file-local types.

## Decided

### OQ-1: Cancellation during a publish (2026-11-26)

Cancelling the publish token does not skip handlers. Every handler is still invoked with the (possibly cancelled) token; an
`OperationCanceledException` a handler throws is collected like any other failure into the `AggregateException`. Folded into the ADR
(Notifications, Cancellation).

### OQ-2: Single notification handler failure (2026-11-25)

Always `AggregateException`, even when exactly one handler fails. Folded into the ADR (Notifications, Failures).

### OQ-6: Typed `Send` overload for `IRequest` (2026-11-26)

Typed overloads for `IRequest` return `ValueTask<Unit>`, not plain `ValueTask`, so there is no second async adapter. The yielding-path
rule stays strict: generated adapters add 0 bytes in steady state (`UnitAdapter` uses `PoolingAsyncValueTaskMethodBuilder` on net10+).
Folded into the ADR (Generated host, Performance contract, Answered open questions).

### OQ-7: Synchronous `void` handlers for `IRequest` (2026-11-25)

Allowed when the method is synchronous. `async void` stays an error (KRX002). The invoker calls the handler and returns a completed
`ValueTask<Unit>`. Folded into the ADR (Handler rules, return adaptation).

### OQ-12: Behavior interface name (2026-11-25, revised 2026-11-26)

On 2026-11-25 the decision was to keep `IPipelineBehavior<TRequest, TResponse>` with no rename. Revised on 2026-11-26: every behavior
interface follows the pattern of the notification ones, and the names that had no `Pipeline` in them gain it:

| Before                                   | After                                           |
| ---------------------------------------- | ----------------------------------------------- |
| `IPipelineBehavior<TRequest, TResponse>` | `IRequestPipelineBehavior<TRequest, TResponse>` |
| `INotificationBehavior<TNotification>`   | `INotificationPipelineBehavior<TNotification>`  |
| `INotificationHandlerBehavior<T>`        | `INotificationHandlerPipelineBehavior<T>`       |
| `IStreamBehavior<TRequest, TItem>`       | `IStreamPipelineBehavior<TRequest, TItem>`      |
| `IStreamWrapBehavior<TRequest, TItem>`   | `IStreamWrapPipelineBehavior<TRequest, TItem>`  |

Shapes and semantics are unchanged. Applied to code, tests, the public API snapshot, `KnownTypeNames`, the ADR, the brief and the
glossary; the source files are named after the interfaces. Folded into the ADR (Behaviors, Alternatives considered).
The stream rows were superseded later the same day by "Stream behaviors: wrap model" below: `IStreamPipelineBehavior` changed shape,
`IStreamWrapPipelineBehavior` was removed, and the hooks moved to `IStreamHookBehavior`.

### Stream allocation exceptions (2026-11-26)

Per `Stream` call: one iterator per fused hook segment (one when there is no `IStreamWrapPipelineBehavior`), plus one linked
`CancellationTokenSource` only when two distinct cancellable tokens are supplied, plus whatever a wrap behavior's own enumerable
allocates; 0 per item. Allocation tests include the two-token case. Folded into the ADR (Streaming, Performance contract).
Restated for the wrap model by "Stream behaviors: wrap model" below.

### Parallel publish (2026-11-26)

Start each handler pipeline in handler order on the calling thread without awaiting (no `Task.Run`), catching synchronous throws. 0 B
when all complete synchronously; otherwise one array plus `AsTask()` per pending `ValueTask`, then `Task.WhenAll`. `AggregateException`
in handler order from synchronous and asynchronous failures. Folded into the ADR (Notifications, Publish strategy; Performance contract).

### OQ-20: C# language version floor (2026-11-26)

The question: `init` properties on `KeryxHandlerAttribute`, `KeryxManifestAttribute` and `BehaviorOrderAttribute` need C# 9 when set
through an attribute named argument, and a `netstandard2.0` project defaults to C# 7.3, so generated code would not compile there.

Decided: keep the `init` properties. The floor is **C# 11**, because the generic `RunsAfter<T>`/`RunsBefore<T>` attributes (decided
below) need it too. The package's `build/Keryx.props` sets `<LangVersion>latest</LangVersion>` when the consuming project leaves
`LangVersion` empty, as `Specular.Scanning.Sources` does for its own default; a value the project or its `Directory.Build.props` sets
still wins. A project below C# 11 gets error **KRX022** (Phase 2 implements the analyzer; the generators emit nothing there).
`ManifestContractTests` keeps pinning the C# 8 failure of the `init` named arguments. Verified by packing: a `netstandard2.0` project
with no `LangVersion` that references the package evaluates `LangVersion` to `latest` and compiles `[RunsAfter<T>]` and
`[BehaviorOrder(..., Order = n)]`; with `LangVersion` 10 in its `Directory.Build.props` it keeps 10 and fails with CS8936. Folded into
the ADR (Package and assemblies, Diagnostics, Alternatives considered, Consequences).

### Generic `RunsAfter<T>`/`RunsBefore<T>` (2026-11-26)

`RunsAfterAttribute<TBehavior>` and `RunsBeforeAttribute<TBehavior>` sit beside the `typeof` forms, with the same `AttributeUsage`
(class, `AllowMultiple`) and a `BehaviorType` property that returns `typeof(TBehavior)`. They name only closed or non-generic behaviors,
because C# forbids an unbound generic type as a type argument (CS7003); open behaviors keep `typeof(X<,>)`. The generator treats both
forms identically: the behavior comes from the constructor argument of the `typeof` form and from the type argument of the generic form.
`KnownTypeNames` has `RunsAfterAttributeOfT` (``Keryx.RunsAfterAttribute`1``) and `RunsBeforeAttributeOfT`
(``Keryx.RunsBeforeAttribute`1``). `RunsAfterBeforeFormsTests` checks that both forms, compiled against the `netstandard2.0`
`Keryx.dll` and read back from metadata, give the same edges. Folded into the ADR (Ordering, Attribute reference, Manifest).

### One analyzer assembly (2026-11-26)

`Keryx.Analyzers.CodeFixes` is folded into `Keryx.Analyzers`: one analyzer assembly holds the generators, the analyzers and the code
fixes (in its `CodeFixes` folder), and references `Microsoft.CodeAnalysis.CSharp.Workspaces`. The package ships
`Keryx.Analyzers.dll` and `System.Text.Json.dll` in `analyzers/dotnet/cs`, and `Specular.Scanning.Cache.targets` in `build/`. RS1038 is
suppressed in `Keryx.Analyzers.csproj` with a comment. The accepted risk: the command-line compiler does not ship Workspaces, so a
code-fix type reached from a generator or analyzer code path fails command-line builds; code-fix types are never touched from those
paths. Folded into the ADR (Package and assemblies, Repository layout, Diagnostics and code fixes, Alternatives considered,
Consequences).

### Stream behaviors: wrap model (2026-11-26)

Stream behaviors follow the Immediate.Handlers `StreamingBehavior` model
(`src/Immediate.Handlers.Shared/StreamingBehavior.cs`: the behavior receives the request, calls `Next(request, ct)` to get the inner
`IAsyncEnumerable`, and re-yields it with `await foreach`), adapted to Keryx's struct links:

- `IStreamPipelineBehavior<TRequest, TItem>` is the wrap shape:
  `IAsyncEnumerable<TItem> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct,
  IStreamNext<TItem>`, still with `where TRequest : IStreamRequest<TItem>`. The link holds the request, as `INext<T>` links do, so
  `IStreamNext<TItem>.InvokeAsync(CancellationToken)` takes only the token and returns `IAsyncEnumerable<TItem>`.
- `IStreamNext<TItem>` is new, named after `INext<T>`; `KnownTypeNames.IStreamNext` is ``Keryx.IStreamNext`1``.
- `IStreamWrapPipelineBehavior` is removed (merged into `IStreamPipelineBehavior`).
- The fused hooks (`OnStartAsync`/`OnItem`/`OnCompleteAsync`/`OnErrorAsync`, with the outward semantics decided before) are kept as the
  optional zero-extra-iterator form `IStreamHookBehavior<TRequest, TItem>` (`KnownTypeNames.IStreamHookBehavior`). Hook behaviors between
  two wrap behaviors are fused into one segment. The name was given with the decision; it does not follow the
  `I{Kind}PipelineBehavior` pattern of OQ-12.
- Phases, scopes, constraints, ordering and diagnostics apply to both kinds. Both are sorted in one list; a class implementing both
  interfaces is KRX019 (more than one behavior interface closing).
- Allocation contract per `Stream` call: one iterator per wrap behavior plus one per fused hook segment, plus one linked
  `CancellationTokenSource` only when two distinct cancellable tokens are supplied; 0 per item; with no behaviors the handler's enumerable
  is returned as is.

Applied to `src/Keryx` (`IStreamPipelineBehavior.cs`, `IStreamHookBehavior.cs`, `IStreamNext.cs`), `KnownTypeNames`, the shape tests,
the public API snapshot, the ADR, the brief (Phase 5) and the glossary. Exceptions raised outside a fused segment are OQ-21. Folded into
the ADR (Behaviors, Streaming, Performance contract, Alternatives considered).

### Phase 4 gate does not stop work (2026-11-26)

A failed Phase 4 performance gate no longer stops implementation. The Phase 4 implementer records the measured numbers for each gate
criterion (allocations at depths 1, 3 and 8; the `DisassemblyDiagnoser` findings for the `Handle` and `next.InvokeAsync` calls; mean time
at depth 3 against the nested-call baseline) in a new entry in this file, commits Phase 4 with the struct-based pipeline as designed, and
continues to Phase 5. The hook fallback is still not adopted without a decision. Folded into the ADR (Phase 4 gate, Alternatives
considered) and the brief (Git workflow, Phase 4).
