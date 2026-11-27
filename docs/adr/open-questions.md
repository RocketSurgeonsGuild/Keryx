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
OQ-54 (at the end). OQ-55 was raised afterwards, while reviewing that round, and decided the same day (at the end). OQ-56 to OQ-58 were
raised while implementing the static-behavior rework and decided by the owner the same day (at the end). The owner's decisions for the
second rework (2026-11-26) follow OQ-58 at the end, starting with "Requests are handled in their own assembly"; OQ-59 and OQ-60 were
raised while implementing it and were decided by the owner on 2026-11-27 (at the end), and OQ-61 and OQ-62 were raised while
implementing "A host is an MSBuild property" and were decided by the owner on 2026-11-27 (at the end). "Test compilations go through the
testing library" and "Jump-table dispatch has tests" (decisions 6, 8, 10 and 13) follow, and "A pipeline context resolves each shared
service once" (decision 9) follows; OQ-63 and OQ-64 were raised while implementing it and were decided by the owner on 2026-11-27 (at
the end). "Generated code is built with SyntaxFactory", "Generic dispatch is a switch expression" and "Links with primary constructors"
(decisions 5, 7 and 11) follow; OQ-65 to OQ-67 were raised while implementing them. "`next` stays a struct link
interface" (decision 4, which keeps the design as it was) follows. OQ-67, decided by the owner (decision 16), and "Requests are sealed"
(decision 14) follow; OQ-68 and OQ-69 were raised while implementing them, and OQ-68, decided by the owner (decision 18),
follows. OQ-65, decided by the owner (decision 16), follows; OQ-70 was raised while implementing it. OQ-66, decided by
the owner (decision 16), follows; OQ-71 was raised while implementing it. OQ-69, OQ-71 and OQ-70, decided by the owner on 2026-11-27
(decisions 19, 21 and 20), follow; decision 21 supersedes "Generated code is built with SyntaxFactory" and OQ-66's implementation.
"Generic `Stream` at depth 0 is tracked for the release" (decision 22, 2026-11-27) follows. OQ-61 and OQ-62, decided by the owner on
2026-11-27 (decisions 25 and 26), follow, then OQ-59 and OQ-60 (decisions 23 and 24, the same day), and OQ-63 and OQ-64 (decisions 27
and 28, the same day). "Real-world benchmark scenarios" (decision 30, 2026-11-27) and "NativeAOT benchmark run" (decisions 29 and 31,
the same day) follow; OQ-72 was raised while implementing the first and OQ-73 while implementing the second. "Full benchmark run"
(decision 32, the same day) follows; OQ-74 and OQ-75 were raised while doing it. OQ-74, decided by the owner on 2026-11-27 (decision 35),
follows; OQ-76 was raised while implementing it, and is open. OQ-72, OQ-73 and OQ-75, decided by the owner the same day (decisions 33, 34
and 36), come last.

## Open

### OQ-76: `ValidateOnBuild` no longer reports a singleton that injects `IMediator`, `ISender` or `IPublisher` (2026-11-27)

Decision 35 (OQ-74, Decided) makes the three interfaces transient factories that forward to the scoped `KeryxMediator`. With the scoped
forwarders, a singleton that injects one of them was rejected when the provider was built with `ValidateOnBuild`: "Cannot consume scoped
service 'IMediator' from singleton '…'". `ValidateOnBuild` cannot see inside a factory, so with the transient forwarders the provider
builds, and with `ValidateScopes` the singleton fails the first time it is resolved, with the less direct "Cannot resolve scoped service
'…KeryxMediator' from root provider". A probe (Microsoft.Extensions.DependencyInjection 10.0.15, `ValidateScopes` and `ValidateOnBuild`
on, a scoped class, a scoped or transient forwarding factory for its interface and a singleton that takes the interface) showed both
outcomes. ASP.NET Core turns both options on in the Development environment, as do the sample host and the benchmark subjects, so a
hosted service or other singleton that injects `IMediator` now passes the startup check, and one resolved lazily fails later. Without
validation nothing changes: the singleton captures the root provider's mediator either way. Resolving an interface from the root still
throws under `ValidateScopes`. MediatR registers its `IMediator`, `ISender` and `IPublisher` transient by default, with the same effect.
Neither OQ-74 nor decision 35 named this trade-off.

- **Interim:** the transient forwarders as decided; the caveat is in the ADR (DI registration) and the README (Hosts), and
  `HostRuntimeTests.A_Singleton_Taking_A_Mediator_Interface_Passes_Build_Validation_And_Fails_When_Resolved` pins it: with
  `ValidateScopes` and `ValidateOnBuild` the provider builds, and resolving a singleton that takes `IMediator`, `ISender` or `IPublisher`
  throws `InvalidOperationException` naming `KeryxMediator`.
- **Proposal:** accept the trade-off, as MediatR does, and keep the caveat documented. Alternative: have the host generator report a
  singleton consumer where it can see one (a `services.AddSingleton<T>()` in the host whose `T` takes one of the interfaces), as a
  warning; it cannot see registrations made elsewhere or by reflection. Needs a decision.

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
- The Phase 4 gate is re-run after the rework. (Done 2026-11-26: it passed on all three criteria; numbers and disassembly in OQ-36.)

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
    - **Amended (2026-11-26):** `KeryxContracts` and `ExternallyHandled` are removed ("Requests are handled in their own assembly", at
      the end); their designs are kept in [future-requirements.md](../future-requirements.md).

Folded into the ADR (Status, Handlers, Injected parameters, Diagnostics and code fixes, Behaviors, Ordering, Streaming, Notifications,
Generated host, Cross-assembly discovery, Attribute reference, Performance contract, Phase 4 gate, Alternatives considered,
Consequences) and the glossary. Implemented in the static-behavior rework (7d2fd01 and f54dc49).

### Stream hook behaviors removed (2026-11-26)

`IStreamHookBehavior`, fused hook segments and all hook semantics are removed. Streams have wrap behaviors (`[StreamBehavior]`) only.
This reverses the "hooks stay optional (`IStreamHookBehavior`)" part of "Stream behaviors: wrap model" above, and makes OQ-21, OQ-38
and OQ-39 moot. Allocation contract per `Stream` call: one iterator per stream behavior, plus one linked `CancellationTokenSource` only
when two distinct cancellable tokens are supplied; with no behaviors the handler's enumerable is returned as is. The comparison
benchmarks drop the Keryx hook-behavior stream variant. Folded into the ADR (Streaming, Performance contract, Benchmarks, Alternatives
considered) and the glossary. Implemented in the static-behavior rework (f54dc49).

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
KRX009 and KRX042 rows, Alternatives considered, Consequences) and the glossary. Implemented in the static-behavior rework (f54dc49).

### Message constraint on open behavior methods (2026-11-26)

Decided by the owner in the second round of decisions. An open behavior method must constrain its message type parameter to the kind's
message interface: `TRequest : IRequest<TResponse>` for `[RequestBehavior]`, `TRequest : IStreamRequest<TItem>` for `[StreamBehavior]`
and `TNotification : INotification` for `[NotificationBehavior]`. An open method without it is KRX019. A closed method names a concrete
message type, so the rule does not apply to it. Folded into the ADR (Behavior kinds, Behavior methods, KRX019 row, the examples).
Implemented in the static-behavior rework (f54dc49).

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
  (Package and assemblies, Injected parameters, Consequences); implemented in the static-behavior rework (7d2fd01).
  **Revised 2026-11-26** by OQ-56 (below): the `net11.0` dependency group is empty, because the .NET 11 shared framework carries the
  assembly.

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
  Alternatives considered); implemented in the static-behavior rework (7d2fd01).
- **Superseded (2026-11-26):** `ExternallyHandled` is removed ("Requests are handled in their own assembly", at the end); its design is
  kept in [future-requirements.md](../future-requirements.md).

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
- **Superseded (2026-11-26):** the "Mark this assembly as a contracts assembly" action is removed with `[assembly: KeryxContracts]`
  ("Requests are handled in their own assembly", at the end; design in [future-requirements.md](../future-requirements.md)); the KRX002
  fix stays.

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
  into the ADR (Host control, KRX030 row, Alternatives considered); implemented in the static-behavior rework (7d2fd01).

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
- **Amended (2026-11-26):** `KeryxContracts` and `ExternallyHandled` are removed ("Requests are handled in their own assembly", at the
  end); their designs are kept in [future-requirements.md](../future-requirements.md).

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
  gate was re-run after the static-behavior rework and passed criterion 1 again (0 B at depths 1, 3 and 8; OQ-36), so it stays moot.

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
  BenchmarkDotNet) with `DOTNET_JitDisasm` set, and the gate is judged from that listing. With the instance behaviors of Phases 4 to 8
  the gate **passed** on all three criteria (re-run 2026-11-26 after a review found the first baseline handicapped; BenchmarkDotNet
  0.15.8 default job, .NET 10.0.15 Arm64 RyuJIT, Apple M4 Max, macOS 27.2, Dynamic PGO on; `test/Keryx.Benchmarks/Prototype`,
  `--filter *PipelinePrototype*`):

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

    **Static design (re-run 2026-11-26 after the static-behavior rework, f54dc49): passed on all three criteria.** Same machine, job and
    filter as above; the machine ran nothing else during the run. The prototype now has eight `static` behavior classes (`Behavior1` to
    `Behavior8`), each with one open, message-constrained static method `Handle<TRequest, TResponse, TNext>(request, next, counter, ct)`
    (`TNext : struct, INext<TResponse>`), no behavior instance and no interface; a lazily created sealed pipeline context per depth holds
    the handler and each behavior method's injected `CallCounter`; each flat `readonly struct` link calls the next behavior method with
    explicit type arguments (`Behavior2.Handle<Ping, int, Link2>(...)`) and the last calls `PingHandler.InvokeAsync`. The baselines are
    unchanged.

    | Depth | Nested-call baseline | Struct pipeline, static behaviors | Ratio | Allocated (both) | Instance behaviors (above) |
    | ----- | -------------------- | --------------------------------- | ----- | ---------------- | -------------------------- |
    | 1     | 0.52 ns              | 0.70 ns                           | 1.35  | 0 B              | 0.67 ns (1.28)             |
    | 3     | 1.63 ns              | 1.08 ns                           | 0.66  | 0 B              | 1.09 ns (0.66)             |
    | 8     | 2.07 ns              | 2.33 ns                           | 1.13  | 0 B              | 2.09 ns (1.00)             |

    Disassembly (`DOTNET_JitDisasm`, Tier 1, "optimized using Synthesized PGO", Arm64): in `StructPipeline_Depth3` (164 bytes) and
    `StructPipeline_Depth8` (244 bytes) every static behavior method (`BehaviorN.Handle<Ping, int, LinkN>`) and every `LinkN.InvokeAsync`,
    down to `PingHandler.InvokeAsync`, is inlined into the benchmark loop (the JIT reports 16 and 31 single-block inlinees); depth 1 (132
    bytes) is the same with one behavior. The loop body is one load of the lazy context and, per behavior, a load of its injected
    `CallCounter` from the context and the increment, then the handler's body. The only call is the cold `CreateDepthN()`. No
    generic-virtual-method lookup, runtime generic dictionary lookup or interface dispatch appears: at Tier 0 the behavior methods are
    shared canonical code because `Ping` is a reference type, but the struct `TNext` argument is exact, so `next.InvokeAsync` is a direct
    constrained call that inlines at Tier 1. The baselines inline completely too (`NestedCalls_Depth3`, 116 bytes), and the load shape
    explains depth 3 as before (independent loads from the context against a dependent load per level). The listing does not explain the
    depth-8 ratio of 1.13 (0.26 ns per send): the struct pipeline touches nine objects (the context and eight counters) where the
    baseline touches eight behaviors (244 bytes against 196, neither with a call); depth 8 is not a timing criterion. Static calls inline at
    least as well as the instance behaviors did, as the ADR expected.

    Criteria: (1) 0 B at depths 1, 3 and 8, pass, so OQ-22 stays moot; (2) behavior method and `next.InvokeAsync` called directly, pass
    on the `DOTNET_JitDisasm` listing; (3) depth 3 at 0.66x the baseline (1.08 ns against 1.63 ns), pass. The Linux run with
    `DisassemblyDiagnoser` is the `prototype-disassembly` job of `.github/workflows/benchmarks.yml` (artifact
    `pipeline-prototype-disassembly`); its output is to be added here after the job's first CI run.

- **Proposal:** accept a `DOTNET_JitDisasm` listing as equivalent evidence for criterion 2, and when the gate is re-run in CI, run it on a
  Linux runner so `DisassemblyDiagnoser` output is available too.
- **Decision:** A `DOTNET_JitDisasm` listing is accepted as equivalent evidence for criterion 2, and the benchmarks CI job re-runs the
  pipeline prototype on a Linux runner with `DisassemblyDiagnoser` (Phase 8 of the brief). Folded into the ADR (Phase 4 gate).

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
  KRX035 and KRX036 rows); implemented in the static-behavior rework (f54dc49). The KRX009 item for a polymorphic notification
  handler became moot when per-handler notification behaviors were removed ("One notification behavior kind", above): nothing is
  attached to a notification handler.

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
  The static-behavior rework (f54dc49) removed both with the per-handler behaviors.

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
- **Amended (2026-11-26):** with KRX023, a host can no longer provide its own handler for a referenced request it excludes (OQ-59).
- **Amended (2026-11-27):** `ExcludeHandler` names only notification handlers; naming a request or stream request handler is KRX039 and
  excludes nothing (OQ-59, decided).

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
- **Amended (2026-11-26):** with KRX023, generated manifests can no longer make two assemblies handle one request, and the host skips
  its own handlers of referenced requests; the host check was removed on 2026-11-27 (OQ-60, decided).

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
  dropped with hook behaviors in the static-behavior rework (f54dc49). Folded into the ADR (Benchmarks).

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
  considered, Consequences). Implemented in the static-behavior rework (7d2fd01).

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
  (Host control, Diagnostics, Attribute reference, Alternatives considered); implemented in the static-behavior rework (7d2fd01).
- **Amended (2026-11-26):** `ExternallyHandled` is removed ("Requests are handled in their own assembly", at the end); KRX043 covers
  `ExcludeHandler` and `ExcludeBehavior`.

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
  and code fixes). Implemented in the static-behavior rework (f54dc49).

### OQ-56: The `net11.0` dependency on `Microsoft.Extensions.DependencyInjection.Abstractions` (2026-11-26)

OQ-8 makes the `Keryx` package depend on `Microsoft.Extensions.DependencyInjection.Abstractions` 11.0.x for `net11.0`. The .NET 11 shared
framework (`Microsoft.NETCore.App`) now carries `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, so the SDK's package pruning
drops the `PackageReference` for `net11.0`, and `dotnet pack` writes an empty `net11.0` dependency group. The ADR and the brief's Phase 1
"Done when" list expect the package in every TFM's group.

- **Interim:** `Keryx.csproj` removes the package from `PrunePackageReference` (target `_KeryxKeepDependencyInjectionAbstractions`), so
  the `net11.0` group lists `Microsoft.Extensions.DependencyInjection.Abstractions` 11.0.0 as the ADR says. A `net11.0` consumer's own
  restore prunes it again (the framework already provides the assembly), so it changes nothing at run time.
- **Proposal:** keep the interim (the package lists the dependency for every TFM, as decided), or let pruning drop it for `net11.0` and
  say in the ADR (Package and assemblies) that `net11.0` gets the assembly from the shared framework. Needs a decision.
- **Decision (2026-11-26):** drop the `net11.0` dependency. `Microsoft.NETCore.App` 11.0.0 (runtime and reference pack) ships
  `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, so `Keryx.csproj` no longer keeps the package out of pruning (the
  `_KeryxKeepDependencyInjectionAbstractions` target is gone) and the packed `net11.0` dependency group is empty; `net10.0` (10.0.x) and
  `netstandard2.0` (8.0.x) keep the package dependency. Verified with `dotnet pack` and the nuspec; a target in `Keryx.csproj` checks
  the packed nuspec after every pack. Folded into the ADR (Package and assemblies; Injected parameters; Consequences), the brief (Phase 1)
  and the README.

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
- **Decision (2026-11-26):** (1) per kind, the alternative. KRX034 is reported for the behavior method whose injected parameter type
  cannot be resolved, naming the method and its kind; the referenced class stays in the pipelines of the kinds whose methods resolve.
  Global and `[assembly: Behavior]` classes are checked for every kind they apply to, whatever targets exist; an attached behavior for
  the kind of its target. KRX035 lists a global behavior per class and kind ("Global request behavior 'Audit' from assembly …"),
  leaving out a kind KRX034 removes. (2) A message constraint through a derived interface counts, as the interim did: the constraint
  type is the kind's message interface or implements it with the method's response or item type parameter; a constraint to an unrelated
  interface is KRX019. Folded into the ADR (Behavior methods, Generic shape; KRX019; KRX034; KRX035; How the host reads it; Host
  control) and the brief (Phases 4 and 7).

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
- **Decision (2026-11-26):** accept `TResponse?` / `TItem?`, nullability-aware (the alternative). The KRX019 rejection is removed.
  Matching substitutes the annotated type where the method writes the annotation and checks the substituted constraints, `next` and the
  return type with nullability against the target: a `TResponse?` method fits a request whose response is annotated (`string?`),
  nullable-oblivious or a value type (`T?` on an unconstrained `T` substituted with `int` is `int`; on a `struct`-constrained `T` it is
  `Nullable<T>` and fits only a nullable value type response exactly), and does not fit a non-annotated reference response. A global or
  assembly behavior that does not fit is skipped for that target; an attached one is KRX009. A fit through an oblivious response is
  called between `#nullable disable warnings` and `#nullable restore warnings`, so generated code compiles without warnings for every
  fit. A closed method's `next` and return type are checked with nullability the same way (an annotated position over an oblivious
  response fits with the pragma; `ValueTask<string?>` over a `string` response does not fit). Folded into the ADR (Behavior methods, Generic shape; KRX019; Matching) and the brief (Phases 4 and 7).

### Requests are handled in their own assembly (2026-11-26)

Owner decision 1 of the second rework. `KeryxContractsAttribute` and `ExternallyHandledAttribute` are removed, with everything tied to
them: their `KnownTypeNames` and `KeryxSymbols` entries, `KeryxSymbols.IsContracts`, the KRX001 code-fix action "Mark this assembly as a
contracts assembly", the host's `ExternallyHandled` model and KRX030 filtering, the `ExternallyHandled` reasons of KRX036 (whose title is
now "An exclusion has no effect") and its mention in KRX043, and the `AnalyzerReleases.Unshipped.md` notes. Their designs (and OQ-10 and
OQ-54, which shaped them) are kept in [future-requirements.md](../future-requirements.md).

- A request's or stream request's handler lives in the request's own assembly. KRX001 applies in every assembly, hosts included.
- A handler of a request or stream request declared in another assembly is the new error **KRX023** ("Handler 'H.Handle(Ping)' handles
  'Ping', which is declared in assembly 'Messages'; a request or stream request is handled in the assembly that declares it"), reported
  by `HandlerAnalyzer` on the handler's request parameter, in libraries and hosts. Like every handler error, the generators skip the
  handler (no partial, no manifest entry, not composed by a host); in a host it still counts as handling its request, so KRX030 is not
  added on top. KRX023 is the next free ID in the per-assembly range (KRX010 and KRX020 are retired).
- Notifications are unaffected: declared anywhere, handled in any assembly.
- Host KRX030 stays as a safety net for **referenced** requests (a referenced handler the host excludes, an internal handler of a
  referenced host, a library built with KRX001 suppressed). The host's own request types are KRX001's, so the host's syntax pipeline for
  them (`HostModelFactory.CreateRequest` and the `Keryx.Host.Request*` and `Keryx.Host.DiagnosticsInputs` tracking names) is removed and
  a request without a handler in a host is reported once. Without `ExternallyHandled`, the only relief for KRX030 is its severity in
  `.editorconfig`; a test host that references an app host with internal handlers gets KRX030 for those requests (OQ-16).
- Consequences raised while implementing it: a host can no longer replace an excluded referenced request handler with its own (OQ-59,
  decided 2026-11-27: `ExcludeHandler` names only notification handlers, KRX039), and the host's cross-assembly KRX005 can no longer be
  reached from generated manifests (OQ-60, decided 2026-11-27: removed).

Folded into the ADR (status; Package and assemblies; Handlers; Diagnostics and code fixes: KRX001, KRX005, KRX023, KRX030, KRX031, KRX036,
KRX043 and the KRX001 code fix; How the host reads it; Host control; Attribute reference; Alternatives considered; Consequences;
Answered open questions), the brief (Phases 1, 2, 3, 7 and 8), CONTEXT.md (Contracts assembly) and the README. Tests: KRX023 positive
(library and host, request and stream request) and negative (own request, a notification declared in another assembly, an own request
whose request interface comes from another assembly); KRX001 in a host; the code fix in a host; KRX030 for a referenced request only;
cross-assembly tests with the request types moved into the handler library.

### Sample order requests next to their handlers (2026-11-26)

Owner decision 2 of the second rework. `PlaceOrder`, `GetOrder`, `CancelOrder` and `ListOrders` (with their `OrderSummary` response) move
from `Keryx.Samples.Contracts` to `Keryx.Samples.Orders` (`Requests.cs`, namespace `Keryx.Samples.Orders`), next to their handlers.
`Keryx.Samples.Contracts` stays `netstandard2.0` and keeps the order notifications (`IOrderEvent`, `OrderPlaced`, `OrderCancelled`,
now in `OrderEvents.cs`), without `[assembly: KeryxContracts]`. Folded into the sample project files, `samples/libraries/README.md`, the
brief (Phase 7) and the README. The AOT job (`.github/workflows/aot.yml`) publishes every host under `samples/hosts/` and names no
library, so it is unchanged; the sample host builds and runs ("every check passed").

### Pre-commit formatting is whitespace only (2026-11-26)

Owner decision 12 of the second rework. The `dotnet-format` step of `.config/hk.pkl` runs `dotnet format whitespace` (check:
`--verify-no-changes`), not `dotnet format` with its style and analyzer passes, whose code fixes had rewritten code on commit four times
(conflict markers, `NotImplementedException` switch arms, a rewritten conditional, a stray `case ...: break;` that skipped a nullability
mismatch in `BehaviorMatching.PositionsFit`). The `--exclude-diagnostics` list, which only those passes read, is gone. The build's
analyzers, with warnings as errors, enforce style. The same config serves the CI lint job (`hk check`/`hk fix`). Verified by running the
hook on a staged change. Folded into the ADR (Repository layout) and the brief (Git workflow).

### A host is an MSBuild property (2026-11-26)

Owner decision 3 of the second rework. `KeryxHostAttribute` is removed (with its `KnownTypeNames` and `KeryxSymbols` entries and
`KeryxSymbols.IsHost`). A project is a host when its `KeryxHost` MSBuild property is `true`, the pattern Dovetail uses:

- `build/Keryx.targets` sets `KeryxHost` when the project leaves it empty, after the project body (NuGet imports it from
  `nuget.g.targets`, so `OutputType` and `IsTestProject` are known): `true` for `OutputType` `Exe` or `WinExe` and for `IsTestProject`,
  else `false` (until OQ-61 and OQ-62's decisions, 2026-11-27, which added `AndroidApplication` and the `net10.0` condition; see the
  end). A value the project or its `Directory.Build.props` sets wins. `build/Keryx.props` adds it to `CompilerVisibleProperty`
  (with `RootNamespace`, `TargetFrameworkIdentifier`, `TargetFrameworkVersion`). The package now ships `buildTransitive/Keryx.props` and
  `.targets`, which import `build/`, so a project that gets Keryx transitively gets the same defaults; `buildMultiTargeting/` stays.
- Every generator and analyzer reads `build_property.KeryxHost` (`KeryxBuildProperties.IsHost`; `true`, case-insensitive and trimmed, is
  a host; unset or anything else is not): `HandlerGenerator` (a new `Keryx.HostProperty` step), `HostGenerator` (`HostBuildProperties.IsHost`;
  the scan requests now combine the compilation with the build properties, a new `Keryx.Host.ScanInputs` step), `HandlerAnalyzer`,
  `MessageTypeAnalyzer`, `BehaviorAnalyzer` and `HostOnlyAttributeAnalyzer` (KRX043 keys on the property).
- Host diagnostics that fell back to the attribute's location are reported with `Location.None`: KRX031, KRX030 (a referenced request),
  KRX032, KRX033, KRX034, KRX005 across assemblies, KRX009 for a referenced target, KRX008 without an in-source edge, KRX035 for a
  referenced global, KRX037 for a referenced notification type, and KRX036 when its attribute has no location. Consequence: their severity follows only project-wide configuration (a global AnalyzerConfig such as `.globalconfig`,
  `<NoWarn>`, `<WarningsAsErrors>`); a per-file `.editorconfig` section, which applied on the attribute's file (OQ-35), no longer does.
- KRX031's message names the property and suggests `<KeryxHost>false</KeryxHost>` (until OQ-62's decision, which changed the suggestion); KRX043's and `KeryxHandlerNotFoundException`'s
  messages name the property.
- `ReferenceScan` no longer projects whether a referenced assembly is a host (the composition never read it; a host is not visible in
  metadata any more), and `ProjectionDataVersion` is 3.
- In the repository: the sample host is an `Exe` (default); `test/benchmark-subjects/Keryx.Benchmarks.KeryxHost`, a class library, sets
  `<KeryxHost>true</KeryxHost>`. `Keryx.Tests` and `Keryx.Benchmarks` are an `Exe`/test project but reference the analyzers as an
  assembly (or not at all) and import none of the package's build files, so `KeryxHost` is never set there and neither is a host; no
  `<KeryxHost>false</KeryxHost>` is needed (their project files say why).

Folded into the ADR (status; Which projects may target what; Repository layout; Diagnostics and code fixes: KRX005, KRX009, KRX030, KRX031,
KRX034, KRX035, KRX043; Opting in; How the host reads it; Host control; Attribute reference; Alternatives considered; Consequences), the
brief (Summary, Repository layout, Phases 1, 3 and 7), CONTEXT.md (host), the README (Requirements, Quick start, testing, Hosts,
Contracts and handler libraries, KRX030, KRX031, KRX043) and `samples/hosts/README.md`. Tests: every `[assembly: KeryxHost]` in test
sources became the `build_property.KeryxHost` option (`BuildProperties.WithKeryxHost()`, `BuildProperties.Host` for direct Roslyn
drivers, `host:` parameters on the analyzer and code-fix helpers); host generator off for unset, `false`, empty and non-boolean values and
on for `true` in any case; `HostModelFactory.CreateSettings` keyed on the property; KRX043 with the property unset and `false`, and not
in a host; KRX031 and referenced-host diagnostics with `Location.None`; KRX030 severity from a global option but not a per-file one;
MSBuild evaluation tests (`BuildFiles/KeryxHostPropertyTests`) of the defaults through `build/` and `buildTransitive/`, the project's
own and `Directory.Build.props` values, and the compiler-visible property. `dotnet pack` lists `build/`, `buildMultiTargeting/` and
`buildTransitive/`, and a target in `Keryx.csproj` (`_KeryxCheckPackedBuildFiles`, after `GenerateNuspec`) fails the pack if any of
their `Keryx.props`/`Keryx.targets` is missing from the nuspec. Checked by hand: a consumer solution from the packed package (an `Exe` that references a class library
that references Keryx) generates the mediator in the `Exe` only (`KeryxHost` true there through `buildTransitive/`, false in the
library) and runs. Gaps raised: OQ-61 (apps whose `OutputType` is not `Exe`) and OQ-62 (executables and test projects below `net10.0` are hosts by
default and fail with KRX031).

### Test compilations go through the testing library (2026-11-26)

Owner decisions 6, 10 and 13 of the second rework. Every generator, analyzer and code-fix test compiles through
`Rocket.Surgery.Extensions.Testing.SourceGenerators`, the way Dovetail and Specular use it; the hand-built `CSharpCompilation` and
`CSharpGeneratorDriver` paths are gone. `Directory.Packages.props` pins the package at 11.0.2 (RocketSurgeonsGuild/Testing#2430), which
adds what 11.0.1 lacked, so no interim harness is kept: `DirectCompilation.cs` and `Hosts/TestLibraries.cs` are deleted, and so are
`HostTest.LibraryLoadContext`, `LoadHost`, `CompileLibrary` and `BuildProperties.Host`/`Provider`.
`Rocket.Surgery.Extensions.Testing.TUnit` stays at 11.0.1: its 11.0.2 needs `Microsoft.Extensions.DependencyInjection.Abstractions`
11.0.0, above the 10.0.15 that `Keryx` pins (NU1605).

- One shared builder (`GeneratorBuilderConstants`: `References`, and `Builder` with the handler generator) cloned per test into a load
  context of the test's own (`GeneratorTest`).
- Referenced libraries are built with the library: `GeneratorTest.CompileLibraryAsync(name, source, ...)` (kinds `Handlers`, `Plain`,
  `Host`; nullable enabled; a distinct project name per library; errors fail with the library's diagnostics), passed on with
  `AddCompilationReferences`. Every library is loaded into the test's load context, where the host resolves it by name; a second build
  of the same assembly name (another version of it) takes `ownLoadContext: true`. An IDE project reference is
  `FinalCompilation.ToMetadataReference()`.
- Compilation options through the builder: `WithNullableContext` (the nullable-oblivious handler test, libraries, incremental tests),
  `WithAllowUnsafe` (KRX013 pointer parameters), `WithOptimizationLevel(Release)` (`CompileReleaseHostAsync`, which now generates and
  analyzes under Release too), `WithDiagnosticOption` (KRX030 severity), and `dotnet_diagnostic.KRX030.severity` as a global or per-file
  option (KRX030 without a location takes only the global one).
- No custom "compiles" helper: 11.0.2's `AssertCompilationWasSuccessful` lists the failing diagnostics (and fails when emit fails);
  libraries assert at `DiagnosticSeverity.Error`.
- `build_property.*` options through named builder helpers in `BuildProperties`: `WithKeryxHost`, `WithRootNamespace`,
  `WithTargetFramework`, `WithIntermediateOutputPath` (the scan-cache tests, which feed the cache file back with `AddAdditionalText`).
- Incremental tests (`HandlerGeneratorIncrementalTests`, `Hosts/HostGeneratorIncrementalTests`) build with `WithIncrementalTracking()`
  and rerun with `RerunAsync` (an added source, a replaced tree, a replaced reference), through the shared `IncrementalRuns` helpers; the
  host ones now set `RootNamespace` like every other host test.
- `ManifestContractTests` compile the netstandard2.0 library through the library too (`CreateWithoutDefaultReferences`, the emit result
  from `EmitResult`); `BehaviorMatchingTests` share one compilation built through it; only `KnownTypeNamesTests` builds a Roslyn
  compilation by hand.
- Code-fix tests take each action's changes from the library (`CodeActionTestResult.Changes`) and re-verify every fixed project through
  the library with the handler generator and the analyzer; the generator diagnostics are no longer discarded (no snapshot changed).
- `UseParameters` on every snapshotted argument row (already the case); the snapshots' temp directory is scrubbed to `{TempPath}`. Shared
  references (`TestReferences`), the shared usings and the generated-source reader (`TestSources`) live outside the tests.
- One snapshot shape per kind of output (the review's convention 10). Host-generator output is always the single object
  `{ Mediator, AddKeryx }` in one `.verified.txt`, through `HostTest.VerifyHost(result)`; generator diagnostics are asserted by the test,
  not snapshotted. Handler-generator output, and analyzer and code-fix results, stay `Verify(result)`. Before, host output was snapshotted
  four ways (the mediator string, `{ Mediator }`, `{ Mediator, AddKeryx }` and `Verify(result)`); the shapes change now so that the
  SyntaxFactory step's snapshot diff stays formatting only (decision 6).

Deliberate snapshot changes, with identical generated code in every one: the nullable-oblivious handler test snapshots `Verify(result)`
(its `.verified.txt` plus one `#hint.verified.cs` per generated file) instead of one string. The host snapshots take the one host shape:
the stream and publish pipeline depth rows, `HostGeneratorTests.Should_Dispatch_Through_A_FrozenDictionary_Above_The_Threshold` and
`AnnotatedResponseTests.Should_Generate_Pipelines_For_Annotated_Response_Type_Parameters` add `AddKeryx` next to the mediator they held;
`HostGeneratorTests.Should_Generate_The_Mediator_And_AddKeryx` and `Should_Generate_An_Empty_Mediator_For_A_Host_Without_Handlers` move
from `Verify(result)` to the host shape. The handler partials and manifest the first of these snapshotted move unchanged to a new test,
`HostGeneratorTests.Should_Generate_The_Handlers_Of_A_Host` (handler generator only, `Verify(result)`); the empty host's manifest is the
one `HandlerGeneratorTests` already snapshots. No test was removed. Found while
migrating, in the testing library: a project name with a dot emits an assembly named after its first segment (emit passes the project
name as the output file name), so a library meant to be read back by assembly name uses a name without a dot. Folded into the ADR
(Repository layout) and the brief (Engineering rules).

### Jump-table dispatch has tests (2026-11-26)

Owner decision 8 of the second rework, ahead of the switch-expression change it guards (decision 7). `Hosts/JumpTableDispatchTests`
(non-sealed record messages, each with its own handler):

- generic `Stream` above the threshold: every `S{n}` streams from its own handler through `IMediator.Stream`, and an unknown type throws
  `KeryxHandlerNotFoundException` when `Stream` is called;
- for requests, stream requests and notifications above the threshold: a type derived from a handled one, compiled into another assembly
  against the host (so the host does not know it), is not dispatched to the base type's handler (`KeryxHandlerNotFoundException` for a
  request or stream request, no handler run for a notification), while the base type is; and the not-found path of each kind;
- the boundary for streams and notifications (the request kind's already existed): at `DispatchThreshold` the generic method compares
  types and has no table, one above it a lookup governs the dispatch and the table exists;
- the IL of the generic `Send`, `Stream` and `Publish<TNotification>`, read from the emitted image with System.Reflection.Metadata, has one
  `switch` opcode above the threshold and none at or below it, for each kind.

The text asserts that would break on the switch-expression change are structural now: `GenericDispatch.Of(mediator, method)` reads, from
the syntax of the generic method, the index method a `switch` statement or expression is governed by, the number of `requestType ==
typeof(X)`/`notificationType == typeof(X)` comparisons (in an `if` or a `when` guard alike) and the mediator's `FrozenDictionary` tables;
`HostGeneratorTests` (both request boundary tests), `StreamGeneratorTests` and `NotificationGeneratorTests` use it. All pass on today's
generated code. Folded into the ADR (Generic entry points), the brief (Phases 3, 5 and 6) and the README (Hosts, Notifications). Owner
decision 15 later added Verify snapshots of the generated dispatch per kind at and one above the threshold (2e8e4e1), and decision 14
sealed the request and stream request messages: the derived-type test keeps its notification row, and a request or stream request type
that is not sealed is tested as not dispatched at all ("Requests are sealed" and OQ-68, at the end).

### A pipeline context resolves each shared service once (2026-11-26)

Owner decision 9 of the second rework. A pipeline context (request, stream and publish) has one field per distinct service its behavior
methods inject, not one per behavior parameter (before: `Behavior{k}Service{j}` per behavior, so a depth-8 pipeline whose odd steps
inject `IClock` resolved and held it four times). The model does it, not the emitter: `PipelineBehaviors.Compose` calls
`PipelineBehaviors.ShareServices`, which walks the pipeline in sorted order (outermost first, each method's injected parameters in declared
order) and returns the distinct services as `ContextServiceModel`s (equatable: service type, key, value-type shape, `IServiceProvider`,
required) and, on each `PipelineBehaviorModel`, `ServiceSlots`: the index of the service each injected parameter receives. The pipeline
models carry the list (`HostHandlerModel.ContextServices`, `HostNotificationModel.ContextServices`); the emitter writes one field per entry
and reads each argument by slot.

- Identity is (service type without a top-level nullable annotation, `[FromKeyedServices]` key).
- A service with any required consumer is resolved with `GetRequiredService`/`GetRequiredKeyedService`, and every consumer receives
  that instance as is. A service whose consumers are all optional is resolved once with `GetService`/`GetKeyedService` into a nullable
  field (`T?`; a value type through the type pattern, into `Nullable<T>`), and each call site applies its own default
  (`ctx.ServiceN ?? <default>`, `GetValueOrDefault()` for a value type's `default`, nothing for a `null` default). A single optional
  consumer therefore applies its default per call (`?? 3`) instead of once at resolution; no allocation either way.
- Fields are named `Service0`, `Service1`, ... in order of first use. Names stay stable across edits that change neither the set of
  services nor the order of their first uses (adding a behavior that injects only `IClock` to a pipeline that already shares it changes no
  field). An edit that keeps the set but changes that order renumbers the fields, which the decision's "stable across edits that do not
  change the set" does not allow; that conflict is OQ-64 (decided at the end: numbered by a stable sort of type and key since).
- The unkeyed `IServiceProvider` is the context's own provider, never resolved; where it is held is OQ-63 (decided at the end: a
  dedicated `ServiceProvider` field since).
- Consequence: a transient service that several behavior methods of one pipeline inject is one instance per pipeline context (per scope
  and pipeline), not one per behavior method.

Tests (`Behaviors/SharedServiceTests`): a snapshot of a host whose request, stream and publish pipelines each have four behaviors
injecting `IClock` (one field per context, twelve calls reading it); keyed and unkeyed `IClock` (and two keys) stay separate; a required
consumer between optional ones makes `IClock`, `int` and `int?` required for all, and an `IServiceProvider` (optional or not) is one
field assigned the scope's provider; all-optional consumers with different defaults (keyed
`string`, `int`, `int?`, `IClock?`, `TimeSpan`), in generated code and at run time with and without registrations; at run time every
behavior of a request (required and optional consumers, keyed and unkeyed), stream and publish pipeline receives the same transient
instance, and the required and optional `IServiceProvider` parameters both receive the scope's own provider; at run time a keyed optional
`int` falls back to each consumer's own default when missing and reaches both when registered; an incremental rerun that adds an outermost
global behavior injecting `IClock` leaves the pipeline's `ContextServices` (model) and its fields unchanged; and a rerun that adds an
outermost behavior injecting (`IClock`, `IStore`) to a pipeline sharing `IClock`, `ILog`, `IStore` swaps `Service1` and `Service2`
(OQ-64). The depth, stream, publish and static-handler snapshots changed only in field names and counts and the
optional `retries` default moving to the call site; `CrossAssemblyTests` asserts `Service0`. Folded into the ADR (Injected parameters,
Behavior methods, Generated pipeline shape, Mediator), the brief (Phases 4, 5 and 6), CONTEXT.md (pipeline context) and the README
(Behaviors, Hosts).

### Generated code is built with SyntaxFactory (2026-11-26)

**Superseded** (2026-11-27) by owner decision 21 (OQ-71, at the end): the generators write text through `CodeWriter` again and
`GeneratedSyntax` is removed. The history follows.

Owner decision 5 of the second rework. `HandlerEmitter` and `HostEmitter` (with `HostEmitter.Streams` and `HostEmitter.Notifications`) build
their sources with Roslyn's `SyntaxFactory`, following Dovetail (`Joinery.Dovetail.Analyzers/Helpers.cs`, `Support/ExportDovetails.cs`,
`Support/ImportDovetails.cs`); `CodeWriter` is gone. The shared helpers are `src/Keryx.Analyzers/GeneratedSyntax.cs`: types and names from
the models' `global::` strings (`TypeName`, `Qualified`; the models' default values and service keys, which are C# expressions, through
`Code`), small builders (members, invocations, `ref`/`out var` arguments, target-typed `new`, switch-expression arms, removed with OQ-66), the
`[GeneratedCode("Keryx", version)]`, `[MethodImpl(NoInlining)]` and pooling builder attributes, documentation lines (comment trivia, so
normalization leaves the XML as written), the nullable directives, and `Render`. Syntax is built only inside `RegisterSourceOutput` (the
emitters are called from the source outputs; the models still carry strings). Until OQ-66 was decided (at the end: the syntax is now
built with its whitespace and nothing normalizes it, writing the same bytes for every snapshot), `Render` normalized each compilation unit once
(`NormalizeWhitespace`, four spaces, `\n`), ran a layout pass where normalization alone is hard to read or wrong (blank lines between
members except plain fields; a break after `=>` with the body indented; the closing brace of a switch expression on its own line, which
normalization writes as `throw x};`; the space before `is` after `)`, which it drops; a constructor's parameters one per line; a comment in
an empty block indented as content; no space after the `?` of a nullable type unless a name or keyword follows, so `typeof(int?)`,
`string?[]` and `int?[]?`), and added the header trivia (`// <auto-generated/>`, `#nullable enable`, the obsolete-type `#pragma`, which in
the mediator also disabled CS9113 for the link parameters some links did not read until OQ-65 was decided) last. No type declaration carries a `;`, and
nullable types render `T?`. The `[GeneratedCode]` attributes and the `#nullable disable warnings` islands of oblivious fits (OQ-58) are
unchanged. `GeneratedSyntaxTests` pins each layout rule.

Every handler-generator snapshot changed in whitespace only (token-for-token equal), and so did the host snapshots apart from the shapes
of decisions 7 and 11 (below). Runtime, allocation, IL-switch and jump-table tests pass unchanged. Generator time on a large host is
OQ-66 (decided at the end): 66 to 384 ms for the host generator, 35 to 102 ms for the handler generator; 109 ms and 48 ms once the
whitespace was built in (warm compiler server). Folded into the ADR (Incremental generation rules, Alternatives considered), the brief
(Engineering rules).

### Generic dispatch is a switch expression (2026-11-26)

Owner decision 7 of the second rework: generic `Send`, `Stream` and `Publish<TNotification>` dispatch with switch expressions,
`return requestType switch { _ when requestType == typeof(X) => ..., _ => throw __HandlerNotFound(requestType) };` (`Publish` ends with
`_ => default`), exact-type `when` guards and never type patterns, and above `DispatchThreshold`
`return __RequestIndex(requestType) switch { 0 => ..., _ => ... };`; the change must not regress. Implemented and measured: the switch
expressions keep the method-table compares and the jump table but lose the arms' tail calls, and they measured slower (OQ-67, open). So
the generated code uses the decision's sections in a switch statement with one `return` each until OQ-67 is decided:
`switch (requestType) { case var _ when requestType == typeof(X): return __Dispatch0(request, cancellationToken); ... }`, above the
threshold `switch (__RequestIndex(requestType)) { case 0: return ...; }`, then `throw __HandlerNotFound(requestType);` (`Publish`:
`return default;`). The value-type branch of `Publish` is
`switch (typeof(TNotification)) { case var _ when typeof(TNotification) == typeof(X): return ...; }` then `return default;` (a plain
`return default;` when no notification type is a struct). With no type to dispatch, the entry point throws or returns directly. The typed
overloads keep their single `if` (a two-way check, not a chain). `Hosts/GenericDispatch` reads both shapes, so the boundary tests are
unchanged, and the IL tests still find one `switch` opcode above the threshold and none at or below it. `DispatchBenchmarks` is written in
the switch-statement shape. The before and after numbers of the comparison benchmarks (linear and above the threshold; the new
`KeryxDispatchPadding` switch of `Keryx.Benchmarks.KeryxHost` compiles 32 more request, stream request and notification types) are in
OQ-67. Folded into the ADR (Generic entry points, Alternatives considered), the brief (Phase 3) and the README (Hosts, Benchmarks). Owner decision 16 later decided OQ-67 for the switch statements (OQ-67, at the end), and decision 14 replaced the
exact-type guards of `Send` and `Stream` with type patterns ("Requests are sealed", at the end).

### Links with primary constructors (2026-11-26)

Owner decision 11 of the second rework. Each link is one non-generic `readonly struct` per position, written
`LinkK(__PipelineN pipeline, TRequest request) : INext<TResponse>` (`notification` in a publish pipeline, `StreamLinkK` for streams), with
an expression-bodied `InvokeAsync`, a target-typed `new(pipeline, request)` at every behavior call and at the entry call (`TNext` stays an
explicit type argument), a one-line `<summary>` naming where `next` goes, and `global::` names without `using` aliases. Every pipeline
context that existed before stays (no pipeline with behaviors is without one, so "pipelines without a context drop the `pipeline`
parameter" applies to none), and every link takes both parameters. A parameter its `InvokeAsync` does not read (the context in the last
link of a pipeline whose handler's invoker is static; both in the last link of a notification type without handlers) is not captured, so
every link holds the fields it held before and the machine code is unchanged; the mediator's header suppresses the CS9113 such a parameter
raises (`#pragma warning disable CS0612, CS0618, CS9113`). Trimming link parameters and dropping contexts that hold nothing is OQ-65
(open). `NotificationGeneratorTests` and `NotificationBehaviorTests` cover a notification type with a behavior that injects nothing and no
handlers (snapshot, typed and generic publish). The Phase 4 gate prototype (`test/Keryx.Benchmarks/Prototype/PrototypePipeline.cs`) is
written the same way: `--prototype-disasm` gives Tier 1 code for `SendDepth1`, `SendDepth3` and `SendDepth8` byte for byte the same as
before (112, 144 and 224 bytes; 6, 12 and 27 inlinees; the only call the context's slow-path creation), and
`--filter '*PipelinePrototype*' --job short` allocates nothing at any depth, with depth 3 at 0.67x the nested calls. The allocation tests
pass unchanged. Rejected shapes (stage-generic links, a runtime-provided generic link, composed chain types, a single index link) are in
the ADR's Alternatives considered. Folded into the ADR (Generated pipeline shape, Streaming, Behavior methods, Injected parameters,
Alternatives considered), the brief (Phase 4) and CONTEXT.md (link). Owner decision 16 later decided OQ-65 (at the end): pipelines that
resolve nothing have no context, and each link takes only what it reads or passes on, so the CS9113 suppression is gone.

### `next` stays a struct link interface (2026-11-26)

Owner decision 4 of the second rework. `INext<TResponse>`, `INext` and `IStreamNext<TItem>` stay as they are: a behavior method takes
`next` as a method type parameter constrained `struct` and the kind's interface, and the generated links are structs that implement it.
Each closing of a behavior method over a link is its own specialization, so `next.InvokeAsync` is a direct call the JIT can inline, and
nothing is allocated for `next` (the Phase 4 gate, OQ-36, re-run with the primary-constructor links: 0 B at depths 1, 3 and 8, every link
and behavior method inlined). Typed next delegates (a `delegate ValueTask<TResponse> RequestNext<TResponse>(CancellationToken)` passed to
the behavior method instead of `TNext`, and their stream and notification counterparts) were considered and rejected: a delegate call is
an indirect call that the JIT does not inline, one per pipeline step, and each delegate bound to a pipeline context is a heap object
allocated per scope (per pipeline and position, or per call when it captures the request). Nothing in the code changes. Folded into the
ADR (Behavior kinds, Alternatives considered), the brief (Phase 1) and the README (Behaviors).

### OQ-67: Switch-expression dispatch loses the arms' tail calls (2026-11-26)

Owner decision 7 ("Generic dispatch is a switch expression", at the end) asks for switch expressions in generic `Send`, `Stream` and
`Publish<TNotification>` and says the change must not regress. C# lowers a switch expression to one result local that every arm stores
and a single return, and the JIT does not turn that back into tail calls. Measured on the comparison host (`Keryx.Benchmarks.KeryxHost`)
with every pipeline context kept (OQ-65's interim), so the dispatch shape is the only difference: BenchmarkDotNet 0.15.8 default job,
net10.0, Apple M4 Max, macOS 27.2, 2026-11-26; "before" is the text emitters' `if` chains, each row from two interleaved runs of the three
builds (before, switch expressions, switch statements).

- **JIT code** (`DOTNET_TieredCompilation=0`, `DOTNET_JitDisasm`, `Keryx.Benchmarks --verify`): with switch expressions the method-table
  compares (ten `cmp` in the linear `Send<int>`) and the jump table survive, but the linear `Send<int>` goes from 10 tail calls (`br`) to
  none, the above-threshold one from 43 to 1, and `Publish<__Canon>` from 2 to 0 (35 to 1 above the threshold); each zeroes a `ValueTask`
  slot in its prolog (the effect OQ-15 measured with one local per arm). With switch statements of the same sections (one `return` each)
  the code of all four generic entry points, linear and above the threshold, is instruction for instruction the code of the `if` chains.
- **Linear** (10 request types): `Publish` generic 2.80/2.81 ns (1 handler) and 3.07/3.04 ns (5 handlers) before, 3.16/3.10 and
  3.34/3.32 ns with switch expressions (+0.3 ns), 2.89/2.79 and 3.09/3.15 ns with switch statements. `Send` sync generic: before
  2.99 to 3.48 ns, switch expressions 3.14 to 4.21 ns (one run of depths 0 and 1 +0.6 to 1.0 ns, the other within noise), switch
  statements 2.98 to 3.54 ns. `DispatchBenchmarks` (hand-written, one full run each): the switch-expression chain 4 to 9% slower than the
  `if` chain at the middle request from 16 types up (32 types: 2.94 to 3.19 ns), the switch statement 2 to 3% (3.02 ns).
- **Above the threshold** (42 request types, `KeryxDispatchPadding=true`): `Send` sync generic before 6.25 to 6.70 ns, switch expressions
  6.38 to 8.23 ns, switch statements 6.36 to 6.83 ns; `Publish` generic noisy in every build (6.10 to 7.02 ns).
- `Stream` changed by neither shape beyond noise (it was never a tail call: the arm's result is cast), and allocates the same (120 B at
  depth 0, 680 B at depth 3).
- The yielding `Send` rows (`SendYieldDepth*`, `SendUnitYield*`, about 1 µs) are bimodal from run to run whatever the build: in the
  interleaved runs above the threshold, before measured 881 ns to 1.39 µs, switch expressions 905 ns to 1.42 µs and switch statements 900 ns
  to 1.56 µs, and within one run two depths of the same build differ by 400 ns. Their spread is about 0.4 µs; they show no regression of the
  dispatch either way.

- **Interim:** switch statements, `switch (requestType) { case var _ when requestType == typeof(X): return __Dispatch0(...); ... }` and
  `switch (__RequestIndex(requestType)) { case 0: return ...; }`, then `throw __HandlerNotFound(requestType);` (`return default;` in
  `Publish`; the value-type branch `switch (typeof(TNotification)) { case var _ when typeof(TNotification) == typeof(X): return ...; }`).
  They keep the exact-type guards, the compares, the IL `switch` opcode above the threshold and the arms' tail calls, and do not regress.
- **Proposal:** keep the switch statements. Alternative: the switch expressions as decided, at up to about 0.3 ns per generic `Publish`
  and an occasional 0.3 to 1.8 ns per generic `Send` with a synchronous handler, and nothing through the typed overloads. Needs a decision.
- **Decision:** switch statements (owner decision 16, 2026-11-26), superseding decision 7's switch expressions. With "Requests are
  sealed" (below) the linear sections of generic `Send` and `Stream` are type patterns, `switch (request) { case global::Sample.Ping r:
  return __Dispatch0<TResponse>(r, cancellationToken); ... default: throw __HandlerNotFound(request.GetType()); }`; above the threshold
  `var requestType = request.GetType(); switch (__RequestIndex(requestType)) { case 0: return ...; ... default: throw
  __HandlerNotFound(requestType); }`; `Publish<TNotification>` keeps exact-type guards and ends `default: return default;` (both its
  branches). The tail calls are kept (JIT code below, under "Requests are sealed"). Folded into the ADR (status, Generic entry points,
  Alternatives considered), the brief (Phase 3) and the README (Hosts).

### Requests are sealed (2026-11-26)

Owner decision 14 of the second rework. Request and stream request types (types implementing `IRequest`, `IRequest<T>` or
`IStreamRequest<T>`) are sealed or part of a closed hierarchy; notifications and interfaces deriving the message interfaces are not
affected.

- A request or stream request class or record that is neither `sealed` nor abstract is the new error **KRX024** ("Request type 'Get' is
  not sealed; make it sealed, or abstract (or closed) if it is the base of other request types"; "Stream request type" for a stream
  request), reported by `MessageTypeAnalyzer` on the type, in libraries and hosts. KRX024 is the next free ID in the per-assembly range
  (KRX010, the draft's "request is not sealed", stays retired; KRX020 too). Structs are always sealed. The code fix _Make sealed_
  (`SealRequestCodeFixProvider`, equivalence key `Keryx.MakeSealed`) adds `sealed` to the declaration the diagnostic is on, after its
  accessibility and `new` modifiers, keeping `partial` and the rest (one part of a partial type is enough). It is not offered when the
  type declares a `virtual` or `protected` (`protected internal`, `private protected`) member that does not override one, including a
  record's own `PrintMembers` or copy constructor: sealing would not compile (CS0549, CS8879) or would warn (CS0628); a second code-fix
  snapshot covers those shapes and an `override` that still gets the fix.
- Abstract bases (`abstract record Command : IRequest<Unit>`, `abstract record Query<T> : IRequest<T>`) and C# 15 closed types are
  allowed as request bases. The compiler makes a closed type abstract, in source and in metadata, so every rule treats it as an abstract
  base. The analyzers build against Roslyn 5.0, so `MessageType.IsClosed` reads the `closed` modifier's token text on the declaring
  syntax and, for a metadata type, looks for `IsClosedTypeAttribute`; Roslyn 5.12 does not return that attribute from `GetAttributes`,
  so a referenced closed base is named as abstract (OQ-69, decided at the end: accepted).
- A handler of an abstract or closed request base reuses **KRX003** ("Handler 'H.Handle(Shape)' must take a sealed request type, not the
  closed type 'Shape'; each sealed request type has its own handler"; "the abstract type", "the interface" for the other shapes); each
  sealed case has its own handler (KRX001).
- Dispatch: generic `Send` and `Stream` use type-pattern switch statements (OQ-67 above); each arm `__Dispatch{n}<TResponse>` takes the
  request as its exact type (called with an explicit `<TResponse>`, which a parameter of the concrete type cannot infer), and above the
  threshold the section reinterprets it with `Unsafe.As` (or unboxes a struct) at the call. Typed overloads of sealed requests have no
  runtime type check. `Publish<TNotification>` keeps exact-type guards, because notification types are not sealed: a type pattern would
  send a derived notification the host does not know to its base type's pipeline. A request type that is not sealed reaches a host only
  with KRX024 suppressed; it is never composed, and a referenced one is the host error KRX038 (OQ-68, at the end). Review follow-up: a
  struct request's section has no pattern variable (`case global::S:`), and its arm takes `IRequest<TResponse>` and unboxes inside, as
  before this decision; with the pattern variable the struct was unboxed into a local of `Send`, which a 32-byte struct with references
  made the JIT zero (64 bytes) in the prolog of every generic `Send` and copy for its arm, which then was no tail call (a probe host with
  `readonly record struct Big(string A, string B, string C, long D)` and five class requests: 4 of 5 tail calls; with the fix 6 of 6, a
  0x20 frame and no zeroing, `DOTNET_TieredCompilation=0`, net10.0 arm64). A struct stream request's section likewise has no designation
  and is unboxed as the arm's argument. The comparison host has no struct request, and its generic `Send`, `Stream` and `Publish` code is
  instruction for instruction unchanged (10 / 43 / 2 / 35 tail calls).
- Tests: KRX024 positive (class, record, `IRequest`, stream request, a non-sealed case of an abstract base, a partial type, a nested type,
  in a host) and negative (sealed, structs, abstract bases with sealed cases, a sealed partial type, interfaces, notifications);
  closed bases with C# preview (sealed cases, a closed base of a closed base, a case that is not sealed); KRX003 for an abstract, generic
  abstract, closed and interface request base, for an abstract and a closed stream request base, and for a closed base from a referenced
  assembly; the code-fix snapshots; host runtime tests
  for the sealed cases of an abstract base (typed and generic, requests and streams) and of a closed base declared in the host or a
  referenced library; the KRX024-suppressed tests of OQ-68; `GenericDispatch` counts type-pattern sections as type tests. On .NET 10 a
  compilation with a closed type needs `IsClosedTypeAttribute` defined (CS0656); the tests add it there (`TestSources.ClosedTypeAttribute`).
  Updated fixtures that used requests that were not sealed: the host snapshot's `Animal`, `StreamGeneratorTests`' `Shapes` (now an
  abstract base with a sealed `Circles`), `HostRuntimeTests`' `Animal`/`Dog` (an abstract base with `Dog` and `Cat`),
  `StreamRuntimeTests`' `Shape`/`Square` (with `Circle`), `HostAllocationTests`' `Base` (with `Case`), the empty host's `Split`, and
  `JumpTableDispatchTests` (request and stream messages sealed; the derived-type test is for notifications only, OQ-68).
- Snapshots: every host snapshot with requests or streams changed only in the generic `Send`/`Stream` dispatch, the arms' signatures and
  calls, the dropped typed-overload checks of the two fixtures above, the `default:` sections, and `Publish`'s `return default;` moving
  into `default:`; `MessageTypeAnalyzerTests.Reports_Every_Rule_With_Its_Message` gained the two KRX024 messages; the code-fix snapshot is
  new. The jump-table snapshots of decision 15 (committed in 2e8e4e1 on the earlier output) show the change as a diff.
- Measured (comparison host `Keryx.Benchmarks.KeryxHost`, BenchmarkDotNet 0.15.8 default job, net10.0, Apple M4 Max, macOS 27.2,
  2026-11-26; the machine carried other load during the full runs, so their rows are noisy and two interleaved reruns decide):
  JIT code (`DOTNET_TieredCompilation=0`, `DOTNET_JitDisasm`, `--verify`): tail calls 10 / 43 / 2 / 35 for linear `Send<int>`,
  above-threshold `Send<int>`, linear `Publish<__Canon>` and above-threshold `Publish<__Canon>`, the same as before; the linear
  `Send<int>` loads the request's method table once and compares it with each sealed type's (no cast helper call), 512 bytes instead
  of 784; the above-threshold `Send` and both `Publish` are instruction for instruction the same as before. Interleaved reruns (before,
  sealed, before, sealed): linear `Send` sync generic 2.96 to 3.54 ns before, 2.53 to 2.85 ns sealed (0.4 to 0.7 ns faster at every
  depth); linear `Publish` generic 2.85 to 3.27 and 2.98 to 3.08 ns before, 2.75 to 2.77 and 2.98 to 3.06 ns sealed; above the threshold
  `Send` 6.20 to 7.08 ns before and 6.25 to 6.98 ns sealed, `Publish` 6.08 to 6.72 and 6.23 to 6.72 ns, within noise (the code is the
  same). One row is slower beyond noise: `StreamDepth0` through generic `Stream` (100 items, no stream behaviors) measured 691, 717, 713,
  693 and 668 ns in five runs against 656, 657, 659, 661 and 649 ns before (+20 to +60 ns, 3 to 9%), while its typed row and
  `StreamDepth3` (generic and typed) did not move. The generic `Stream<int>` code got shorter (352 bytes instead of 356: the type pattern
  reads the method table instead of calling `GetType()`), the benchmark method's Tier 1 code is the same, and a `--job short` run of
  both builds measured 662 and 656 ns, so the cause is not the dispatch instructions; code placement in the rebuilt host assembly is the
  likely suspect, not verified. Allocation is unchanged (0 B for every synchronous row, 120 B and 680 B for the streams).

Folded into the ADR (status; Requests and notifications; Handler rules; Diagnostics and code fixes: KRX003, KRX010, KRX024 and the KRX024
code fix; Mediator; Generic entry points; Alternatives considered; Consequences), the brief (Phases 2, 3 and 6), CONTEXT.md (request,
stream request, request base) and the README (Requests and handlers, Hosts, Diagnostics).

### OQ-68: A request type that is not sealed, when KRX024 is suppressed (2026-11-26)

Owner decision 14 ("Requests are sealed", at the end) makes every non-abstract request and stream request type sealed (KRX024, an error),
and generic `Send` and `Stream` test each request type with a type pattern, which a derived type also matches. KRX024 is an analyzer
diagnostic, so a project can still lower its severity (`dotnet_diagnostic.KRX024.severity = none`), and a host can reference a library
built that way. The decision does not say what the host generates for such a type: a bare type pattern would send a derived request
statically unknown to the host (or one with a handler of its own placed after it) to the base type's handler, which breaks exact-type
dispatch silently.

- **Interim:** the host keeps exact-type dispatch for a request or stream request type that is not sealed (its model already carries
  whether the type is sealed): its generic section is `case global::X r when r.GetType() == typeof(global::X):` (above the threshold the
  index lookup is by exact type anyway), and its typed overload keeps the runtime type check of OQ-31 (`if (request.GetType() !=
  typeof(X)) return Send<T>((IRequest<T>)request, cancellationToken);`). Sealed types, the only ones a project without the suppression
  has, get the bare type pattern and no check. Covered by `HostRuntimeTests.A_Request_That_Is_Not_Sealed_Keeps_Exact_Type_Dispatch_When_KRX024_Is_Suppressed`
  and the request and stream rows of `JumpTableDispatchTests.A_Derived_Type_Is_Not_Dispatched_To_Its_Base_Types_Handler_Above_The_Threshold`.
- **Proposal:** keep the interim: it costs nothing for sealed types and keeps the semantics when the rule is bypassed. Alternatives: make
  KRX024 not configurable in effect by having the host report an error for a referenced request type that is not sealed (a new host
  diagnostic), or generate the bare type pattern and document that suppressing KRX024 changes dispatch. Needs a decision.
- **Decision:** the host reports an error (owner decision 18, 2026-11-26), so turning KRX024 off in a library cannot reach dispatch. The
  new host diagnostic **KRX038** (Error, the next free ID in the host range): "Request type 'Get' from assembly 'Open' is not sealed, so
  this host does not dispatch it; make it sealed in 'Open' (KRX024)" ("Stream request type" for a stream request), reported by
  `HostGenerator` without a location (like KRX030, its severity is set for the whole host) for each referenced request or stream request
  type it would compose that is neither sealed nor a struct. The host's own types are KRX024 in the host and are not reported again. Such
  a type is not composed: no dispatch section and no typed overload, so sending it throws `KeryxHandlerNotFoundException`, and its request
  counts as handled (no KRX030 on top). The same applies to the host's own type that is not sealed when KRX024 is suppressed in the host.
  So the generated code keeps only bare type patterns: the interim's guarded sections and the typed overloads' runtime type check are
  removed, and no generated path depends on a request type that is not sealed (`HostHandlerModel.MessageIsSealed` is gone). Abstract
  and closed bases (never composed, KRX003 for a handler) and notifications are not affected. Tests: `CrossAssemblyTests.KRX038_For_A_Referenced_Request_Type_That_Is_Not_Sealed`
  (a library built with `MessageTypeAnalyzer` and KRX024 = none; KRX038 for its request and stream request types, not for its sealed
  type, the sealed cases of its abstract and closed bases, its notification type or the host's own type that is not sealed; none
  composed); `HostRuntimeTests.A_Request_Type_That_Is_Not_Sealed_Is_Not_Dispatched` (KRX038 turned off too: not-found for the referenced
  request and stream request and the host's own type, the sealed siblings dispatch) replaces
  `A_Request_That_Is_Not_Sealed_Keeps_Exact_Type_Dispatch_When_KRX024_Is_Suppressed`; `JumpTableDispatchTests.A_Request_Type_That_Is_Not_Sealed_Is_Not_Dispatched_Above_The_Threshold`
  (request and stream) replaces the request and stream rows of the derived-type test, which keeps its notification row as
  `A_Derived_Notification_Is_Not_Dispatched_To_Its_Base_Types_Handler_Above_The_Threshold`. No snapshot showed the guarded sections, so
  none changed for this. Folded into the ADR (status; Requests and notifications; Diagnostics and code fixes: KRX038; HostGenerator's
  host rules; Mediator; Generic entry points), `AnalyzerReleases.Unshipped.md`, the README (Requests and handlers, Diagnostics) and the
  brief (Phases 3 and 7).

### OQ-65: Which pipelines have no context, and which link parameters are dropped (2026-11-26)

Owner decision 11 ("Links with primary constructors", at the end) sets every link's signature as
`LinkK(__PipelineN pipeline, TRequest request)` (`notification` in a publish pipeline), says "pipelines without a context drop the
`pipeline` parameter", and says the machine code must stay identical. Before the decision every pipeline with behaviors had a context,
created once per scope, even when it held nothing (a static invoker and no injected services: "nothing but its links"; or a notification
type with behaviors but no handlers whose behaviors inject nothing), so no pipeline with links was without one. Two things are not decided:
whether those pipelines should lose their context, and whether a link should drop a parameter its `InvokeAsync` never reads (with primary
constructors, such a parameter is warning CS9113, an error under warnings-as-errors in the user's project).

- **Interim:** every pipeline context that existed before stays, and every link takes `(pipeline, request)` (`(pipeline, notification)` in a
  publish pipeline). A primary constructor parameter that `InvokeAsync` does not read is not captured, so each link holds exactly the fields
  it held before (the context only where it reads or passes it on; nothing in the last link of a notification type without handlers), and
  the field layout and machine code are unchanged. The mediator's header pragma is `#pragma warning disable CS0612, CS0618, CS9113`.
  Checked: all 117 links of the host snapshots hold the same fields as before the rework.
- **Proposal:** take only what a link reads or passes on (the last link of a pipeline whose handler's invoker is static takes the request
  alone, the last link of a publish pipeline without handlers takes nothing, `new()`), which removes the CS9113 suppression; and give no
  context to a pipeline with nothing to resolve (a request or stream pipeline whose handler's invoker is static and whose behaviors inject
  nothing, or a publish pipeline without handlers whose behaviors inject nothing): no `__pipelineN` field, no `__CreatePipelineN`, the entry
  calls the outermost behavior method directly, and the links are nested in a `private static class __PipelineN`. That changes machine
  code: measured with it (2026-11-26, the comparison host, whose pipelines are all of that kind), `SendSyncDepth0` generic went from 3.41 to
  3.01 ns and `StreamDepth3` allocated 648 B instead of 680 B (smaller links captured by the stream behaviors' iterators). Alternative: keep
  the interim. Needs a decision.
- **Decision:** adopt the proposal (owner decision 16, 2026-11-26: "keep it"): pipelines that resolve nothing get no context, their links
  are in a private static class and take only the message, and every link takes only the parameters it reads or passes on. Implemented in
  `HostEmitter` for requests, streams and notifications. A request or stream pipeline has a context when its handler's invoker is not
  static or its behaviors inject a service; a publish pipeline when it has handlers or its behaviors inject a service. Without one there
  is no `__pipelineN` field and no `__CreatePipelineN`, the entry calls the outermost behavior method directly (no `Volatile.Read`), and
  the links sit in `private static class __PipelineN` (`__StreamPipelineN`, `__NotificationN`). Each link's parameters are computed from
  the end of the pipeline backwards: the end reads the request and, when the handler is resolved into the context, the context (a publish
  end reads both when there are handlers, nothing otherwise); a call of a behavior method needs the message, the context when it injects a
  service, and whatever the link it constructs takes. So a link is `(pipeline, request)`, `(request)` or, as the last link of a publish
  pipeline without handlers, has no parameter list (`new()`); the entry declares `var pipeline = ...` only when its call needs it. No link
  has an unread parameter, so `GeneratedSyntax.MediatorPragma` is removed and the mediator's header is the obsolete-type pragma alone
  (`#pragma warning disable CS0612, CS0618`). A publish pipeline with handlers that resolves nothing (static invokers, nothing injected)
  keeps its context, because the publish strategy is an instance method of it and the proposal did not list it: OQ-70 (then open;
  decided 2026-11-27, at the end).
  Snapshots: every host snapshot's header loses CS9113 (35 files); the pipelines without a context (`AnnotatedResponseTests`, the
  `Unattended` notification of `NotificationGeneratorTests.Should_Generate_Publish_Pipelines`) become static link classes, and the last
  links of static handlers lose `pipeline` (`PipelineGeneratorTests.Should_Generate_Pipelines_For_Static_Handlers_Struct_Requests_And_IRequest`,
  the `SharedServiceTests`, `StreamGeneratorTests`); new `PipelineGeneratorTests.Should_Give_No_Context_To_A_Pipeline_That_Resolves_Nothing`
  covers every shape and asserts the host compiles without warnings and without a CS9113 suppression. `NotificationAllocationTests` adds
  a publish without a context (typed and generic, 0 B); the request (`Echo`) and stream allocation tests already covered such pipelines.
  Measured (BenchmarkDotNet 0.15.8 default job, net10.0, Apple M4 Max, macOS 27.2, 2026-11-27; the comparison host, whose request and
  stream pipelines all resolve nothing): `StreamDepth3` allocates 648 B instead of 680 B (typed and generic, every run); generic
  `SendSyncDepth0` 2.44 ns in both interleaved runs against 2.47 and 2.95 ns at HEAD (the full run against the earlier "Requests are
  sealed" run, 3.77 to 2.46 ns, is mostly that run's noise); typed `SendSyncDepth8` 0.45 and 0.47 ns against 0.61 and 0.49 ns; every
  other Keryx row within noise, 0 B wherever it was 0 B. Generic JIT code (`DOTNET_TieredCompilation=0`): `Send<int>` keeps its 10 (linear)
  and 43 (above the threshold) tail calls and `Publish<__Canon>` its 2 and 35; the inlined stream pipelines no longer read a context, so
  `Stream<int>` shrinks from 352 to 308 bytes (1260 to 1208 above the threshold); `Publish<__Canon>` above the threshold shrinks from 2884
  to 2856 bytes because the mediator's remaining fields moved within reach of an immediate offset. The Phase 4 gate prototype holds a
  handler and services, so every link reads or passes the context on and its shape is unchanged: `--prototype-disasm` gives the same Tier
  1 code as before (112, 144 and 224 bytes; 6, 12 and 27 inlinees). Folded into the ADR (Generated pipeline shape, Streaming, Mediator,
  Incremental generation rules, Alternatives considered), the brief (Engineering rules, Phase 4), CONTEXT.md (link, pipeline context)
  and the README (Hosts, Benchmarks).

### OQ-66: Generator time of the SyntaxFactory emitters on large hosts (2026-11-26)

Owner decision 5 ("Generated code is built with SyntaxFactory", at the end) prescribes one `NormalizeWhitespace` per compilation unit. On a
host of 300 requests, each with five behaviors (two of them injecting a service), plus 30 stream requests and 30 notifications, the host
generator's time in a Release build (`ReportAnalyzer`, median of ten warm compiler-server builds) went from 66 ms with the text emitters to
384 ms, and the handler generator's (330 handler files) from 35 ms to 102 ms. Skipping `GeneratedSyntax.Render`'s layout pass changed
nothing measurable (368 ms), nor did caching parsed type names (402 ms): the time is `NormalizeWhitespace` over the one large
`KeryxMediator` compilation unit (re-measured after the generated shape returned to switch statements and every pipeline context: 366 ms and
101 ms). The source output runs only when the composition model changes (an edit that changes no handler, behavior or notification leaves it
cached), so the cost is paid on a full build and on edits to the pipelines.

- **Interim:** as decided: one `NormalizeWhitespace` per compilation unit.
- **Proposal:** accept the cost. Alternatives, if it matters for very large hosts: normalize each top-level member of the mediator on its
  own and assemble the unit (bounded work per member, against the decision's one call per unit), or build the syntax with its trivia and
  skip normalization (Dovetail's pattern without `NormalizeWhitespace`). Needs a decision.
- **Decision:** build the syntax with its whitespace and skip normalization (owner decision 16, 2026-11-26): no `NormalizeWhitespace`
  and no layout fix-up pass, with generator time back near the 66 ms / 35 ms baseline as the target and the snapshots unchanged.
  `GeneratedSyntax.Render` now writes the header and the tree as built. Within a line, every helper creates its tokens with the spaces
  they need (`Modifiers`, `Param`, `Assign`, `Return`, `Throw`, the binary operators, `Conditional`, `Is`, `New`, `NewArray`,
  `StaticLambda`, `Named`; the emitters' remaining direct `SyntaxFactory` statements and operators moved to helpers). Lines are laid out
  by the containers from a depth: `WithBody` for a type (members one level deeper, a blank line between two members unless both are
  fields without attributes, documentation lines and attribute lists at the member's depth), `Body` for a block (a blank line after a
  statement that ends with a block; the nullable island of an oblivious fit, OQ-58), `Switch`, `If`, `Try`/`Catch`, `ForEach`,
  `Expression` (the break after `=>`), `Where`, `Constructor` (one parameter per line from two), `Namespace` and `CompilationUnitOf`; a
  container gives each line it starts a line feed and indentation as the leading trivia of its first token, set through the typed `With`
  methods of the nodes the helpers build, never by rewriting a tree. The emitters pass each construct's depth (`KeryxMediator`'s members at
  2, a pipeline context's at 3, a link's `InvokeAsync` at 4; a handler's from its namespace and containing types). The link body written
  with nullable warnings disabled keeps the layout the fix-up pass gave it, a blank line after `=>` and the body at the member's depth after
  `#nullable disable warnings` (`NullableObliviousExpression`), so that its bytes do not change either. The switch-expression helpers
  (`SwitchOn` and its arms), unused since OQ-67, are removed. Without normalization, the time left was mostly the parser: the emitters
  parse type names from the models' strings (`TypeName`, `Qualified`), 28,606 times for the large host; each text is now parsed once per
  file (a per-thread cache that `Render` empties), and the constant attribute lists are built once.
  Output: every Verify snapshot is unchanged byte for byte, and no snapshot was re-accepted. While the change was made, a temporary check
  (not committed) rendered every generated file both ways from the same tree, normalized with the old layout pass and as built, and failed
  on any difference: none in the 7,040 files the test suite generates, the 393 of the large host or the 39 of the solution's own builds;
  the large host's generated sources also equal the baseline's (`diff -r`). Byte for byte for every snapshot is not byte for byte for
  every input: a review built hosts with shapes no snapshot covers and found two differences, both improvements that compile to the same
  thing. (a) Normalization wrote a multi-dimensional rank with a space, `ValueTask<int[, ]>` for a handler returning `int[,]` or
  `int[][,]` (in the handler's `InvokeAsync`, the typed `Send`, `Unsafe.As`, `__SendN`, the behavior call's type arguments and the link's
  `INext<...>`); the type name is now written as parsed, `int[,]`. (b) Normalization wrote an unbound generic `typeof` in a service key,
  `[FromKeyedServices(typeof(Dictionary<,>))]`, as `typeof(global::System.Collections.Generic.Dictionary<, >)` (in the handler
  constructor's attribute and in `AddKeryx`'s `GetRequiredKeyedService`); it is now `Dictionary<,>`. Every other shape tried was
  byte-identical (keyword namespaces and identifiers, `record struct` and `readonly record struct` nesting, 13-deep nesting, the global
  namespace, oblivious fits, struct notifications, keyed and default-valued services, `string?[]` and `int?[]?`), and so was the large host.
  `GeneratedSyntaxTests` are rewritten against the helpers (20 cases): the spacing of nullable types and arrays of them (`string?[]`,
  `int?[]?`), multi-dimensional ranks and unbound generic service keys as written (`int[,]`, `int[][,]`, `Dictionary<,>`), switch sections (type patterns, `when` guards,
  constants, `default`), the blank line after `if`/`else if`, `try`/`catch` (with a comment in an empty block) and `foreach`, blank lines
  between members but not plain fields, the break after `=>`, constraint clauses, constructor parameters one per line, documentation and
  attribute lines, the nullable islands, the assembly attribute and the header.
  Measured on the large host above (`ReportAnalyzer`, Release, median of ten builds; Apple M4 Max, macOS 27.2, .NET SDK 11.0.100,
  2026-11-27; `time_gen.sh` with `gen_bighost.py`, kept with the results in the session scratchpad under `followup-trivia/`). Step 5's
  method (one warm-up build, then runs 2 to 11): host generator 384 to 207 ms, handler generator 102 to 78 ms. The "after" runs were still
  getting faster (287 ms at run 2, 107 ms at run 11) while the compiler server JIT-compiled the new analyzer, so the same was measured after
  ten more warm-up builds: host 372 to 109 ms, handler 93 to 48 ms. In process (the same generators on the same host, run 40 times, median
  of the last 20), the host generator spends 50 ms in `HostEmitter.Mediator` (13 ms of it writing the text) instead of 350 to 420 ms, and
  about 60 ms outside the emitters, the same before and after (handler models, composition, the reference scan, the generated source added
  to the compilation). So the target of about 66 ms and 35 ms is not reached: OQ-71 (then open). Folded into the ADR (status, Incremental
  generation rules, Alternatives considered), the brief (Engineering rules) and the README (Hosts).
- **Superseded** (2026-11-27): owner decision 21 (OQ-71, at the end) replaced the `SyntaxFactory` emitters, and with them this
  implementation, by text emitters that write the same bytes; the measurements above stay as the record of why.

### OQ-69: A closed request base from a referenced assembly (2026-11-27)

Owner decision 14 ("Requests are sealed", at the end) says to detect closed types without new Roslyn APIs: the `closed` modifier's text on
the declaring syntax for a type in source, and `IsClosedTypeAttribute` in metadata for a referenced type. Checked with Roslyn 5.12 (the
tests' compiler; the .NET 11 SDK's compiler was not checked): `GetAttributes()` of a metadata type does not return `IsClosedTypeAttribute`, as it does not
return the compiler's other marker attributes, so a closed type from a referenced assembly cannot be told from an abstract one through
the Roslyn 5.0 API. The compiler makes a closed type abstract (`IsAbstract` is `true` in source and in metadata), so every rule (not a
request, no handler, KRX003 for a handler that takes it, no KRX024) already treats it as a request base; only KRX003's wording ("the
closed type" or "the abstract type") depends on telling them apart. Also observed: a closed type in a compilation against .NET 10 reference
assemblies needs `System.Runtime.CompilerServices.IsClosedTypeAttribute` defined (CS0656 otherwise); the tests add it on .NET 10.

- **Interim:** `MessageType.IsClosed` reads the modifier from syntax and, for a metadata type, looks for the attribute anyway (so it works
  with a compiler that returns it); today a referenced closed base is treated, and named in KRX003, as abstract.
  `HandlerAnalyzerTests.KRX003_Handler_Of_A_Closed_Request_Base_From_A_Referenced_Assembly` pins that.
- **Proposal:** accept: the behavior is right and only a message differs. Alternatives: read Roslyn's own closed-type API through
  reflection when the running compiler has it, or raise the analyzers' Roslyn floor to a version that exposes it. Needs a decision.
- **Decision:** accept (owner decision 19, 2026-11-27): a closed request base from a referenced assembly is treated, and named in KRX003,
  as abstract. The interim stays as the implementation: `MessageType.IsClosed` reads the `closed` modifier from syntax and, for a metadata
  type, still looks for `IsClosedTypeAttribute`, so a compiler that returns it gets the closed wording. No code changed. Folded into the
  ADR (Requests are sealed) and the brief (Phase 2); the README's KRX003 row already names abstract and closed bases alike.

### OQ-71: Generator time after OQ-66 (2026-11-27)

Owner decision 16 decided OQ-66: build the syntax with its whitespace, with generator time "back near the 66 ms / 35 ms baseline" on the
large host (300 requests with five behaviors each, 30 stream requests, 30 notifications) as the target. With the whitespace built in, no
normalization and each type name parsed once per file, the output is byte for byte what it was for every snapshot (two shapes no snapshot covers now read `int[,]` and `Dictionary<,>`
where normalization wrote `int[, ]` and `Dictionary<, >`; OQ-66) and the generators measured 109 ms (host)
and 48 ms (handler) once the compiler server had JIT-compiled the analyzer, against 372 ms and 93 ms before (OQ-66, at the end). In
process, about 60 ms of the host generator is outside the emitters (handler models, composition, the reference scan, adding the 29,000-line
mediator to the compilation) and unchanged by OQ-66; building the mediator's tree takes about 37 ms and writing it 13 ms. The 66 ms
baseline was measured with the text emitters at an earlier commit, so how much of the difference is `SyntaxFactory` itself (one node and
its red wrapper per construct, where the text emitters appended strings) and how much is code added since is not separated.

- **Interim:** as implemented: the whitespace built in, type names parsed once per file, constant attribute lists built once.
- **Proposal:** accept the time: the emitters are now under half of the host generator's time, the source output runs only when the
  composition changes, and a host of this size is far above the samples. Alternatives: build repeated subtrees once per file (the
  parameter lists and link shapes every pipeline repeats) for perhaps a further 10 to 20 ms, or measure the text emitters' commit again on
  this machine to separate the costs before deciding. Needs a decision.
- **Decision:** text emitters everywhere (owner decision 21, 2026-11-27, "Use text-based emitters everywhere"). It supersedes owner
  decision 5 ("Generated code is built with SyntaxFactory", above) and the implementation of OQ-66's decision (syntax built with explicit
  trivia, above); both entries keep their history with a note. Every generator writes its source as text through `CodeWriter`
  (`src/Keryx.Analyzers/CodeWriter.cs`), the indented writer removed in 1fddbdd, recovered and extended: it writes the header, lines at
  the current depth (four spaces, `\n`), `{`/`}` blocks, `///` documentation lines, directives at column 0, a blank line between members
  unless both are fields without attributes (`Member`), a blank line after a statement that ends with a block when another follows
  (`CloseStatement`), the break after `=>` (`Arrow`, and `NullableObliviousArrow` for the OQ-58 island), a constructor's parameters one
  per line from two (`Parameters`), and switch sections (`Switch`, `Section`). The emitters (`HandlerEmitter`, `HostEmitter`,
  `HostEmitter.Streams`, `HostEmitter.Notifications`) build each expression as a string from the models' strings, which are written as
  they are (no parsing). `GeneratedSyntax` and its `SyntaxFactory` helpers are removed; `GeneratedSyntaxTests` became `CodeWriterTests`
  (the header and pragma, the manifest, switch sections, the blank-line rules, the break after `=>`, constructor parameters, documentation
  and attribute lines, a nested type without `;`, the nullable islands, and, through a generated host, `string?[]`, `int?[]?`, `int[,]`,
  `int[][,]`, the `where` line of generic `Publish`, a `Dictionary<,>` service key, escaped string and char keys and an `@`-escaped
  parameter). Code fixes keep editing Roslyn syntax; that is not emission.
  Output: no Verify snapshot changed and none was re-accepted. A temporary check (not committed) wrote every source the generators
  produced during the whole test suite, on net10.0 and net11.0, at HEAD and with the text emitters, and compared them: 734 distinct
  sources (6,056 files at HEAD), identical byte for byte, including three hosts added for the check only (keywords as the root namespace,
  namespaces, types, records, parameters and handler classes; `string?[]`, `int?[]?`, `int[,]`, `int[][,]`, nested generics and tuples
  as responses and items; `typeof(Dictionary<,>)`, `typeof(List<>)` and `typeof(int?)` service keys; string and char keys with quotes,
  backslashes, control characters and U+2028; enum and `long` keys; `NaN`, infinity, `decimal`, `long.MinValue` and `'\0'` defaults; struct
  requests and notifications, boxed and parallel handlers; 34 of each kind, above the jump-table threshold). The large host below (393
  files) and the solution's own builds (the samples and the benchmark host, 168 files) generate the same bytes too.
  Measured on the large host (300 requests with five behaviors each, 30 stream requests, 30 notifications; `ReportAnalyzer`, Release,
  median of runs 2 to 11 of `time_gen.sh` with `gen_bighost.py`; Apple M4 Max, macOS 27.2, .NET SDK 11.0.100, 2026-11-27; results in the
  session scratchpad under `text-before/` and `text-after/`, not kept in the repository), with ten extra warm-up builds so the compiler
  server has JIT-compiled the analyzer: host generator 97 ms with `SyntaxFactory` and explicit trivia (HEAD), 58 ms with the text
  emitters of eca51b5 (the commit before 1fddbdd, re-measured on this machine to compare like with like), 51 ms with the new text
  emitters; handler generator 46, 33 and 29 ms. With step 5's method (one warm-up build): 150, 57 and 78 ms, and 54, 32 and 40 ms; the
  new emitters were still getting faster during those runs (278 ms at run 2, 52 ms at run 11) while the server JIT-compiled them. So the
  target of the 66 ms / 35 ms baseline is met. Folded into the ADR (status, Incremental generation rules, Alternatives considered), the
  brief (Summary, Engineering rules), the README (Hosts) and whats-left.md.

### OQ-70: A publish pipeline whose handlers resolve nothing (2026-11-27)

Owner decision 16 decided OQ-65 by adopting its proposal: a pipeline that resolves nothing gets no context. The proposal named the
pipelines that have nothing to resolve as "a request or stream pipeline whose handler's invoker is static and whose behaviors inject
nothing, or a publish pipeline without handlers whose behaviors inject nothing", and that is what was measured. A publish pipeline whose
handlers all have static invokers and whose behaviors inject nothing (with or without behaviors) also resolves nothing, but it was not in
that list: its context holds no field, only the publish strategy (`RunHandlers`, and for the sequential strategy `RunHandlersAsync`), which
is an instance method of the context. Giving it no context means making the strategy static in a static class of the same name, which
changes the publish code (every `PublishTo` comparison benchmark is such a pipeline) and was neither proposed nor measured.

- **Interim:** such a publish pipeline keeps its context: `__NotificationN` is created once per scope with an empty constructor, its
  strategy runs on it, and its links take `(pipeline, notification)` because the last one calls `pipeline.RunHandlers`. Every link still
  takes only what it reads or passes on, so nothing raises CS9113. The ADR (Generated pipeline shape) says so.
- **Proposal:** give it no context too: `RunHandlers` and `RunHandlersAsync` become `static` members of `private static class
  __NotificationN`, the entry (or the last link) calls `__NotificationN.RunHandlers(notification, cancellationToken)`, and the mediator has
  no `__notificationN` field and no `__CreateNotificationN`; measure `PublishTo1` and `PublishTo5` (typed and generic) before and after.
  Alternative: keep the interim (one empty context per scope and type, a `Volatile.Read` per publish). Needs a decision.
- **Decision:** adopt the proposal (owner decision 20, 2026-11-27): a publish pipeline whose handlers all have static
  invokers and whose behaviors inject nothing, with or without behaviors, gets no context. `HostEmitter.HasContext` for a publish
  pipeline is now "a behavior injects a service, or a handler's invoker is not static" (a type without handlers is the case OQ-65 already
  covered). Such a pipeline's `RunHandlers` and, for the sequential strategy, `RunHandlersAsync` are `static` members of
  `private static class __NotificationN`; without behaviors the entry is `__PublishN(...) => __NotificationN.RunHandlers(notification,
  cancellationToken)`, with behaviors the last link calls the same; the mediator has no `__notificationN` field and no
  `__CreateNotificationN`, and the links take only the notification (`LinkK(TNotification notification)`, `new(notification)`). Behavior
  is unchanged: the strategy bodies are the same code (handler order, every failure in one `AggregateException`, cancellation collected
  like any failure, the parallel start-all-then-await), only `static`. The runtime tests already published through such pipelines
  (`NotificationRuntimeTests`' `Failing`, `OneFailure`, `Cancellable`, `Fanout`, `FanoutFailing` and `CancellableParallel` have only
  static handlers) and pass unchanged.
  Snapshots: the three `JumpTableDispatchTests` snapshots with notifications (`kind=Notification` at 32 and 33, and every kind above the
  threshold), whose handlers are all static, lose the 32 or 33 `__notificationN` fields and `__CreateNotificationN` methods and the
  empty context constructors; each `__PublishN` becomes an expression-bodied call of `__NotificationN.RunHandlers`, and each
  `__NotificationN` becomes a static class with static strategy methods (net10.0 and net11.0 output identical; re-accepted after reading
  each diff, which has no other change). No other snapshot changed: every other publish pipeline in a snapshot has a handler with
  injected parameters or a behavior that injects a service, or no handlers. New
  `NotificationGeneratorTests.Should_Give_No_Context_To_A_Publish_Pipeline_That_Resolves_Nothing` snapshots such a pipeline with and
  without two behaviors, sequential and parallel (four snapshots), and asserts no field, no create method and no `pipeline`.
  `HostGeneratorIncrementalTests.Adding_A_Notification_Type_A_Polymorphic_Handler_Receives_Modifies_The_Host_Sources` asserted the new
  type's `__CreateNotification1`; its handler is static, so it now asserts the static class and the direct handler call.
  `NotificationAllocationTests` adds six rows (typed and generic): sequential with a behavior, parallel without and with a behavior, all
  without a context, 0 B (the sequential `Tick` rows were already such a pipeline).
  Measured (BenchmarkDotNet 0.15.8 default job, net10.0, Apple M4 Max, macOS 27.2, 2026-11-27; `--filter '*Comparison.PublishTo*Keryx_*'`,
  rounds interleaved before, after, before, after; HEAD 100bfe1 against the change; results in the session scratchpad under
  `oq70-before/` and `oq70-after/`, compared in `oq70-after/comparison.txt`, not kept in the repository). Every row 0 B before and after.
  Typed `PublishTo1` 0.67 and 0.65 ns to 0.35 and 0.42 ns, typed `PublishTo5` 0.80 and 0.75 ns to 0.46 and 0.51 ns; generic
  `PublishTo5` 3.03 and 3.03 ns to 2.74 and 2.75 ns, generic `PublishTo1` within noise (2.77 and 2.76 ns against 2.79 and 2.74 ns). Above
  the threshold (`KeryxDispatchPadding=true`): typed `PublishTo1` 0.70 and 0.69 ns to 0.40 and 0.40 ns, typed `PublishTo5` 0.55 and
  0.71 ns to 0.36 and 0.47 ns, generic rows within noise (6.02 and 5.96 ns against 5.92 and 5.96 ns; 6.04 and 6.01 ns against 5.99 and
  5.90 ns). Generic JIT code (`DOTNET_TieredCompilation=0`, `DOTNET_JitDisasm`, the `--verify` run): `Publish<__Canon>` keeps its 2
  (linear) and 35 (above the threshold) tail calls and shrinks from 392 to 300 bytes and from 2,856 to 1,468 bytes, its calls from 7 to 5
  and from 40 to 6 (the context reads and the non-inlined create calls are gone). Folded into the ADR (status, Generated pipeline shape,
  Notifications, Mediator, Alternatives considered), the brief (Phases 4 and 6), CONTEXT.md (pipeline context), the README (Hosts,
  Benchmarks) and whats-left.md.

### Generic `Stream` at depth 0 is tracked for the release (2026-11-27)

Owner decision 22. "Requests are sealed" (above) measured one row slower beyond noise after the type patterns: `StreamDepth0` through
generic `Stream` (100 items, no stream behaviors) at 668 to 717 ns in five runs against 649 to 661 ns before (+20 to +60 ns, 3 to 9%),
while its typed row and `StreamDepth3` (typed and generic) did not move and the generic `Stream<int>` code got shorter; code placement in
the rebuilt host assembly is the suspect, not verified. Decision: track it for the release and do not investigate it now. It stays on the
before-release list in [whats-left.md](../whats-left.md) (check it when the comparison benchmarks are re-run on the release candidate,
then decide whether it needs work). No code changed. Folded into the ADR (status, Benchmarks), the brief (Phase 8), the README
(Benchmarks) and whats-left.md (owner decisions 19 to 22, Gates and checks, Before a first release).

**Measured again** (2026-11-27, decision 32's full run at 1652e50, "Full benchmark run" at the end): the slowdown does not show.
`StreamDepth0` measured 637.8 ns through `IMediator` (standard deviation 5.0 ns), 631.9 ns through the typed overload (2.4 ns) and 632.5 ns
for the direct call (1.8 ns): 6 ns (1%) between generic and typed, and all three below the 649 to 661 ns the reruns measured before the
type patterns. That run is not an interleaved before-and-after comparison, so the item stays on the before-release list as decided.

### OQ-61: Apps whose `OutputType` is not `Exe` or `WinExe` (2026-11-27)

"A host is an MSBuild property" (below) defaults `KeryxHost` to `true` for `OutputType` `Exe` or `WinExe` and for test projects. Some app
models build an application without either: the .NET for Android SDK rewrites an app's `OutputType` from `Exe` to `Library` and marks it
with `AndroidApplication` (Dovetail, whose pattern this follows, checks `AndroidApplication` for that reason), and other workloads may do
the same. Such an app is not a host by default.

- **Interim:** the rule as decided: only `Exe`, `WinExe` and `IsTestProject`. An app that needs the mediator sets
  `<KeryxHost>true</KeryxHost>`; without it, `AddKeryx()` is missing (a compile error in the app, not a silent failure). The README's Hosts
  section says how to set it.
- **Proposal:** also default `KeryxHost` to `true` when `AndroidApplication` is `true`, as Dovetail does, and list further app models
  when they are found. Alternative: keep the interim and document Android in the README.
- **Decision:** adopt the proposal (owner decision 25, 2026-11-27), subject to OQ-62's framework condition: `KeryxHost` also defaults to
  `true` when `AndroidApplication` is `true`, as Dovetail does. The .NET for Android SDK sets it, and rewrites `OutputType` from `Exe` to
  `Library`, in `Microsoft.Android.Sdk.DefaultProperties.targets`, which the workload import in `Microsoft.NET.Sdk.BeforeCommon.targets`
  brings in before `nuget.g.targets` imports the package's targets (checked in a preprocessed `net10.0-android` project on a machine
  with the Android SDK pack). No other app model was found that rewrites `OutputType` and needs the mediator; one found later is a new
  entry. `KeryxHostPropertyTests.KeryxHost_Defaults_To_Android_Apps` evaluates `net10.0-android` (true), `net8.0-android` (false, OQ-62),
  `AndroidApplication` `false` (false), a project's own `false` (false), `net10.0` with `AndroidApplication` (true) and the
  `buildTransitive/` route; each row sets `OutputType` `Library` and `AndroidApplication` as the Android SDK leaves them, so it evaluates
  the same with the Android workload installed or not (checked here with `MSBuildEnableWorkloadResolver=false`). Folded with OQ-62 (below).

### OQ-62: Executables and test projects below `net10.0` (2026-11-27)

"A host is an MSBuild property" (below) defaults `KeryxHost` to `true` for every `Exe`, `WinExe` or test project, whatever it targets,
and the package's new `buildTransitive/` files apply that default to projects that get Keryx transitively. KRX031 is an error, so a
`net8.0` or .NET Framework executable or test project that runs the Keryx generators (its own reference, or a `ProjectReference` to a
project that references Keryx) now fails to build although it never asked to be a host. Before the change it built, because it had no
`[assembly: KeryxHost]`. Reproduced in review with the packed package: a `netstandard2.0` library with a request and handler, and a
`net8.0` console app that only references that library, fails with KRX031. The common library-author setup is hit too: a
`netstandard2.0` Keryx library whose test project multi-targets `net8.0;net10.0` (or `net48`) fails in every inner build below
`net10.0`. OQ-61 covers only app models whose `OutputType` is not `Exe`.

- **Interim:** the rule as decided. The README's Hosts section and KRX031 row tell such a project to set `<KeryxHost>false</KeryxHost>`,
  per target framework in a multi-targeting test project:
  `<KeryxHost Condition="!$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)','net10.0'))">false</KeryxHost>`.
- **Proposal:** in `build/Keryx.targets`, default to `true` only when the target framework can host. `TargetFrameworkIdentifier` and
  `TargetFrameworkVersion` are already inferred before `nuget.g.targets` is imported, so the condition becomes
  `('$(OutputType)' == 'Exe' or '$(OutputType)' == 'WinExe' or '$(IsTestProject)' == 'true') and '$(TargetFrameworkIdentifier)' == '.NETCoreApp' and $([MSBuild]::VersionGreaterThanOrEquals('$(TargetFrameworkVersion.TrimStart(vV))', '10.0'))`.
  KRX031 then fires only for an explicit `<KeryxHost>true</KeryxHost>`. If adopted, `KeryxHostPropertyTests` gains rows for a `net8.0`
  `Exe` (false), a `net10.0` `Exe` (true) and a `net8.0` project with `<KeryxHost>true</KeryxHost>` (true). Needs a decision.
- **Decision:** adopt the proposal (owner decision 26, 2026-11-27). In `build/Keryx.targets` (which `buildTransitive/` and
  `buildMultiTargeting/` import, so all three routes get it) `KeryxHost` defaults to `true` only when (`OutputType` is `Exe` or `WinExe`,
  or `IsTestProject` or `AndroidApplication` is `true`) **and** `TargetFrameworkIdentifier` is `.NETCoreApp` **and**
  `TargetFrameworkVersion` (without its `v`) is 10.0 or later; otherwise `false`. A value the project sets still wins, so KRX031 fires only
  for an explicit `<KeryxHost>true</KeryxHost>` below `net10.0`. Where the inputs come from: `nuget.g.targets` is imported by
  `Microsoft.Common.CurrentVersion.targets`, after the project body and after `Microsoft.NET.TargetFrameworkInference.targets`
  (`Microsoft.NET.Sdk.BeforeCommon.targets`) has inferred the identifier and version from `TargetFramework`. The identifier is compared
  first, and MSBuild stops at the first false operand of `and`, so the outer build of a multi-targeting project (no target framework,
  hence no version) is `false` without calling `VersionGreaterThanOrEquals` on an empty string. KRX031's message no longer suggests
  `<KeryxHost>false</KeryxHost>`; it says to target `net10.0` or newer or stop setting `KeryxHost` to `true`, and its description states the
  default (`HostGeneratorTests.KRX031_Is_Reported_Without_A_Location_And_Nothing_Is_Generated` asserts the new message).
  Tests: `KeryxHostPropertyTests` now imports the package's files from `obj/Consumer.csproj.nuget.g.props` and `.nuget.g.targets`, where
  restore puts them and where `Microsoft.Common.props` and `Microsoft.Common.CurrentVersion.targets` import them (before, it imported them
  from `Directory.Build.props` and `Directory.Build.targets`), with `buildMultiTargeting/` for the outer build of a multi-targeting project
  as NuGet's generated files do; so the evaluation position the default depends on is the real one. New
  `KeryxHost_Defaults_Only_Where_A_Host_Can_Run` rows: `net8.0` `Exe` false, `net10.0` `Exe` true, `net11.0` `Exe` true, `net8.0` `WinExe`
  false, `net8.0` and `net48` test projects false, `net48` `Exe` false, a `netstandard2.0` test project false, `net8.0` with an explicit
  `true` true (with and without `Exe`), `net10.0` `Exe` with an explicit `false` false, and `buildTransitive/` rows for `net8.0` and
  `net10.0`; `A_Multi_Targeting_Test_Project_Is_A_Host_Per_Inner_Build` evaluates a `net8.0;net10.0` test project's inner builds (with
  `TargetFramework` as a global property, as the outer build dispatches them: false and true) and its outer build (false, no error),
  through `build/` and `buildTransitive/`. `HostGeneratorTests.Should_Report_No_KRX031_Outside_A_Host_Below_Net10` covers the generator
  half: `net8.0`, `netstandard2.0` and `net48` with `KeryxHost` unset or `false`, and a referenced request without a handler, generate
  nothing and report neither KRX031 nor KRX030. The existing rows (on `net10.0`) are unchanged. The harness retries a `dotnet msbuild` run once
  when it exits non-zero without printing anything: one such run (exit 1, empty output) was seen in about fifteen runs of the class, right
  after a solution build, and could not be reproduced with 100 parallel evaluations.
  Checked by hand with the packed package (`dotnet pack`, version `0.0.0-hostcheck.2611271`, a scratch feed and packages folder; a
  `netstandard2.0` library with a request and handler, consumers that reference it by `ProjectReference` and so get Keryx transitively):
  a `net8.0` console app builds (`KeryxHost` false; with HEAD's `build/Keryx.targets` swapped into the same package it failed with
  KRX031); a test project multi-targeting `net8.0;net10.0` builds (`KeryxHost` false in `net8.0`, true in `net10.0`, which compiles a call
  to the generated `AddKeryx()` under `#if NET10_0_OR_GREATER`; with HEAD's targets its `net8.0` build failed with KRX031); a `net10.0`
  console app builds and sends a request through the mediator; a `net8.0` app with `<KeryxHost>true</KeryxHost>` fails with KRX031 and the
  new message. Folded into the ADR (status; Which projects may target what; Diagnostics and code fixes: KRX031; Opting in; Alternatives
  considered; Consequences), the brief (Summary, Repository layout, Phase 3), CONTEXT.md (host, the owner's edit), the README
  (Requirements, testing, Hosts, KRX031) and whats-left.md.

### OQ-59: A host replacing an excluded referenced request handler (2026-11-27)

Before the second rework, a host (typically a test host) replaced a referenced handler by excluding it with
`[assembly: ExcludeHandler(typeof(X))]` and handling the request itself (ADR, Host control; OQ-45). With "Requests are handled in their own
assembly" (below), a host's handler of a request declared in a referenced assembly is KRX023, hosts included, and the generators skip it
like any handler error. So a host can still exclude a referenced request handler (the request is then KRX030), but it cannot provide its
own; a test host that cannot resolve a handler's infrastructure (the case OQ-45 describes) is left with KRX030. Notification handlers
are unaffected: a host may exclude a referenced notification handler and handle the notification itself.

- **Interim:** KRX023 applies in a host as in any assembly, as decided. The host's handler of a referenced request is reported and
  skipped; it counts as handling the request, so KRX030 is not added on top, and nothing dispatches the request. The ADR (Host control)
  and the README say that `ExcludeHandler` can no longer replace a referenced request handler. Tests:
  `CrossAssemblyTests.ExcludeHandler_Cannot_Replace_A_Referenced_Request_Handler` and
  `ExcludeHandler_Skips_A_Referenced_Handler_The_Host_Cannot_Resolve` (now KRX030).
- **Proposal:** allow it as the one exception to KRX023: in a host, a handler of a request declared in another assembly is allowed when
  the host excludes that request's referenced handler with `ExcludeHandler` (KRX023 is still reported otherwise, and KRX036 still covers
  an exclusion that names nothing). This keeps test hosts able to replace handlers that need infrastructure they do not have.
  Alternative: keep the interim and document replacing through DI (a handler's injected services) as the way to swap behavior in tests.
  Needs a decision.
- **Decision:** no exception to KRX023 (owner decision 23, 2026-11-27): a host's handler of a request or stream request declared in
  another assembly stays KRX023, hosts included, and a test swaps a request handler's behavior through its injected services (DI). As a
  follow-up the owner limited `ExcludeHandler` to **notification** handlers: naming a request or stream request handler, referenced or
  the host's own, is an error, and the handler is not excluded. It got a new ID, **KRX039** (Error, the next free ID in the host range),
  rather than an extension of KRX036: KRX036 is a warning that an exclusion removes nothing, while this is an error about what
  `ExcludeHandler` may name, and one ID with two severities would read worse than two. Message: "[assembly: ExcludeHandler(typeof(X))]
  names a request handler, which a host cannot exclude; ExcludeHandler names only notification handlers" ("stream request handler" for
  a stream handler); reported on the attribute. A referenced handler is recognized by its manifest entry's kind, the host's own by its
  handler candidate (a request or stream request parameter). The named handler is composed as if the exclusion were not there (so its
  request is dispatched, and a referenced one the host cannot resolve is KRX034, no longer skipped), and the exclusion counts as used (no
  KRX036). Excluding a referenced notification handler, and KRX036 for the host's own notification handler or a type that is not a
  handler, are unchanged. Tests: `CrossAssemblyTests.KRX039_ExcludeHandler_Cannot_Replace_A_Referenced_Request_Handler` (KRX039 beside the
  host's KRX023 replacement; the referenced handler is dispatched), `KRX039_For_Referenced_Request_And_Stream_Request_Handlers_Which_Stay_In_The_Host`
  (replaces `ExcludeHandler_Without_A_Replacement_Leaves_The_Request_Unhandled`; severity, location, help link, both dispatched, no
  KRX030 or KRX036), `KRX039_For_The_Hosts_Own_Request_And_Stream_Request_Handlers`, `KRX039_Does_Not_Skip_A_Referenced_Request_Handler_The_Host_Cannot_Resolve`
  (KRX039 and KRX034); `ExcludeHandler_Skips_A_Referenced_Handler_The_Host_Cannot_Resolve` now excludes a notification handler whose
  injected type the host does not reference (no diagnostics, nothing generated names it). Folded into the ADR (status; Diagnostics and
  code fixes: the host-rule list, KRX030, KRX036 and KRX039 rows; Host control; Attribute reference; Alternatives considered;
  Consequences), the brief (Summary, Host attributes, Phase 7), the README (Testing handlers, Contracts and handler libraries, KRX005,
  KRX030, KRX036 and KRX039), `AnalyzerReleases.Unshipped.md` (KRX036, KRX039) and whats-left.md. CONTEXT.md needs no change (no term
  changed).

### OQ-60: KRX005 across assemblies once KRX023 exists (2026-11-27)

The host reports KRX005 when a request or stream request has handlers in more than one assembly of its composed set (OQ-49). With
KRX023, every request handler lives in its request's assembly: the handler generator skips a handler of another assembly's request, so no
generated manifest describes one, and the host skips its own such handlers. Two assemblies of the composed set can then no longer handle
one request, and the host check is reachable only through a manifest that Keryx did not generate (written by hand, or by a Keryx build
from before KRX023).

- **Interim:** keep the host's cross-assembly KRX005 unchanged as a safety net. Its test
  (`CrossAssemblyTests.KRX005_For_Two_Referenced_Assemblies_That_Handle_One_Request`) now builds the two libraries with hand-written
  manifests. The ADR's KRX005 row says the host check is a safety net.
- **Proposal:** keep it (cheap, and it guards against manifests built with another rule). Alternative: remove the host's KRX005 and the
  `DuplicateHandlerInHost` descriptor, leaving KRX005 to the analyzer within one assembly. Needs a decision.
- **Decision:** remove it (owner decision 24, 2026-11-27): the host's cross-assembly KRX005, the `DuplicateHandlerInHost` descriptor and
  its hand-written-manifest test (`KRX005_For_Two_Referenced_Assemblies_That_Handle_One_Request`) are gone; KRX005 stays the analyzer's
  rule within one assembly. Manifests that Keryx did not generate and that name handlers of one request in two assemblies leave the request
  undispatched (the composition keeps only request types with exactly one handler, as for duplicates in one assembly), with no host
  diagnostic. `KRX005_Is_Not_Reported_Again_For_Duplicates_Inside_One_Referenced_Assembly` became
  `Duplicates_Inside_One_Referenced_Assembly_Are_Its_Own_KRX005` (no host diagnostic, the request not dispatched).
  `AnalyzerReleases.Unshipped.md` only changes KRX005's notes: the rule stays, and nothing has shipped, so there is no "Removed Rules" or
  "Changed Rules" entry to add. Folded into the ADR (status; the host-rule list and ID ranges; KRX005 row; Host control; Alternatives
  considered), the brief (Phase 7), the README (KRX005), future-requirements.md and whats-left.md.

### OQ-63: A behavior's `IServiceProvider` in a pipeline context (2026-11-27)

Owner decision 9 ("A pipeline context resolves each shared service once", at the end) says that `IServiceProvider` needs no field. A
behavior method's unkeyed `IServiceProvider` parameter receives the scope's provider (ADR, Injected parameters), which is resolved by
nothing: it is the provider the pipeline context is built with. But the links that call behavior methods reach only the pipeline context
and the message, and the context does not otherwise keep the provider, so the call site has nowhere else to read it from.

- **Interim:** the provider is a shared service like any other: one `ServiceN` field (assigned `serviceProvider` in the constructor, never
  resolved, always counted as required), shared by every behavior method of the pipeline that injects it. A context whose behaviors do
  not inject it has no such field. A keyed `IServiceProvider` is an ordinary keyed service. The ADR (Injected parameters) says so.
- **Proposal:** keep the interim (one field, only when used, consistent with the other shared services). Alternative: give every pipeline
  context whose behaviors inject `IServiceProvider` a fixed `ServiceProvider` field outside the numbered services, or reach the provider
  through the mediator (the context would hold the mediator instead). Needs a decision.
- **Decision:** a fixed `ServiceProvider` field (owner decision 27, 2026-11-27). A pipeline context (request, stream, publish) whose
  behavior methods inject the unkeyed `IServiceProvider` (required or optional) holds it in a dedicated `ServiceProvider` field, assigned
  from the constructor's provider and never resolved, outside the numbered `ServiceN` services and shared by every behavior method that
  injects it; a context whose behaviors do not inject it has no such field. A behavior that injects only the provider gives its pipeline
  a context (a static handler's pipeline included). The same rule applies to handlers: a handler's injected `IServiceProvider` is the
  provider directly (the generated factory already passed `serviceProvider` to the constructor, and the handler's own field holds it), so
  nothing changed there. A keyed `IServiceProvider` is an ordinary keyed service. `IServiceScope` is not supported as a special case: it
  is not injectable through DI, and an injected one stays an ordinary resolved service. In the model, `PipelineBehaviors.ShareServices`
  leaves the unkeyed provider out of `ContextServices`, gives its parameters the slot `PipelineBehaviors.ServiceProviderSlot` (-1) and
  sets `ContextServiceProvider` on `HostHandlerModel`/`HostNotificationModel`; `ContextServiceModel` lost `IsServiceProvider` (only the
  unkeyed provider was special, and it is no longer a context service). Tests (`Behaviors/SharedServiceTests`):
  `Should_Hold_The_Scope_Provider_In_One_ServiceProvider_Field` (snapshot: request, stream and publish pipelines with static handlers and
  two behaviors each injecting the provider, one required and one optional; one field per context, six reads, no `ServiceN`, nothing
  resolves `IServiceProvider`); `Should_Resolve_A_Keyed_IServiceProvider_As_An_Ordinary_Keyed_Service` (a keyed provider, required and
  optional, is `Service0` resolved with `GetRequiredKeyedService`, beside the unkeyed `ServiceProvider`);
  `Should_Pass_The_Scope_Provider_To_A_Handler_That_Injects_It` (the factory passes `serviceProvider` and resolves the keyed one; no
  `ServiceProvider` field in a context whose behaviors do not inject it); `Should_Give_A_Service_Four_Behaviors_Share_One_Field` asserts
  no `ServiceProvider` field; `Should_Resolve_A_Service_With_A_Required_Consumer_As_Required_For_All` reads `pipeline.ServiceProvider`
  instead of `Service3`; at run time every behavior's provider (request, stream and publish, required and optional) and a request and a
  notification handler's provider are the scope's (`Behaviors_Of_One_Pipeline_Receive_The_Same_Instance_Of_A_Transient`).
  `PipelineAllocationTests`' injecting behaviors also take the provider: still 0 B at depths 1, 3 and 8. The link and context shapes are
  unchanged (one more field read), so `--prototype-disasm` was not re-run. Folded into the ADR (status; Injected parameters; Generated
  pipeline shape; Alternatives considered, added by the docs sweep after OQ-59 to OQ-64), the brief (Phase 4), CONTEXT.md (pipeline
  context), the README (Injected parameters, Behavior methods) and whats-left.md.

### OQ-64: Shared service field names and set-preserving edits (2026-11-27)

Owner decision 9 ("A pipeline context resolves each shared service once", at the end) names a pipeline context's fields "by service, in
first-use order (e.g. Service0, Service1), stable across edits that do not change the set". The two cannot both hold: numbering by first
use depends on the order of the behaviors and their parameters, not only on the set. A pipeline whose behaviors inject `IClock`, `ILog` and
`IStore` (`Service0` to `Service2`) gains an outermost behavior injecting (`IClock`, `IStore`): the set and the service used first are
unchanged, but the first-use order becomes `IClock`, `IStore`, `ILog`, so `Service1` and `Service2` swap. Behavior is unaffected (each
behavior parameter still receives its own service, and the fields are private to the generated context); what changes is the generated
text and the order of the model's `ContextServices`.

- **Interim:** first-use order, as the decision's naming rule says. Names are stable for edits that change neither the set nor the order
  of first uses. `SharedServiceTests.Adding_A_Behavior_That_Uses_Shared_Services_In_Another_Order_Renumbers_The_Context_Services` pins
  the swap. The ADR (Injected parameters) and the brief (Phase 4) state the narrower guarantee.
- **Proposal:** number the services by a stable ordinal sort of (service type, key) instead of first use, so any edit that keeps the set
  keeps every name (the incremental test would then assert unchanged `ContextServices`). Cost: the numbering no longer follows the
  pipeline, so `Service0` is not necessarily the outermost behavior's first service. Alternative: keep first-use order and drop the
  stability clause from the decision. Needs a decision.
- **Decision:** a stable ordinal sort (owner decision 28, 2026-11-27). A pipeline context's services are numbered by (service type's fully
  qualified name without a top-level nullable annotation, ordinally; then the key: none first, then the key's C# literal text ordinally),
  so any edit that keeps the set keeps every name; `Service0` is no longer necessarily the outermost behavior's first service.
  `PipelineBehaviors.ShareServices` collects the distinct services (merging required consumers as before), sorts them with
  `PipelineBehaviors.CompareServices` and indexes each behavior's parameters into the sorted list; it no longer sorts the behaviors.
  `SharedServiceTests.Adding_A_Behavior_That_Uses_Shared_Services_In_Another_Order_Renumbers_The_Context_Services` is replaced by
  `Adding_A_Behavior_That_Uses_Shared_Services_In_Another_Order_Keeps_The_Context_Services` (the rerun's `ContextServices` equal the
  first run's, and so do the fields), and `Should_Number_The_Services_By_Type_And_Key_Not_By_First_Use` pins the order (types, then no key,
  then keys). Snapshots that changed, each reviewed: `Should_Keep_Keyed_And_Unkeyed_Services_Of_One_Type_Apart` (`"earth"` is now
  `Service1` and `"utc"` `Service2`), `Should_Resolve_A_Service_With_Only_Optional_Consumers_Once_And_Apply_Each_Default` (fields sorted
  by service type: `IClock`, `TimeSpan`, `int`, `int?` (`Nullable<int>`) and the keyed `string`, each held nullable; the call sites read the renumbered fields with the same
  defaults) and `Should_Resolve_A_Service_With_A_Required_Consumer_As_Required_For_All` (OQ-63's change only; its three services were
  already in sorted order). No other host snapshot changed: their pipelines share one service, or already used them in sorted order.
  Folded into the ADR (status; Injected parameters; Generated pipeline shape; Alternatives considered, added by the docs sweep after
  OQ-59 to OQ-64), the brief (Phase 4), CONTEXT.md (pipeline context), the README (Behavior methods) and whats-left.md.

### Real-world benchmark scenarios (2026-11-27)

Owner decision 30. The comparison suite gains real-world `Ping` -> `Pong` scenarios for every subject (direct call, Keryx typed and
`IMediator`, Mediator scoped and singleton, MediatR, Immediate.Handlers, ZeroAlloc.Mediator static and `IMediator`), each where the
library has an API for it, under JIT and NativeAOT (the NativeAOT run is decision 29): (a) a scope per request: create a DI scope, resolve
the mediator (or the library's entry point) from it, `Send(Ping)`, dispose the scope, which shows the first-call-per-scope cost (the
pipeline context for Keryx) that the other scenarios hide; (b) typical behaviors: a logging behavior through
Microsoft.Extensions.Logging with an in-memory provider that really formats the messages, a validation behavior calling a hand-written
`IValidator<Ping>`, and a handler that injects a scoped repository doing a dictionary lookup, run both from a long-lived scope and per
request; (c) a data-access handler running a small parameterized query against an in-memory Microsoft.Data.Sqlite database, created and
seeded once per benchmark process, with the connection choice documented and the README noting that SQLite's async methods complete
synchronously; (d) (added by the owner on 2026-11-27 while (a) to (c) were being implemented, required in the same commit)
notifications, 1 and 5 handlers each, for every subject with a publish API: a real-world `Pinged` notification published per request
scope (scope created, entry point resolved, publish, scope disposed inside the measured call), every handler doing a scoped repository
lookup and a Microsoft.Extensions.Logging message to the bounded in-memory provider, plus the same logging behavior as a notification
behavior where the library supports notification behaviors (n/a otherwise, with the reason); the same handlers made async, each awaiting
the SQLite query from (c) (or `Task.Yield`, if that reads better, saying which), published sequentially; and that async publish with each
library's parallel strategy (Keryx's parallel publish strategy, Mediator's `TaskWhenAllPublisher` or equivalent, MediatR's
`TaskWhenAllPublisher`, the others n/a with the reason) next to the sequential one; `--verify` counts every handler call for each. The
handler bodies and behaviors are the same for every library, each in its `test/benchmark-subjects` project, and `--verify` counts
handler and behavior calls for the new scenarios. Owner decision 31's first part (the Benchmarks workflow's Dry job covers the new
scenarios) needed no change: the Dry job already runs `--filter '*'`.

Implemented as `RealWorldPing`, `RealWorldPingScopePerRequest`, `RealWorldBehaviors`, `RealWorldBehaviorsScopePerRequest` and
`RealWorldSqlite` (`Comparison/RealWorldBenchmarks.cs`, 48 benchmarks), and for (d) `RealWorldPublish1ScopePerRequest`/`5`,
`RealWorldPublishBehavior1ScopePerRequest`/`5` (the notification behavior, Keryx only), `RealWorldPublishAsync1`/`5` (awaiting the
SQLite query, not `Task.Yield`) and `RealWorldPublishParallel1`/`5` (`Comparison/RealWorldPublishBenchmarks.cs`, 56 benchmarks), 104 in
all; Mediator's parallel publisher is a generator option, so it got a subject project of its own (`Keryx.Benchmarks.MediatorParallel`).
How each library is set up, the connection choice, the parallel publishers and the n/a cells are OQ-72 (decided, owner decision 33).
`--verify` (238 comparison benchmarks, net10.0 and net11.0) and a Dry-job run of `*RealWorld*` (104 benchmarks) pass; no measured run yet
(the README's numbers come from the full local run of decision 32). Folded into the ADR (Benchmarks), the brief (Phase 8), the README
(Benchmarks) and whats-left.md.

### NativeAOT benchmark run (2026-11-27)

Owner decisions 29 and 31. The comparison suite (`test/Keryx.Benchmarks`) also runs with BenchmarkDotNet's NativeAOT toolchain (net10.0),
next to the JIT run, with the same scenarios and subjects. A subject library that cannot run a scenario under NativeAOT shows n/a with
the reason (a build, trim or AOT warning, or a runtime failure), verified, not assumed. `--verify` works in the NativeAOT build too, or an
equivalent check proves that each NativeAOT subject really runs the handlers and behaviors (29). A new job in the Benchmarks workflow
builds the suite and dry-runs it with the NativeAOT toolchain on Linux, numbers never published from shared runners, so NativeAOT breakage
fails the pull request (31; its first part, the Dry job covering the real-world scenarios, is under "Real-world benchmark scenarios").

Implemented:

- **Running it.** `dotnet run -c Release -f net10.0 -- --filter '*Comparison*' --runtimes nativeaot10.0` with the usual job options, on its
  own. `--runtimes net10.0 nativeaot10.0` runs both in one summary but gives no NativeAOT ratios: BenchmarkDotNet 0.15.8 makes the first of
  several runtimes the baseline job and then counts as the baseline only the case that is both the `Baseline` method and in that job
  (`BaseliningStrategy`, checked by decompiling). The comparison, prototype and Host benchmark classes group by job
  (`BenchmarkLogicalGroupRule.ByJob`), so such a run shows `?` in the NativeAOT rows' ratio columns instead of ratios against the JIT's
  `Baseline` (checked with a Dry run of `*RealWorldPing*` and `*PipelineContext*` both ways: side by side, the JIT rows' ratios and `?`;
  `--runtimes nativeaot10.0` alone, the NativeAOT `Baseline` at 1.00). BenchmarkDotNet 0.15.8 needs no extra configuration for it: its
  NativeAOT toolchain publishes a generated project that references `Keryx.Benchmarks` with `PublishAot` and the SDK's ILCompiler (.NET
  10.0.15 here). `Program.cs` passes a config with two additions: `NativeAot.Filter` leaves the n/a subjects and the Phase 4 pipeline
  prototype out of NativeAOT jobs (JIT jobs keep every benchmark), and `ScenarioListValidator` (below). The prototype is JIT-only: it is
  disassembly evidence for the JIT's code, and on Linux and Windows its `GateConfig` adds `DisassemblyDiagnoser`, whose validator in
  BenchmarkDotNet 0.15.8 returns a critical error for any NativeAOT job ("Currently NativeAOT has no DisassemblyDiagnoser support"),
  stopping the whole run. Config filters run before validation, so the validator never sees a NativeAOT case.
- **n/a, verified by building and running** (`Comparison/NativeAot.cs`, which `Subjects`, `--verify` and the filter read): MediatR
  15.0.0, in every scenario (`MediatR_Generic`, 23 benchmarks). With the suite's behaviors, `AddMediatR` throws at startup
  (`AddOpenStreamBehavior`: "StreamPass1`2 must implement MediatR.IStreamPipelineBehavior`2"); without any behaviors (the parallel publish
  container) the validated container cannot construct MediatR's own `Mediator` ("A suitable constructor for type 'MediatR.Mediator' could
  not be located"); the publish reports MediatR.dll's trim and AOT warnings (IL2104, IL3053). Under NativeAOT, `Subjects` does not build
  MediatR's two containers. Every other subject (the direct call, Keryx typed and `IMediator`, Mediator scoped, singleton and its parallel
  assembly, Immediate.Handlers, ZeroAlloc.Mediator static and `IMediator`) runs every scenario it runs under the JIT, with the same results
  and hit counts: 215 of the 238 comparison benchmarks. The n/a cells of OQ-72 (entry points a library does not have) are the same under
  both.
- **The NativeAOT check.** `dotnet publish -c Release -f net10.0 --use-current-runtime -p:KeryxBenchmarksNativeAot=true -o aot` publishes
  `Keryx.Benchmarks` itself with NativeAOT; `aot/Keryx.Benchmarks --verify` calls every benchmark twice with the probe on and checks its
  result and hit count exactly as under the JIT, and lists the n/a ones with the reason instead of calling them (215 verified, 23 n/a).
  `Verification` lists the scenarios (`Verification.Scenarios`, each `Scenario.Of<T>()`, whose `[DynamicallyAccessedMembers(PublicMethods)]`
  keeps the benchmark methods) instead of scanning the assembly, so it has no trim warnings, and reports a benchmark that throws as a
  failure instead of stopping. `ScenarioListValidator` fails any BenchmarkDotNet run that finds a comparison scenario missing from that
  list (checked by leaving one out: the run exits 1).
- **Warnings.** `PublishAot` is set only for that publish (`KeryxBenchmarksNativeAot=true`, `net10.0`): in a JIT build it also writes the
  AOT feature switches into the runtimeconfig (`RuntimeFeature.IsDynamicCodeSupported` false, EventSource off, and more), so a JIT run
  would no longer measure what an ordinary JIT app runs (with it, the JIT build's `--verify` reported NativeAOT). Every build of the
  benchmark and subject projects runs the trim, AOT and single-file analyzers instead (`EnableTrimAnalyzer`, `EnableAotAnalyzer`,
  `EnableSingleFileAnalyzer`), and none reports anything. In the publish, Keryx and the subject projects are project references, whose
  trim and AOT warnings ILC reports one by one, and those stay errors: there are none, and nothing in Keryx's own code is suppressed. The
  package assemblies' warnings stay warnings (`WarningsNotAsErrors`): BenchmarkDotNet and its dependencies (CommandLine,
  Gee.External.Capstone, Microsoft.CodeAnalysis, TraceEvent: IL2104, IL3053, IL3000, IL3002) and MediatR (IL2104, IL3053).
- **CI.** The workflow's `native-aot` job (ubuntu-latest) installs clang and zlib1g-dev (the ILCompiler's linker prerequisites), builds,
  publishes with NativeAOT and runs `--verify` in the binary, then runs every benchmark once with `--job Dry --runtimes nativeaot10.0`
  (the filter leaves out MediatR and the prototype).
- **Checked locally** (macOS arm64, .NET SDK 11.0.100): `--verify` under the JIT, 238 passed; in the NativeAOT publish, 215 passed and 23
  n/a; a NativeAOT Dry run of every benchmark (`--filter '*'`, one generated executable: the 215 comparison benchmarks, the Host dispatch
  and pipeline-context benchmarks, 242 in all; the prototype is JIT-only) and the JIT Dry run (271) both passed. The same NativeAOT job
  steps (build, NativeAOT publish, `--verify`, `--filter '*' --job Dry --runtimes nativeaot10.0`) were run on Linux in the
  `mcr.microsoft.com/dotnet/sdk:11.0` image with the .NET 10 runtime, clang and zlib1g-dev (linux-arm64; CI is linux-x64), where
  `GateConfig` does add the diagnoser: no warning in Keryx or the subjects, `--verify` 215 passed and 23 n/a, and the Dry run 242
  benchmarks, exit 0. Nothing was measured: the NativeAOT numbers come from decision 32's full local run. The verification probe's cost in
  NativeAOT builds is OQ-73 (decided, owner decision 34: compiled out of the benchmark builds).

Folded into the ADR (status; Benchmarks), the brief (Phase 8), the README (Benchmarks) and whats-left.md.

### Full benchmark run (2026-11-27)

Owner decision 32 (session default). The README's existing tables are refreshed from a full local JIT run at the commit that adds the new
scenarios, with the same settings (IterationCount=15, IterationTime=200ms, LaunchCount=1, WarmupCount=5), and gain sections for the
real-world scenarios (30d's notification benchmarks included) and for the NativeAOT run; the benchmarks run with nothing else heavy
running on the machine.

Done:

- **The runs.** At 1652e50 (the NativeAOT commit, after the real-world scenarios' 1a14841), built from a clean `git archive` copy so the
  working tree was not used, on an Apple M4 Max (macOS 27.2.1, .NET SDK 11.0.100, .NET 10.0.15). Nothing else was building, testing or
  benchmarking (checked with `ps`; the run script also waited before each run until no `dotnet` process was busy). `--verify` passed
  first: 238 under the JIT; in the NativeAOT publish, 215 passed and 23 were n/a (MediatR). Then `--filter '*Comparison*'` with those
  settings under the JIT (238 benchmarks, 25 min 29 s, exit 0) and right after with `--runtimes nativeaot10.0` (215 benchmarks, MediatR's
  23 left out, 21 min 58 s, exit 0). The reports (CSV, GitHub markdown and HTML per scenario, and the logs) are in the session
  scratchpad, `bench-final/jit` and `bench-final/aot`; every number in the README comes from them. The allocations are the exact bytes
  per call from each benchmark's `// GC:` line in the logs (total bytes over total operations, rounded, as BenchmarkDotNet computes
  them), because the CSV and markdown reports give a column in KB with two decimals once every row in it is 1 KB or more.
- **README.** Every existing table is refreshed from the JIT run, with the date, commit and environment. The real-world scenarios have
  three tables (requests; publish per request scope with and without the notification behavior; async handlers sequential and parallel)
  and notes. The NativeAOT section shows, for every scenario, the JIT and NativeAOT time side by side with the allocations (for the
  yielding ones the allocations are the meaningful column, since the hop dominates the time), and MediatR n/a with its reason. The "since
  that run" paragraphs became what this run shows, and the how-to-read notes were brought up to date (the yield range, the direct call's
  113 to 114 B, comparing real-world rows with the direct call, the NativeAOT floor of 1.49 to 1.52 ns for `Send` and the `Publish` rows
  that still measure about a nanosecond or less, the probe of OQ-73 and the type names of OQ-75).
- **What the run shows of earlier caveats.** Keryx's `IMediator` `Send` with a synchronous handler measured 2.44 to 2.74 ns (3.22 to 3.51
  ns in the 2026-11-26 run, before the type patterns); three stream behaviors allocate 648 B (OQ-65); the typed `Publish` measured 0.34
  and 0.44 ns (OQ-70); the generic `Stream` depth-0 slowdown of decision 22 does not show ("Generic `Stream` at depth 0 is tracked for
  the release", above).
- **Found while reading the results.** Keryx's `IMediator` rows with a scope per request pay for the forwarding registration (OQ-74), and
  the behavior rows under NativeAOT for the type names in the log messages (OQ-75); both open then, measured as they are (since decided:
  owner decisions 35 and 36).

Folded into the ADR (status; Benchmarks), the brief (Phase 8), the README (Benchmarks) and whats-left.md.

### OQ-74: `IMediator`, `ISender` and `IPublisher` each take a scope entry (2026-11-27)

Decision 32's run ("Full benchmark run", at the end) measured Keryx's `IMediator` rows with a scope per request slower than its typed
overload: `RealWorldPingScopePerRequest` 80.4 ns against 54.3 ns (448 B both), `RealWorldPublish1ScopePerRequest` 219 ns and 920 B against
161 ns and 560 B, and `RealWorldPublish5ScopePerRequest` 634 ns and 2,136 B against 557 ns and 1,336 B (the notification-behavior pair
the same: 360 B and 800 B more). The generated typed and generic `Publish` both call the same private `__PublishN` (read in the emitted
host source), so the difference is in resolving the entry point: `AddKeryx()` registers `IMediator`, `ISender` and `IPublisher` each as a
scoped factory that returns `GetRequiredService<KeryxMediator>()`, and the container caches a scoped factory's result as an entry of its
own in the scope, so resolving `IMediator` runs a second factory and adds a second entry to the scope's resolved services, whose storage
grows when the count crosses a capacity boundary. A probe (a bare `ServiceCollection` with a scoped class, a scoped forwarding factory for
its interface and 1 or 5 more scoped services, Microsoft.Extensions.DependencyInjection 10.0.15, `GC.GetAllocatedBytesForCurrentThread`
over 1,000 scopes) measured 384 B per scope through the class and 744 B through the interface with 1 service, 840 B and 1,640 B with 5:
exactly the 360 B and 800 B above. With the forwarding factory registered transient, the interface measured 384 B and 840 B, the same as
the class. A scope that resolves fewer services (`RealWorldPingScopePerRequest`) or already crossed the boundary
(`RealWorldBehaviorsScopePerRequest`, 1,408 B both ways) shows only the time.

- **Interim:** unchanged: the three interfaces were scoped forwarding factories (ADR, the services `AddKeryx()` registers). The README
  reported the rows as measured and pointed here.
- **Proposal:** register `IMediator`, `ISender` and `IPublisher` as transient factories that return the scope's `KeryxMediator`
  (`TryAddTransient<IMediator>(static serviceProvider => serviceProvider.GetRequiredService<KeryxMediator>())`): still one instance per
  scope, because the factory only forwards to the scoped registration, and nothing new in the scope, because the container tracks a
  transient result only when it is disposable and `KeryxMediator` is not. A registration snapshot and an allocation test (resolve
  `IMediator` and `KeryxMediator` in new scopes and compare) would cover it; the ADR's registration table keeps "the scoped mediator",
  with a note that the interfaces forward to it.
- **Decision** (owner decision 35, 2026-11-27): adopt the proposal. `AddKeryx()` registers `IMediator`, `ISender` and `IPublisher` as
  transient factories that forward to the scoped `KeryxMediator`
  (`TryAddTransient<IMediator>(static serviceProvider => serviceProvider.GetRequiredService<KeryxMediator>())`, the same for `ISender` and
  `IPublisher`): still one mediator per scope, and no extra scope entry. Done in `HostEmitter.ServiceCollectionExtensions` (the three
  lines and the `AddKeryx()` doc summary); the 34 Verify snapshots that contain `AddKeryx()` change in those seven lines only.
  Tests: `HostRuntimeTests.AddKeryx_Registers_One_Scoped_Mediator_Behind_Every_Interface` checks the lifetimes (the three interfaces
  transient, the mediator and the handlers scoped, all factories), that `IMediator`, `ISender`, `IPublisher` and `KeryxMediator` resolve
  to the same instance in one scope whichever is resolved first and to a different one in another scope, and that resolving any of the
  three from the root provider still fails scope validation; `HostAllocationTests.Resolving_An_Interface_In_A_New_Scope_Allocates_The_Same_As_Resolving_The_Mediator`
  measures 1,000 new scopes that each resolve `KeryxMediator`, `IMediator`, `ISender`, `IPublisher` or all four, and then, as a real
  request scope does, scopes that resolve one of them and send a request or publish a notification whose handler takes a scoped service,
  and asserts that each row allocates what the same work starting from `KeryxMediator` allocates. Checked once with the three
  registrations reverted to `TryAddScoped` (both target frameworks): the rows that resolve a single interface and nothing else pass either
  way (376 B per scope, like `KeryxMediator`: the scope's resolved services grow only when their count crosses a capacity boundary, and
  two entries do not cross it), so they alone would not catch a return to scoped forwarders; the rows that fail are "all four" (736 B
  against 376 B) and every send and publish row, `IMediator` and `ISender` then `Send`, `IMediator` and `IPublisher` then `Publish` (808 B
  against 448 B). With the transient forwarders every row matches its baseline. A consequence for `ValidateOnBuild` was found while
  implementing this and is OQ-76 (open). The README's
  scope-per-request and `IMediator` rows are from decision 32's run, before this change; decision 37's run refreshes them.

Folded into the ADR (status; Injected parameters; DI registration), the brief (the host), the README (Hosts, Injected parameters,
Benchmarks notes) and whats-left.md.

### OQ-72: How the real-world benchmark scenarios set up each library (2026-11-27)

Decision 30 ("Real-world benchmark scenarios", below) names the scenarios and the subjects, but, as OQ-51 found for the first scenarios,
not the setup each number depends on: what the baseline does when the scenario has behaviors, the lifetimes of the new services, where
the SQLite connection comes from, how each library attaches the logging and validation behaviors and gets their services, how each
library publishes the notification scenarios (30d) sequentially and in parallel, and which subjects cannot run a scenario.

- **Interim** (`test/Keryx.Benchmarks/Comparison/RealWorldBenchmarks.cs` and `RealWorldPublishBenchmarks.cs`, `RealWorld.cs` and
  `RealWorldPublish.cs` in each project under `test/benchmark-subjects`, the shared services in `Keryx.Benchmarks.Common`):
    - **Messages.** Each subject declares `Ping`, `TypicalPing` and `QueryPing` (`sealed record` with an `int Id`) with its library's
      request interface; every library returns the same `Pong(int Id, string Name)` record from `Keryx.Benchmarks.Common`. The
      benchmarks send id 42.
    - **Scenarios.** `RealWorldPing` (a handler that returns a new `Pong`, no behaviors), `RealWorldBehaviors` (decision 30b) and
      `RealWorldSqlite` (30c) send through the entry point resolved once from the scope that lives for the whole benchmark, like the
      other comparison scenarios. `RealWorldPingScopePerRequest` and `RealWorldBehaviorsScopePerRequest` (30a, and 30a with 30b) create
      a scope from the container's `IServiceScopeFactory` (read once in setup, as ASP.NET Core holds it), resolve the entry point from it
      (Keryx's `KeryxMediator` or `IMediator`, Mediator's `Mediator` class or `IMediator`, MediatR's `IMediator`, Immediate.Handlers'
      generated `Handler`, ZeroAlloc.Mediator's `IMediator`, the baseline's handler class), send, await and dispose the scope inside the
      measured method; every one of those methods is `async`, so each row carries the same state machine. The SQLite scenario has no
      per-request variant: with the singleton connection below, it would add only the scope cost the `Ping` pair already shows.
    - **Logging behavior** (outermost). It injects `ILogger<TRequest>` and writes two source-generated messages per request
      (`RequestLog.Handling` with the request type's name, `RequestLog.Handled` with the `Pong`, formatted through its `ToString`; the
      subjects read the name with `typeof(TRequest).Name`, the baseline writes `nameof`, which differs under NativeAOT: OQ-75). The
      only provider is `InMemoryLoggerProvider` at `Information`: it calls the formatter for every message, as a console or file provider
      does, and keeps the last 1,024 messages in a ring buffer, so nothing grows across iterations. MediatR's license category stays
      filtered.
    - **Validation behavior** (inside logging). It injects `IValidator<TRequest>`, a hand-written interface in `Keryx.Benchmarks.Common`
      that returns an error message or `null`, calls it, and throws `ValidationException` on an error. Each subject's
      `TypicalPingValidator` checks the same rule (the id names a stored pong) and is registered scoped, the lifetime FluentValidation's
      assembly scan uses.
    - **Handler with a scoped repository.** `IPongRepository` (`PongRepository` in `Keryx.Benchmarks.Common`, registered scoped): a
      `Dictionary<int, string>` lookup over 100 pongs. Keryx's handler method injects it (the generated handler class is registered
      scoped by `AddKeryx()`); Mediator, MediatR and ZeroAlloc.Mediator take it in the constructor of a handler with the library's default
      lifetime (transient, scoped for Mediator scoped); Immediate.Handlers injects it as a `HandleAsync` parameter.
    - **How each library attaches the behaviors.** Keryx: `[Behavior]` on the handler method, the two static behavior classes ordered by
      `[BehaviorOrder(Phase.Observability)]` and `[BehaviorOrder(Phase.Validation)]`, injecting the services as behavior method
      parameters (held in the pipeline context). Immediate.Handlers: `[Behaviors]` on the handler class, services in the behavior
      constructors. Mediator scoped and MediatR: constrained open behaviors selected by `ILogged` and `IValidated` marker interfaces,
      listed before the pass-through ones, services in the constructors. ZeroAlloc.Mediator: two
      `[PipelineBehavior(Order = 100 and 101, AppliesTo = typeof(TypicalPing))]` classes with `where TState : IServiceProvider?` that
      resolve the logger and the validator from the state on every call, the library's documented way for a behavior to get services
      (its generated `IMediator` passes the provider it was resolved from).
    - **SQLite.** Microsoft.Data.Sqlite 11.0.0. A named shared-cache in-memory database (`Mode=Memory;Cache=Shared`) is created and seeded
      with 100 rows once per process by a connection that is never closed (`PongDatabase`). Each container registers a singleton
      `SqliteConnection` opened to it, and every handler runs the same query helper, `PongDatabase.FindNameAsync`: a new command,
      `SELECT name FROM pongs WHERE id = $id` with `AddWithValue`, `ExecuteScalarAsync`. A singleton open connection keeps the
      measurement about the mediator plus a realistic query, with no pool open per call; the benchmarks are single-threaded, so the
      shared connection is never used concurrently. SQLite's async methods complete synchronously. `Subjects` resolves every
      container's connection right after building the container (`RealWorldServices.OpenPongDatabase`), which also creates and seeds the
      database the first time, so no subject opens its connection or seeds the database inside a measured call (before, only the subjects
      whose handler class was resolved in setup had an open connection; the others opened it, and the first one seeded the database, in
      their first benchmark call).
    - **Baseline.** A hand-written handler class per request, registered scoped and called directly (from the long-lived scope, or
      resolved from a new scope per request). In the behavior scenarios it does the behaviors' work inline (the two log messages, the
      validator, the lookup), unlike the other scenarios' baseline, which never models behaviors (OQ-51): here the work is far larger than
      the mediator, so the ratio column shows the mediator's cost on top of the same work.
    - **Notification scenarios (30d).** Each subject with a publish API declares the notifications in `RealWorldPublish.cs`:
      `TypicalPinged1`/`TypicalPinged5` (handlers inject the scoped `IPongRepository` and `ILogger<TNotification>`), and
      `QueryPinged1`/`QueryPinged5` (handlers inject the singleton `SqliteConnection` and `ILogger<TNotification>`); Keryx adds
      `LoggedPinged1`/`LoggedPinged5` (the `TypicalPinged` handlers' twins, with `[Behavior(typeof(LoggingBehavior))]` on the type) and
      Keryx and ZeroAlloc.Mediator add `ParallelQueryPinged1`/`ParallelQueryPinged5` (their parallel strategy is an attribute on the
      type). Every handler is one call to the same body in `Keryx.Benchmarks.Common`, `PingedWork.Lookup` (look the pong up, write
      `RequestLog.Pinged`) or `PingedWork.QueryAsync` (await `PongDatabase.FindNameAsync`, write `RequestLog.Pinged`), returned in the
      library's handler shape (MediatR's `Task` through `AsTask()`, which allocates nothing for a completed `ValueTask`). The logger
      category is the notification type in every subject, as the request behaviors use the request type.
        - `RealWorldPublish1ScopePerRequest`/`5`: a scope per request, as the `Send` ones, around `Publish(TypicalPinged)`.
        - `RealWorldPublishBehavior1ScopePerRequest`/`5`: the same with the notification logging behavior: Keryx's `LoggingBehavior`
          also carries `[NotificationBehavior]`, a fully open method that injects `ILogger<TNotification>` and writes
          `RequestLog.Publishing` and `RequestLog.Published` around `next`. The baseline's `LoggedPinged` publisher writes the two messages
          inline.
        - `RealWorldPublishAsync1`/`5`: `Publish(QueryPinged)` from the long-lived scope with each library's default sequential strategy
          (Keryx sequential, Mediator's `ForeachAwaitPublisher`, MediatR's `ForeachAwaitPublisher`, ZeroAlloc.Mediator's sequential
          publish). The awaited operation is the SQLite query, not `Task.Yield`: the query is the realistic await, and a yield would
          measure thread-pool scheduling more than the mediator. Because SQLite completes synchronously, these rows measure each
          library's async-handler path on completed tasks, not overlapping I/O.
        - `RealWorldPublishParallel1`/`5`: the same handlers with each library's parallel strategy. Keryx: `[PublishStrategy(Parallel)]`
          on `ParallelQueryPinged`. Mediator: `NotificationPublisherType = typeof(TaskWhenAllPublisher)`, a generator option fixed per
          assembly (the generated wrapper holds the concrete publisher type), so it is a new subject project,
          `Keryx.Benchmarks.MediatorParallel`, with Mediator's default singleton lifetime and only the `QueryPinged` handlers; its rows
          are named `MediatorSingleton_*`. MediatR: `NotificationPublisherType = typeof(TaskWhenAllPublisher)` in a second container
          (`MediatRSetup.AddMediatRParallelBenchmarks`, the same handlers, no behaviors) publishing the same `QueryPinged` types.
          ZeroAlloc.Mediator: `[ParallelNotification]` on `ParallelQueryPinged` (its generated publish starts every handler, keeps the
          completed ones in place and awaits the rest with `Task.WhenAll`). The baseline starts every handler before awaiting any, with
          no failure aggregation (the floor); with one handler it is the sequential call.
        - Baseline: hand-written handler classes with the same bodies and a publisher class that takes them in its constructor
          (`TypicalPinged5Publisher` and so on, all scoped), resolved from the scope per request or once from the long-lived scope, and
          written as an application would (`async`, awaiting each handler).
        - ZeroAlloc.Mediator's static `Mediator` gets the `QueryPinged` and `ParallelQueryPinged` handlers, which have constructor
          dependencies, from the factories `ZeroAllocSetup.UseStaticFactories` registers, like the SQLite request handler.
    - **`--verify`.** The probe also counts validator calls, repository lookups, SQLite queries and formatted log messages, and a
      real-world benchmark's result is checked by its `Pong` (`ExpectAttribute.Result` for the id, `Name` for the name): `RealWorldPing`
      1 hit; `RealWorldBehaviors` 7 (2 behaviors, 2 messages, validator, handler, lookup; the baseline 5); `RealWorldSqlite` 2 (handler,
      query). A real-world notification handler makes 3 (itself, its lookup or query, its message), so the publish scenarios expect 3 and
      15 hits, the notification-behavior pair 6 and 18 (the behavior and its 2 messages; the baseline, which writes the messages inline,
      5 and 17). A parallel publish's handlers all complete synchronously here, so the counts are exact after the call.
    - **n/a cells**, each checked, not assumed:
        - Mediator singleton in both behavior scenarios: with the singleton lifetime Mediator registers handlers and behaviors as
          singletons, which cannot take the scoped repository and validator. Building that subject's container with the scenario
          (`ValidateOnBuild`) fails with "Cannot consume scoped service 'Keryx.Benchmarks.Common.IPongRepository' from singleton
          'Keryx.Benchmarks.MediatorSingleton.TypicalPingHandler'" (and the same for the validator in `ValidationBehavior`), so the subject
          leaves `TypicalPing` out.
        - ZeroAlloc.Mediator's static `Mediator` in both behavior scenarios. The generated static `Send(TypicalPing)` passes a literal
          `null` as the behaviors' `TState` (read in the generated source; the library's docs say the same), so this subject's behavior
          body, which resolves the logger and the validator from the state, gets no services. The library's
          static-path mechanism for behavior services is an `IPipelineBehaviorStateInitializer`, run by `PipelineBehaviorStateActivation`
          when the container is first used: it hands a process-wide bridge the container, and the bridge opens a new scope for each
          request that needs scoped services (as its validation bridge does). Using it would take a different behavior body
          (`state ?? the bridge's container`, plus that scope) and put a scope creation inside every call of the long-lived scenario,
          unlike every other subject. The mechanism `RealWorldSqlite` uses does not apply: `SetFactory` supplies a handler instance (the
          static `Send` would take a `TypicalPingHandler` factory too), but behaviors are static methods the generated code calls with
          the state, never created through a factory. Pointing a static field of our own at the long-lived scope would work, but it is a
          workaround outside the library's API, so the cell stays n/a.
        - ZeroAlloc.Mediator's static `Mediator` in every scope-per-request scenario (`RealWorldPingScopePerRequest`,
          `RealWorldBehaviorsScopePerRequest`, `RealWorldPublish1ScopePerRequest`/`5` and the behavior pair): the static class has no
          scope to resolve it from.
        - Mediator singleton in `RealWorldPublish1ScopePerRequest`/`5`: the same reason as for `TypicalPing`; its singleton
          notification handlers cannot take the scoped repository, so that subject leaves `TypicalPinged` out.
        - Every library but Keryx in the notification-behavior pair, each checked: Mediator's generated `Publish` goes straight to its
          notification publisher (a `TypicalPinged1` given the `ILogged` marker, which the open `LoggingBehavior` is constrained to,
          still published without it); MediatR's `Publish` hands the handlers to its `INotificationPublisher`, and its pipeline behaviors
          wrap `Send` only (decompiled `Mediator.PublishNotification`); ZeroAlloc.Mediator's generator ignores a
          `[PipelineBehavior(AppliesTo = typeof(TypicalPinged1))]` behavior (the build succeeds and the generated publish never calls
          it), its behaviors have the request shape `Handle<TRequest, TResponse, TState>`. A custom notification publisher is a strategy,
          not a behavior, so none is written.
        - Mediator scoped in the parallel pair: one Mediator assembly carries the `TaskWhenAllPublisher`, and its lifetime is the
          default, singleton.
        - Immediate.Handlers in every notification scenario: it has no notifications.
        - MediatR has no typed entry point and Immediate.Handlers no generic one, as in OQ-51.
    - **ZeroAlloc.Mediator's static path with a constructor dependency.** In `RealWorldSqlite` it gets the handler through the library's
      documented factory, `Mediator.Configure(c => c.SetFactory(...))` (`ZeroAllocSetup.UseStaticFactories`), which resolves the transient
      handler from the long-lived scope on every call. ZAM008 (a handler without a parameterless constructor) is suppressed in that
      project for this reason.
    - **Effect on the existing scenarios.** Every container now also registers logging, the repository and the connection (opened in
      setup), and every comparison benchmark's setup builds the two parallel-publish containers too, and the
      logging and validation behaviors join the Mediator scoped and MediatR open behavior lists (their marker constraints exclude every
      other request). The existing scenarios' setup is otherwise unchanged; their README tables were re-run at the commit that adds these
      scenarios (decision 32, "Full benchmark run", at the end).
- **Proposal:** confirm, and add these setup rules to the ADR's Benchmarks section beside OQ-51's, in particular the behavior-scenario
  baseline that does the behaviors' work inline, the singleton connection, the separate notification-behavior pair (so the per-request
  publish compares every library on the same work) and the Mediator parallel assembly. Alternative for the baseline: the handler alone, as in the
  other scenarios, which makes the ratio mostly the cost of logging. Needs a decision.
- **Decision** (owner decision 33, 2026-11-27): confirm the setup as recorded in the interim above, including the behavior-scenario
  baseline that does the behaviors' work inline, the singleton SQLite connection, the separate notification-behavior pair and the Mediator
  parallel project, and add the setup rules to the ADR's Benchmarks section beside OQ-51's. No code changed for it: the rules are in the
  ADR ("Real-world setup rules", after the first scenarios' rules), with the n/a cells and their reasons; the checks behind each n/a cell
  stay recorded here. One detail of the interim changed the same day: the logging behaviors and the baseline read the type name through
  `MessageName<T>` (owner decision 36, OQ-75, below) instead of `typeof(T).Name` and `nameof`.

Folded into the ADR (status; Benchmarks), the brief (Phase 8), the README (Benchmarks) and whats-left.md.

### OQ-73: The verification probe in NativeAOT benchmark builds (2026-11-27)

Every subject's handlers and behaviors call `Probe.Hit()` (`Keryx.Benchmarks.Common`), which counts calls only in a `--verify` run, where
an AppContext switch turns it on. Under the JIT, `Probe.Enabled` is a `static readonly` field that Tier 1 code reads as a constant, so in a
benchmark run `Hit()` compiles to nothing. The NativeAOT compiler has no tiers: it folds a `static readonly` field only when it can run the
class's static constructor at compile time, and `Probe`'s reads the AppContext switch, so a NativeAOT benchmark keeps a static-base
access, a field load and a branch in every handler and behavior call. Every subject makes the same calls in a scenario (the baseline
makes fewer in the behavior scenarios), but the cost is inside every NativeAOT number and grows with the depth. It has not been measured
(this step measured nothing), and the generated code was not inspected, so "not folded" follows from how the compiler preinitializes
statics, not from an observation.

- **Interim:** the probe stays as it is; the NativeAOT run (decision 29) includes it. The NativeAOT numbers decision 32's run put in
  the README (2026-11-27, "Full benchmark run" at the end) include it, and the README says so.
- **Proposal:** compile the probe out of the builds BenchmarkDotNet makes: an MSBuild property (for example
  `KeryxBenchmarksProbe=false`) that `Program.cs` sets in its own environment before BenchmarkDotNet builds, so its JIT and NativeAOT
  builds see it the way they see `KeryxDispatchPadding`, under which `Probe.Enabled` is a `const` `false`; `--verify`, under the JIT and
  in the NativeAOT publish, keeps the switch. Alternative: measure first (the depth-8 scenarios under NativeAOT with and without the
  property) and keep the probe if the difference is within noise. Needs a decision.
- **Decision** (owner decision 34, 2026-11-27): adopt the proposal: compile the probe out of the builds BenchmarkDotNet makes (JIT and
  NativeAOT); `--verify`, under the JIT and in the NativeAOT publish, keeps the AppContext switch. Done:
    - **The property.** `Keryx.Benchmarks.Common.csproj` defines `KERYX_BENCHMARKS_NO_PROBE` when `KeryxBenchmarksProbe` is `false`;
      under it `Probe.Enabled` is `public const bool Enabled = false` and `Probe.Hit()` has an empty body (`#if`, so the probe-on build is
      unchanged). `Probe.EnsureCounting()` replaces `Verification`'s read of `Probe.Enabled`: a `const` read there would make the rest of
      the method unreachable (CS0162, an error here), and it fails a verify run of a probe-less build with a message saying so (checked:
      `KeryxBenchmarksProbe=false dotnet run -- --verify` throws "This build has no verification probe").
    - **Setting it.** `Program.cs` calls `Environment.SetEnvironmentVariable("KeryxBenchmarksProbe", "false")` just before
      `BenchmarkSwitcher` runs, not in `--verify` or `--prototype-disasm`; BenchmarkDotNet's `dotnet restore`, `build` and `publish`
      inherit the environment, where MSBuild reads it as a property, as with `KeryxDispatchPadding`. BenchmarkDotNet 0.15.8 passes
      `ArtifactsPath` and `OutDir` to both toolchains' builds (its build log shows them), so the rebuilt Common, subject and benchmark
      projects go to the generated project's folder, never over the runner's own binaries, which stay probe-on (checked: the runner's
      `bin/Release/net10.0/Keryx.Benchmarks.Common.dll` and its `obj` copy have the same modification time before and after a Dry run).
    - **No probe in the benchmark builds** (checked with `--keepFiles`). JIT (`--filter '*RealWorldPing*' --job Dry`): in the generated
      project's `Keryx.Benchmarks.Common.dll`, `Enabled` is `.field public static literal bool Enabled = bool(false)`, `Hit` is a single
      `ret` and `Probe` has no static constructor; the runner's own build has `static initonly bool Enabled`, a `Hit` that loads it and
      branches around `Interlocked.Increment`, and a `.cctor` that reads the switch. NativeAOT (`--runtimes nativeaot10.0`, the same filter,
      with `StripSymbols=false` and `IlcGenerateMapFile=true` in the environment, against the `--verify` publish built with the same two
      properties): in the `--verify` binary `Probe` has a static constructor and a `__GetNonGCStaticBase` helper, and the baseline's
      `PingHandler.Handle` (128 B of code) starts with the class-initialization check (a load and a branch to that helper), then loads
      `Enabled` and branches around an atomic add; in the benchmark binary neither the constructor nor the helper exists, and
      `PingHandler.Handle` is 80 B: the `Pong` allocation and the return.
    - **`--verify` still counts.** JIT net10.0 and net11.0: 238 passed; the NativeAOT publish: 215 passed, 23 n/a. Full Dry runs pass:
      JIT 271 benchmarks, NativeAOT 242.
    - **Enforced on every run** (review finding, after the inspection above). The inspection proves one build; a BenchmarkDotNet
      upgrade, an MSBuild change or a toolchain that reuses the runner's probe-on binaries would bring the probe back silently, since the
      probe-on build runs the same benchmarks without error. `Probe.IsCompiledOut()` (a method, not a `const`, so its callers have no
      unreachable code) is true only under `KERYX_BENCHMARKS_NO_PROBE`, and `ComparisonBenchmark.Setup()`, the `[GlobalSetup]` every
      comparison benchmark shares, throws `InvalidOperationException` ("This benchmark build carries the verification probe;
      KeryxBenchmarksProbe=false did not reach BenchmarkDotNet's build.") unless it is, so both CI Dry jobs (JIT and
      `--runtimes nativeaot10.0`) now enforce the guarantee on every pull request. `--verify`, which runs the probe-on build, calls
      `CreateSubjects()` (what `Setup()` does after the check) instead. `--inProcess` measures the runner's own probe-on build, so it fails
      too, deliberately: its numbers would include the probe. Checked: full Dry runs pass (JIT 271 benchmarks, NativeAOT 242), `--verify`
      passes (238 under the JIT, net10.0 and net11.0; 215 and 23 n/a in the NativeAOT publish), and `--filter '*RealWorldPing.*' --job Dry
      --inProcess` fails with that message.

    Nothing was measured: the README's NativeAOT numbers are still decision 32's, which include the probe, until decision 37's run.

Folded into the ADR (status; Benchmarks), the brief (Phase 8), the README (Benchmarks) and whats-left.md.

### OQ-75: Type names in the benchmark behaviors' log messages under NativeAOT (2026-11-27)

Every subject's real-world logging behavior reads the message type's name with `typeof(TRequest).Name` (Keryx's notification behavior
with `typeof(TNotification).Name`) for its two messages, while the baseline, which does the behaviors' work inline (OQ-72), writes
`nameof(TypicalPing)`. Under the JIT a type's name is cached and the read allocates nothing; under NativeAOT every read allocates a new
string: a probe (`typeof(TypicalPing).Name` in a shared generic method, 1,000 reads) measured 0 B per read under the JIT and 48 B
published with NativeAOT. Two reads per request or publish make Keryx's NativeAOT behavior rows 96 B larger than its JIT rows in decision
32's run (`RealWorldBehaviors` 520 to 616 B, `RealWorldBehaviorsScopePerRequest` 1,408 to 1,504 B,
`RealWorldPublishBehavior1ScopePerRequest` 712 to 808 B), and the same reads are in the other libraries' behavior rows, but not in the
direct call's. Under NativeAOT, those rows' distance from the direct call therefore includes two string allocations that belong to the
benchmark code, not to the library.

- **Interim:** as measured; the README's NativeAOT section says so.
- **Proposal:** read the name once per type, the same way in every subject and in the baseline: a `MessageName<T>` static class in
  `Keryx.Benchmarks.Common` (`public static readonly string Value = typeof(T).Name;`) that every logging behavior, and the baseline with
  its concrete types, passes to `RequestLog`, so no row allocates for it under either runtime; then re-run the behavior rows under
  NativeAOT. Alternative: keep `typeof(T).Name` and read it in the baseline too, so every row pays the same. Needs a decision.
- **Decision** (owner decision 36, 2026-11-27): adopt the proposal. `MessageName<T>` (`Keryx.Benchmarks.Common/MessageName.cs`,
  `public static readonly string Value = typeof(T).Name`) is what every real-world logging behavior passes to `RequestLog`: Keryx's request
  and notification behavior methods, Mediator scoped's, MediatR's, Immediate.Handlers' and ZeroAlloc.Mediator's
  (`MessageName<TRequest>.Value`, `MessageName<TMessage>.Value`, `MessageName<TNotification>.Value`), and the baseline with its concrete
  types (`MessageName<TypicalPing>.Value` in `TypicalPingHandler`, `MessageName<TypicalPinged1>.Value` and
  `MessageName<TypicalPinged5>.Value` in the two logged publishers), so no row allocates for the name under either runtime. The strings
  are the same as before, so `--verify`'s results and hit counts are unchanged (238 passed under the JIT, 215 and 23 n/a in the NativeAOT
  publish). Nothing was measured; decision 37's run re-measures the behavior rows.

Folded into the ADR (status; Benchmarks), the README (Benchmarks) and whats-left.md.
