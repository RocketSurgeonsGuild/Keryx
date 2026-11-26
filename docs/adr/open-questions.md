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

- **Interim:** 16.
- **Proposal:** set it from the Phase 3 benchmark and record the measured crossover here.

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
  than `internal` is skipped. Generated member accessibility follows the signature alone (`public` only when every type it mentions is
  effectively public), so a `protected internal` handler class with internal signature types also gets `internal` members rather than
  public ones that fail with CS0051.
- **Proposal:** confirm and extend the KRX016 row of the ADR ("... and every type the generated constructor and invoker mention").

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
