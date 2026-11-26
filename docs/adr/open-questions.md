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

### Stream allocation exceptions (2026-11-26)

Per `Stream` call: one iterator per fused hook segment (one when there is no `IStreamWrapPipelineBehavior`), plus one linked
`CancellationTokenSource` only when two distinct cancellable tokens are supplied, plus whatever a wrap behavior's own enumerable
allocates; 0 per item. Allocation tests include the two-token case. Folded into the ADR (Streaming, Performance contract).

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
