# Keryx

A source-generated mediator for .NET. Handlers are plain `private static` methods on `partial` classes, and a Roslyn generator in the
same package emits all dispatch and pipeline code at compile time: no reflection, no delegate chains, no closures, nothing allocated
per call by the mediator itself, and safe for Native AOT and trimming.

> **Status:** early development, not yet on nuget.org. The design is recorded in [ADR 0001](docs/adr/0001-keryx-mediator.md); the
> decisions behind it, and any undecided points with their interim behavior, are in [open-questions.md](docs/adr/open-questions.md); what
> is left before a first release is in [docs/whats-left.md](docs/whats-left.md).

- [Install](#install)
- [Quick start](#quick-start)
- [Requests and handlers](#requests-and-handlers)
- [Testing handlers](#testing-handlers)
- [Behaviors](#behaviors)
- [Streaming](#streaming)
- [Notifications](#notifications)
- [Hosts](#hosts)
- [Contracts and handler libraries](#contracts-and-handler-libraries)
- [Diagnostics](#diagnostics)
- [Benchmarks](#benchmarks)
- [Building](#building)

## Install

```bash
dotnet add package Keryx
```

One package holds everything: `Keryx.dll` (the public types, for `netstandard2.0`, `net10.0` and `net11.0`) and the generators,
analyzers and code fixes (`analyzers/dotnet/cs`).

**Requirements**

- **The .NET 10 SDK (Roslyn 5.0) or later**, whatever framework your project targets. The generator does not load in older compilers.
- **C# 14 or later** (the .NET 10 SDK's default) in every project that references Keryx, whatever framework it targets. The package sets
  `LangVersion` to `latest` when your project leaves it empty; a `LangVersion` below 14 that you set yourself is error [KRX022](#krx022).
- Requests, notifications and handler libraries may target `netstandard2.0`. A **host** (the project with the mediator: by default an
  executable, test project or Android app, see [Hosts](#hosts)) targets `net10.0` or later ([KRX031](#krx031)); one that targets an
  older framework is not a host by default.
- The `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions` (8.0.x for `netstandard2.0`, 10.0.x for
  `net10.0`), which the generated host code uses. On `net11.0` the assembly comes from the shared framework, so that target lists no
  package dependency.
- Reference the `Keryx` package directly from every project that declares requests or handlers, and from every host. A project that
  gets the package only transitively behaves differently by route. Through a `ProjectReference` to a project that references Keryx, the
  generators and analyzers still run unless that reference sets `PrivateAssets`. Through a package that depends on Keryx, they do not
  flow (NuGet packs the dependency with `exclude="Build,Analyzers"`). In both cases the package's `buildTransitive` files apply the
  same build defaults (`LangVersion`, `KeryxHost`).

## Quick start

```csharp
// Program.cs
using Keryx;
using Microsoft.Extensions.DependencyInjection;
using MyApp; // the host's root namespace, where AddKeryx() is generated

// An executable that references Keryx is a host: it gets the generated KeryxMediator and AddKeryx().
var services = new ServiceCollection();
services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
services.AddKeryx();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

Guid id = await mediator.Send(new CreateOrder.Command("apple", 3));
```

```csharp
// CreateOrder.cs
using Keryx;

namespace MyApp;

public partial class CreateOrder
{
    public sealed record Command(string Sku, int Quantity) : IRequest<Guid>;

    private static async ValueTask<Guid> Handle(Command request, IOrderRepository orders, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await orders.Add(id, request.Sku, request.Quantity, cancellationToken);
        return id;
    }
}
```

The generator writes the other half of `CreateOrder`: a constructor that takes `IOrderRepository`, a public `InvokeAsync` that runs the
handler, and the metadata a host reads. In the host it writes `KeryxMediator` (with a typed `Send(CreateOrder.Command)` overload) and
`AddKeryx()`, which registers the mediator and the handler class with generated factories. Behaviors are static methods, so there is
nothing to register for them.

## Requests and handlers

A **request** implements exactly one of `IRequest<TResponse>`, `IRequest` (no response; it is `IRequest<Unit>`) or
`IStreamRequest<TItem>`. A **notification** implements `INotification`. Requests may be classes, records or structs; they must not be
generic, nested in a generic type or `ref struct`s ([KRX012](#krx012)). A request class or record is `sealed` ([KRX024](#krx024), with a
code fix); a struct always is. A family of requests is an abstract (or C# 15 `closed`) base with sealed cases, and each case has its own
handler; the base has none ([KRX003](#krx003) for a handler that takes it):

```csharp
public abstract record Command : IRequest;

public sealed record Ship(Guid Order) : Command;
public sealed record Cancel(Guid Order) : Command;
```

Dispatch is by exact runtime type: a handler never receives another request type. A host composes only sealed request types: one that
is not sealed (KRX024 turned off) is not dispatched, and a referenced one is the host error [KRX038](#krx038). Notifications need not be
sealed (see [Notifications](#notifications)).

A **handler** is a method named `Handle` (or `HandleAsync`) with the request as a parameter:

- it is `private static` ([KRX002](#krx002), with a code fix);
- it returns `ValueTask<T>`, `Task<T>` or `T` for `IRequest<T>`; also `ValueTask`, `Task` or `void` for `IRequest`;
  `IAsyncEnumerable<T>` for `IStreamRequest<T>`; `ValueTask` or `Task` for a notification;
- it has exactly one request or notification parameter ([KRX003](#krx003)), at most one `CancellationToken` and no `ref`, `in`, `out`
  or `params` parameters ([KRX013](#krx013)). Every other parameter is **injected** from the container.

The **handler class** contains exactly one handler ([KRX006](#krx006)), is `partial` all the way out, and is `public` outside a host
([KRX004](#krx004), with a code fix). It declares no instance constructor ([KRX007](#krx007)); Keryx generates one for the injected
parameters. A `static partial` class is fine when nothing is injected ([KRX014](#krx014)). Group handlers with nested classes:

```csharp
public static partial class Orders
{
    public partial class Create
    {
        public sealed record Command(string Sku, int Quantity) : IRequest<Guid>;

        private static ValueTask<Guid> Handle(Command request, IOrderRepository orders, CancellationToken ct) => orders.Create(request, ct);
    }

    public partial class Cancel
    {
        public sealed record Command(Guid Id) : IRequest;

        private static Task Handle(Command request, IOrderRepository orders, CancellationToken ct) => orders.Cancel(request.Id, ct);
    }
}
```

Each request has one handler in the whole application, in the request's own assembly ([KRX005](#krx005) within it, [KRX023](#krx023)
elsewhere): a request without one is [KRX001](#krx001) in its assembly, hosts included (code fix: add a handler stub), and a handler of a
request declared in another assembly is [KRX023](#krx023). Notifications may be handled in any assembly.

**Injected parameters** are resolved by generated code, never by reflection:

| Parameter                                 | Resolved with                                                                       |
| ----------------------------------------- | ----------------------------------------------------------------------------------- |
| `T`                                       | `GetRequiredService<T>()`                                                           |
| `T` with a default value                  | `GetService<T>()`, falling back to the default                                      |
| `[FromKeyedServices("key")] T`            | `GetRequiredKeyedService<T>("key")`; the key is a constant ([KRX018](#krx018))      |
| `IServiceProvider`                        | the scope's provider itself, never resolved (keyed: an ordinary keyed service)      |
| `IMediator`, `ISender`, `IPublisher`      | the scoped mediator (the interfaces forward to it)                                  |
| `CancellationToken`                       | the token of the call                                                               |

## Testing handlers

Every handler class gets a public **invoker**, `InvokeAsync`, which runs the handler and nothing else: no behaviors, no container.
Unit tests construct the class with fakes and call it:

```csharp
var handler = new CreateOrder(new FakeOrderRepository());
var id = await handler.InvokeAsync(new CreateOrder.Command("apple", 3), CancellationToken.None);
```

A handler with nothing injected gets a `static InvokeAsync`. To test whole pipelines (behaviors, notification fan-out), reference Keryx
from the test project, which makes it a host (a test project that targets `net10.0` or later is one by default), call `AddKeryx()` on a
`ServiceCollection` and send through the mediator. A test host can reference the app host.

A test swaps what a **request handler** does through the services it injects: it registers fakes for them (after the app's own
registrations, if it reuses them: with Microsoft's container the last registration wins), and the handler's generated factory resolves
the fakes.

```csharp
var services = new ServiceCollection();
services.AddKeryx();
services.AddSingleton<IOrderRepository, FakeOrderRepository>();
```

A request handler cannot be excluded or replaced: it lives in its request's assembly, a host's handler of a referenced request is
[KRX023](#krx023), and `ExcludeHandler` naming one is [KRX039](#krx039). A **notification handler** can be dropped with
`[assembly: ExcludeHandler(typeof(TheHandler))]`, and the test host may handle the notification with its own.

## Behaviors

A **behavior** wraps a pipeline. It is a `static` class marked with one or more **kind attributes**, and for each kind it declares one
`static` behavior method named `Handle` (or `HandleAsync`):

| Kind attribute           | Wraps                                                          | Open method's message type         | `next`                       | Returns                   |
| ------------------------ | -------------------------------------------------------------- | ---------------------------------- | ---------------------------- | ------------------------- |
| `[RequestBehavior]`      | one `Send`                                                     | `TRequest : IRequest<TResponse>`   | `struct, INext<TResponse>`   | `ValueTask<TResponse>`    |
| `[StreamBehavior]`       | the inner stream of one `Stream` (see [Streaming](#streaming)) | `TRequest : IStreamRequest<TItem>` | `struct, IStreamNext<TItem>` | `IAsyncEnumerable<TItem>` |
| `[NotificationBehavior]` | one whole `Publish` (see [Notifications](#notifications))      | `TNotification : INotification`    | `struct, INext`              | `ValueTask`               |

```csharp
[RequestBehavior]
[BehaviorOrder(Phase.Observability)]
public static class Audit
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
        TRequest request, TNext next, IAuditLog log, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
        where TNext : struct, INext<TResponse>
    {
        var response = await next.InvokeAsync(cancellationToken);
        log.Record(request, response);
        return response;
    }
}

[RequestBehavior]
[BehaviorOrder(Phase.Validation)]
public static class CheckQuantity
{
    public static ValueTask<Guid> Handle<TNext>(CreateOrder.Command request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<Guid>
        => request.Quantity > 0 ? next.InvokeAsync(cancellationToken) : throw new ArgumentOutOfRangeException(nameof(request));
}
```

`next` is a generated struct, and the generated pipeline calls each behavior method directly, with explicit type arguments: no behavior
instance, no delegate, no closure and no virtual or generic virtual call per step. Because `next` is a struct type argument rather than
a delegate, each behavior method is compiled for its own link and the JIT can inline `next.InvokeAsync`. A behavior may call `next` more than once (retry) or
not at all (short-circuit). A request with no response is `IRequest<Unit>`, so a request behavior sees `TResponse = Unit`.

**Behavior methods.** The message, `next` and an optional `CancellationToken` are found by type, in any order; every other parameter is
**injected** like a handler's (keyed and default-valued forms included). The host resolves those services once per scope into the
pipeline context, so no behavior instance is ever created (a `[StreamBehavior]` method's own iterator is still allocated per `Stream`
call; see [Streaming](#streaming)). The context holds each service once, however many behavior methods of the pipeline inject it (same
type and same key; a keyed and an unkeyed `IClock` are two services): it is resolved as required when any of them requires it, and
otherwise once as optional, with each method's own default applied when it is missing. So **a transient service that several behaviors
of one pipeline inject is one instance per scope and pipeline**, shared by those behaviors, not one per behavior. An unkeyed
`IServiceProvider` is the scope's provider itself, which the context holds in its own field only when a behavior method injects it. The
context's fields are named by service type and key, not by where they are first used, so reordering behaviors or their parameters
does not rename them. A method is either:

- **open:** generic in the message, its response (or item) and `TNext`, with the message type parameter constrained to the kind's
  message interface (table above) or to an interface that derives from it: `where TRequest : ICommand<TResponse>` with
  `ICommand<T> : IRequest<T>` counts and limits the method to commands, as `where TNotification : IDomainEvent` limits a notification
  behavior to domain events. Further constraints narrow where it applies. The response (or item) type parameter may be written
  annotated, `TResponse?`, in `next`, the return type and the message constraint: the method then fits responses that are annotated
  (`string?`), declared nullable-oblivious, or value types (`int`; with `where TResponse : struct`, `TResponse?` is `Nullable<TResponse>`
  and fits only `int?`), but not a non-annotated `string`; or
- **closed:** generic only in `TNext`, over one concrete message type, with `next` carrying exactly that message's response or item
  type (`INext<Unit>` for an `IRequest`).

A class that is not `static` or is generic, a kind attribute without a matching method, two methods of one kind, a partially open
method or one with other type parameters, an open method without the message constraint (or constrained to an unrelated interface), a
wrong `next` or return type, or a `static`
`Handle`/`HandleAsync` whose kind the class does not carry, that has no valid `next`, or whose `next` and message type belong to
different kinds is [KRX019](#krx019). One class may carry several kind attributes; when two of its methods would have the same parameter
list, name one `Handle` and the other `HandleAsync`. Outside a host the class and method are `public`; in a host `internal` is enough
([KRX016](#krx016)).

**Attaching.** A behavior applies through any of three scopes, and the union (without duplicates) runs. `typeof(X)` always names the
behavior class; in a pipeline it means X's method of that pipeline's kind.

- per target: `[Behavior(typeof(Audit))]` on a request or stream handler method, or, for a `[NotificationBehavior]`, on the
  notification type. `[Behavior]` on a notification handler method has no effect ([KRX042](#krx042)): notification handlers have no
  pipelines of their own;
- per assembly: `[assembly: Behavior(typeof(Transaction))]` applies to every matching target in that assembly (for a notification
  behavior, the notification types declared there);
- global: `[GlobalBehavior]` on the behavior class applies each of its behaviors to every matching target in every host that references
  it; on a behavior method, only that method's kind is global. A host removes one with
  `[assembly: ExcludeBehavior(typeof(Audit))]`, which removes the class from pipelines of every kind; Info [KRX035](#krx035) lists every
  global behavior a host applies, once per kind.

An open method applies where its constraints hold for the target's types, nullable annotations included; a closed one where its message
and response (or item) types are identical to the target's. Assembly and global behaviors that do not fit are skipped; one attached explicitly that does not fit is
[KRX009](#krx009).

**Ordering.** Lower runs first (further out). Behaviors sort by `[BehaviorOrder(Phase)]` (`Outermost` 0, `Observability` 100, `Security`
200, `Validation` 300, `Default` 400, `Caching` 450, `Transaction` 500, `Innermost` 600; any value in between works), then by its
`Order`, then by `[RunsAfter(typeof(X))]`/`[RunsBefore(typeof(X))]` edges, then by class name. These attributes go on the behavior class;
the same attribute on a behavior method overrides the class's for that method's kind. A cycle, or an edge that contradicts phase or
order, is [KRX008](#krx008).

## Streaming

A stream request's handler returns `IAsyncEnumerable<TItem>`, usually as an `async` iterator with `[EnumeratorCancellation]` on its token:

```csharp
public partial class ListOrders
{
    public sealed record Query : IStreamRequest<Order>;

    private static async IAsyncEnumerable<Order> Handle(Query request, IOrderRepository orders, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in orders.All(ct))
        {
            yield return order;
        }
    }
}

await foreach (var order in mediator.Stream(new ListOrders.Query(), ct)) { /* ... */ }
```

With no stream behaviors, `Stream` returns the handler's enumerable as is. A `[StreamBehavior]` wraps the stream: its method gets the
inner stream from `next.InvokeAsync(ct)` and returns the stream the caller (or the next behavior out) sees, typically by re-yielding it
from its own `async` iterator. It can filter, map, buffer, catch, retry by calling `next` again, or serve items without calling `next`:

```csharp
[StreamBehavior]
public static class SkipNulls
{
    public static async IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(
        TRequest request, TNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TItem>
        where TNext : struct, IStreamNext<TItem>
    {
        await foreach (var item in next.InvokeAsync(cancellationToken))
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }
}
```

Per `Stream` call, Keryx allocates one iterator per stream behavior (its own), nothing per item, and one linked
`CancellationTokenSource` only when both the `Stream` token and the `WithCancellation` token can be cancelled and differ. A null or
unknown stream request throws from the `Stream()` call itself, before any behavior runs.

## Notifications

A notification has zero or more handlers, in any assembly the host references:

```csharp
public sealed record OrderPlaced(Guid Id) : INotification;

public partial class SendConfirmation
{
    private static ValueTask Handle(OrderPlaced notification, IEmailSender email, CancellationToken ct) => email.Confirm(notification.Id, ct);
}

public partial class AuditEverything
{
    [HandlerOrder(-10)]
    private static ValueTask Handle(INotification notification, IAuditLog audit) => audit.Write(notification); // every notification
}

await mediator.Publish(new OrderPlaced(id), ct);
```

- A handler whose parameter is a base type or interface (or `INotification` itself) receives every notification type the host knows
  that converts to it. A type the host does not know reaches no handler.
- Handlers run in `[HandlerOrder]` order (default 0), then by class name.
- Publishing is sequential by default. `[PublishStrategy(PublishStrategy.Parallel)]` on the notification type starts every handler
  before awaiting any.
- Every handler runs, whatever the others do. If any fail, `Publish` throws one `AggregateException` with every failure in handler
  order, even for a single failure. Cancelling the token does not skip handlers; each sees the cancelled token.
- A `[NotificationBehavior]` wraps the whole publish, handlers and strategy included, once per `Publish`, also when there are no
  handlers. Attach it with `[Behavior]` on the notification type, `[assembly: Behavior]` in the notification type's assembly, or
  `[GlobalBehavior]`; its `TNotification` is the concrete published type. Handlers have no behaviors of their own: they are called
  directly under either strategy.
- A struct notification delivered to a handler typed as an interface is boxed once per `Publish` (Info [KRX037](#krx037)).

```csharp
[NotificationBehavior]
public static class CountPublishes
{
    public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IPublishCounter counter, CancellationToken ct)
        where TNotification : INotification
        where TNext : struct, INext
    {
        counter.Increment(typeof(TNotification).Name);
        return next.InvokeAsync(ct);
    }
}

[Behavior(typeof(CountPublishes))]
public sealed record OrderShipped(Guid Id) : INotification;
```

## Hosts

A project is a **host** when its `KeryxHost` MSBuild property is `true`. The package sets it for you when the project leaves it empty:
executables (`OutputType` `Exe` or `WinExe`), test projects and Android apps (`AndroidApplication`) that target `net10.0` or later are
hosts, and everything else is not: class libraries, and any project that targets an older framework (`net8.0`, .NET Framework). So a
`net8.0` app or test project that uses a Keryx library builds without being a host, and a test project that multi-targets
`net8.0;net10.0` is a host in its `net10.0` build only. Set the property yourself to change that:

```xml
<PropertyGroup>
    <!-- A class library that hosts the mediator; or false in an executable or test project that is not a host. -->
    <KeryxHost>true</KeryxHost>
</PropertyGroup>
```

A host needs `net10.0` or later: setting `<KeryxHost>true</KeryxHost>` in a project below it is [KRX031](#krx031).

Host diagnostics about something without a source location (a referenced
type, the target framework) are reported without a location, so their severity is set for the whole project (a `.globalconfig` or
`<NoWarn>`), not in an `.editorconfig` section.

The host composes every pipeline at compile time from its
own handlers and from the handlers, behaviors and notification types of every assembly it references, and generates, in its root
namespace:

- `internal sealed partial class KeryxMediator : IMediator`, with `ISender.Send`, `ISender.Stream` and `IPublisher.Publish`, plus a typed
  overload for each request and notification (`ValueTask<Guid> Send(CreateOrder.Command)`), which skips type dispatch;
- `internal static IServiceCollection AddKeryx(this IServiceCollection services)`, which registers `KeryxMediator` (scoped), `IMediator`,
  `ISender` and `IPublisher` (transient factories that return the scope's `KeryxMediator`, so all four resolve to one instance per scope
  and resolving an interface adds nothing to the scope; open-questions OQ-74) and every handler class with injected parameters (scoped),
  all with generated factories and `TryAdd`. It registers no behavior types: behaviors are static. Because the interfaces are factories,
  `ValidateOnBuild` does not report a singleton that injects `IMediator`, `ISender` or `IPublisher` (MediatR's transient `IMediator`
  behaves the same); with `ValidateScopes` on, as in ASP.NET Core's Development environment, that singleton fails the first time it is
  resolved ("Cannot resolve scoped service '…KeryxMediator' from root provider"). Inject the mediator into scoped or transient services,
  or create a scope (open-questions OQ-76).

The mediator is **scoped**. It creates one pipeline context per pipeline once per scope, lazily, holding the handler instance and the
services its behavior methods inject; it never creates a behavior instance. A request or stream pipeline with a static handler whose
behaviors inject nothing, and a notification type whose handlers are all static (or that has none) and whose behaviors inject nothing, have
no context at all (open-questions OQ-65 and OQ-70): such a pipeline's entry calls the outermost behavior method, or the handler, or for a
notification its static publish strategy, directly, and each `next` link holds only the request or notification. From then on a `Send`
allocates nothing. Singletons and hosted services that need it create a scope with `IServiceScopeFactory`. A handler's or behavior method's
transient dependencies therefore live as long as the scope, and the behaviors of one pipeline that inject the same transient share one
instance.

The generic `Send<TResponse>(IRequest<TResponse>)` dispatches on the exact runtime type: a switch statement with a type pattern per
sealed request type (`case Ping r: return ...;`, one method-table compare each), and above 32 request types a `FrozenDictionary` lookup
of `request.GetType()` and a jump table. It never boxes the response, and every section returns its handler's pipeline as a tail call.
A struct request is boxed when sent through `ISender`; the typed overload avoids it (Info [KRX021](#krx021)). An unknown request type
throws `KeryxHandlerNotFoundException`: dispatch never falls back to a base type's handler. Generic `Stream` dispatches the same way.
Generic `Publish` compares `notification.GetType()` with each notification type instead (notification types need not be sealed), so a
notification type the host does not know (even one derived from a type it does) runs nothing; polymorphic handlers receive only the
types the host knows.

Hosts are Native AOT and trimming safe: CI publishes the [sample host](samples/hosts/Keryx.Samples.Host) with `PublishAot=true`, fails
on any trim or AOT warning and runs it.

The generated sources are readable C#, written as text by the generators (nothing reformats them afterwards). On a host of 300
requests with five behaviors each, 30 stream requests and 30 notifications, the host generator takes about 50 ms of a build and the
handler generator about 30 ms, and neither runs again on an edit that changes no handler, behavior or message type (open-questions
OQ-71).

## Contracts and handler libraries

Requests, notifications and handlers can live in any project that references `Keryx`, including `netstandard2.0` libraries. Each such
assembly carries a **manifest** in its metadata (`[assembly: KeryxManifest]` and one `[KeryxHandler]` attribute per public handler class),
which every host that references it reads, whether the reference is a project or a package.

- A request or stream request is declared in the same assembly as its handler. KRX001 checks that every request has one there, in
  libraries and hosts alike, and a handler of a request from another assembly is [KRX023](#krx023).
- A **contracts assembly** declares notifications (and shared types) that other assemblies handle; a notification may be handled in any
  assembly. It declares no requests.
- A **handler library** declares its requests with their `public` handler classes, may handle notifications declared elsewhere, and may
  declare `[GlobalBehavior]`s that every referencing host applies.
- In the host, `[assembly: ExcludeHandler(typeof(X))]` drops a referenced notification handler, which the host may replace with its own.
  It names only notification handlers: a request or stream request handler, referenced or the host's own, is [KRX039](#krx039) and stays
  (a host's handler of a referenced request would be KRX023); swap what it does through the services it injects ([Testing
  handlers](#testing-handlers)). A referenced request the host has no handler for is [KRX030](#krx030); a `.globalconfig` (or
  `<NoWarn>`) can lower KRX030 for the whole host.
- A host referenced by another host (a test project referencing the app) gets only the handlers the referenced host keeps `public`; the
  referencing host reports KRX030 for every request of the referenced host without such a handler.
- `ExcludeHandler` and `ExcludeBehavior` are read only by a host; anywhere else they are [KRX043](#krx043).

See [samples/libraries](samples/libraries) (a `netstandard2.0` contracts library with the order notifications, and a handler library
with the order requests, their handlers, a global behavior and notification handlers) and [samples/hosts](samples/hosts).

## Diagnostics

Every diagnostic links here. KRX001, KRX005 (within one assembly), KRX008 (within one assembly) and KRX022 need the whole compilation:
the IDE shows them with full-solution analysis on, and every build reports them. KRX010 and KRX020 are retired.

| ID                             | Severity | Meaning                                                                                                                                                        |
| ------------------------------ | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| <a id="krx001"></a>KRX001      | Error    | A request or stream request has no handler in its assembly (in every assembly, hosts included). Code fix: add a handler stub.                                                               |
| <a id="krx002"></a>KRX002      | Error    | A handler is not `private static`, or returns a type its request kind does not allow (including `async void`). Code fix: make it `private static`.           |
| <a id="krx003"></a>KRX003      | Error    | A handler has no request or notification parameter, more than one, or a request parameter typed as `IRequest<T>`, an abstract or closed request base or an interface. |
| <a id="krx004"></a>KRX004      | Error    | A handler class or a containing type is not `partial`, is file-local, is not `public` outside a host (narrower than `internal` in a host), or is not a class. Code fix: add `partial` (and `public`). |
| <a id="krx005"></a>KRX005      | Error    | A request has two handlers in one assembly. A request's handler lives in its request's assembly ([KRX023](#krx023)), so there is no check across assemblies. |
| <a id="krx006"></a>KRX006      | Error    | A class directly contains more than one handler.                                                                                                              |
| <a id="krx007"></a>KRX007      | Error    | A handler class declares an instance or primary constructor, or has injected parameters and is `abstract`, has `required` members, or has a base type without an accessible parameterless constructor. |
| <a id="krx008"></a>KRX008      | Error    | Behavior ordering has a cycle, or `RunsAfter`/`RunsBefore` contradicts phase or order. Reported once per pair of behavior classes (and per kind where a method overrides the class's ordering). |
| <a id="krx009"></a>KRX009      | Error    | A behavior attached with `[Behavior]` to a request or stream handler method or a notification type does not fit it: not a behavior class, no behavior method of the target's kind, constraints that do not hold (nullable annotations included, such as a `TResponse?` method against a `string` response), or a closed method over other types. The host reports it for behaviors a referenced handler or notification type attaches. |
| <a id="krx011"></a>KRX011      | Error    | A type implements more than one request closing, both request kinds, or a request interface and `INotification`.                                              |
| <a id="krx012"></a>KRX012      | Error    | A request, stream request or notification is generic, nested in a generic type or a `ref struct`; or a handler class is generic.                             |
| <a id="krx013"></a>KRX013      | Error    | A handler or behavior method parameter is `ref`, `in`, `out` or `params`, an injected parameter is a ref struct or a pointer, or there is more than one `CancellationToken`. |
| <a id="krx014"></a>KRX014      | Error    | A `static` handler class has injected parameters. Code fix: remove `static`.                                                                                  |
| <a id="krx015"></a>KRX015      | Error    | A handler class declares a member named `InvokeAsync` or starting with `__keryx_`.                                                                            |
| <a id="krx016"></a>KRX016      | Error    | Outside a host, a type a pipeline needs (request, response, notification, injected parameter type) or a behavior class or method is not public. In a host, one is narrower than `internal` or file-local. |
| <a id="krx017"></a>KRX017      | Warning  | A handler's response type differs from the request's only in nullability.                                                                                     |
| <a id="krx018"></a>KRX018      | Error    | A `[FromKeyedServices]` key on a handler or behavior method parameter is not a compile-time constant, or uses the parameterless form. |
| <a id="krx019"></a>KRX019      | Error    | A behavior class has an unsupported shape: not `static` or generic, a kind attribute without a matching behavior method, two methods of one kind, a partially open method or one with other type parameters, an open method whose message type parameter lacks the kind's message constraint (directly or through a derived interface), a `next` without the `struct` and interface constraint, a wrong return type, or a `static` `Handle`/`HandleAsync` whose kind the class does not carry, that has no valid `next`, or whose `next` and message type belong to different kinds. |
| <a id="krx021"></a>KRX021      | Info     | A struct request is boxed when sent through `ISender`; the host's typed overload avoids it.                                                                   |
| <a id="krx022"></a>KRX022      | Error    | The project compiles with C# below 14. Remove the `LangVersion` setting or set it to 14 or later. The generators emit nothing until then.                     |
| <a id="krx023"></a>KRX023      | Error    | A handler takes a request or stream request declared in another assembly. A request's handler lives in the request's own assembly; move the request or the handler. Notifications are not affected. |
| <a id="krx024"></a>KRX024      | Error    | A request or stream request class or record is neither `sealed` nor abstract (or `closed`). Seal it, or make it an abstract base of sealed requests. Notifications are not affected. Code fix: make it `sealed`. |
| <a id="krx030"></a>KRX030      | Error    | A referenced request (including one declared in another host) has no handler in the host: a safety net for an internal handler of a referenced host, or a library built with KRX001 suppressed. The host's own requests are KRX001's. Reported without a location: change its severity in a `.globalconfig` or with `<NoWarn>`. |
| <a id="krx031"></a>KRX031      | Error    | A host (`KeryxHost` is `true`) targets a framework older than `net10.0`. The host generator emits nothing there. Keryx defaults `KeryxHost` to `true` only on `net10.0` or later, so this means the project (or a file it imports) sets `<KeryxHost>true</KeryxHost>` itself ([Hosts](#hosts)). |
| <a id="krx032"></a>KRX032      | Error    | A referenced assembly's manifest is newer than this Keryx understands. Upgrade Keryx in the host.                                                             |
| <a id="krx033"></a>KRX033      | Warning  | A referenced assembly uses Keryx but has no manifest (the generator did not run there; reference the `Keryx` package directly).                               |
| <a id="krx034"></a>KRX034      | Error    | A type a referenced manifest names, or one generated code derives from it (a handler's response or constructor parameter type, an injected parameter type of a referenced behavior method), cannot be resolved from the host, or a manifest entry no longer fits the types the host resolves. Reference the assembly the message names. For a behavior method it names the method and its kind; the class still applies through its other kinds. |
| <a id="krx035"></a>KRX035      | Info     | Lists each global behavior the host applies, once per kind, and the assembly it comes from.                                                                   |
| <a id="krx036"></a>KRX036      | Warning  | An exclusion has no effect: `ExcludeHandler` names no referenced notification handler class the host would compose (a request handler is [KRX039](#krx039)), or `ExcludeBehavior` names a type that is not part of any pipeline. |
| <a id="krx037"></a>KRX037      | Info     | A struct notification is delivered to a handler typed as an interface or `INotification`, so it is boxed once per `Publish`.                                 |
| <a id="krx038"></a>KRX038      | Error    | A referenced request or stream request type is not sealed (its assembly suppressed [KRX024](#krx024)). The host does not compose it: sending it throws `KeryxHandlerNotFoundException`. Seal it in its own assembly. |
| <a id="krx039"></a>KRX039      | Error    | `ExcludeHandler` names a request or stream request handler (referenced or the host's own). It names only notification handlers, so the handler stays in the host; swap what it does through the services it injects ([Testing handlers](#testing-handlers)). |
| <a id="krx040"></a>KRX040      | Warning  | `[HandlerOrder]` on a method that is not a notification handler.                                                                                              |
| <a id="krx041"></a>KRX041      | Warning  | `[PublishStrategy]` on a type that is not a notification.                                                                                                     |
| <a id="krx042"></a>KRX042      | Warning  | `[Behavior]` where it has no effect (including on a notification handler method), or `[GlobalBehavior]` on a class without a behavior kind attribute or on a method that is not a behavior method. |
| <a id="krx043"></a>KRX043      | Warning  | `[assembly: ExcludeHandler]` or `[assembly: ExcludeBehavior]` in a project that is not a host (`KeryxHost` is not `true`), where it has no effect.                                  |

## Benchmarks

`test/Keryx.Benchmarks` compares Keryx with a hand-written direct call and with the other source-generated and reflection-based
mediators, each in its own small project under `test/benchmark-subjects`, all running the same requests, handler bodies and
behaviors. Every benchmark is one call on a mediator resolved beforehand (scoped ones from a scope that lives for the whole run), except
the real-world scope-per-request rows; behaviors complete synchronously. "Typed" is a library's per-request entry point, `IMediator` its
generic method; n/a marks a scenario the library has no API for. How each library is set up is recorded in
[open-questions.md](docs/adr/open-questions.md) (OQ-51, and OQ-72 for the real-world scenarios).

Each cell is the mean time per call and the bytes allocated per call (exact: BenchmarkDotNet's reports round them to KB once a column
reaches 1 KB, so they are taken from its log, total bytes over operations). One local run of the whole comparison suite on 2026-11-27, at
commit `1652e50` (built from a clean `git archive` of it), with nothing else running on the machine (owner decision 32), first under the
JIT and then under NativeAOT ([below](#nativeaot)), with the settings below:

```
BenchmarkDotNet v0.15.8, macOS 27.2.1 (26B131) [Darwin 27.2.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), Arm64 RyuJIT armv8.0-a
  Job-UDEAPE : .NET 10.0.15 (10.0.15, 10.0.1526.53008), Arm64 RyuJIT armv8.0-a

IterationCount=15  IterationTime=200ms  LaunchCount=1  WarmupCount=5
```

How to read them:

- Times under about 0.5 ns are below what BenchmarkDotNet resolves: the JIT inlined the whole call, pipeline included, into the
  benchmark (the direct call's synchronous `Send` at every depth and its `Publish` to one handler, Keryx's typed `Send` at every depth
  and its typed `Publish`, and Mediator singleton's `Mediator` class at depth 0). Compare those rows by order of magnitude.
- With a yielding handler, the time is the thread-pool hop of `Task.Yield()` and varies by a few hundred nanoseconds from run to run (844
  to 1,384 ns in the table below, with no pattern by library or depth); the allocation column is the meaningful one. The direct call's
  113 to 114 B (averaged over thread-pool work) is the handler's own async state machine, so a library that shows the same adds nothing.
- In the stream scenarios the direct call's 120 B is the handler's iterator. Each stream behavior adds its own iterator.
- In the real-world scenarios the handler's work (the `Pong` it allocates, the log messages, the query) is in every row, the direct call
  included, so compare each row with the direct call rather than with zero.

### Send, synchronous handler

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.02 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.47 ns · 0 B |
| **Keryx**, `IMediator` | 2.44 ns · 0 B | 2.70 ns · 0 B | 2.69 ns · 0 B | 2.74 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 25.3 ns · 64 B | 45.1 ns · 160 B | 67.2 ns · 352 B | 122 ns · 832 B |
| Mediator 3.0.2 scoped, `IMediator` | 26.6 ns · 64 B | 44.6 ns · 160 B | 66.9 ns · 352 B | 120 ns · 832 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.35 ns · 0 B | 1.36 ns · 0 B | 5.11 ns · 0 B | 13.4 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 4.12 ns · 0 B | 4.56 ns · 0 B | 8.94 ns · 0 B | 18.2 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 33.3 ns · 200 B | 68.5 ns · 440 B | 102 ns · 728 B | 189 ns · 1,448 B |
| Immediate.Handlers 4.4.0, `Handler` class | 7.82 ns · 0 B | 9.04 ns · 0 B | 9.58 ns · 0 B | 13.8 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 9.46 ns · 0 B | 12.6 ns · 0 B | 9.74 ns · 0 B | 14.7 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 20.5 ns · 24 B | 26.9 ns · 24 B | 21.5 ns · 24 B | 22.0 ns · 24 B |

Generic dispatch tests each sealed request type with a type pattern (open-questions, "Requests are sealed"): Keryx's `IMediator` rows
measure 2.44 to 2.74 ns here, against 3.22 to 3.51 ns in the previous run (2026-11-26, before the type patterns).

### Send, handler awaits `Task.Yield()`

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 971 ns · 113 B | 851 ns · 113 B | 972 ns · 113 B | 933 ns · 114 B |
| **Keryx**, typed overload | 1,115 ns · 113 B | 980 ns · 113 B | 864 ns · 113 B | 887 ns · 113 B |
| **Keryx**, `IMediator` | 846 ns · 113 B | 924 ns · 113 B | 906 ns · 113 B | 871 ns · 113 B |
| Mediator 3.0.2 scoped, `Mediator` class | 933 ns · 176 B | 941 ns · 273 B | 931 ns · 464 B | 977 ns · 944 B |
| Mediator 3.0.2 scoped, `IMediator` | 1,153 ns · 176 B | 991 ns · 272 B | 891 ns · 464 B | 983 ns · 944 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,234 ns · 112 B | 916 ns · 113 B | 936 ns · 113 B | 876 ns · 113 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,265 ns · 112 B | 907 ns · 113 B | 891 ns · 113 B | 866 ns · 113 B |
| MediatR 15.0.0, `IMediator` | 1,297 ns · 232 B | 909 ns · 472 B | 844 ns · 760 B | 970 ns · 1,480 B |
| Immediate.Handlers 4.4.0, `Handler` class | 1,384 ns · 373 B | 894 ns · 372 B | 891 ns · 371 B | 876 ns · 373 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 890 ns · 246 B | 906 ns · 245 B | 927 ns · 244 B | 943 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 990 ns · 278 B | 976 ns · 279 B | 985 ns · 278 B | 867 ns · 279 B |

### Stream of 100 items

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 632 ns · 120 B | 636 ns · 120 B |
| **Keryx**, typed overload | 632 ns · 120 B | 2,741 ns · 648 B |
| **Keryx**, `IMediator` | 638 ns · 120 B | 2,700 ns · 648 B |
| Mediator 3.0.2 scoped, `Mediator` class | 662 ns · 184 B | 2,784 ns · 1,048 B |
| Mediator 3.0.2 scoped, `IMediator` | 659 ns · 184 B | 2,810 ns · 1,048 B |
| Mediator 3.0.2 singleton, `Mediator` class | 634 ns · 120 B | 2,718 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 630 ns · 120 B | 2,715 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,291 ns · 512 B | 7,698 ns · 2,336 B |
| Immediate.Handlers 4.4.0, `Handler` class | 640 ns · 120 B | 2,757 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 630 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 651 ns · 144 B | n/a |

With 3 stream behaviors Keryx allocates 648 B per `Stream` (typed and `IMediator`), 32 B less than the previous run's 680 B: a pipeline
that resolves nothing has no pipeline context and its `next` links hold only the request (open-questions OQ-65). The generic `Stream`
slowdown with no stream behaviors that interleaved reruns found after the type patterns (20 to 60 ns, 3 to 9%; owner decision 22) does not
show in this run: `IMediator` measured 637.8 ns (standard deviation 5.0 ns), the typed overload 631.9 ns (2.4 ns) and the direct call
632.5 ns (1.8 ns), and all three are below the 649 to 661 ns those reruns measured before the type patterns. It stays on the
release checklist ([whats-left.md](docs/whats-left.md)).

### Real-world Ping → Pong

Beside the depth scenarios above, real-world scenarios send a `Ping` that a handler answers with a new `Pong` record, or publish a
`Pinged` notification, closer to what an application does (owner decision 30). The handler bodies and behaviors are the same in every
library.

Requests:

- `RealWorldPing`: the handler alone, from the long-lived scope.
- `RealWorldPingScopePerRequest`: the same with a scope per request: the measured call creates a DI scope, resolves the mediator (or the
  library's entry point) from it, sends and disposes the scope, the way an ASP.NET Core request does. For Keryx this includes creating the
  scoped `KeryxMediator`, which the long-lived rows hide.
- `RealWorldBehaviors`: a logging behavior (Microsoft.Extensions.Logging, two messages per request, really formatted by an in-memory
  provider that keeps the last 1,024) and a validation behavior (a hand-written `IValidator<TRequest>`, scoped) around a handler that
  injects a scoped repository doing a dictionary lookup, from the long-lived scope.
- `RealWorldBehaviorsScopePerRequest`: the same with a scope per request, so the repository, the validator and the pipeline (for Keryx,
  the pipeline context that holds the behaviors' services) are created for each call.
- `RealWorldSqlite`: a handler that runs a parameterized scalar query against an in-memory SQLite database (Microsoft.Data.Sqlite), seeded
  once per process, on the container's singleton open connection (opened in setup), from the long-lived scope.

| Subject | `Ping` | `Ping`, scope per request | Behaviors | Behaviors, scope per request | SQLite query |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hand-written direct call | 2.30 ns · 32 B | 53.4 ns · 368 B | 119 ns · 520 B | 217 ns · 920 B | 1,285 ns · 1,144 B |
| **Keryx**, typed overload | 2.65 ns · 32 B | 54.3 ns · 448 B | 128 ns · 520 B | 321 ns · 1,408 B | 1,287 ns · 1,144 B |
| **Keryx**, `IMediator` | 6.02 ns · 32 B | 80.4 ns · 448 B | 134 ns · 520 B | 353 ns · 1,408 B | 1,362 ns · 1,144 B |
| Mediator 3.0.2 scoped, `Mediator` class | 29.4 ns · 96 B | 97.0 ns · 472 B | 209 ns · 776 B | 380 ns · 1,648 B | 1,353 ns · 1,208 B |
| Mediator 3.0.2 scoped, `IMediator` | 29.3 ns · 96 B | 94.4 ns · 472 B | 204 ns · 776 B | 404 ns · 1,648 B | 1,335 ns · 1,208 B |
| Mediator 3.0.2 singleton, `Mediator` class | 2.75 ns · 32 B | 24.9 ns · 160 B | n/a | n/a | 1,288 ns · 1,144 B |
| Mediator 3.0.2 singleton, `IMediator` | 7.85 ns · 32 B | 30.5 ns · 160 B | n/a | n/a | 1,286 ns · 1,144 B |
| MediatR 15.0.0, `IMediator` | 36.8 ns · 232 B | 74.7 ns · 432 B | 258 ns · 1,176 B | 322 ns · 1,608 B | 1,342 ns · 1,344 B |
| Immediate.Handlers 4.4.0, `Handler` class | 12.6 ns · 32 B | 80.7 ns · 400 B | 145 ns · 520 B | 299 ns · 1,400 B | 1,318 ns · 1,144 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 21.3 ns · 32 B | n/a | n/a | n/a | 1,286 ns · 1,168 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 24.9 ns · 56 B | 52.7 ns · 208 B | 187 ns · 544 B | 260 ns · 928 B | 1,330 ns · 1,168 B |

- `Ping` from the long-lived scope: Keryx's typed overload is within half a nanosecond of the direct call (the 32 B is the `Pong`).
- Scope per request: Keryx's typed overload measures 54.3 ns against the direct call's 53.4 ns and allocates 80 B more: the scoped
  `KeryxMediator` (one field per pipeline context, 104 B in this host) where the direct call resolves a handler object without fields.
  Mediator singleton allocates least here because nothing scoped is resolved.
- Behaviors from the long-lived scope: Keryx's typed overload measures 128 ns against the direct call's 119 ns, with the same 520 B; the
  other libraries measure 145 to 258 ns.
- Behaviors with a scope per request: Keryx's typed overload measures 321 ns and 1,408 B against the direct call's 217 ns and 920 B: the
  first call in a scope creates the mediator and the pipeline context, which resolves the handler's and the behaviors' services. The other
  libraries measure 260 to 404 ns; ZeroAlloc.Mediator, whose behaviors resolve their services from the provider on every call, is the
  fastest here (260 ns), while from the long-lived scope it measures 187 ns, behind Immediate.Handlers (145 ns) and Keryx (128 to
  134 ns).
- The SQLite query (about 1.3 µs and 1,144 B) is most of every row in the last column.
- `IMediator` with a scope per request is slower than the typed overload in this run because `AddKeryx()` then registered `IMediator` as
  its own scoped service forwarding to `KeryxMediator`, so resolving it ran a second factory and added an entry to the scope. `AddKeryx()`
  now registers the interfaces as transient forwarders, which add nothing to the scope (open-questions OQ-74); these rows are from the
  run before that change and are refreshed by the next full run.

Notifications, each to 1 and to 5 handlers:

- `RealWorldPublish1ScopePerRequest` and `RealWorldPublish5ScopePerRequest`: a publish with a scope per request; each handler looks the
  pong up in the scoped repository and writes a log message.
- `RealWorldPublishBehavior1ScopePerRequest` and `RealWorldPublishBehavior5ScopePerRequest`: the same with the logging behavior as a
  notification behavior (two messages per publish). Only Keryx has notification behaviors; Mediator's, MediatR's and ZeroAlloc.Mediator's
  behaviors wrap requests only, so they are n/a here, and the scenario is a pair of its own so the rows above compare every library on
  the same work.
- `RealWorldPublishAsync1` and `RealWorldPublishAsync5`: handlers that await the SQLite query and then log, published sequentially (each
  library's default) from the long-lived scope. The await is the query's, not `Task.Yield`'s.
- `RealWorldPublishParallel1` and `RealWorldPublishParallel5`: the same handlers with each library's parallel strategy: Keryx's
  `[PublishStrategy(PublishStrategy.Parallel)]`, Mediator's `TaskWhenAllPublisher` (a generator option, so a third Mediator project,
  singleton, shown in the Mediator singleton rows), MediatR's `TaskWhenAllPublisher` (a second container) and ZeroAlloc.Mediator's
  `[ParallelNotification]`.

Per request scope:

| Subject | 1 handler | 5 handlers | 1 handler + behavior | 5 handlers + behavior |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 124 ns · 456 B | 450 ns · 1,232 B | 201 ns · 608 B | 520 ns · 1,384 B |
| **Keryx**, typed overload | 161 ns · 560 B | 557 ns · 1,336 B | 246 ns · 712 B | 638 ns · 1,488 B |
| **Keryx**, `IMediator` | 219 ns · 920 B | 634 ns · 2,136 B | 298 ns · 1,072 B | 709 ns · 2,288 B |
| Mediator 3.0.2 scoped, `Mediator` class | 232 ns · 888 B | 680 ns · 2,104 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 238 ns · 888 B | 714 ns · 2,104 B | n/a | n/a |
| MediatR 15.0.0, `IMediator` | 234 ns · 1,248 B | 640 ns · 2,496 B | n/a | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 127 ns · 456 B | 372 ns · 840 B | n/a | n/a |

Handlers that await the SQLite query, from the long-lived scope:

| Subject | Sequential, 1 handler | Sequential, 5 handlers | Parallel, 1 handler | Parallel, 5 handlers |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,322 ns · 1,176 B | 6,748 ns · 5,880 B | 1,355 ns · 1,176 B | 6,743 ns · 5,880 B |
| **Keryx**, typed overload | 1,370 ns · 1,176 B | 6,647 ns · 5,880 B | 1,326 ns · 1,176 B | 6,914 ns · 5,880 B |
| **Keryx**, `IMediator` | 1,353 ns · 1,176 B | 6,892 ns · 5,880 B | 1,352 ns · 1,176 B | 6,686 ns · 5,880 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,378 ns · 1,176 B | 6,732 ns · 5,880 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 1,383 ns · 1,176 B | 6,907 ns · 5,880 B | n/a | n/a |
| Mediator 3.0.2 singleton, `Mediator` class | 1,352 ns · 1,176 B | 6,817 ns · 5,880 B | 1,347 ns · 1,176 B | 7,348 ns · 5,880 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,406 ns · 1,176 B | 6,694 ns · 5,880 B | 1,353 ns · 1,176 B | 6,943 ns · 5,880 B |
| MediatR 15.0.0, `IMediator` | 1,514 ns · 1,952 B | 7,380 ns · 7,648 B | 1,588 ns · 2,232 B | 7,297 ns · 8,032 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,354 ns · 1,208 B | 7,301 ns · 6,040 B | 1,376 ns · 1,208 B | 7,011 ns · 6,040 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,385 ns · 1,208 B | 6,826 ns · 6,040 B | 1,428 ns · 1,208 B | 6,900 ns · 6,040 B |

- Per request scope, Keryx's typed overload measures 161 ns (1 handler) and 557 ns (5 handlers) against the direct call's 124 ns and
  450 ns. Its `IMediator` rows allocate 360 B and 800 B more than the typed ones, all of it the scope entry of the scoped forwarding
  `IMediator` registration this run measured (a probe resolving the same registrations from a bare container allocates exactly 360 B and
  800 B more per scope through `IMediator`, and nothing more when the forwarding registration is transient). `AddKeryx()` now registers it
  transient (OQ-74); these rows are refreshed by the next full run.
- SQLite's async methods complete synchronously, so no row waits on I/O: the async and parallel rows measure each library's path for
  handlers that return completed tasks, not overlapping work, and each library's parallel strategy measures within 8% of its sequential
  publish. Keryx allocates what the direct call allocates in all four columns.

Here the direct call is a hand-written handler class (or a publisher class taking the handler classes) resolved from DI that does the
same work, the behaviors' logging and validation included, so the ratio is each mediator's cost on top of that work. n/a, besides the
entry points a library does not have: Mediator singleton in the two request behavior scenarios and the per-request publish (its singleton
handlers and behaviors cannot take the scoped repository and validator); ZeroAlloc.Mediator's static `Mediator` in the two request
behavior scenarios (its static `Send` passes behaviors a `null` state, and the library's static-path alternative would need another
behavior body that opens a scope inside each call) and in every scope-per-request scenario (it has no scope); every library but Keryx in
the notification-behavior pair; Immediate.Handlers in every notification scenario (it has none); Mediator scoped in the parallel pair (its
parallel publisher is the singleton project's). The setup is recorded in [open-questions.md](docs/adr/open-questions.md) (OQ-72). Run them
on their own with `--filter '*RealWorld*'`.

### Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only)

The handler of a request with no response returns a non-generic `ValueTask` or `Task`, so Keryx adapts it to `ValueTask<Unit>` through
its one generated async path (`UnitAdapter.ToUnit`, pooled builder on net10+). The direct call awaits the same handler body, so its
allocation is the handler's own state machine; the Performance contract says the adapter adds 0 bytes to it in steady state. The other
libraries are left out: this checks Keryx's contract, not a comparison.

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,199 ns · 97 B | 840 ns · 96 B |
| **Keryx**, typed overload | 1,184 ns · 96 B | 1,269 ns · 96 B |
| **Keryx**, `IMediator` | 931 ns · 96 B | 1,312 ns · 96 B |

### Publish, sequential, synchronous handlers

The direct call calls the handlers in turn without an `async` method of its own (it only falls back to one when a handler does not
complete synchronously), so it is the floor. With one handler the JIT inlines all of it and the time is below what BenchmarkDotNet
resolves.

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.00 ns · 0 B | 0.61 ns · 0 B |
| **Keryx**, typed overload | 0.34 ns · 0 B | 0.44 ns · 0 B |
| **Keryx**, `IMediator` | 3.19 ns · 0 B | 3.93 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 16.1 ns · 0 B | 31.7 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 20.2 ns · 0 B | 25.9 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.09 ns · 0 B | 15.7 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 8.79 ns · 0 B | 17.4 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 120 ns · 768 B | 326 ns · 1,728 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 8.28 ns · 0 B | 8.68 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 18.2 ns · 24 B | 58.5 ns · 120 B |

A publish whose handlers are all static and whose behaviors inject nothing (every row here) has no pipeline context and its publish
strategy is static (open-questions OQ-70): Keryx's typed overload measures 0.34 ns and 0.44 ns (0.65 and 0.84 ns in the previous run),
inlined like the direct call, and `IMediator` 3.19 ns and 3.93 ns (3.11 and 3.27 ns in the previous run; at this scale a nanosecond
moves between runs), all 0 B.

### NativeAOT

The same comparison scenarios also run with BenchmarkDotNet's NativeAOT toolchain on net10.0 (owner decision 29), each benchmark compiled
ahead of time instead of JIT-compiled with tiering and Dynamic PGO. That run followed the JIT run above, from the same build, with the
same settings:

```
NativeAOT 10.0 : .NET 10.0.15, Arm64 NativeAOT armv8.0-a
Job=NativeAOT 10.0  Runtime=NativeAOT 10.0  Toolchain=Latest ILCompiler
IterationCount=15  IterationTime=200ms  LaunchCount=1  WarmupCount=5
```

A library that cannot run under NativeAOT is n/a there, with the reason found by building and running it: **MediatR 15.0.0 is n/a in
every scenario**. With the suite's behaviors, `AddMediatR` throws at startup (`AddOpenStreamBehavior`: "StreamPass1`2 must implement
MediatR.IStreamPipelineBehavior`2"), and without any behaviors the container cannot construct MediatR's `Mediator`; MediatR.dll also
produces trim and AOT warnings (IL2104, IL3053). Every other subject runs every scenario it runs under the JIT, and `--verify` checks them
in a NativeAOT build of the suite (215 benchmarks pass, MediatR's 23 are listed as n/a). The setup is recorded in
[open-questions.md](docs/adr/open-questions.md) ("NativeAOT benchmark run").

Each cell is the JIT time and the NativeAOT time, then the bytes allocated (both, when they differ). Read the NativeAOT numbers with
three things in mind:

- In the `Send` scenarios nothing is inlined into the benchmark the way the JIT inlines it, so the direct call's synchronous `Send`
  measures 1.49 to 1.52 ns at every depth: that is the floor of a NativeAOT `Send` row, and the sub-nanosecond JIT `Send` rows have no
  NativeAOT equivalent. The `Publish` rows of the direct call and Keryx's typed overload still measure about a nanosecond or less
  (the direct call 0.17 ns and 1.20 ns, Keryx 0.87 ns and 1.85 ns), so compare those by order of magnitude.
- In NativeAOT builds the call counter `--verify` uses is not compiled away as it is under the JIT, so each handler and behavior call
  carries a field load and a branch (OQ-73, open). It is in every subject's rows alike, and grows with the depth.
- The behaviors' log messages take the request or notification type's name from `typeof(T).Name`, which allocates a new string on every
  read under NativeAOT (48 B for `TypicalPing`, checked with a NativeAOT probe; nothing under the JIT), while the direct call uses
  `nameof`. That is Keryx's extra 96 B in the behavior rows under NativeAOT (two messages), and the same reads are in every other
  library's behavior rows (OQ-75, open). In the scope-per-request rows, Microsoft.Extensions.DependencyInjection resolves without
  generated IL under NativeAOT, so every row's scope cost changes, the direct call's included.

Send, synchronous handler:

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.02 ns → 1.49 ns · 0 B | 0.00 ns → 1.52 ns · 0 B | 0.00 ns → 1.49 ns · 0 B | 0.00 ns → 1.52 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns → 1.59 ns · 0 B | 0.00 ns → 2.09 ns · 0 B | 0.00 ns → 2.12 ns · 0 B | 0.47 ns → 2.67 ns · 0 B |
| **Keryx**, `IMediator` | 2.44 ns → 3.26 ns · 0 B | 2.70 ns → 3.45 ns · 0 B | 2.69 ns → 3.97 ns · 0 B | 2.74 ns → 5.19 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 25.3 ns · 64 B → 52.6 ns · 48 B | 45.1 ns · 160 B → 79.1 ns · 128 B | 67.2 ns · 352 B → 101 ns · 288 B | 122 ns · 832 B → 158 ns · 688 B |
| Mediator 3.0.2 scoped, `IMediator` | 26.6 ns · 64 B → 58.4 ns · 48 B | 44.6 ns · 160 B → 82.6 ns · 128 B | 66.9 ns · 352 B → 102 ns · 288 B | 120 ns · 832 B → 161 ns · 688 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.35 ns → 1.95 ns · 0 B | 1.36 ns → 3.40 ns · 0 B | 5.11 ns → 6.76 ns · 0 B | 13.4 ns → 20.2 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 4.12 ns → 5.01 ns · 0 B | 4.56 ns → 6.38 ns · 0 B | 8.94 ns → 10.2 ns · 0 B | 18.2 ns → 23.4 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 33.3 ns · 200 B → n/a | 68.5 ns · 440 B → n/a | 102 ns · 728 B → n/a | 189 ns · 1,448 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 7.82 ns → 20.8 ns · 0 B | 9.04 ns → 20.6 ns · 0 B | 9.58 ns → 22.1 ns · 0 B | 13.8 ns → 26.2 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 9.46 ns → 17.9 ns · 0 B | 12.6 ns → 18.2 ns · 0 B | 9.74 ns → 20.5 ns · 0 B | 14.7 ns → 25.7 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 20.5 ns → 38.8 ns · 24 B | 26.9 ns → 41.2 ns · 24 B | 21.5 ns → 43.7 ns · 24 B | 22.0 ns → 49.0 ns · 24 B |

Send, handler awaits `Task.Yield()` (the time is the thread-pool hop under both runtimes; compare the allocations):

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 971 ns · 113 B → 872 ns · 114 B | 851 ns · 113 B → 848 ns · 114 B | 972 ns · 113 B → 791 ns · 114 B | 933 ns → 842 ns · 114 B |
| **Keryx**, typed overload | 1,115 ns · 113 B → 748 ns · 114 B | 980 ns → 782 ns · 113 B | 864 ns → 787 ns · 113 B | 887 ns → 768 ns · 113 B |
| **Keryx**, `IMediator` | 846 ns → 738 ns · 113 B | 924 ns → 774 ns · 113 B | 906 ns → 778 ns · 113 B | 871 ns → 801 ns · 113 B |
| Mediator 3.0.2 scoped, `Mediator` class | 933 ns · 176 B → 841 ns · 162 B | 941 ns · 273 B → 867 ns · 240 B | 931 ns · 464 B → 840 ns · 400 B | 977 ns · 944 B → 867 ns · 800 B |
| Mediator 3.0.2 scoped, `IMediator` | 1,153 ns · 176 B → 765 ns · 161 B | 991 ns · 272 B → 828 ns · 240 B | 891 ns · 464 B → 826 ns · 400 B | 983 ns · 944 B → 889 ns · 800 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,234 ns · 112 B → 836 ns · 113 B | 916 ns → 764 ns · 113 B | 936 ns → 780 ns · 113 B | 876 ns → 803 ns · 113 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,265 ns · 112 B → 760 ns · 114 B | 907 ns → 795 ns · 113 B | 891 ns · 113 B → 806 ns · 114 B | 866 ns → 813 ns · 113 B |
| MediatR 15.0.0, `IMediator` | 1,297 ns · 232 B → n/a | 909 ns · 472 B → n/a | 844 ns · 760 B → n/a | 970 ns · 1,480 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 1,384 ns → 870 ns · 373 B | 894 ns → 881 ns · 372 B | 891 ns → 947 ns · 371 B | 876 ns · 373 B → 893 ns · 372 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 890 ns → 837 ns · 246 B | 906 ns · 245 B → 877 ns · 246 B | 927 ns · 244 B → 823 ns · 247 B | 943 ns → 921 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 990 ns · 278 B → 793 ns · 279 B | 976 ns → 842 ns · 279 B | 985 ns → 926 ns · 278 B | 867 ns · 279 B → 838 ns · 280 B |

Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only):

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,199 ns → 710 ns · 97 B | 840 ns → 720 ns · 96 B |
| **Keryx**, typed overload | 1,184 ns → 841 ns · 96 B | 1,269 ns → 820 ns · 96 B |
| **Keryx**, `IMediator` | 931 ns → 773 ns · 96 B | 1,312 ns → 800 ns · 96 B |

Stream of 100 items:

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 632 ns → 901 ns · 120 B | 636 ns → 910 ns · 120 B |
| **Keryx**, typed overload | 632 ns → 895 ns · 120 B | 2,741 ns → 4,086 ns · 648 B |
| **Keryx**, `IMediator` | 638 ns → 901 ns · 120 B | 2,700 ns → 4,183 ns · 648 B |
| Mediator 3.0.2 scoped, `Mediator` class | 662 ns · 184 B → 973 ns · 168 B | 2,784 ns · 1,048 B → 4,328 ns · 984 B |
| Mediator 3.0.2 scoped, `IMediator` | 659 ns · 184 B → 963 ns · 168 B | 2,810 ns · 1,048 B → 4,285 ns · 984 B |
| Mediator 3.0.2 singleton, `Mediator` class | 634 ns → 894 ns · 120 B | 2,718 ns → 4,297 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 630 ns → 913 ns · 120 B | 2,715 ns → 4,145 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,291 ns · 512 B → n/a | 7,698 ns · 2,336 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 640 ns → 928 ns · 120 B | 2,757 ns → 4,203 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 630 ns → 907 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 651 ns → 940 ns · 144 B | n/a |

Publish, sequential, synchronous handlers:

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.00 ns → 0.17 ns · 0 B | 0.61 ns → 1.20 ns · 0 B |
| **Keryx**, typed overload | 0.34 ns → 0.87 ns · 0 B | 0.44 ns → 1.85 ns · 0 B |
| **Keryx**, `IMediator` | 3.19 ns → 6.50 ns · 0 B | 3.93 ns → 6.88 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 16.1 ns → 37.6 ns · 0 B | 31.7 ns → 56.3 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 20.2 ns → 41.0 ns · 0 B | 25.9 ns → 60.5 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.09 ns → 10.6 ns · 0 B | 15.7 ns → 25.5 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 8.79 ns → 12.1 ns · 0 B | 17.4 ns → 25.0 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 120 ns · 768 B → n/a | 326 ns · 1,728 B → n/a |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 8.28 ns → 16.7 ns · 0 B | 8.68 ns → 18.4 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 18.2 ns → 56.0 ns · 24 B | 58.5 ns → 164 ns · 120 B |

Real-world requests (`Ping`, from the long-lived scope and per request, the behavior pair and the SQLite query):

| Subject | `Ping` | `Ping`, scope per request | Behaviors | Behaviors, scope per request | SQLite query |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hand-written direct call | 2.30 ns → 3.69 ns · 32 B | 53.4 ns → 85.3 ns · 368 B | 119 ns → 138 ns · 520 B | 217 ns · 920 B → 332 ns · 968 B | 1,285 ns · 1,144 B → 1,295 ns · 1,128 B |
| **Keryx**, typed overload | 2.65 ns → 3.76 ns · 32 B | 54.3 ns → 85.4 ns · 448 B | 128 ns · 520 B → 205 ns · 616 B | 321 ns · 1,408 B → 510 ns · 1,504 B | 1,287 ns · 1,144 B → 1,301 ns · 1,128 B |
| **Keryx**, `IMediator` | 6.02 ns → 5.32 ns · 32 B | 80.4 ns → 132 ns · 448 B | 134 ns · 520 B → 206 ns · 616 B | 353 ns · 1,408 B → 551 ns · 1,504 B | 1,362 ns · 1,144 B → 1,306 ns · 1,128 B |
| Mediator 3.0.2 scoped, `Mediator` class | 29.4 ns · 96 B → 56.5 ns · 80 B | 97.0 ns · 472 B → 195 ns · 488 B | 209 ns · 776 B → 310 ns · 824 B | 380 ns · 1,648 B → 736 ns · 1,824 B | 1,353 ns · 1,208 B → 1,377 ns · 1,176 B |
| Mediator 3.0.2 scoped, `IMediator` | 29.3 ns · 96 B → 59.9 ns · 80 B | 94.4 ns · 472 B → 197 ns · 488 B | 204 ns · 776 B → 322 ns · 824 B | 404 ns · 1,648 B → 732 ns · 1,824 B | 1,335 ns · 1,208 B → 1,383 ns · 1,176 B |
| Mediator 3.0.2 singleton, `Mediator` class | 2.75 ns → 3.74 ns · 32 B | 24.9 ns → 39.7 ns · 160 B | n/a | n/a | 1,288 ns · 1,144 B → 1,314 ns · 1,128 B |
| Mediator 3.0.2 singleton, `IMediator` | 7.85 ns → 6.66 ns · 32 B | 30.5 ns → 43.9 ns · 160 B | n/a | n/a | 1,286 ns · 1,144 B → 1,312 ns · 1,128 B |
| MediatR 15.0.0, `IMediator` | 36.8 ns · 232 B → n/a | 74.7 ns · 432 B → n/a | 258 ns · 1,176 B → n/a | 322 ns · 1,608 B → n/a | 1,342 ns · 1,344 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 12.6 ns → 19.8 ns · 32 B | 80.7 ns · 400 B → 139 ns · 432 B | 145 ns · 520 B → 222 ns · 616 B | 299 ns · 1,400 B → 540 ns · 1,640 B | 1,318 ns · 1,144 B → 1,339 ns · 1,128 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 21.3 ns → 21.1 ns · 32 B | n/a | n/a | n/a | 1,286 ns · 1,168 B → 1,378 ns · 1,184 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 24.9 ns → 44.2 ns · 56 B | 52.7 ns · 208 B → 127 ns · 240 B | 187 ns · 544 B → 331 ns · 672 B | 260 ns · 928 B → 495 ns · 1,088 B | 1,330 ns · 1,168 B → 1,386 ns · 1,184 B |

Real-world publish per request scope:

| Subject | 1 handler | 5 handlers | 1 handler + behavior | 5 handlers + behavior |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 124 ns · 456 B → 238 ns · 528 B | 450 ns · 1,232 B → 789 ns · 1,496 B | 201 ns · 608 B → 326 ns · 688 B | 520 ns · 1,384 B → 880 ns · 1,656 B |
| **Keryx**, typed overload | 161 ns → 257 ns · 560 B | 557 ns → 899 ns · 1,336 B | 246 ns · 712 B → 414 ns · 808 B | 638 ns · 1,488 B → 1,040 ns · 1,584 B |
| **Keryx**, `IMediator` | 219 ns → 338 ns · 920 B | 634 ns → 1,019 ns · 2,136 B | 298 ns · 1,072 B → 492 ns · 1,168 B | 709 ns · 2,288 B → 1,151 ns · 2,384 B |
| Mediator 3.0.2 scoped, `Mediator` class | 232 ns · 888 B → 497 ns · 960 B | 680 ns · 2,104 B → 1,447 ns · 2,336 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 238 ns · 888 B → 508 ns · 960 B | 714 ns · 2,104 B → 1,412 ns · 2,336 B | n/a | n/a |
| MediatR 15.0.0, `IMediator` | 234 ns · 1,248 B → n/a | 640 ns · 2,496 B → n/a | n/a | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 127 ns · 456 B → 271 ns · 528 B | 372 ns · 840 B → 779 ns · 1,072 B | n/a | n/a |

Real-world publish to handlers that await the SQLite query:

| Subject | Sequential, 1 handler | Sequential, 5 handlers | Parallel, 1 handler | Parallel, 5 handlers |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,322 ns · 1,176 B → 1,435 ns · 1,160 B | 6,748 ns · 5,880 B → 7,218 ns · 5,800 B | 1,355 ns · 1,176 B → 1,396 ns · 1,160 B | 6,743 ns · 5,880 B → 6,932 ns · 5,800 B |
| **Keryx**, typed overload | 1,370 ns · 1,176 B → 1,498 ns · 1,160 B | 6,647 ns · 5,880 B → 7,104 ns · 5,800 B | 1,326 ns · 1,176 B → 1,381 ns · 1,160 B | 6,914 ns · 5,880 B → 6,944 ns · 5,800 B |
| **Keryx**, `IMediator` | 1,353 ns · 1,176 B → 1,420 ns · 1,160 B | 6,892 ns · 5,880 B → 7,229 ns · 5,800 B | 1,352 ns · 1,176 B → 1,379 ns · 1,160 B | 6,686 ns · 5,880 B → 6,847 ns · 5,800 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,378 ns · 1,176 B → 1,479 ns · 1,160 B | 6,732 ns · 5,880 B → 7,273 ns · 5,800 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 1,383 ns · 1,176 B → 1,514 ns · 1,160 B | 6,907 ns · 5,880 B → 7,364 ns · 5,800 B | n/a | n/a |
| Mediator 3.0.2 singleton, `Mediator` class | 1,352 ns · 1,176 B → 1,427 ns · 1,160 B | 6,817 ns · 5,880 B → 7,154 ns · 5,800 B | 1,347 ns · 1,176 B → 1,426 ns · 1,160 B | 7,348 ns · 5,880 B → 6,873 ns · 5,800 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,406 ns · 1,176 B → 1,435 ns · 1,160 B | 6,694 ns · 5,880 B → 6,998 ns · 5,800 B | 1,353 ns · 1,176 B → 1,397 ns · 1,160 B | 6,943 ns · 5,880 B → 6,977 ns · 5,800 B |
| MediatR 15.0.0, `IMediator` | 1,514 ns · 1,952 B → n/a | 7,380 ns · 7,648 B → n/a | 1,588 ns · 2,232 B → n/a | 7,297 ns · 8,032 B → n/a |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,354 ns · 1,208 B → 1,504 ns · 1,232 B | 7,301 ns · 6,040 B → 7,508 ns · 6,160 B | 1,376 ns · 1,208 B → 1,459 ns · 1,232 B | 7,011 ns · 6,040 B → 7,355 ns · 6,160 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,385 ns · 1,208 B → 1,554 ns · 1,232 B | 6,826 ns · 6,040 B → 7,379 ns · 6,160 B | 1,428 ns · 1,208 B → 1,462 ns · 1,232 B | 6,900 ns · 6,040 B → 7,268 ns · 6,160 B |

- Without behaviors, Keryx's typed overload stays at the direct call's floor: 1.59 ns against 1.49 ns at depth 0, 1.85 ns against
  1.20 ns for a publish to 5 handlers, 3.76 ns against 3.69 ns for the real-world `Ping` and 85.4 ns against 85.3 ns for it per request
  scope. Each behavior adds a little (2.67 ns at depth 8), still 0 B; Mediator singleton measures 1.95 to 23.4 ns over the same depths
  (class and `IMediator`), the other libraries 17.9 to 161 ns.
- Outside the yielding scenarios, Keryx allocates exactly what it allocates under the JIT, except in the behavior rows (96 B more: the
  type names above) and in the SQLite and async-handler rows (16 B less per query), where the direct call's allocations change by the
  same amount.
- With a yielding handler every subject measured 738 to 947 ns under NativeAOT. Keryx allocates 113 to 114 B as under the JIT and as the
  direct call. Mediator scoped allocates less than under the JIT (161 to 162, 240, 400 and 800 B at depths 0, 1, 3 and 8, against 176,
  272 to 273, 464 and 944 B), as in its synchronous rows; Mediator singleton, Immediate.Handlers and ZeroAlloc.Mediator stay within 3 B
  of their JIT allocations. With an `IRequest` handler Keryx allocates 96 B, as the direct call does (96 to 97 B).
- Streams cost 40 to 60% more for every subject (the direct call 632 ns under the JIT, 901 ns under NativeAOT); Keryx's allocations do
  not change.
- The real-world behavior rows: Keryx's typed overload measures 205 ns from the long-lived scope (the direct call 138 ns) and 510 ns per
  request (332 ns); the other libraries 222 to 331 ns and 495 to 736 ns.

Run them yourself (the JIT run took 25 minutes and the NativeAOT run 22 minutes with these settings; `--verify` checks that every
library really runs each scenario's handlers and behaviors):

```bash
cd test/Keryx.Benchmarks
dotnet run -c Release -f net10.0 -- --verify
dotnet run -c Release -f net10.0 -- --filter '*Comparison*' --launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 200
```

Under NativeAOT (the ILCompiler needs the platform's native toolchain: Xcode command line tools on macOS, clang and zlib1g-dev on
Ubuntu), publish the suite with NativeAOT and run `--verify` in that binary, then run it with `--runtimes nativeaot10.0` (MediatR's
benchmarks and the JIT-only pipeline prototype are left out of the NativeAOT job):

```bash
dotnet publish -c Release -f net10.0 --use-current-runtime -p:KeryxBenchmarksNativeAot=true -o aot
aot/Keryx.Benchmarks --verify
dotnet run -c Release -f net10.0 -- --filter '*Comparison*' --runtimes nativeaot10.0 --launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 200
```

Run it on its own: `--runtimes net10.0 nativeaot10.0` runs both in one summary, but BenchmarkDotNet then takes the JIT job's `Baseline`
as the only baseline, so the NativeAOT rows have no ratios (`?`).

The Keryx subject has fewer than 32 request, stream request and notification types, so its `Keryx_Generic` rows measure the linear type
compares. With the environment variable `KeryxDispatchPadding=true` (an MSBuild property, so BenchmarkDotNet's own build sees it too),
it compiles 32 more of each (`Padding.cs`), and the same rows measure the `FrozenDictionary` lookup and jump table above the threshold:

```bash
KeryxDispatchPadding=true dotnet run -c Release -f net10.0 -- --filter '*Comparison*Keryx_Generic*'
```

`--filter '*PipelinePrototype*'` runs the hand-written struct pipeline the generated code follows (static behavior methods, struct
`next` links) against nested direct calls at depths 1, 3 and 8: 0 B at every depth, and 0.66x the nested calls' time at depth 3 on the
machine above. On macOS, `--prototype-disasm` runs the same pipelines to Tier 1 for a `DOTNET_JitDisasm` listing (see `Program.cs`).

CI builds the suite, runs `--verify`, runs every benchmark (the real-world scenarios included) once with `--job Dry`, does the same under
NativeAOT on Linux (publish with NativeAOT and `--verify` in that binary, then `--job Dry --runtimes nativeaot10.0`, owner decision 31),
and runs the pipeline prototype on Linux with BenchmarkDotNet's `DisassemblyDiagnoser`, uploading the disassembly as a build artifact.
Numbers from shared runners are never published.

## Building

```bash
mise run build
```

or, without mise:

```bash
dotnet build Keryx.slnx
dotnet test --solution Keryx.slnx
```

Warnings are errors everywhere.

## License

[MIT](LICENSE)
