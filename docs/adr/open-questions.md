# Open questions

This file collects questions raised while writing or implementing [ADR 0001](0001-keryx-mediator.md). Each entry states the gap, what the
implementation does until it is decided (the safest option), and a proposed answer. The ADR stays authoritative. When an entry is
decided, fold the answer into the ADR and move the entry to **Decided** with the date.

Implementers: add new entries at the end of **Open**, tell the user, and continue with the stated interim behavior. Do not change the ADR
on your own. Every open entry has an **Interim** line; that is what the code does until the entry is decided.

Entries that were Open with Interim and Proposal text keep it as a record and end with a **Decision** line; earlier entries state the
decision directly. Where the two disagree, the Decision line and the ADR win. Entries decided on 2026-11-26 after the review of the
whole design follow the "Static behaviors" and "Stream hook behaviors removed" entries; the owner's second round of decisions the same
day added "One notification behavior kind" and "Message constraint on open behavior methods" after those two, and decided OQ-52 to
OQ-54 (at the end). OQ-55 was raised afterwards, while reviewing that round, and decided the same day (at the end).

## Open

### OQ-56: The `net11.0` dependency on `Microsoft.Extensions.DependencyInjection.Abstractions` (2026-11-26)

OQ-8 makes the `Keryx` package depend on `Microsoft.Extensions.DependencyInjection.Abstractions` 11.0.x for `net11.0`. The .NET 11 shared
framework (`Microsoft.NETCore.App`) now carries `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, so the SDK's package pruning
drops the `PackageReference` for `net11.0`, and `dotnet pack` writes an empty `net11.0` dependency group. The ADR and the Phase 9 "Done
when" list expect the package in every TFM's group.

- **Interim:** `Keryx.csproj` removes the package from `PrunePackageReference` (target `_KeryxKeepDependencyInjectionAbstractions`), so
  the `net11.0` group lists `Microsoft.Extensions.DependencyInjection.Abstractions` 11.0.0 as the ADR says. A `net11.0` consumer's own
  restore prunes it again (the framework already provides the assembly), so it changes nothing at run time.
- **Proposal:** keep the interim (the package lists the dependency for every TFM, as decided), or let pruning drop it for `net11.0` and
  say in the ADR (Package and assemblies) that `net11.0` gets the assembly from the shared framework. Needs a decision.

### OQ-57: Which behavior methods KRX034 checks, and message constraints implied through another interface (2026-11-26)

Two details of the static-behavior rework that the ADR does not settle:

1. **KRX034 for a referenced behavior class with several kinds.** The ADR (How the host reads it; KRX034) says the host checks "the
   injected parameter types of a referenced behavior method the host calls". A global or `[assembly: Behavior]` class is resolved before
   the host knows which of its kinds any pipeline uses, and KRX035 lists a global behavior per class, so checking only the methods a
   pipeline calls would make KRX035 and KRX034 depend on which targets exist.
2. **Message constraint through a derived interface.** An open behavior method's message type parameter "must be constrained to the
   kind's message interface" (Behavior methods; KRX019). The ADR does not say whether a constraint to an interface that itself implements
   it (`where TRequest : ICommand<TResponse>` with `ICommand<T> : IRequest<T>`, `where TNotification : IDomainEvent` with
   `IDomainEvent : INotification`) counts.

- **Interim:** (1) a referenced behavior class is left out of every pipeline, with KRX034, when an injected parameter type of any of its
  behavior methods (any kind it carries) cannot be resolved; a global one is then not listed by KRX035. (2) The constraint counts when
  the constraint type is the kind's message interface or implements it (with the response or item type parameter for requests and
  streams); the compiler guarantees the same conversion either way.
- **Proposal:** keep both. Alternative for (1): check per kind, leaving the class in the pipelines of the kinds whose methods resolve.
  Alternative for (2): require the kind's message interface itself in the constraint list.

### OQ-58: An open behavior method that annotates its response type parameter (2026-11-26)

A fully open behavior method has its message and response (or item) positions as method type parameters (Behavior methods, Generic
shape). The ADR does not say whether `TResponse?` counts as that type parameter: `where TRequest : IRequest<TResponse?>`,
`where TNext : struct, INext<TResponse?>` (`IStreamNext<TItem?>`) or a return type of `ValueTask<TResponse?>`
(`IAsyncEnumerable<TItem?>`). Matching substitutes the bare type parameter and ignores the annotation, but `IRequest<>` and `INext<>` are
invariant, so for a request that responds with `string` the generated code fails with CS8631 (`Ping` does not satisfy
`IRequest<string?>`; the link is `INext<string>`, not `INext<string?>`) or CS8619, and nothing reports it. As a global or assembly
behavior, it breaks every host it reaches.

- **Interim:** KRX019. An open request or stream behavior method whose `next` constraint, return type or message constraint carries
  `TResponse?` (`TItem?`) is rejected: "carries its response type parameter as 'TResponse?'; an open behavior method's 'next', return
  type and message constraint carry the bare type parameter 'TResponse', not 'TResponse?'". The host skips the class, so generated code
  never names it.
- **Proposal:** keep the interim and say in the ADR (Behavior methods, Generic shape; KRX019) that the open positions are the bare type
  parameters. Alternative: accept the annotation and make matching substitute `string?` where the method writes `TResponse?`, then
  check each substituted constraint with its nullability against the target (a `TResponse?` method would then fit only requests that
  respond with an annotated or oblivious type).

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
**Superseded 2026-11-26** by "Static behaviors" below: the behavior interfaces are removed, and behaviors are static classes marked
with kind attributes, which are `[RequestBehavior]`, `[StreamBehavior]` and `[NotificationBehavior]` since "One notification behavior
kind" below.

### Stream allocation exceptions (2026-11-26)

Per `Stream` call: one iterator per fused hook segment (one when there is no `IStreamWrapPipelineBehavior`), plus one linked
`CancellationTokenSource` only when two distinct cancellable tokens are supplied, plus whatever a wrap behavior's own enumerable
allocates; 0 per item. Allocation tests include the two-token case. Folded into the ADR (Streaming, Performance contract).
Restated for the wrap model by "Stream behaviors: wrap model" below, and again by "Stream hook behaviors removed" below (one iterator
per stream behavior; fused hook segments are gone).

### Parallel publish (2026-11-26)

Start each handler pipeline in handler order on the calling thread without awaiting (no `Task.Run`), catching synchronous throws. 0 B
when all complete synchronously; otherwise one array plus `AsTask()` per pending `ValueTask`, then `Task.WhenAll`. `AggregateException`
in handler order from synchronous and asynchronous failures. Folded into the ADR (Notifications, Publish strategy; Performance contract).
Since "One notification behavior kind" (below) handlers are called directly; there are no per-handler pipelines.

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
**Note 2026-11-26:** the generic ordering attributes were removed with "Static behaviors" below, so the C# 11 reason given here is
gone; OQ-52 below raises the floor to C# 14.

### Generic `RunsAfter<T>`/`RunsBefore<T>` (2026-11-26)

`RunsAfterAttribute<TBehavior>` and `RunsBeforeAttribute<TBehavior>` sit beside the `typeof` forms, with the same `AttributeUsage`
(class, `AllowMultiple`) and a `BehaviorType` property that returns `typeof(TBehavior)`. They name only closed or non-generic behaviors,
because C# forbids an unbound generic type as a type argument (CS7003); open behaviors keep `typeof(X<,>)`. The generator treats both
forms identically: the behavior comes from the constructor argument of the `typeof` form and from the type argument of the generic form.
`KnownTypeNames` has `RunsAfterAttributeOfT` (``Keryx.RunsAfterAttribute`1``) and `RunsBeforeAttributeOfT`
(``Keryx.RunsBeforeAttribute`1``). `RunsAfterBeforeFormsTests` checks that both forms, compiled against the `netstandard2.0`
`Keryx.dll` and read back from metadata, give the same edges. Folded into the ADR (Ordering, Attribute reference, Manifest).
**Superseded 2026-11-26** by "Static behaviors" below: a static class cannot be a type argument (CS0718), so the generic forms cannot
name a behavior and are removed. Only the `typeof` forms remain; the ADR lists the generic forms under Alternatives considered.

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
**Partly superseded 2026-11-26:** the hooks part (`IStreamHookBehavior`, fused hook segments, one iterator per segment, a class
implementing both interfaces) is reversed by "Stream hook behaviors removed" below, and `IStreamPipelineBehavior` becomes a static
`[StreamBehavior]` by "Static behaviors" below. The wrap model itself (a behavior gets the inner stream from a struct
`IStreamNext<TItem>` link and re-yields it) stands.

### Phase 4 gate does not stop work (2026-11-26)

A failed Phase 4 performance gate no longer stops implementation. The Phase 4 implementer records the measured numbers for each gate
criterion (allocations at depths 1, 3 and 8; the `DisassemblyDiagnoser` findings for the `Handle` and `next.InvokeAsync` calls; mean time
at depth 3 against the nested-call baseline) in a new entry in this file, commits Phase 4 with the struct-based pipeline as designed, and
continues to Phase 5. The hook fallback is still not adopted without a decision. Folded into the ADR (Phase 4 gate, Alternatives
considered) and the brief (Git workflow, Phase 4).

### Static behaviors (2026-11-26)

Replaces OQ-11 and the interface-based behavior model of Phases 4 to 8.

- A behavior is a `static` class. Its behavior methods are `static` methods named `Handle` or `HandleAsync`, written by the user and
  called directly by the host's generated pipeline. No generated half, no `partial` requirement.
- Accessibility: the class and method are `public` to be used from another assembly (a library's attached, assembly or global behaviors
  read by a host); `internal` is allowed inside a host for its own use. Narrower is KRX016.
- No behavior instance ever exists, so nothing is allocated for the behavior itself per scope or per call. A behavior method's injected
  parameters are resolved once per DI scope into the generated pipeline context (which already holds the handler), and the link calls
  `Behavior.Handle(request, next, injected..., cancellationToken)`. A transient service injected into a behavior is therefore cached per
  scope per pipeline, as it was when behaviors were scoped instances.
- Kind attributes on the class, one per kind, three kinds: `[RequestBehavior]` (was `IRequestPipelineBehavior`), `[StreamBehavior]`
  (was `IStreamPipelineBehavior`, the wrap model) and `[NotificationBehavior]` (per publish; was `INotificationPipelineBehavior`). As
  first decided there were four: the per-publish kind was named `[PublishBehavior]`, and a per-handler kind,
  `[NotificationHandlerBehavior]` (was `INotificationHandlerPipelineBehavior`), sat beside it; "One notification behavior kind" below
  removed the per-handler kind and renamed the per-publish one.
- One class may carry several kind attributes. For each one it declares exactly one behavior method of that kind; a kind attribute with
  no matching method, or two methods of one kind, is a compile error. KRX019 becomes the static-behavior shape rule (class not static,
  kind attribute without a valid method, more than one method per kind, partially open method, and so on).
- `typeof(X)` in `[Behavior]`, `[ExcludeBehavior]`, `[RunsAfter]` and `[RunsBefore]` names the class and means "the behavior of X whose
  kind fits the target or pipeline".
- Generics: the method carries the type parameters (non-generic static class). A behavior method is fully open (every message and
  response or item position is a method type parameter; constraints decide matching, and the message type parameter must be
  constrained to the kind's message interface, per "Message constraint on open behavior methods" below) or fully closed (generic only
  in `TNext`). Partially open methods are rejected.
- Signature rules mirror handlers: the message, `next` and an optional `CancellationToken` are found by type, in any order; every other
  parameter is an injected service, with the handler parameter rules (KRX013, KRX018). `next` is a method type parameter constrained
  `struct, INext<TResponse>` (request), `struct, IStreamNext<TItem>` (stream) or `struct, INext` (notification).
  Return `ValueTask<TResponse>`, `IAsyncEnumerable<TItem>` or `ValueTask`, synchronous or `async`.
- `[BehaviorOrder]`, `[RunsAfter]`/`[RunsBefore]` and `[GlobalBehavior]` go on the class and apply to every behavior it holds; the same
  attribute on a behavior method overrides the class's for that kind only.
- Removed from the public API: `IRequestPipelineBehavior`, `IStreamPipelineBehavior`, `IStreamHookBehavior`,
  `INotificationPipelineBehavior`, `INotificationHandlerPipelineBehavior`. Kept: `INext<T>`, `IStreamNext<T>`, `INext`, `Phase`.
- Removed: the generic `RunsAfterAttribute<T>`/`RunsBeforeAttribute<T>` (a static class cannot be a type argument, CS0718), reversing
  "Generic `RunsAfter<T>`/`RunsBefore<T>`" above; and the closed forms that matched one closing only (closed `ExcludeBehavior`, closed
  `RunsAfter` edges): every reference names the whole class.
- KRX020 (exactly one public behavior constructor) is retired.
- The Phase 4 gate is re-run after the rework.

Details added while folding these decisions into the ADR, which follow from them and were confirmed by the owner in the second round
of decisions the same day:

- A method's `[RunsAfter]` (or `[RunsBefore]`) list replaces the class's list of that attribute for that kind. `[GlobalBehavior]` on a
  method makes only that kind global; on both the class and a method, every kind is global.
- KRX019 also rejects an open position inside a constructed type (`INext<Result<T>>`; full type-parameter unification is future work),
  a closed method whose `next` does not carry the message's response or item type, and a `Handle`/`HandleAsync` method whose kind the
  class does not carry or that has no valid `next` parameter.
- KRX042 also covers `[GlobalBehavior]` on a class without a kind attribute or on a method that is not a behavior method.
- The host's global-behavior scan also finds classes whose `[GlobalBehavior]` is only on a method. Specular.Scanning's type filter has
  no method-attribute filter, but it can express this: `WithAnyAttribute` on the three kind attributes (then checking the attributes in
  the transform) or a `CustomTypeFilter` through `Where`.
- C# does not allow two methods that differ only in their constraints (CS0111), so a class holding two kinds with the same parameter
  list names one method `Handle` and the other `HandleAsync`.
- `ExternallyHandledAttribute` exposes the type as a property named `RequestType`.

Folded into the ADR (Status, Handlers, Injected parameters, Diagnostics and code fixes, Behaviors, Ordering, Streaming, Notifications,
Generated host, Cross-assembly discovery, Attribute reference, Performance contract, Phase 4 gate, Alternatives considered,
Consequences) and the glossary. Implemented in Phase 9 of the brief.

### Stream hook behaviors removed (2026-11-26)

`IStreamHookBehavior`, fused hook segments and all hook semantics are removed. Streams have wrap behaviors (`[StreamBehavior]`) only.
This reverses the "hooks stay optional (`IStreamHookBehavior`)" part of "Stream behaviors: wrap model" above, and makes OQ-21, OQ-38
and OQ-39 moot. Allocation contract per `Stream` call: one iterator per stream behavior, plus one linked `CancellationTokenSource` only
when two distinct cancellable tokens are supplied; with no behaviors the handler's enumerable is returned as is. The comparison
benchmarks drop the Keryx hook-behavior stream variant. Folded into the ADR (Streaming, Performance contract, Benchmarks, Alternatives
considered) and the glossary. Implemented in Phase 9 of the brief.

### One notification behavior kind (2026-11-26)

Decided by the owner in the second round of decisions, on top of "Static behaviors" above.

- Only one notification behavior kind remains: the per-publish kind, renamed `[NotificationBehavior]` (was `[PublishBehavior]`, and
  before that `INotificationPipelineBehavior`). It wraps the whole `Publish` call once (`next` constrained `struct, INext`, returning
  `ValueTask`) and runs even with zero handlers (OQ-18).
- Per-handler notification behaviors (`[NotificationHandlerBehavior]`, formerly `INotificationHandlerPipelineBehavior`) are removed
  entirely. The handlers of a publish are called without per-handler pipelines.
- A notification behavior is attached with `[Behavior]` on the notification type, with `[assembly: Behavior]` in the notification
  type's assembly (OQ-3), or with `[GlobalBehavior]`. `[Behavior]` on a notification handler method has no effect and is KRX042 (the
  no-effect placement warning).
- The behavior kinds are therefore `[RequestBehavior]`, `[StreamBehavior]` and `[NotificationBehavior]`. A method's kind is fully
  determined by its signature (`next` constraint and message type), so a class may still carry several kind attributes, with one method
  per kind.
- Why: one notification kind keeps a method's kind inferred purely from its signature; publish-wide concerns run once per publish; a
  handler that needs a retry can do it itself.

This makes OQ-53 moot, OQ-40 moot, and the per-handler part of OQ-43 moot. Folded into the ADR (Status, Behavior kinds, Behavior
methods, Three ways to attach a behavior, Matching, Ordering, Notifications, Manifest, Attribute reference, Performance contract,
KRX009 and KRX042 rows, Alternatives considered, Consequences) and the glossary. Implemented in Phase 9 of the brief.

### Message constraint on open behavior methods (2026-11-26)

Decided by the owner in the second round of decisions. An open behavior method must constrain its message type parameter to the kind's
message interface: `TRequest : IRequest<TResponse>` for `[RequestBehavior]`, `TRequest : IStreamRequest<TItem>` for `[StreamBehavior]`
and `TNotification : INotification` for `[NotificationBehavior]`. An open method without it is KRX019. A closed method names a concrete
message type, so the rule does not apply to it. Folded into the ADR (Behavior kinds, Behavior methods, KRX019 row, the examples).
Implemented in Phase 9 of the brief.

### OQ-3: Assembly scope for notification behaviors (2026-11-26)

"Per assembly" was defined for request handlers only. The ADR applies `[assembly: Behavior]` to per-handler notification behaviors in the
**handler's** assembly and to per-publish notification behaviors in the **notification type's** assembly.

- **Interim:** as the ADR says (handler's assembly for per-handler behaviors, notification type's assembly for per-publish behaviors).
- **Proposal:** confirm.
- **Decision:** Accepted as proposed. `[assembly: Behavior]` applies notification handler behaviors in the handler's assembly and publish
  behaviors in the notification type's assembly. Folded into the ADR (Notifications, Behaviors). Per-handler notification behaviors
  were removed later the same day ("One notification behavior kind", above), so only the notification type's assembly remains: it is
  where `[assembly: Behavior]` applies `[NotificationBehavior]`s.

### OQ-4: Notification behavior signature (2026-11-26)

The ADR adds a non-generic `INext` (`ValueTask InvokeAsync(CancellationToken)`) for notification behaviors, rather than reusing
`INext<Unit>`, so notification behaviors return plain `ValueTask`.

- **Interim:** non-generic `INext`, as in the ADR.
- **Proposal:** confirm. The alternative (`INext<Unit>`, `ValueTask<Unit>`) reuses one interface but makes every notification behavior
  return `Unit`.
- **Decision:** Accepted as proposed. Publish and notification handler behaviors take the non-generic `INext` and return plain `ValueTask`.
  Folded into the ADR (Behavior kinds, Notifications). Since "One notification behavior kind" (above) this is the one
  `[NotificationBehavior]` kind.

### OQ-5: `IPublisher.Publish` shape (2026-11-26)

The ADR uses `ValueTask Publish<TNotification>(TNotification, CancellationToken) where TNotification : INotification`. A non-generic
`Publish(INotification)` overload was not added.

- **Interim:** generic form only.
- **Proposal:** keep only the generic form. With a value-type `TNotification`, the generated dispatch compares `typeof(TNotification)`
  (folded by the JIT) and converts with `Unsafe.As`, so a struct notification passed by its own type is never boxed by dispatch. A
  non-generic overload would box every struct notification. Base-typed calls (`Publish<IDomainEvent>(e)`) still work through the
  `GetType()` path for reference-type `TNotification`.
- **Decision:** Accepted as proposed. `IPublisher` has only the generic `Publish<TNotification>`; no non-generic overload. Folded into the
  ADR (Notifications).

### OQ-8: `Microsoft.Extensions.DependencyInjection.Abstractions` dependency (2026-11-26)

Generated host code uses `IServiceCollection`, `IServiceProvider` extensions and keyed-service APIs. The ADR does not say whether the
`Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions`.

- **Interim:** `Keryx` takes no DI dependency. Hosts reference it themselves (every ASP.NET Core and generic host app already does).
- **Proposal:** add a host error when the host compilation cannot resolve `Microsoft.Extensions.DependencyInjection.IServiceCollection`, or
  add the dependency to the package (per-TFM versions, as Specular's CPM does). Needs a decision.
- **Decision:** The `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions`, per TFM: 10.0.x for `net10.0`,
  11.0.x for `net11.0` and 8.0.x for `netstandard2.0` (the oldest version with keyed services). No host error is added. Folded into the ADR
  (Package and assemblies, Injected parameters, Consequences); implemented in Phase 9.

### OQ-9: Analyzer packing folder and Roslyn pin (2026-11-26)

The analyzers compile against `Microsoft.CodeAnalysis.CSharp` 5.0.0, while the Specular template pins 5.12.0 in CPM for everything else
(tests need the newer Roslyn through `Rocket.Surgery.Extensions.Testing.SourceGenerators`).

- **Interim:** analyzer projects use 5.0.0; tests use the newer version. Pack to `analyzers/dotnet/cs`. No multi-Roslyn `*.supports`
  projects in v1.
- **Proposal:** confirm, or pack to `analyzers/dotnet/roslyn5.0/cs` as Specular does.
- **Decision:** Confirmed as built. The analyzers compile against `Microsoft.CodeAnalysis.CSharp` 5.0.0 and pack to `analyzers/dotnet/cs`;
  no Roslyn-versioned folder and no `*.supports` projects in v1. Folded into the ADR (Package and assemblies).

### OQ-10: Per-assembly relief for KRX030 (2026-11-26)

KRX030 (request visible to the host with no handler) can only be lowered globally through `.editorconfig`. A host that references one
contracts assembly handled out of process, and another it does handle, cannot keep the error for the second.

- **Interim:** global severity only.
- **Proposal:** add `[assembly: KeryxIgnoreUnhandledFrom(typeof(MarkerInThatAssembly))]` on the host. Needs a decision.
- **Decision:** Relief is per request type, not per assembly. The host attribute `[assembly: ExternallyHandled(typeof(X))]`
  (`AllowMultiple`) turns KRX030 off for `X`; naming a type the host does handle, or a type that is not a request or stream request, has no
  effect and is KRX036. `KeryxIgnoreUnhandledFrom` is not added. Folded into the ADR (Host control, Diagnostics, Attribute reference,
  Alternatives considered); implemented in Phase 9.

### OQ-11: Behavior constructors (2026-11-26)

The ADR requires exactly one public constructor on each behavior (KRX020), because the host emits explicit factories. Behaviors with
several constructors (for example one for tests) are rejected.

- **Interim:** exactly one public constructor (KRX020).
- **Proposal:** confirm, or pick the constructor with the most parameters as MS.DI does.
- **Decision:** Moot. Behaviors are static classes with static behavior methods and have no constructors (Static behaviors, above), so
  KRX020 is retired. Folded into the ADR (Behaviors, Diagnostics, Alternatives considered).

### OQ-13: Code-fix additions (2026-11-26)

Not in the original ADR, added while writing it:

- KRX001 offers a second action, "Mark this assembly as a contracts assembly". KRX001 is never reported in a host, so this action never
  appears there.
- KRX002 has a code fix that makes the method `private static`.

- **Interim:** both code fixes are implemented.
- **Proposal:** confirm.
- **Decision:** Accepted as proposed: both code fixes stay. Folded into the ADR (Code fixes).

### OQ-14: Host namespace and target framework source (2026-11-26)

Generated host types go in the host's root namespace, read from `RootNamespace` through `CompilerVisibleProperty`. KRX031 needs the
target framework, which a `Compilation` does not carry. Both come from `build/Keryx.props` in the package, which exposes
`RootNamespace;TargetFrameworkIdentifier;TargetFrameworkVersion`.

- **Interim:** as the ADR says. Namespace falls back to the sanitised assembly name; KRX031 falls back to the referenced `System.Runtime`
  version when the properties are absent.
- **Proposal:** confirm; an explicit `[assembly: KeryxHost(Namespace = "...")]` override can be added later if needed.
- **Decision:** Accepted as proposed. An explicit `[assembly: KeryxHost(Namespace = "...")]` override can still be added later. Folded into
  the ADR (Which projects may target what, Opting in).

### OQ-15: Dispatch threshold (2026-11-26)

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

- **Decision:** The threshold is 32 (the ADR's "more than 16 request types" becomes 32), and the ADR documents the non-inlined per-arm
  `__Dispatch{n}`/`__RequestIndex` shape found while measuring. Folded into the ADR (Generic entry points).

### OQ-16: A host that references another host (2026-11-26)

A test project that is a host and references the app host sees the app's public request types, but the app host may keep its handlers
`internal`, and internal handlers carry no manifest. Without a rule, the test host would get KRX030 for every such request.

- **Interim:** KRX030 skips request types declared in an assembly that carries `[assembly: KeryxHost]`. The referencing host dispatches
  such a request only if the referenced host's handler is public; otherwise it throws `KeryxHandlerNotFoundException` at run time.
- **Proposal:** keep the interim. Alternative: require a host that is referenced by another host to keep its handlers public, with an
  analyzer warning for internal handlers in a host that grants `InternalsVisibleTo`.
- **Decision:** A host referenced by another host keeps the handlers it wants composed `public`. Enforcement is KRX030 only: the referencing
  host no longer skips KRX030 for request types declared in a referenced host, and nothing new is reported in the referenced host. Folded
  into the ADR (Host control, KRX030 row, Alternatives considered); implemented in Phase 9.

### OQ-17: Struct notifications delivered to interface-typed handlers (2026-11-26)

A polymorphic handler typed as an interface or `INotification` cannot receive a struct notification without boxing it. The performance
contract promised 0 extra allocations for a sequential publish, which cannot hold for this case without dropping polymorphic delivery of
struct notifications.

- **Interim:** box once per `Publish` and reuse that box for every interface-typed handler; Info KRX037 reports it; the performance
  contract lists it as a documented exception.
- **Proposal:** keep the interim. Alternative: make "struct notification with an interface-typed handler" an error, which narrows the
  polymorphic-handler decision.
- **Decision:** Kept as the interim: box once per `Publish`, reuse the box for every interface-typed handler, Info KRX037, documented
  exception in the Performance contract. Folded into the ADR (Notifications, Performance contract).

### OQ-18: Per-publish behaviors when a notification has zero handlers (2026-11-26)

The ADR said both "zero handlers: `Publish` completes immediately" and "`INotificationPipelineBehavior` wraps the whole `Publish` call". The
verification pass picked one reading.

- **Interim:** for a notification type the host knows, the per-publish pipeline runs even with zero handlers. `Publish` completes
  immediately only when there are neither handlers nor per-publish behaviors, or when the runtime type is unknown.
- **Proposal:** confirm (logging and outbox behaviors then see every publish).
- **Decision:** Accepted as proposed: the publish pipeline runs even with zero handlers. Folded into the ADR (Notifications, Zero handlers).
  The per-publish behavior is now `[NotificationBehavior]` ("One notification behavior kind", above).

### OQ-19: Shapes of the attributes the ADR lists only in the Attribute reference table (2026-11-26)

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
- **Decision:** Accepted as proposed (`Inherited` keeps its default). The declarations are in the ADR's Attribute reference, together with
  the behavior kind attributes and `ExternallyHandled`; with static behaviors, `GlobalBehavior`, `BehaviorOrder`, `RunsAfter` and
  `RunsBefore` also target methods. Folded into the ADR (Attribute reference, Ordering).

### OQ-21: Hook behaviors and exceptions raised outside their fused segment (2026-11-26)

With the wrap model (decided 2026-11-26, below), a wrap behavior splits hook behaviors into separate fused segments, each its own
iterator. An exception raised outside a segment, in an outer wrap behavior or in a hook of an outer segment, never passes through the
inner segment's iterator: the outer code stops enumerating and disposes it. Before the wrap model, all hooks shared one iterator and
`OnErrorAsync` saw every exception from any hook.

- **Interim:** a segment's `OnErrorAsync` observes exceptions from its inner stream (inner behaviors and the handler) and from its own
  hooks. An exception raised outside the segment reaches it as an early dispose, so its hooks run `OnCompleteAsync`. This is what nested
  iterators do on their own and costs nothing extra. Within one segment, the previously decided semantics are unchanged.
- **Proposal:** confirm. The alternative (inner hooks also see outer exceptions through `OnErrorAsync`) would need shared state between
  segments per `Stream` call, an extra allocation the stream contract does not allow.
- **Decision:** Moot: hook behaviors removed (Stream hook behaviors removed, above).

### OQ-22: Phase 4 0 B allocation tests after a failed allocation gate (2026-11-26)

The Phase 4 "Done when" requires allocation tests at depths 1, 3 and 8 to show 0 B, and the Performance contract promises 0 B for a
synchronous `Send` at any depth. The decision "Phase 4 gate does not stop work" (below) says a failed gate does not block Phase 4. If the
gate fails on criterion 1 (0 B at depths 1, 3 and 8), the generated pipeline allocates the same way, those tests fail, and with warnings
as errors and CI Phase 4 cannot be committed. How the tests behave in that case is not decided.

- **Interim:** keep the 0 B assertions, but mark the failing ones skipped, with a skip reason that points to the Phase 4 gate-results
  entry in this file (which records the measured bytes). Do not loosen the assertions to the measured value and do not delete them. If
  the gate passes criterion 1, nothing is skipped.
- **Proposal:** re-enable the skipped tests once the allocation is fixed, and keep the Performance contract at 0 B. Alternative: record
  the measured allocation as a documented exception in the Performance contract and assert that value instead.
- **Decision:** Moot. The Phase 4 gate passed criterion 1 (OQ-36) and the generated pipelines allocate 0 B, so no test was skipped. The
  gate must be re-checked after the static-behavior rework (Phase 9); if criterion 1 then fails, this question reopens.

### OQ-23: Hint name of a nested handler class (2026-11-26)

The ADR's hint-name rule replaces `+` (and any character outside `[A-Za-z0-9_.]`) in the fully qualified metadata name with `_`, but its
example names `MyApp.Orders.Create.Keryx.g.cs`, which matches the rule only when `Orders` is a namespace. For the nested-class grouping the
ADR recommends (`public static partial class Orders { public partial class Create { ... } }`), the rule gives
`MyApp.Orders_Create.Keryx.g.cs`.

- **Interim:** follow the rule: `MyApp.Orders+Create` becomes `MyApp.Orders_Create.Keryx.g.cs`. The assembly manifest goes to the fixed
  hint name `KeryxManifest.g.cs`, which cannot collide with a handler hint name because it does not end in `.Keryx.g.cs`. OQ-28 adds
  a hash before `.Keryx.g.cs` (`MyApp.Orders_Create.<hash>.Keryx.g.cs`) so that sanitised names cannot collide.
- **Proposal:** confirm the rule and change the example to a namespace-qualified class, or add a nested example.
- **Decision:** Accepted, with OQ-28's hash: the ADR's example now shows a namespace-qualified class and a nested one. Folded into the ADR
  (Incremental generation rules).

### OQ-24: Types the generated public constructor and invoker mention (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Handler classes, KRX016 row).

### OQ-25: Where KRX021 is reported (2026-11-26)

KRX021 says a struct request is boxed when sent through `ISender`, but not where the diagnostic goes: on the struct's declaration (every
struct request, sent or not) or where it is boxed.

- **Interim:** at the call site: the request argument of an `ISender.Send`/`ISender.Stream` call (including through `IMediator`) whose
  argument is a value type converted to the interface. A struct request that is never sent through `ISender` reports nothing.
- **Proposal:** confirm. The alternative (Info on every struct request declaration) also fires in hosts that only use the typed overloads.
- **Decision:** Accepted as proposed: KRX021 is reported at the call site. Folded into the ADR (KRX021 row).

### OQ-26: Handler candidate and handler class edge cases (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Which methods are handler candidates, Handler classes, KRX004 and KRX012 rows).

### OQ-27: KRX022 as a compilation-end diagnostic (2026-11-26)

The ADR names KRX001, same-assembly KRX005 and in-source KRX008 as compilation-end diagnostics. KRX022 is "reported once per compilation"
from the parse options, which an analyzer can only do from a compilation action, and RS1037 then requires the `CompilationEnd` tag. In the
IDE it therefore appears only with full-solution analysis or on build, like KRX001.

- **Interim:** KRX022 is reported from `RegisterCompilationAction` with `Location.None` and carries `CompilationEnd`. Every other Keryx
  analyzer and both generators stay silent below C# 11, so KRX022 is the only Keryx error there.
- **Proposal:** confirm and list KRX022 with the compilation-end diagnostics in the ADR. The alternative (reporting it from a syntax-tree
  action on one chosen tree) shows it in the IDE, but on an arbitrary file.
- **Decision:** Accepted as proposed. Folded into the ADR (Diagnostics and code fixes, KRX022 row).

### OQ-28: Hint names that the sanitised metadata name merges (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Incremental generation rules).

### OQ-29: Accessibility floor of a handler class in a host (2026-11-26)

The ADR says only that "a host may keep handlers `internal` for its own mediator". A handler class, or a type containing it, that is
`private`, `protected` or `private protected` cannot be reached by the host's generated `KeryxMediator`/`AddKeryx` (Phase 3), which live
outside the class. Nothing reported it, and the handler generator still generated its partial.

- **Interim:** KRX004 is also reported in a host when the handler class or any containing type is narrower than `internal` (with the
  "make public" code fix), and the handler generator skips such a handler in a host. `protected internal` counts as `internal`.
- **Proposal:** fold into the KRX004 row: "... is not `public` outside a host, or is narrower than `internal` in a host".
- **Decision:** Accepted as proposed. Folded into the ADR (Handler classes, KRX004 row, Code fixes).

### OQ-30: Injected parameter types a field cannot hold (2026-11-26)

KRX013 lists `ref`, `in`, `out`, `params` and a second `CancellationToken`. An injected service is stored in a `private readonly` field of
the handler class, which a ref struct (`Span<T>`, `ReadOnlySpan<T>`, `TypedReference`, any `ref struct`), a pointer or a function
pointer cannot be. Without a rule the generated half fails to compile (CS8345, CS0214), which goes against reporting or skipping
(OQ-26).

- **Interim:** report KRX013 on such a parameter ("is a ref struct; an injected service is stored in a field of the handler class") and
  skip the handler. A parameter that breaks KRX013 for any reason is not counted as an injected service, so a static handler class does not
  also get KRX014 (whose "remove static" fix would not help) and the base-constructor part of KRX007 does not apply.
- **Proposal:** extend the KRX013 row: "Handler parameter is `ref`, `in`, `out` or `params`, an injected parameter is a ref struct or a
  pointer, or there is more than one `CancellationToken`."
- **Decision:** Accepted as proposed; the rule also covers behavior methods' injected parameters. Folded into the ADR (Injected parameters,
  KRX013 row).

### OQ-31: A typed overload called with a derived request (2026-11-26)

Typed overloads "skip type dispatch" (ADR, Mediator), and "a handler for a base request never receives a derived request" (ADR,
Requests and notifications). The two conflict for a request class that is not sealed: `Animal` and `Dog : Animal` are both requests, and
`mediator.Send(animal)` with an `Animal`-typed variable that holds a `Dog` binds to the `Send(Animal)` overload at compile time, which would
hand the `Dog` to `Animal`'s handler.

- **Interim:** the typed overload of a request class that is not sealed compares `request.GetType()` with its own type (a method-table
  compare, no allocation) and, when they differ, sends through the generic entry point, which dispatches on the exact runtime type (or
  throws `KeryxHandlerNotFoundException`). Sealed classes and structs skip the check.
- **Proposal:** confirm and add one sentence to the ADR's Mediator section ("A typed overload for a request class that is not sealed checks
  the runtime type first").
- **Decision:** Accepted as proposed; it covers notification types that are not sealed too (OQ-43). Folded into the ADR (Mediator).

### OQ-32: What the host generator emits when KRX031 is reported (2026-11-26)

KRX031 reports a host that targets a framework older than `net10.0`. The ADR does not say whether the host generator still emits the
mediator and `AddKeryx()`, which use `Unsafe.As`, `FrozenDictionary` and `ArgumentNullException.ThrowIfNull` and so fail to compile there.

- **Interim:** like KRX022, the host generator emits nothing in such a host and reports KRX031 on `[assembly: KeryxHost]` (KRX030 is still
  reported), so the user sees KRX031 instead of compiler errors in generated files. The handler generator is unaffected.
- **Proposal:** confirm and add "the host generator emits nothing there" to the KRX031 row.
- **Decision:** Accepted as proposed. Folded into the ADR (Opting in, KRX031 row).

### OQ-33: Host namespace sanitisation for keywords and empty segments (2026-11-26)

The ADR's fallback rule (characters not valid in an identifier become `_`, a segment that starts with a digit gets a `_` prefix) leaves
two cases that still give an invalid namespace: a segment that is a C# keyword (`Contoso.namespace`, from an assembly named that way) and
an empty segment (`My..App`). It also does not say whether `RootNamespace` itself is sanitised.

- **Interim:** the same sanitisation runs on `RootNamespace` and on the assembly-name fallback (a valid namespace is unchanged); an empty
  segment becomes `_` and a keyword segment is escaped with `@` (`Contoso.@namespace`).
- **Proposal:** confirm and add both cases to the rule in the ADR's Opting in section.
- **Decision:** Accepted as proposed. Folded into the ADR (Opting in).

### OQ-34: Handler classes a host cannot construct with `new` (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Handler classes, KRX007 row).

### OQ-35: Where host diagnostics are reported, and KRX030 severity in `.editorconfig` (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Diagnostics and code fixes).

### OQ-36: Phase 4 gate disassembly where `DisassemblyDiagnoser` is not available (2026-11-26)

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
- **Decision:** A `DOTNET_JitDisasm` listing is accepted as equivalent evidence for criterion 2, and the benchmarks CI job re-runs the
  pipeline prototype on a Linux runner with `DisassemblyDiagnoser` (Phase 9). Folded into the ADR (Phase 4 gate).

### OQ-37: Behavior edge cases (2026-11-26)

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
- **Decision:** Rewritten for static behaviors (Static behaviors, above). No longer apply: struct behaviors, explicit interface
  implementations, abstract behavior classes and behavior constructor parameters (there are no behavior instances, interfaces or
  constructors), and the `#nullable disable` note on the manifest attribute (behavior classes are not generic, so `typeof` names no
  closing). Still apply, restated for behavior methods: the constraint items (`Nullable<T>` and interface constraints, annotated reference
  types, `new()` and `required`, constraint types nested in generic types), closed methods and nullability, KRX008 per pair of behavior
  classes, `[assembly: Behavior(typeof(X))]` naming a non-behavior reporting nothing, KRX035, KRX036 for `ExcludeBehavior`, behaviors
  narrower than `internal` in a host and file-local behaviors (KRX016), and allocation tests of `async` behaviors on an optimized test host.
  Folded into the ADR (Behavior methods, Three ways to attach a behavior, Ordering, Notifications, Performance contract, KRX016, KRX019,
  KRX035 and KRX036 rows); implemented in Phase 9. The KRX009 item for a polymorphic notification handler became moot when per-handler
  notification behaviors were removed ("One notification behavior kind", above): nothing is attached to a notification handler.

### OQ-38: Terminal hook calls in a fused segment that throw, and disposing the inner stream (2026-11-26)

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
- **Decision:** Moot: hook behaviors removed (Stream hook behaviors removed, above).

### OQ-39: `ConfigureAwait` in fused hook segments (2026-11-26)

A fused hook segment awaits the hooks' `OnStartAsync`, `OnCompleteAsync` and `OnErrorAsync` and the inner enumerator's `MoveNextAsync` and
`DisposeAsync`. The ADR does not say whether these awaits capture the caller's synchronization context.

- **Interim:** every await in a generated segment uses `ConfigureAwait(false)`, as library code usually does: hooks and inner elements may
  continue on a thread-pool thread after an asynchronous hook or item. Wrap behaviors, which users write, await however they choose, and
  the consumer's own `await foreach` still resumes on its context.
- **Proposal:** confirm and state it in the ADR's Streaming section. The alternative (capturing the context) keeps thread-affine hooks on
  the caller's context but costs a context switch per asynchronous step in UI and legacy ASP.NET hosts.
- **Decision:** Moot: hook behaviors removed (Stream hook behaviors removed, above).

### OQ-40: Attached behaviors whose constraints a polymorphic handler's concrete type does not satisfy (2026-11-26)

`TNotification` of a per-handler behavior is always the concrete published type, so a behavior attached with `[Behavior]` to a
polymorphic handler (`Handle(IDomainEvent e)`) is closed once per concrete type the handler receives. The ADR says an attached behavior
that does not fit its target is KRX009, but a polymorphic handler has several targets, and a constrained behavior (`where T : IAudited`)
may fit some of them and not others. OQ-37 already leaves the constraints out of the analyzer's KRX009 for such a handler.

- **Interim:** the host skips the behavior for each concrete type it does not fit, silently, and applies it to the ones it fits. No
  diagnostic, also when it fits none of them.
- **Proposal:** keep skipping per concrete type, and have the host report KRX009 (or a new warning) only when an attached behavior fits
  none of the concrete types the handler receives, which is almost certainly a mistake.
- **Decision:** Moot. Per-handler notification behaviors were removed ("One notification behavior kind", above), so no behavior is
  attached to a polymorphic handler, and `[Behavior]` on a notification handler method is KRX042. Decided earlier the same day: the
  host skips an attached behavior for each concrete type it does not fit, and reports KRX009 only when the behavior fits none of the
  concrete types the polymorphic handler receives. The per-type skipping (the interim) was implemented; the KRX009 report never was.
  Phase 9 removes both with the per-handler behaviors.

### OQ-41: `ConfigureAwait` in generated publish code (2026-11-26)

The sequential strategy's slow path awaits each handler pipeline after the first one that did not complete synchronously, and the
parallel strategy awaits `Task.WhenAll`. The ADR does not say whether these awaits capture the caller's synchronization context, so
whether later handlers run on it.

- **Interim:** every await in generated publish code uses `ConfigureAwait(false)`, as in fused hook segments (OQ-39): after a handler
  yields, the next handlers of a sequential publish may run on a thread-pool thread. Handlers and behaviors, which users write, await
  however they choose, and the caller's own `await Publish(...)` still resumes on its context.
- **Proposal:** confirm together with OQ-39 and state it in the ADR's Notifications section. The alternative keeps thread-affine handlers
  on the caller's context at the cost of a context switch per yielding handler.
- **Decision:** Keep `ConfigureAwait(false)` in generated publish code. Folded into the ADR (Notifications). Since "One notification
  behavior kind" (above) handlers are called directly; there are no per-handler pipelines.

### OQ-42: Which notification types the host knows before cross-assembly discovery (2026-11-26)

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
  (Phase 7 does so: the public concrete notification types of every referenced assembly that references Keryx, from the third scan, and
  the concrete notification types referenced handlers take, are known too, each with a typed `Publish` overload.)
- **Decision:** Accepted as proposed, with the scanned referenced notification types Phase 7 added. Folded into the ADR (Notifications,
  Known notification types). The per-publish behavior is now `[NotificationBehavior]` ("One notification behavior kind", above).

### OQ-43: Exceptions from per-publish behaviors, and derived notifications of unknown types (2026-11-26)

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
- **Decision:** Accepted as proposed. Folded into the ADR (Notifications). The part about failures of per-handler behaviors is moot:
  per-handler notification behaviors were removed ("One notification behavior kind", above). A per-publish behavior is now a
  `[NotificationBehavior]`.

### OQ-44: File-local types in a host (2026-11-26)

A file-local (`file`) type, or any type nested in one, has declared accessibility `internal` or wider, but generated code in another file
cannot name it. OQ-42 generates a typed `Publish` overload for every notification type declared in the host's source, so a
`file sealed record Hidden : INotification;` made the generated mediator fail to compile (CS0234) with no Keryx diagnostic. Behaviors
(global, assembly or attached) that are file-local hit the same problem. Handler classes do not: KRX004 reports them (OQ-26).

- **Interim:** a file-local type anywhere in the containing chain counts as narrower than `internal`. A file-local notification type is
  unknown to the host (no overload, no dispatch, polymorphic handlers do not receive it), like a `private` nested one (OQ-42), and a
  file-local behavior is skipped by the host and reported where the host already reports a behavior narrower than `internal` (KRX016
  with "at least internal", for `[GlobalBehavior]` and attached behaviors).
- **Proposal:** confirm, and say in the ADR that "narrower than `internal`" includes file-local types.
- **Decision:** Accepted as proposed, for behavior classes and behavior methods too. Folded into the ADR (Handler classes, Behavior methods,
  Notifications, KRX016 row).

### OQ-45: `ExcludeHandler` naming a type that is not a referenced handler class (2026-11-26)

The ADR's Attribute reference says `ExcludeHandler` makes the "host ignore a referenced handler class", and KRX036 covers an exclusion that
"names a type that is not part of any pipeline". It does not say what happens when the type is one of the host's own handler classes,
which is part of a pipeline.

- **Interim:** `ExcludeHandler` removes only handler classes that come from references (their `[KeryxHandler]` manifest entries). Naming
  the host's own handler class, or any type that is not a referenced handler class the host would otherwise compose, has no effect and is
  KRX036 on the attribute. Excluding a referenced handler without providing another one leaves its request unhandled (KRX030). The
  exclusion is matched against the manifest's name of the handler class (assembly and metadata name) before anything else of the entry is
  resolved, so an excluded handler whose types the host cannot resolve (its request, attached behaviors or constructor parameters, as in a
  test host without the handler's infrastructure) reports no KRX034, and the exclusion counts as used (no KRX036).
- **Proposal:** confirm, and say "a referenced handler class" in the KRX036 row. Alternative: let `ExcludeHandler` remove the host's own
  handlers too (deleting the handler does the same).
- **Decision:** Accepted as proposed. Folded into the ADR (Host control, KRX036 row).

### OQ-46: Where the host composes its pipelines once references take part (2026-11-26)

The ADR has the scan's transform project each referenced assembly's attributes into equatable records cached per MVID, and the host's
own handlers come from its syntax pipeline. It does not say where pipelines are composed. Composing (closing behaviors, checking
constraints, polymorphic expansion) needs symbols, and the host's own handlers need the references' global behaviors and notification
types, which a syntax-provider transform cannot see (it never sees the scan's output).

- **Interim:** the transform produces only the per-assembly projection, which names types by assembly and metadata name (handler classes
  with their `[KeryxHandler]` arguments, global behaviors, request and notification types, the assembly's `[assembly: Behavior]`s, the
  manifest's format version and whether the assembly is a host). One composition step combines the settings, the host's own handler
  entries and notification type names from the syntax pipeline, and the projection with the compilation, resolves the names, and composes
  every pipeline, own and referenced, into one equatable `HostModel`. It is fed by the compilation, so it runs on every compilation, as the
  syntax transforms that composed the host's own handlers did before; the source output stays cached while the model is unchanged
  (`Unchanged`), and the incremental tests assert that. The syntax pipeline's handler step now carries only what identifies a handler
  (the request it handles, its class's metadata name and its location).
- **Proposal:** confirm and describe the composition step in the ADR's How the host reads it section.
- **Decision:** Accepted as proposed. Folded into the ADR (How the host reads it).

### OQ-47: What KRX034 covers, and what the host does with the handler (2026-11-26)

KRX034 is "a type named in a manifest cannot be resolved or is not accessible from the host". Generated code also names types the host
derives from a referenced handler (its response or item type, and its generated constructor's parameter types), which can come from an
assembly the host does not reference just as well.

- **Interim:** KRX034 is reported for every type a referenced manifest names (the handler class, `TargetType`, `Behaviors`, global
  behaviors, `[assembly: Behavior]` types, request and notification types) that does not resolve, has a type argument that does not
  resolve, or is not accessible from the host, and also for a missing response, item or constructor parameter type of a referenced
  handler, and for a missing parameter type of a public constructor of any behavior from another assembly that the host would register
  (global, `[assembly: Behavior]`, attached through `KeryxHandler.Behaviors`, and `[Behavior]` on a referenced notification type, which is
  read from the type rather than the manifest; the generated factory names those types too). Each type is reported once per manifest
  assembly, on `[assembly: KeryxHost]`, naming the assembly to reference (the one the metadata names for the missing type). The handler is
  left out of the host; when its request type resolves, the request still counts as handled, so KRX030 does not add a second error. A
  global or assembly behavior that fails is left out (and a global one is not listed by KRX035); an attached behavior that fails leaves its
  handler out too, so the handler's pipeline does not change silently.
  KRX034 also covers a referenced handler whose entry resolves but no longer describes a handler the host can compose with the types it
  resolves (its `Kind` does not fit the target, for example a request that became a stream request in the contracts assembly the host
  references, or a handler class without exactly one public constructor), through a second descriptor with the same ID and its own message
  ("Handler 'X' in the Keryx manifest of 'Lib' does not fit the types this host resolves: ...; build 'Lib' against the assemblies this host
  references"). The handler is left out, and its request still counts as handled, as above; before this, such a handler was dropped with no
  diagnostic and `Send` threw `KeryxHandlerNotFoundException` at run time.
- **Proposal:** confirm and extend the KRX034 row: "..., or a type the generated code derives from it (response type, constructor
  parameters, behavior constructor parameters), or a manifest entry that does not fit the types the host resolves (version skew)".
  Alternative for the entry that does not fit: no diagnostic of its own, and its request no longer counts as handled, so KRX030 reports it
  (whose severity is configurable, so it can be silenced, and which says nothing for a notification handler).
- **Decision:** Accepted as proposed, with the second descriptor for a manifest entry that does not fit (not the alternative). With static
  behaviors, "public constructor of a behavior" becomes "injected parameters of a behavior method". Folded into the ADR (How the host reads
  it, KRX034 row).

### OQ-48: What the host reads from an assembly that has KRX032 or KRX033 (2026-11-26)

The ADR names the two diagnostics but not what the host composes from such an assembly.

- **Interim:** nothing. An assembly whose manifest has a newer format version (KRX032, error) or that references Keryx without a manifest
  (KRX033, warning) contributes no handlers, global behaviors, assembly behaviors, request types or notification types; so its request
  types get no KRX030 and its notification types are unknown to the host.
- **Proposal:** confirm. Alternative for KRX033: still read its request types, so KRX030 reports them as unhandled.
- **Decision:** Accepted as proposed. Folded into the ADR (How the host reads it).

### OQ-49: KRX005 across assemblies (2026-11-26)

KRX005 in the host names "both handlers and assemblies"; the ADR does not give the message, the location, or what the host dispatches.

- **Interim:** a request or stream request type with handlers in more than one assembly of the composed set (the host's own included) is
  KRX005 with its own message ("Request type 'X' has more than one handler in this host: 'A' (assembly 'Lib'), 'B' (assembly 'Host')"),
  through a second descriptor with the same ID that is not a compilation-end diagnostic. It is reported on the host's own handler when one
  of them is the host's, otherwise on `[assembly: KeryxHost]`. The host dispatches none of the handlers, as for a duplicate in its own
  source, so the error is not joined by errors in generated code. Duplicates inside one referenced assembly are not reported again (that
  assembly's analyzer reported them).
- **Proposal:** confirm and give the message in the KRX005 row.
- **Decision:** Accepted as proposed. Folded into the ADR (KRX005 row).

### OQ-50: KRX009 in the host (2026-11-26)

The ADR has the host report "KRX008/KRX009 for pairs involving referenced behaviors", but not where, with what target name, or how a
polymorphic referenced handler is checked. A behavior that a referenced handler's manifest attaches (`KeryxHandler.Behaviors`) can stop
fitting when the host resolves the target or the behavior to another version than the library was built against.

- **Interim:** for each referenced handler the host composes, each attached behavior from its manifest that does not fit the target as
  the host resolves it is KRX009 (error, the analyzer's descriptor, which is not a compilation-end diagnostic), with the analyzer's reasons
  ("it is not a Keryx behavior", the wrong kind, constraints that do not hold), on `[assembly: KeryxHost]`, naming the target
  `HandlerClass.Handle` (the method is private, so its signature is not visible). As in the analyzer, a polymorphic notification handler is
  checked for the behavior kind only and the host keeps skipping the behavior per concrete type (OQ-40). A behavior class the behavior
  rules reject is the library's KRX019/KRX020 and is not reported again. The behavior is skipped and the handler kept, as for the host's own
  handlers (the error fails the build either way). Assembly and global behaviors that do not fit are skipped silently, as the ADR says.
- **Proposal:** confirm, and say in the Diagnostics section where the host reports KRX009 and how it names the target.
- **Decision:** Accepted as proposed; a behavior class the rules reject is the library's KRX019 (KRX020 is retired). Folded into the ADR
  (Diagnostics and code fixes, KRX009 row). The polymorphic notification handler part is moot (OQ-40): a notification handler's
  manifest attaches no behaviors.

### OQ-51: How the comparison benchmarks set up each library (2026-11-26)

The ADR's Benchmarks section names the libraries, versions and scenarios, but not how each library is configured, which request shape
they share, what the baseline does at a behavior depth, or what a scenario a library cannot express shows. Each choice changes the
numbers.

- **Interim** (`test/Keryx.Benchmarks/Comparison`, one project per library in `test/benchmark-subjects`):
    - Every library sends the same `sealed record` class requests and notifications, with the same handler bodies. ZeroAlloc.Mediator's
      ZAM003 (use a `readonly record struct`) is suppressed in its project, so all libraries are compared on one request shape.
    - Lifetimes are each library's defaults (MediatR, ZeroAlloc.Mediator: transient handlers; Immediate.Handlers: its generated
      registrations), except Mediator, which runs twice as the ADR says (scoped and singleton). Scoped mediators and handlers are resolved
      once, from one scope that lives for the whole benchmark, as in a request that sends many times; per-scope setup is not measured.
    - Behaviors are pass-through and complete synchronously. Each library attaches them its own way: Keryx and Immediate.Handlers per
      handler, Mediator and MediatR as constrained open behaviors selected by marker interfaces, ZeroAlloc.Mediator with one
      `[PipelineBehavior(AppliesTo = ...)]` class per request and position.
    - The baseline is the handler called directly at every depth (it never models behaviors), so the ratio column is the whole cost of
      the mediator and its behaviors. The Publish baseline calls the handlers in turn from a non-async method and falls back to an
      `async` continuation only when a handler does not complete synchronously, so it has no state machine of its own and stays the floor.
    - `SendUnitYieldValueTask` and `SendUnitYieldTask` are Keryx only: an `IRequest` whose handler returns non-generic `ValueTask`/`Task`
      and yields, against the same handler awaited directly. They are the benchmark the Performance contract names for
      `UnitAdapter.ToUnit` ("must add 0 bytes in steady state"), not a comparison.
    - "Typed" is each library's per-request entry point (Keryx and Mediator typed overloads, Immediate.Handlers' generated `Handler`,
      ZeroAlloc.Mediator's static `Mediator` and its typed `IMediator`), "generic" the `IMediator`/`ISender` generic method. A scenario a
      library has no API for is left out and shown as n/a: Immediate.Handlers has no generic entry point and no notifications,
      MediatR no typed entry point, ZeroAlloc.Mediator no generic entry point and no stream behaviors.
    - Keryx runs the 3-behavior stream twice: with `IStreamPipelineBehavior` wrap behaviors (the shape the other libraries use) and with
      `IStreamHookBehavior` hook behaviors (fused into one iterator).
    - ZeroAlloc.Mediator's generator is a separate package, `ZeroAlloc.Mediator.Generator`, pinned to the same 7.0.1.
    - Besides building and the Dry job, CI runs `Keryx.Benchmarks --verify`, which calls every comparison benchmark with a call counter
      on and fails when a library does not run the scenario's handlers and behaviors (the counter is a `static readonly` switch, a
      constant in benchmark runs).
    - The README's numbers come from local runs with `--launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 200`
      (BenchmarkDotNet's default job needed about two hours for the 130 benchmarks); the Publish and `IRequest` tables were re-run after
      the Publish baseline change.
- **Proposal:** confirm, and add the setup rules (shared request shape, default lifetimes, baseline without behaviors, n/a for
  scenarios a library cannot express, `--verify` in CI) to the ADR's Benchmarks section.
- **Decision:** Accepted as proposed, and the setup rules are in the ADR's Benchmarks section. The Keryx hook-behavior stream variant is
  dropped with hook behaviors (Phase 9). Folded into the ADR (Benchmarks).

### OQ-52: C# language floor without the generic ordering attributes (2026-11-26)

OQ-20 set the floor at C# 11 because the generic `RunsAfter<T>`/`RunsBefore<T>` attributes need it. Static behaviors removed those
attributes (a static class cannot be a type argument), so the only language need the ADR still states is the `init` properties set
through attribute named arguments (C# 9). The 2026-11-26 decisions did not say whether the floor, and KRX022, should change.

- **Interim:** keep C# 11 and KRX022 as they are. `build/Keryx.props` defaults `LangVersion` to `latest`, so a project that does not set
  it never sees KRX022.
- **Proposal:** keep C# 11, which costs nothing for projects that take the default and leaves generated code free to use C# 10 and 11
  features. Alternative: lower the floor and KRX022 to C# 9, after checking that the generated code compiles there.
- **Decision:** first decided as "keep C# 11", then changed by the owner the same day: the floor rises to **C# 14**, the default
  language of the .NET 10 SDK that Keryx already requires. KRX022 reports a language version below C# 14, and the generators emit
  nothing there. `netstandard2.0` stays a supported target for contracts and handler libraries (dropping it was considered and
  rejected); only the language version rises. Folded into the ADR (Status, Package and assemblies, Diagnostics, Alternatives
  considered, Consequences). Implemented in Phase 9.

### OQ-53: A behavior class with both notification kinds (2026-11-26)

The publish and notification handler kinds had the same behavior method shape (`next` constrained `struct, INext`, returning
`ValueTask`), so the method's kind could not follow from its signature. The 2026-11-26 decisions said that a class declares exactly one
behavior method per kind attribute it carries, but not how a class that carries both `[PublishBehavior]` and
`[NotificationHandlerBehavior]` tells its two methods apart, or what a method-level `[BehaviorOrder]`, `[RunsAfter]`/`[RunsBefore]` or
`[GlobalBehavior]` means on a method that serves both kinds.

- **Interim:** in such a class, its one `INext` method is the behavior of both kinds; two `INext` methods are KRX019 (two methods of
  one kind). A behavior that must differ per kind goes in two classes. A method-level ordering or `[GlobalBehavior]` attribute on that
  method applies to both kinds.
- **Proposal:** keep the interim. Alternative: let a behavior method carry the kind attribute itself, so `Handle` and `HandleAsync` can
  serve one kind each.
- **Decision:** Moot. Only one notification kind remains, `[NotificationBehavior]` ("One notification behavior kind", above), so every
  kind has its own method shape and a method's kind follows from its signature. Folded into the ADR (Behavior methods, Ordering,
  Alternatives considered).

### OQ-54: `[assembly: ExternallyHandled]` outside a host (2026-11-26)

`ExternallyHandled` is a host attribute, and KRX036 covers it only in a host (a handled type or a type that is not a request). The ADR
did not say what happens when a library or contracts assembly carries it; the KRX040–KRX049 placement range had no entry for it.

- **Interim:** only the host generator reads it, so outside a host it has no effect and nothing is reported (as with `ExcludeHandler` and
  `ExcludeBehavior`).
- **Proposal:** add a placement warning (KRX043) for the host-only assembly attributes (`ExternallyHandled`, `ExcludeHandler`,
  `ExcludeBehavior`) in an assembly without `[assembly: KeryxHost]`. Alternative: keep ignoring them.
- **Decision:** Accepted as proposed. KRX043 (warning, analyzer) is reported on `[assembly: ExternallyHandled]`,
  `[assembly: ExcludeHandler]` and `[assembly: ExcludeBehavior]` in an assembly without `[assembly: KeryxHost]`. Folded into the ADR
  (Host control, Diagnostics, Attribute reference, Alternatives considered); implemented in Phase 9.

### OQ-55: An attached behavior on a referenced notification type that does not fit (2026-11-26)

The host reports KRX009 for a behavior that a referenced request or stream handler's manifest attaches and that no longer fits the target
as the host resolves it (ADR, Diagnostics and code fixes). Since per-handler notification behaviors were removed ("One notification
behavior kind", below), the only per-target attachment for a notification is `[Behavior]` on the notification type, which the host reads
from the type rather than a manifest. The library's analyzer checks it against the types that library compiled against; when the
behavior class lives in a third assembly that changed since (version skew), the ADR does not say what the host does if the behavior no
longer fits the notification type as the host resolves it. KRX034 covers only types that do not resolve or are not accessible.

- **Interim:** the host skips the behavior for that notification type silently and keeps the rest of its publish pipeline (what the
  code does today: the host composes a notification type's behaviors with the same skip rule as assembly and global behaviors).
- **Proposal:** treat it like a referenced handler's attached behavior: KRX009 (the analyzer's descriptor), with the analyzer's reasons,
  on `[assembly: KeryxHost]`, naming the notification type; the behavior is skipped and the rest of the publish pipeline is kept. A
  behavior class the behavior rules reject is the library's KRX019 and is not reported again.
- **Decision:** Accepted as proposed: KRX009 in the host, on `[assembly: KeryxHost]`, naming the notification type and the behavior,
  with the analyzer's reasons; the behavior is skipped and the rest of the publish pipeline is kept. Folded into the ADR (Diagnostics
  and code fixes). Implemented in Phase 9.
