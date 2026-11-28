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
`AddKeryx()`, which registers the mediator. Neither handlers nor behaviors are registered: the mediator's pipeline contexts resolve the
services a handler injects and call it (in the host, through a generated static entry, so the mediator never constructs the class), and
behaviors are static methods.

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
parameters. A `static partial` class is fine when nothing is injected ([KRX014](#krx014)). A handler class must not implement
`IDisposable` or `IAsyncDisposable` ([KRX025](#krx025)): handlers are stateless and Keryx never disposes one, so inject the disposable
service instead. Group handlers with nested classes:

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
| `IMediator`, `ISender`, `IPublisher`      | a new mediator over the same scope, sharing the scope's pipeline contexts           |
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
registrations, if it reuses them: with Microsoft's container the last registration wins), and the handler's pipeline context resolves
the fakes. Registering the handler class itself changes nothing: the mediator does not resolve handler classes from the container.

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
call; see [Streaming](#streaming)). The context holds each service once, however many behavior methods and handlers of the pipeline
inject it (same type and same key; a keyed and an unkeyed `IClock` are two services): it is resolved as required when any of them
requires it, and otherwise once as optional, with each consumer's own default applied when it is missing. So **a transient service that
several behaviors or handlers of one pipeline inject is one instance per scope and pipeline**, shared by them, not one per behavior or
handler: a request's handler and its behaviors get the same instance, and so do all the handlers of a notification (owner decision 43). An unkeyed
`IServiceProvider` is the scope's provider itself: the context holds it in its own `ServiceProvider` field when a behavior method or one
of the host's own handlers (through its static entry) injects it, a referenced handler's constructor receives the context's constructor
provider directly, and otherwise the context has no such field. The
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
- `internal static IServiceCollection AddKeryx(this IServiceCollection services)`, which registers `KeryxMediator`, `IMediator`,
  `ISender` and `IPublisher` as transient factories that each create a mediator (`sp => new KeryxMediator(sp)`), a scoped state that
  holds the scope's pipeline contexts, with generated factories and `TryAdd`. It registers no handler class (owner decision 42) and no
  behavior: a pipeline context resolves the services the host's own handler injects and calls the handler's generated
  `internal static __keryx_InvokeAsync` (owner decision 44), so the mediator never constructs the host's handler classes; it constructs
  a referenced (library) handler with `new`, once per scope and pipeline. Each pipeline context resolves a service once for all its
  behaviors and handlers (owner decision 43). A polymorphic notification handler gets one instance (or one set of services) per publish
  pipeline.

The mediator is **transient and small** (owner decision 46): it holds the scope's provider and, from its first call that needs a
pipeline context, the **scope state**, which every mediator resolved in the scope shares. So each resolve is a new mediator, but the
pipeline contexts are one per scope and pipeline, whichever mediator, or how many, the scope uses. Each pipeline context is
created once per scope, lazily, and kept in the state; it holds the services the handler injects (or a referenced handler) and the
services the pipeline's behavior methods inject, and nothing creates a behavior instance. A request or stream pipeline with a static handler whose behaviors inject nothing, and a notification type whose
handlers are all static (or that has none) and whose behaviors inject nothing, have no context at all (open-questions OQ-65 and OQ-70):
such a pipeline's entry calls the outermost behavior method, or the handler, or for a notification its static publish strategy, directly,
and each `next` link holds only the request or notification. A scope that uses no context resolves no state and allocates only the 32 B
mediator for Keryx. From then on a `Send` allocates nothing. A handler's or behavior method's transient dependencies live as long as the
scope, and the behaviors and handlers of one pipeline that inject the same transient share one instance.

Singletons and hosted services that need the mediator create a scope with `IServiceScopeFactory`. A singleton that injects
`KeryxMediator`, `IMediator`, `ISender` or `IPublisher` passes `ValidateOnBuild` and is resolved (the mediator is transient), and gets a
mediator over the root provider: with `ValidateScopes` on, as in ASP.NET Core's Development environment, its first call that needs a
pipeline context throws ("Cannot resolve scoped service '…KeryxMediator+__KeryxScopeState' from root provider"). That is a deliberate
trade-off, like the one MediatR makes with its transient `IMediator`, and the generator does not warn about it (open-questions OQ-76,
decided, and "The mediator is transient over a scoped state").

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
| <a id="krx015"></a>KRX015      | Error    | A handler class declares a member named `InvokeAsync` or starting with `__keryx_` (the generated fields and static entry use it).                                                                            |
| <a id="krx016"></a>KRX016      | Error    | Outside a host, a type a pipeline needs (request, response, notification, injected parameter type) or a behavior class or method is not public. In a host, one is narrower than `internal` or file-local. |
| <a id="krx017"></a>KRX017      | Warning  | A handler's response type differs from the request's only in nullability.                                                                                     |
| <a id="krx018"></a>KRX018      | Error    | A `[FromKeyedServices]` key on a handler or behavior method parameter is not a compile-time constant, or uses the parameterless form. |
| <a id="krx019"></a>KRX019      | Error    | A behavior class has an unsupported shape: not `static` or generic, a kind attribute without a matching behavior method, two methods of one kind, a partially open method or one with other type parameters, an open method whose message type parameter lacks the kind's message constraint (directly or through a derived interface), a `next` without the `struct` and interface constraint, a wrong return type, or a `static` `Handle`/`HandleAsync` whose kind the class does not carry, that has no valid `next`, or whose `next` and message type belong to different kinds. |
| <a id="krx021"></a>KRX021      | Info     | A struct request is boxed when sent through `ISender`; the host's typed overload avoids it.                                                                   |
| <a id="krx022"></a>KRX022      | Error    | The project compiles with C# below 14. Remove the `LangVersion` setting or set it to 14 or later. The generators emit nothing until then.                     |
| <a id="krx023"></a>KRX023      | Error    | A handler takes a request or stream request declared in another assembly. A request's handler lives in the request's own assembly; move the request or the handler. Notifications are not affected. |
| <a id="krx024"></a>KRX024      | Error    | A request or stream request class or record is neither `sealed` nor abstract (or `closed`). Seal it, or make it an abstract base of sealed requests. Notifications are not affected. Code fix: make it `sealed`. |
| <a id="krx025"></a>KRX025      | Error    | A handler class implements `IDisposable` or `IAsyncDisposable` (directly, or through a base type or an interface). Handlers are stateless and Keryx never disposes one: inject the disposable service instead. |
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
generic method; n/a marks a scenario the library has no API for. How each library is set up is in the ADR's
[Benchmarks](docs/adr/0001-keryx-mediator.md#benchmarks) section, and the checks behind it in
[open-questions.md](docs/adr/open-questions.md) (OQ-51, and OQ-72 for the real-world scenarios).

Each cell is the mean time per call and the bytes allocated per call (exact: BenchmarkDotNet's reports round them to KB once a column
reaches 1 KB, so they are taken from its log, total bytes over operations). One local run of the whole comparison suite on 2026-11-27, at
commit `3575a50` (built from a clean `git archive` of it, after owner decisions 34 to 36; decision 37), first under the JIT and then under
NativeAOT ([below](#nativeaot)), with the settings below. Every number comes from a run with no other build, test or benchmark busy on
the machine: another session there started some during the JIT run, so the five classes it overlapped (`SendYieldDepth1`, `3` and `8`,
`StreamDepth0` and `StreamDepth3`) were run again on their own, and the NativeAOT run went one class at a time, each started only when
nothing else was busy and run again when something started while it ran. "Decision 32's run" in the notes is the previous full run,
earlier the same day at `1652e50` on the same machine, before decisions 34 to 36.

The tables predate owner decision 46 (2026-11-28: a transient mediator over a scoped state), owner decisions 42 and 44 (2026-11-28:
no handler registrations; the host's own handlers called through a static entry) and owner decision 43 (2026-11-28: a context's
handlers and behaviors share its services), which change Keryx's scope-per-request rows; the notes below give those rows as measured
after each, on the same machine and settings (a run of the affected classes only, compared with a run of the code before it;
open-questions, "The mediator is transient over a scoped state", "Handlers are not registered" and "Handlers and behaviors share a
context's services"). The next full run refreshes the tables.

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
- With a yielding handler, the time is the thread-pool hop of `Task.Yield()` and varies by a few hundred nanoseconds from run to run
  (1,101 to 1,771 ns in the table below, 844 to 1,384 ns in decision 32's run, with no pattern by library or depth); the allocation
  column is the meaningful one. The direct call's 112 B (averaged over thread-pool work, so it moves by a byte or two between runs) is the
  handler's own async state machine, so a library that shows the same adds nothing.
- In the stream scenarios the direct call's 120 B is the handler's iterator. Each stream behavior adds its own iterator.
- In the real-world scenarios the handler's work (the `Pong` it allocates, the log messages, the query) is in every row, the direct call
  included, so compare each row with the direct call rather than with zero.

### Send, synchronous handler

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.01 ns · 0 B | 0.01 ns · 0 B | 0.01 ns · 0 B | 0.00 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.45 ns · 0 B |
| **Keryx**, `IMediator` | 3.27 ns · 0 B | 3.37 ns · 0 B | 2.69 ns · 0 B | 3.03 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 29.0 ns · 64 B | 44.6 ns · 160 B | 68.2 ns · 352 B | 123 ns · 832 B |
| Mediator 3.0.2 scoped, `IMediator` | 32.9 ns · 64 B | 48.9 ns · 160 B | 71.3 ns · 352 B | 122 ns · 832 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.40 ns · 0 B | 1.26 ns · 0 B | 5.47 ns · 0 B | 14.9 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 3.89 ns · 0 B | 4.89 ns · 0 B | 9.87 ns · 0 B | 18.6 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 31.5 ns · 200 B | 71.7 ns · 440 B | 103 ns · 728 B | 188 ns · 1,448 B |
| Immediate.Handlers 4.4.0, `Handler` class | 8.26 ns · 0 B | 8.31 ns · 0 B | 10.3 ns · 0 B | 36.3 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 14.2 ns · 0 B | 10.3 ns · 0 B | 11.1 ns · 0 B | 16.8 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 22.6 ns · 24 B | 24.4 ns · 24 B | 25.9 ns · 24 B | 25.3 ns · 24 B |

Generic dispatch tests each sealed request type with a type pattern (open-questions, "Requests are sealed"): Keryx's `IMediator` rows
measure 2.69 to 3.37 ns here, 2.44 to 2.74 ns in decision 32's run and 3.22 to 3.51 ns in the run of 2026-11-26, before the type
patterns (at this scale a nanosecond moves between runs).

### Send, handler awaits `Task.Yield()`

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,328 ns · 112 B | 1,391 ns · 112 B | 1,358 ns · 112 B | 1,291 ns · 112 B |
| **Keryx**, typed overload | 1,297 ns · 113 B | 1,242 ns · 113 B | 1,266 ns · 113 B | 1,259 ns · 112 B |
| **Keryx**, `IMediator` | 1,730 ns · 113 B | 1,304 ns · 112 B | 1,385 ns · 112 B | 1,338 ns · 112 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,750 ns · 176 B | 1,405 ns · 272 B | 1,341 ns · 464 B | 1,416 ns · 944 B |
| Mediator 3.0.2 scoped, `IMediator` | 1,365 ns · 176 B | 1,293 ns · 272 B | 1,303 ns · 464 B | 1,262 ns · 944 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,373 ns · 113 B | 1,227 ns · 113 B | 1,119 ns · 113 B | 1,162 ns · 112 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,382 ns · 113 B | 1,220 ns · 112 B | 1,186 ns · 112 B | 1,138 ns · 112 B |
| MediatR 15.0.0, `IMediator` | 1,628 ns · 232 B | 1,206 ns · 472 B | 1,129 ns · 760 B | 1,101 ns · 1,480 B |
| Immediate.Handlers 4.4.0, `Handler` class | 1,771 ns · 373 B | 1,255 ns · 372 B | 1,274 ns · 373 B | 1,137 ns · 373 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,365 ns · 246 B | 1,215 ns · 245 B | 1,175 ns · 246 B | 1,215 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,393 ns · 279 B | 1,227 ns · 279 B | 1,286 ns · 279 B | 1,176 ns · 279 B |

### Stream of 100 items

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 646 ns · 120 B | 644 ns · 120 B |
| **Keryx**, typed overload | 648 ns · 120 B | 2,732 ns · 648 B |
| **Keryx**, `IMediator` | 639 ns · 120 B | 2,804 ns · 648 B |
| Mediator 3.0.2 scoped, `Mediator` class | 681 ns · 184 B | 2,867 ns · 1,048 B |
| Mediator 3.0.2 scoped, `IMediator` | 666 ns · 184 B | 2,804 ns · 1,048 B |
| Mediator 3.0.2 singleton, `Mediator` class | 639 ns · 120 B | 2,723 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 643 ns · 120 B | 2,759 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,341 ns · 512 B | 7,599 ns · 2,336 B |
| Immediate.Handlers 4.4.0, `Handler` class | 638 ns · 120 B | 2,726 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 640 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 638 ns · 144 B | n/a |

With 3 stream behaviors Keryx allocates 648 B per `Stream` (typed and `IMediator`), 32 B less than the 680 B before OQ-65: a pipeline
that resolves nothing has no pipeline context and its `next` links hold only the request (open-questions OQ-65). The generic `Stream`
slowdown with no stream behaviors that interleaved reruns found after the type patterns (20 to 60 ns, 3 to 9%; owner decision 22) does not
show in this run either: `IMediator` measured 639.2 ns (standard deviation 3.3 ns), the typed overload 648.2 ns (3.1 ns) and the direct
call 645.8 ns (2.2 ns), all three below the 649 to 661 ns those reruns measured before the type patterns. It stays on the release
checklist ([whats-left.md](docs/whats-left.md)).

### Real-world Ping → Pong

Beside the depth scenarios above, real-world scenarios send a `Ping` that a handler answers with a new `Pong` record, or publish a
`Pinged` notification, closer to what an application does (owner decision 30). The handler bodies and behaviors are the same in every
library.

Requests:

- `RealWorldPing`: the handler alone, from the long-lived scope.
- `RealWorldPingScopePerRequest`: the same with a scope per request: the measured call creates a DI scope, resolves the mediator (or the
  library's entry point) from it, sends and disposes the scope, the way an ASP.NET Core request does. For Keryx this includes creating the
  transient `KeryxMediator` (and, where a pipeline has a context, resolving the scope state), which the long-lived rows hide.
- `RealWorldBehaviors`: a logging behavior (Microsoft.Extensions.Logging, two messages per request, really formatted by an in-memory
  provider that keeps the last 1,024) and a validation behavior (a hand-written `IValidator<TRequest>`, scoped) around a handler that
  injects a scoped repository doing a dictionary lookup, from the long-lived scope.
- `RealWorldBehaviorsScopePerRequest`: the same with a scope per request, so the repository, the validator and the pipeline (for Keryx,
  the pipeline context that holds the behaviors' services) are created for each call.
- `RealWorldSqlite`: a handler that runs a parameterized scalar query against an in-memory SQLite database (Microsoft.Data.Sqlite), seeded
  once per process, on the container's singleton open connection (opened in setup), from the long-lived scope.

| Subject | `Ping` | `Ping`, scope per request | Behaviors | Behaviors, scope per request | SQLite query |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hand-written direct call | 2.34 ns · 32 B | 50.4 ns · 368 B | 120 ns · 520 B | 221 ns · 920 B | 1,378 ns · 1,144 B |
| **Keryx**, typed overload | 2.38 ns · 32 B | 55.1 ns · 448 B | 126 ns · 520 B | 328 ns · 1,408 B | 1,398 ns · 1,144 B |
| **Keryx**, `IMediator` | 6.61 ns · 32 B | 67.9 ns · 448 B | 133 ns · 520 B | 339 ns · 1,408 B | 1,452 ns · 1,144 B |
| Mediator 3.0.2 scoped, `Mediator` class | 31.9 ns · 96 B | 93.0 ns · 472 B | 205 ns · 776 B | 394 ns · 1,648 B | 1,465 ns · 1,208 B |
| Mediator 3.0.2 scoped, `IMediator` | 30.2 ns · 96 B | 97.6 ns · 472 B | 196 ns · 776 B | 412 ns · 1,648 B | 1,451 ns · 1,208 B |
| Mediator 3.0.2 singleton, `Mediator` class | 2.94 ns · 32 B | 26.0 ns · 160 B | n/a | n/a | 1,417 ns · 1,144 B |
| Mediator 3.0.2 singleton, `IMediator` | 7.98 ns · 32 B | 33.4 ns · 160 B | n/a | n/a | 1,402 ns · 1,144 B |
| MediatR 15.0.0, `IMediator` | 38.5 ns · 232 B | 75.5 ns · 432 B | 262 ns · 1,176 B | 333 ns · 1,608 B | 1,418 ns · 1,344 B |
| Immediate.Handlers 4.4.0, `Handler` class | 11.8 ns · 32 B | 83.1 ns · 400 B | 142 ns · 520 B | 301 ns · 1,400 B | 1,417 ns · 1,144 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 21.0 ns · 32 B | n/a | n/a | n/a | 1,423 ns · 1,168 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 24.8 ns · 56 B | 54.1 ns · 208 B | 192 ns · 544 B | 252 ns · 928 B | 1,403 ns · 1,168 B |

- `Ping` from the long-lived scope: Keryx's typed overload is within a tenth of a nanosecond of the direct call (the 32 B is the `Pong`).
- Scope per request: in the table (before decision 46) Keryx's typed overload measures 55.1 ns against the direct call's 50.4 ns and
  allocates 80 B more, the scoped `KeryxMediator` (one field per pipeline context, 104 B in this host) and its scope entry. Since
  decision 46 the mediator is a transient 32 B object that takes no scope entry, and `Ping` resolves no scope state (its handler is
  static): 29.9 ns and 192 B typed, 34.8 ns and 192 B through `IMediator`, against the direct call's 49.2 ns and 368 B and
  ZeroAlloc.Mediator's 52.2 ns and 208 B (55.4 and 67.4 ns, 448 B, before it, in the same session). Mediator singleton allocates least
  here because nothing scoped is resolved.
- Behaviors from the long-lived scope: Keryx's typed overload measures 126 ns against the direct call's 120 ns, with the same 520 B; the
  other libraries measure 142 to 262 ns.
- Behaviors with a scope per request: Keryx's typed overload measures 328 ns and 1,408 B against the direct call's 221 ns and 920 B: the
  first call in a scope creates the mediator and the pipeline context, which resolves the handler's and the behaviors' services. Since
  decision 46 it also resolves the scope state (a 96 B object with a field per pipeline context, which takes the scope entry the scoped
  mediator had): 336 ns and 1,432 B typed, 339 ns and 1,432 B through `IMediator` (322 and 331 ns, 1,408 B, before it, in the same
  session; the direct call 210 ns). Since owner decisions 42 and 44 the handler is neither registered nor constructed (the context holds
  the repository and calls the handler's static entry), so the scope's resolved services stay within their first arrays: 266 ns and
  1,048 B typed, 270 ns and 1,048 B through `IMediator` (337 and 340 ns, 1,432 B, before it, in the same session; the direct call 212 ns,
  ZeroAlloc.Mediator 247 ns and 928 B). The other
  libraries measure 252 to 412 ns; ZeroAlloc.Mediator, whose behaviors resolve their services from the provider on every call, is the
  fastest here (252 ns), while from the long-lived scope it measures 192 ns, behind Immediate.Handlers (142 ns) and Keryx (126 to
  133 ns).
- The SQLite query (about 1.4 µs and 1,144 B) is most of every row in the last column.
- `IMediator` with a scope per request allocates what the typed overload allocates. In the table (decision 35's transient forwarders,
  open-questions OQ-74) `Ping` per request measures 67.9 ns against the typed overload's 55.1 ns, the forwarder resolving
  `KeryxMediator` as a second container call; since decision 46 every service type creates the mediator itself, and the gap is 4.9 ns,
  about the generic dispatch (5.80 ns against 2.25 ns from the long-lived scope).

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
| Hand-written direct call | 128 ns · 456 B | 457 ns · 1,232 B | 208 ns · 608 B | 536 ns · 1,384 B |
| **Keryx**, typed overload | 164 ns · 560 B | 570 ns · 1,336 B | 251 ns · 712 B | 672 ns · 1,488 B |
| **Keryx**, `IMediator` | 172 ns · 560 B | 580 ns · 1,336 B | 256 ns · 712 B | 673 ns · 1,488 B |
| Mediator 3.0.2 scoped, `Mediator` class | 242 ns · 888 B | 701 ns · 2,104 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 244 ns · 888 B | 716 ns · 2,104 B | n/a | n/a |
| MediatR 15.0.0, `IMediator` | 245 ns · 1,248 B | 665 ns · 2,496 B | n/a | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 128 ns · 456 B | 376 ns · 840 B | n/a | n/a |

Handlers that await the SQLite query, from the long-lived scope:

| Subject | Sequential, 1 handler | Sequential, 5 handlers | Parallel, 1 handler | Parallel, 5 handlers |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,482 ns · 1,176 B | 7,226 ns · 5,880 B | 1,455 ns · 1,176 B | 7,381 ns · 5,880 B |
| **Keryx**, typed overload | 1,451 ns · 1,176 B | 7,459 ns · 5,880 B | 1,481 ns · 1,176 B | 7,378 ns · 5,880 B |
| **Keryx**, `IMediator` | 1,499 ns · 1,176 B | 7,267 ns · 5,880 B | 1,495 ns · 1,176 B | 7,434 ns · 5,880 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,534 ns · 1,176 B | 7,368 ns · 5,880 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 1,533 ns · 1,176 B | 7,500 ns · 5,880 B | n/a | n/a |
| Mediator 3.0.2 singleton, `Mediator` class | 1,455 ns · 1,176 B | 7,515 ns · 5,880 B | 1,442 ns · 1,176 B | 7,290 ns · 5,880 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,471 ns · 1,176 B | 7,456 ns · 5,880 B | 1,515 ns · 1,176 B | 7,336 ns · 5,880 B |
| MediatR 15.0.0, `IMediator` | 1,634 ns · 1,952 B | 8,020 ns · 7,648 B | 1,665 ns · 2,232 B | 7,900 ns · 8,032 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,475 ns · 1,208 B | 7,630 ns · 6,040 B | 1,514 ns · 1,208 B | 7,470 ns · 6,040 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,502 ns · 1,208 B | 7,434 ns · 6,040 B | 1,519 ns · 1,208 B | 7,484 ns · 6,040 B |

- Per request scope, Keryx's typed overload measures 164 ns (1 handler) and 570 ns (5 handlers) against the direct call's 128 ns and
  457 ns. Its `IMediator` rows allocate what the typed ones allocate (560 B and 1,336 B; 712 B and 1,488 B with the notification
  behavior) and measure 172 ns and 580 ns (256 ns and 673 ns with the behavior). In decision 32's run, with the scoped forwarding
  `IMediator` registration, they allocated 360 B and 800 B more and measured 219 ns and 634 ns: the scope entry that registration added
  (OQ-74), which the transient forwarders no longer add. Since decision 46 each of these scopes resolves the scope state (96 B) and a 32 B
  mediator, 24 B more than the 104 B scoped mediator they replace: typed, then `IMediator`, 174 and 180 ns with 584 B (1 handler), 579 and 566 ns with 1,360 B
  (5 handlers), 254 and 265 ns with 736 B and 639 and 646 ns with 1,512 B with the notification behavior (162 and 168 ns, 555 and 572 ns,
  247 and 252 ns, 651 and 670 ns before it, in the same session; the direct call 124, 450, 200 and 520 ns). Since owner decisions 42
  and 44 no handler is registered or constructed (each context holds its handlers' services and calls their static entries): typed, then
  `IMediator`, 143 and 147 ns with 560 B (1 handler), 405 and 402 ns with 880 B (5 handlers), 226 and 232 ns with 712 B and 481 and
  486 ns with 1,032 B with the notification behavior (174 and 178 ns, 563 and 573 ns, 258 and 260 ns, 636 and 654 ns, and 584, 1,360,
  736 and 1,512 B, before it, in the same session; the direct call 123, 446, 200 and 518 ns; ZeroAlloc.Mediator 128 ns and 456 B, 366 ns
  and 840 B). With 5 handlers Keryx is now below the direct call (whose handlers are registered scoped). Since owner decision 43 a
  publish context resolves the repository and the logger once for all its handlers (and the behavior's logger with them), so the five
  handlers' ten fields become two: typed, then `IMediator`, 311 and 314 ns with 816 B (5 handlers), 219 and 226 ns with 704 B and 381
  and 390 ns with 960 B with the notification behavior; 1 handler unchanged at 143 and 146 ns with 560 B (397 and 398 ns, 880 B; 226
  and 234 ns, 712 B; 478 and 481 ns, 1,032 B; 145 and 144 ns, before it, in the same session; the direct call 444, 202, 510 and 122 ns;
  ZeroAlloc.Mediator 364 ns and 840 B with 5 handlers). Over three interleaved rounds the publish to 5 handlers measured 398 ns before
  and 305 ns after (typed), ZeroAlloc.Mediator 366 ns.
- SQLite's async methods complete synchronously, so no row waits on I/O: the async and parallel rows measure each library's path for
  handlers that return completed tasks, not overlapping work, and each library's parallel strategy measures within 3% of its sequential
  publish. Keryx allocates what the direct call allocates in all four columns.

Here the direct call is a hand-written handler class (or a publisher class taking the handler classes) resolved from DI that does the
same work, the behaviors' logging and validation included, so the ratio is each mediator's cost on top of that work. n/a, besides the
entry points a library does not have: Mediator singleton in the two request behavior scenarios and the per-request publish (its singleton
handlers and behaviors cannot take the scoped repository and validator); ZeroAlloc.Mediator's static `Mediator` in the two request
behavior scenarios (its static `Send` passes behaviors a `null` state, and the library's static-path alternative would need another
behavior body that opens a scope inside each call) and in every scope-per-request scenario (it has no scope); every library but Keryx in
the notification-behavior pair; Immediate.Handlers in every notification scenario (it has none); Mediator scoped in the parallel pair (its
parallel publisher is the singleton project's). The setup rules are in the ADR's [Benchmarks](docs/adr/0001-keryx-mediator.md#benchmarks)
section (owner decision 33) and the check behind each n/a cell in [open-questions.md](docs/adr/open-questions.md) (OQ-72). Run them on
their own with `--filter '*RealWorld*'`.

### Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only)

The handler of a request with no response returns a non-generic `ValueTask` or `Task`, so Keryx adapts it to `ValueTask<Unit>` through
its one generated async path (`UnitAdapter.ToUnit`, pooled builder on net10+). The direct call awaits the same handler body, so its
allocation is the handler's own state machine; the Performance contract says the adapter adds 0 bytes to it in steady state. The other
libraries are left out: this checks Keryx's contract, not a comparison.

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,195 ns · 97 B | 907 ns · 96 B |
| **Keryx**, typed overload | 1,229 ns · 96 B | 1,202 ns · 96 B |
| **Keryx**, `IMediator` | 1,465 ns · 96 B | 1,337 ns · 96 B |

### Publish, sequential, synchronous handlers

The direct call calls the handlers in turn without an `async` method of its own (it only falls back to one when a handler does not
complete synchronously), so it is the floor. With one handler the JIT inlines all of it and the time is below what BenchmarkDotNet
resolves.

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.01 ns · 0 B | 0.53 ns · 0 B |
| **Keryx**, typed overload | 0.44 ns · 0 B | 0.48 ns · 0 B |
| **Keryx**, `IMediator` | 3.31 ns · 0 B | 4.07 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 16.6 ns · 0 B | 33.7 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 18.2 ns · 0 B | 27.6 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.07 ns · 0 B | 15.4 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 10.5 ns · 0 B | 20.8 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 123 ns · 768 B | 322 ns · 1,728 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 8.80 ns · 0 B | 8.82 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 26.7 ns · 24 B | 55.6 ns · 120 B |

A publish whose handlers are all static and whose behaviors inject nothing (every row here) has no pipeline context and its publish
strategy is static (open-questions OQ-70): Keryx's typed overload measures 0.44 ns and 0.48 ns (0.34 and 0.44 ns in decision 32's run,
0.65 and 0.84 ns before OQ-70), inlined like the direct call, and `IMediator` 3.31 ns and 4.07 ns (3.19 and 3.93 ns in decision 32's run;
at this scale a nanosecond moves between runs), all 0 B.

### NativeAOT

The same comparison scenarios also run with BenchmarkDotNet's NativeAOT toolchain on net10.0 (owner decision 29), each benchmark compiled
ahead of time instead of JIT-compiled with tiering and Dynamic PGO. That run followed the JIT run above, from the same copy of the
commit, with the same settings, one class at a time (above):

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
  measures 1.34 to 1.35 ns at every depth: that is the floor of a NativeAOT `Send` row, and the sub-nanosecond JIT `Send` rows have no
  NativeAOT equivalent. The `Publish` rows of the direct call and Keryx's typed overload measure under a nanosecond (the direct call
  0.04 ns and 0.67 ns, Keryx 0.68 ns both), so compare those by order of magnitude. The direct call's `Publish` to one handler is a
  call that returns `default`, and BenchmarkDotNet 0.15.8 cannot measure it with the job above: its pilot stage reads 0 ns for the
  first 16 calls and throws. So the runner gives that one benchmark's NativeAOT job a fixed invocation count, 160,000,000 calls per
  iteration, which skips the pilot, the job otherwise the same (owner decision 39, open-questions OQ-77; its summary row shows
  `InvocationCount` 160000000, the others `Default`, and BenchmarkDotNet flags it as indistinguishable from an empty method). Its cell
  is from the run that proved it (2026-11-27, `PublishTo1` under NativeAOT with these settings, on the same machine and SDK): 0.04 ns
  (0.06 ns in decision 37's run, where it was run alone with `--invocationCount 160000000`); the class's other rows are decision 37's.
- The builds BenchmarkDotNet makes have no verification probe (owner decision 34, OQ-73: the runner sets `KeryxBenchmarksProbe=false`
  before they build, and every comparison benchmark's setup fails a build that still has it, so CI's Dry runs check it). Decision 32's
  run still had it under NativeAOT, where it was not compiled away as under the JIT: each handler and behavior call carried a field load
  and a branch, so Keryx's typed `Send` grew from 1.59 to 2.67 ns between depth 0 and depth 8 and its `IMediator` `Send` from 3.26 to
  5.19 ns. Without it they measure 1.40 to 1.41 ns and 3.10 to 3.17 ns at every depth, and the direct call's floor moved from 1.49 to
  1.52 ns down to 1.34 to 1.35 ns.
- The behaviors and the direct call read the message type's name once per type, through `MessageName<T>` (owner decision 36, OQ-75).
  Under NativeAOT `typeof(T).Name` allocates a new string on every read (48 B for `TypicalPing`; nothing under the JIT), and in decision
  32's run the behaviors read it for both log messages while the direct call used `nameof`, which put 96 B more in every library's
  behavior rows under NativeAOT. In this run Keryx's behavior rows allocate what they allocate under the JIT (520 B, 1,408 B per request
  scope, 712 B and 1,488 B for the notification behavior; 616 B, 1,504 B, 808 B and 1,584 B then), and the other libraries' behavior rows
  lost the same 96 B. In the scope-per-request rows, Microsoft.Extensions.DependencyInjection resolves without generated IL under
  NativeAOT, so every row's scope cost changes, the direct call's included.

Send, synchronous handler:

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.01 ns → 1.34 ns · 0 B | 0.01 ns → 1.34 ns · 0 B | 0.01 ns → 1.34 ns · 0 B | 0.00 ns → 1.35 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns → 1.40 ns · 0 B | 0.00 ns → 1.41 ns · 0 B | 0.00 ns → 1.41 ns · 0 B | 0.45 ns → 1.41 ns · 0 B |
| **Keryx**, `IMediator` | 3.27 ns → 3.11 ns · 0 B | 3.37 ns → 3.10 ns · 0 B | 2.69 ns → 3.16 ns · 0 B | 3.03 ns → 3.17 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 29.0 ns · 64 B → 51.8 ns · 48 B | 44.6 ns · 160 B → 79.2 ns · 128 B | 68.2 ns · 352 B → 99.0 ns · 288 B | 123 ns · 832 B → 153 ns · 688 B |
| Mediator 3.0.2 scoped, `IMediator` | 32.9 ns · 64 B → 55.4 ns · 48 B | 48.9 ns · 160 B → 82.7 ns · 128 B | 71.3 ns · 352 B → 102 ns · 288 B | 122 ns · 832 B → 156 ns · 688 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.40 ns → 1.99 ns · 0 B | 1.26 ns → 2.89 ns · 0 B | 5.47 ns → 5.61 ns · 0 B | 14.9 ns → 17.1 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 3.89 ns → 4.77 ns · 0 B | 4.89 ns → 5.88 ns · 0 B | 9.87 ns → 8.90 ns · 0 B | 18.6 ns → 20.3 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 31.5 ns · 200 B → n/a | 71.7 ns · 440 B → n/a | 103 ns · 728 B → n/a | 188 ns · 1,448 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 8.26 ns → 20.7 ns · 0 B | 8.31 ns → 19.9 ns · 0 B | 10.3 ns → 21.8 ns · 0 B | 36.3 ns → 24.7 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 14.2 ns → 18.0 ns · 0 B | 10.3 ns → 18.6 ns · 0 B | 11.1 ns → 20.4 ns · 0 B | 16.8 ns → 25.5 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 22.6 ns → 38.9 ns · 24 B | 24.4 ns → 40.6 ns · 24 B | 25.9 ns → 43.8 ns · 24 B | 25.3 ns → 48.0 ns · 24 B |

Send, handler awaits `Task.Yield()` (the time is the thread-pool hop under both runtimes; compare the allocations):

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,328 ns · 112 B → 791 ns · 114 B | 1,391 ns → 738 ns · 112 B | 1,358 ns · 112 B → 808 ns · 114 B | 1,291 ns · 112 B → 778 ns · 114 B |
| **Keryx**, typed overload | 1,297 ns → 757 ns · 113 B | 1,242 ns → 803 ns · 113 B | 1,266 ns → 748 ns · 113 B | 1,259 ns · 112 B → 815 ns · 113 B |
| **Keryx**, `IMediator` | 1,730 ns · 113 B → 758 ns · 114 B | 1,304 ns · 112 B → 770 ns · 113 B | 1,385 ns · 112 B → 774 ns · 113 B | 1,338 ns · 112 B → 805 ns · 113 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,750 ns · 176 B → 792 ns · 161 B | 1,405 ns · 272 B → 828 ns · 240 B | 1,341 ns · 464 B → 909 ns · 400 B | 1,416 ns · 944 B → 857 ns · 800 B |
| Mediator 3.0.2 scoped, `IMediator` | 1,365 ns · 176 B → 770 ns · 161 B | 1,293 ns · 272 B → 839 ns · 240 B | 1,303 ns · 464 B → 868 ns · 400 B | 1,262 ns · 944 B → 871 ns · 800 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,373 ns · 113 B → 762 ns · 114 B | 1,227 ns → 802 ns · 113 B | 1,119 ns → 808 ns · 113 B | 1,162 ns · 112 B → 726 ns · 113 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,382 ns · 113 B → 783 ns · 114 B | 1,220 ns · 112 B → 820 ns · 113 B | 1,186 ns · 112 B → 771 ns · 113 B | 1,138 ns · 112 B → 1,011 ns · 113 B |
| MediatR 15.0.0, `IMediator` | 1,628 ns · 232 B → n/a | 1,206 ns · 472 B → n/a | 1,129 ns · 760 B → n/a | 1,101 ns · 1,480 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 1,771 ns · 373 B → 852 ns · 374 B | 1,255 ns · 372 B → 876 ns · 371 B | 1,274 ns · 373 B → 889 ns · 372 B | 1,137 ns · 373 B → 915 ns · 369 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,365 ns → 833 ns · 246 B | 1,215 ns · 245 B → 830 ns · 246 B | 1,175 ns · 246 B → 804 ns · 247 B | 1,215 ns → 800 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,393 ns · 279 B → 813 ns · 280 B | 1,227 ns · 279 B → 818 ns · 280 B | 1,286 ns → 828 ns · 279 B | 1,176 ns → 832 ns · 279 B |

Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only):

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,195 ns → 786 ns · 97 B | 907 ns → 716 ns · 96 B |
| **Keryx**, typed overload | 1,229 ns → 867 ns · 96 B | 1,202 ns → 773 ns · 96 B |
| **Keryx**, `IMediator` | 1,465 ns → 792 ns · 96 B | 1,337 ns → 776 ns · 96 B |

Stream of 100 items:

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 646 ns → 960 ns · 120 B | 644 ns → 914 ns · 120 B |
| **Keryx**, typed overload | 648 ns → 926 ns · 120 B | 2,732 ns → 4,208 ns · 648 B |
| **Keryx**, `IMediator` | 639 ns → 959 ns · 120 B | 2,804 ns → 4,184 ns · 648 B |
| Mediator 3.0.2 scoped, `Mediator` class | 681 ns · 184 B → 955 ns · 168 B | 2,867 ns · 1,048 B → 4,098 ns · 984 B |
| Mediator 3.0.2 scoped, `IMediator` | 666 ns · 184 B → 953 ns · 168 B | 2,804 ns · 1,048 B → 4,356 ns · 984 B |
| Mediator 3.0.2 singleton, `Mediator` class | 639 ns → 913 ns · 120 B | 2,723 ns → 4,066 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 643 ns → 940 ns · 120 B | 2,759 ns → 4,148 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,341 ns · 512 B → n/a | 7,599 ns · 2,336 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 638 ns → 934 ns · 120 B | 2,726 ns → 4,276 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 640 ns → 928 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 638 ns → 965 ns · 144 B | n/a |

Publish, sequential, synchronous handlers:

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.01 ns → 0.04 ns · 0 B | 0.53 ns → 0.67 ns · 0 B |
| **Keryx**, typed overload | 0.44 ns → 0.68 ns · 0 B | 0.48 ns → 0.68 ns · 0 B |
| **Keryx**, `IMediator` | 3.31 ns → 6.26 ns · 0 B | 4.07 ns → 6.48 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 16.6 ns → 41.1 ns · 0 B | 33.7 ns → 55.1 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 18.2 ns → 43.9 ns · 0 B | 27.6 ns → 58.1 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.07 ns → 11.4 ns · 0 B | 15.4 ns → 21.7 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 10.5 ns → 14.8 ns · 0 B | 20.8 ns → 23.7 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 123 ns · 768 B → n/a | 322 ns · 1,728 B → n/a |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 8.80 ns → 16.8 ns · 0 B | 8.82 ns → 18.4 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 26.7 ns → 54.6 ns · 24 B | 55.6 ns → 165 ns · 120 B |

Real-world requests (`Ping`, from the long-lived scope and per request, the behavior pair and the SQLite query):

| Subject | `Ping` | `Ping`, scope per request | Behaviors | Behaviors, scope per request | SQLite query |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hand-written direct call | 2.34 ns → 3.41 ns · 32 B | 50.4 ns → 81.7 ns · 368 B | 120 ns → 137 ns · 520 B | 221 ns · 920 B → 318 ns · 968 B | 1,378 ns · 1,144 B → 1,316 ns · 1,128 B |
| **Keryx**, typed overload | 2.38 ns → 3.52 ns · 32 B | 55.1 ns → 84.3 ns · 448 B | 126 ns → 150 ns · 520 B | 328 ns → 445 ns · 1,408 B | 1,398 ns · 1,144 B → 1,305 ns · 1,128 B |
| **Keryx**, `IMediator` | 6.61 ns → 5.00 ns · 32 B | 67.9 ns → 110 ns · 448 B | 133 ns → 155 ns · 520 B | 339 ns → 471 ns · 1,408 B | 1,452 ns · 1,144 B → 1,319 ns · 1,128 B |
| Mediator 3.0.2 scoped, `Mediator` class | 31.9 ns · 96 B → 54.1 ns · 80 B | 93.0 ns · 472 B → 188 ns · 488 B | 205 ns · 776 B → 248 ns · 728 B | 394 ns · 1,648 B → 673 ns · 1,728 B | 1,465 ns · 1,208 B → 1,392 ns · 1,176 B |
| Mediator 3.0.2 scoped, `IMediator` | 30.2 ns · 96 B → 58.7 ns · 80 B | 97.6 ns · 472 B → 196 ns · 488 B | 196 ns · 776 B → 249 ns · 728 B | 412 ns · 1,648 B → 696 ns · 1,728 B | 1,451 ns · 1,208 B → 1,384 ns · 1,176 B |
| Mediator 3.0.2 singleton, `Mediator` class | 2.94 ns → 3.62 ns · 32 B | 26.0 ns → 39.9 ns · 160 B | n/a | n/a | 1,417 ns · 1,144 B → 1,311 ns · 1,128 B |
| Mediator 3.0.2 singleton, `IMediator` | 7.98 ns → 6.34 ns · 32 B | 33.4 ns → 43.3 ns · 160 B | n/a | n/a | 1,402 ns · 1,144 B → 1,311 ns · 1,128 B |
| MediatR 15.0.0, `IMediator` | 38.5 ns · 232 B → n/a | 75.5 ns · 432 B → n/a | 262 ns · 1,176 B → n/a | 333 ns · 1,608 B → n/a | 1,418 ns · 1,344 B → n/a |
| Immediate.Handlers 4.4.0, `Handler` class | 11.8 ns → 20.2 ns · 32 B | 83.1 ns · 400 B → 143 ns · 432 B | 142 ns → 170 ns · 520 B | 301 ns · 1,400 B → 483 ns · 1,544 B | 1,417 ns · 1,144 B → 1,332 ns · 1,128 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 21.0 ns → 20.9 ns · 32 B | n/a | n/a | n/a | 1,423 ns · 1,168 B → 1,381 ns · 1,184 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 24.8 ns → 42.7 ns · 56 B | 54.1 ns · 208 B → 124 ns · 240 B | 192 ns · 544 B → 280 ns · 576 B | 252 ns · 928 B → 432 ns · 992 B | 1,403 ns · 1,168 B → 1,383 ns · 1,184 B |

Real-world publish per request scope:

| Subject | 1 handler | 5 handlers | 1 handler + behavior | 5 handlers + behavior |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 128 ns · 456 B → 228 ns · 528 B | 457 ns · 1,232 B → 778 ns · 1,496 B | 208 ns · 608 B → 325 ns · 688 B | 536 ns · 1,384 B → 904 ns · 1,656 B |
| **Keryx**, typed overload | 164 ns → 252 ns · 560 B | 570 ns → 876 ns · 1,336 B | 251 ns → 360 ns · 712 B | 672 ns → 986 ns · 1,488 B |
| **Keryx**, `IMediator` | 172 ns → 281 ns · 560 B | 580 ns → 920 ns · 1,336 B | 256 ns → 381 ns · 712 B | 673 ns → 1,031 ns · 1,488 B |
| Mediator 3.0.2 scoped, `Mediator` class | 242 ns · 888 B → 480 ns · 960 B | 701 ns · 2,104 B → 1,338 ns · 2,336 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 244 ns · 888 B → 492 ns · 960 B | 716 ns · 2,104 B → 1,348 ns · 2,336 B | n/a | n/a |
| MediatR 15.0.0, `IMediator` | 245 ns · 1,248 B → n/a | 665 ns · 2,496 B → n/a | n/a | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 128 ns · 456 B → 271 ns · 528 B | 376 ns · 840 B → 784 ns · 1,072 B | n/a | n/a |

Real-world publish to handlers that await the SQLite query:

| Subject | Sequential, 1 handler | Sequential, 5 handlers | Parallel, 1 handler | Parallel, 5 handlers |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,482 ns · 1,176 B → 1,402 ns · 1,160 B | 7,226 ns · 5,880 B → 6,996 ns · 5,800 B | 1,455 ns · 1,176 B → 1,429 ns · 1,160 B | 7,381 ns · 5,880 B → 7,078 ns · 5,800 B |
| **Keryx**, typed overload | 1,451 ns · 1,176 B → 1,396 ns · 1,160 B | 7,459 ns · 5,880 B → 7,019 ns · 5,800 B | 1,481 ns · 1,176 B → 1,443 ns · 1,160 B | 7,378 ns · 5,880 B → 7,133 ns · 5,800 B |
| **Keryx**, `IMediator` | 1,499 ns · 1,176 B → 1,407 ns · 1,160 B | 7,267 ns · 5,880 B → 7,040 ns · 5,800 B | 1,495 ns · 1,176 B → 1,414 ns · 1,160 B | 7,434 ns · 5,880 B → 7,113 ns · 5,800 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,534 ns · 1,176 B → 1,460 ns · 1,160 B | 7,368 ns · 5,880 B → 7,222 ns · 5,800 B | n/a | n/a |
| Mediator 3.0.2 scoped, `IMediator` | 1,533 ns · 1,176 B → 1,474 ns · 1,160 B | 7,500 ns · 5,880 B → 7,228 ns · 5,800 B | n/a | n/a |
| Mediator 3.0.2 singleton, `Mediator` class | 1,455 ns · 1,176 B → 1,420 ns · 1,160 B | 7,515 ns · 5,880 B → 7,137 ns · 5,800 B | 1,442 ns · 1,176 B → 1,447 ns · 1,160 B | 7,290 ns · 5,880 B → 7,017 ns · 5,800 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,471 ns · 1,176 B → 1,441 ns · 1,160 B | 7,456 ns · 5,880 B → 7,319 ns · 5,800 B | 1,515 ns · 1,176 B → 1,438 ns · 1,160 B | 7,336 ns · 5,880 B → 7,182 ns · 5,800 B |
| MediatR 15.0.0, `IMediator` | 1,634 ns · 1,952 B → n/a | 8,020 ns · 7,648 B → n/a | 1,665 ns · 2,232 B → n/a | 7,900 ns · 8,032 B → n/a |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,475 ns · 1,208 B → 1,485 ns · 1,232 B | 7,630 ns · 6,040 B → 7,685 ns · 6,160 B | 1,514 ns · 1,208 B → 1,575 ns · 1,232 B | 7,470 ns · 6,040 B → 7,478 ns · 6,160 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,502 ns · 1,208 B → 1,528 ns · 1,232 B | 7,434 ns · 6,040 B → 7,421 ns · 6,160 B | 1,519 ns · 1,208 B → 1,541 ns · 1,232 B | 7,484 ns · 6,040 B → 7,579 ns · 6,160 B |

- Without behaviors, Keryx's typed overload stays at the direct call's floor: 1.40 ns against 1.34 ns at depth 0, 0.68 ns against
  0.67 ns for a publish to 5 handlers, 3.52 ns against 3.41 ns for the real-world `Ping` and 84.3 ns against 81.7 ns for it per request
  scope. Behaviors add nothing measurable (1.41 ns at depth 8), still 0 B; Mediator singleton measures 1.99 to 20.3 ns over the same
  depths (class and `IMediator`), the other libraries 18.0 to 156 ns.
- Outside the yielding scenarios, Keryx allocates exactly what it allocates under the JIT, the behavior rows included, except in the
  SQLite and async-handler rows (16 B less per query), where the direct call's allocations change by the same amount.
- Per request scope, Keryx's `IMediator` rows allocate what its typed rows allocate here too; `Ping` per request measures 110 ns against
  84.3 ns, and a publish to 1 and 5 handlers 281 ns and 920 ns against 252 ns and 876 ns (decision 32's run, with the scoped forwarders:
  132 ns against 85.4 ns, and 338 ns and 1,019 ns with 360 B and 800 B more).
- With a yielding handler every subject measured 726 to 1,011 ns under NativeAOT. Keryx allocates 113 to 114 B, as the direct call does
  (112 to 114 B). Mediator scoped allocates less than under the JIT (161, 240, 400 and 800 B at depths 0, 1, 3 and 8, against 176, 272,
  464 and 944 B), as in its synchronous rows; Mediator singleton, Immediate.Handlers and ZeroAlloc.Mediator stay within 4 B of their JIT
  allocations. With an `IRequest` handler Keryx allocates 96 B, as the direct call does (96 to 97 B).
- Streams cost 40 to 60% more for every subject (the direct call 646 ns under the JIT, 960 ns under NativeAOT); Keryx's allocations do
  not change.
- The real-world behavior rows: Keryx's typed overload measures 150 ns from the long-lived scope (the direct call 137 ns) and 445 ns per
  request (318 ns); the other libraries 170 to 280 ns and 432 to 696 ns.

Run them yourself (a JIT run of the whole suite took 25 minutes with these settings, and decision 32's NativeAOT run of it 22 minutes;
decision 37's NativeAOT run, one class at a time, built a NativeAOT executable per class and took 42 minutes of runs; `--verify` checks
that every library really runs each scenario's handlers and behaviors, with a call counter that the benchmark runs compile out: the
runner sets the MSBuild property `KeryxBenchmarksProbe=false` in its environment before BenchmarkDotNet builds):

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

That run measures every benchmark and needs no separate step: the runner itself gives the direct call's `Publish` to one handler its
fixed invocation count under NativeAOT (see above; `Comparison/FixedInvocationCount.cs`), and leaves every other benchmark, every JIT
job, the Dry job and an explicit `--invocationCount` as they are.

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
