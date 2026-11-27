# Keryx

A source-generated mediator for .NET. Handlers are plain `private static` methods on `partial` classes, and a Roslyn generator in the
same package emits all dispatch and pipeline code at compile time: no reflection, no delegate chains, no closures, nothing allocated
per call by the mediator itself, and safe for Native AOT and trimming.

> **Status:** early development, not yet on nuget.org. The design is recorded in [ADR 0001](docs/adr/0001-keryx-mediator.md); undecided
> points and their interim behavior are in [open-questions.md](docs/adr/open-questions.md); what is left before a first release is in
> [docs/whats-left.md](docs/whats-left.md).

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

Each request has one handler in the whole application ([KRX005](#krx005)), in the request's own assembly: a request without one is
[KRX001](#krx001) in its assembly, hosts included (code fix: add a handler stub), and a handler of a request declared in another assembly
is [KRX023](#krx023). Notifications may be handled in any assembly.

**Injected parameters** are resolved by generated code, never by reflection:

| Parameter                                 | Resolved with                                                                       |
| ----------------------------------------- | ----------------------------------------------------------------------------------- |
| `T`                                       | `GetRequiredService<T>()`                                                           |
| `T` with a default value                  | `GetService<T>()`, falling back to the default                                      |
| `[FromKeyedServices("key")] T`            | `GetRequiredKeyedService<T>("key")`; the key is a constant ([KRX018](#krx018))      |
| `IServiceProvider`                        | the scope's provider itself, never resolved (keyed: an ordinary keyed service)      |
| `IMediator`, `ISender`, `IPublisher`      | the scoped mediator                                                                 |
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
- `internal static IServiceCollection AddKeryx(this IServiceCollection services)`, which registers `KeryxMediator` and `IMediator`,
  `ISender` and `IPublisher` (one scoped instance) and every handler class with injected parameters, all with generated factories and
  `TryAdd`. It registers no behavior types: behaviors are static.

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
pass-through behaviors. Every benchmark is one call on a mediator resolved beforehand (scoped ones from a scope that lives for the whole
run); behaviors complete synchronously. "Typed" is a library's per-request entry point, `IMediator` its generic method; n/a marks a
scenario the library has no API for. How each library is set up is recorded in
[open-questions.md](docs/adr/open-questions.md) (OQ-51).

Each cell is the mean time per call and the bytes allocated per call. One local run, 2026-11-26, after the move to static behaviors,
with the settings below:

```
BenchmarkDotNet v0.15.8, macOS 27.2.1 (26B131) [Darwin 27.2.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100, .NET 10.0.15 (10.0.1526.53008), Arm64 RyuJIT armv8.0-a, Dynamic PGO on
IterationCount=15  IterationTime=200ms  LaunchCount=1  WarmupCount=5
```

How to read them:

- Times under about 0.5 ns are below what BenchmarkDotNet resolves: the JIT inlined the whole call, pipeline included, into the
  benchmark (the direct call, and Keryx's typed overloads up to 3 behaviors). Compare those rows by order of magnitude.
- With a yielding handler, the time is the thread-pool hop of `Task.Yield()` and varies by a few hundred nanoseconds from run to run;
  the allocation column is the meaningful one. The direct call's 112 to 113 B (averaged over thread-pool work) is the handler's own
  async state machine, so a library that shows the same adds nothing.
- In the stream scenarios the direct call's 120 B is the handler's iterator. Each stream behavior adds its own iterator.

### Send, synchronous handler

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.01 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.02 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.58 ns · 0 B |
| **Keryx**, `IMediator` | 3.22 ns · 0 B | 3.31 ns · 0 B | 3.31 ns · 0 B | 3.51 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 27.3 ns · 64 B | 47.6 ns · 160 B | 64.9 ns · 352 B | 124 ns · 832 B |
| Mediator 3.0.2 scoped, `IMediator` | 29.7 ns · 64 B | 47.2 ns · 160 B | 65.3 ns · 352 B | 125 ns · 832 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.64 ns · 0 B | 1.28 ns · 0 B | 5.06 ns · 0 B | 15.8 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 4.96 ns · 0 B | 4.69 ns · 0 B | 9.29 ns · 0 B | 18.0 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 32.5 ns · 200 B | 71.3 ns · 440 B | 105 ns · 728 B | 193 ns · 1,448 B |
| Immediate.Handlers 4.4.0, `Handler` class | 9.50 ns · 0 B | 8.43 ns · 0 B | 13.9 ns · 0 B | 14.2 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 15.2 ns · 0 B | 10.9 ns · 0 B | 15.0 ns · 0 B | 19.6 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 25.5 ns · 24 B | 24.5 ns · 24 B | 25.4 ns · 24 B | 26.5 ns · 24 B |

### Send, handler awaits `Task.Yield()`

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,277 ns · 113 B | 1,216 ns · 112 B | 1,186 ns · 113 B | 1,227 ns · 113 B |
| **Keryx**, typed overload | 1,253 ns · 113 B | 1,251 ns · 112 B | 1,305 ns · 112 B | 1,275 ns · 112 B |
| **Keryx**, `IMediator` | 1,323 ns · 112 B | 1,290 ns · 112 B | 1,243 ns · 112 B | 1,255 ns · 112 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,269 ns · 176 B | 1,336 ns · 272 B | 1,357 ns · 464 B | 1,479 ns · 944 B |
| Mediator 3.0.2 scoped, `IMediator` | 1,375 ns · 176 B | 1,400 ns · 272 B | 1,475 ns · 464 B | 1,421 ns · 944 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,260 ns · 112 B | 1,199 ns · 113 B | 1,210 ns · 113 B | 1,286 ns · 112 B |
| Mediator 3.0.2 singleton, `IMediator` | 1,280 ns · 112 B | 1,269 ns · 112 B | 1,327 ns · 112 B | 1,301 ns · 112 B |
| MediatR 15.0.0, `IMediator` | 1,360 ns · 232 B | 1,352 ns · 472 B | 1,260 ns · 760 B | 1,400 ns · 1,480 B |
| Immediate.Handlers 4.4.0, `Handler` class | 1,331 ns · 372 B | 1,366 ns · 373 B | 1,382 ns · 373 B | 1,423 ns · 373 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 1,258 ns · 246 B | 1,361 ns · 246 B | 1,365 ns · 246 B | 1,304 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 1,401 ns · 277 B | 1,336 ns · 279 B | 1,327 ns · 278 B | 1,423 ns · 279 B |

### Stream of 100 items

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 652 ns · 120 B | 651 ns · 120 B |
| **Keryx**, typed overload | 643 ns · 120 B | 2,831 ns · 680 B |
| **Keryx**, `IMediator` | 652 ns · 120 B | 2,810 ns · 680 B |
| Mediator 3.0.2 scoped, `Mediator` class | 683 ns · 184 B | 2,888 ns · 1,048 B |
| Mediator 3.0.2 scoped, `IMediator` | 686 ns · 184 B | 2,864 ns · 1,048 B |
| Mediator 3.0.2 singleton, `Mediator` class | 648 ns · 120 B | 2,787 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 658 ns · 120 B | 2,804 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,354 ns · 512 B | 7,369 ns · 2,336 B |
| Immediate.Handlers 4.4.0, `Handler` class | 638 ns · 120 B | 2,850 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 660 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 674 ns · 144 B | n/a |

Since that run, a pipeline that resolves nothing has no pipeline context and its `next` links hold only the request (open-questions
OQ-65), so with 3 stream behaviors Keryx allocates 648 B per `Stream` (typed and `IMediator`) instead of the 680 B above. Generic
dispatch now tests each sealed request type with a type pattern (open-questions, "Requests are sealed"): in interleaved reruns against
the code before it, `IMediator` `Send` with a synchronous handler measured 0.4 to 0.7 ns faster, and `IMediator` `Stream` with no stream
behaviors 20 to 60 ns slower (3 to 9%) for a reason not found in its code, which the release candidate's run checks again. The other
Keryx rows measured within run-to-run noise of the tables, with the same allocations.

### Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only)

The handler of a request with no response returns a non-generic `ValueTask` or `Task`, so Keryx adapts it to `ValueTask<Unit>` through
its one generated async path (`UnitAdapter.ToUnit`, pooled builder on net10+). The direct call awaits the same handler body, so its
allocation is the handler's own state machine; the Performance contract says the adapter adds 0 bytes to it in steady state. The other
libraries are left out: this checks Keryx's contract, not a comparison.

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,364 ns · 96 B | 1,106 ns · 96 B |
| **Keryx**, typed overload | 1,288 ns · 96 B | 1,228 ns · 96 B |
| **Keryx**, `IMediator` | 1,449 ns · 96 B | 1,309 ns · 96 B |

### Publish, sequential, synchronous handlers

The direct call calls the handlers in turn without an `async` method of its own (it only falls back to one when a handler does not
complete synchronously), so it is the floor. With one handler the JIT inlines all of it and the time is below what BenchmarkDotNet
resolves.

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.04 ns · 0 B | 0.67 ns · 0 B |
| **Keryx**, typed overload | 0.65 ns · 0 B | 0.84 ns · 0 B |
| **Keryx**, `IMediator` | 3.11 ns · 0 B | 3.27 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 18.0 ns · 0 B | 34.9 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 19.0 ns · 0 B | 29.1 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.28 ns · 0 B | 15.6 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 4.97 ns · 0 B | 17.9 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 122 ns · 768 B | 316 ns · 1,728 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 8.98 ns · 0 B | 20.3 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 27.5 ns · 24 B | 58.8 ns · 120 B |

Since that run, a publish whose handlers are all static and whose behaviors inject nothing (every row here) has no pipeline context and
its publish strategy is static (open-questions OQ-70): in interleaved reruns against the code before it, the typed overload measured 0.35
to 0.42 ns for 1 handler and 0.46 to 0.51 ns for 5 (0.65 to 0.67 ns and 0.75 to 0.80 ns before), `IMediator` with 5 handlers 2.74 ns (3.03
ns before) and with 1 handler within noise, all still 0 B.

Run them yourself (the full set takes about 15 minutes with these settings; `--verify` checks that every library really runs each
scenario's handlers and behaviors):

```bash
cd test/Keryx.Benchmarks
dotnet run -c Release -f net10.0 -- --verify
dotnet run -c Release -f net10.0 -- --filter '*Comparison*' --launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 200
```

The Keryx subject has fewer than 32 request, stream request and notification types, so its `Keryx_Generic` rows measure the linear type
compares. With the environment variable `KeryxDispatchPadding=true` (an MSBuild property, so BenchmarkDotNet's own build sees it too),
it compiles 32 more of each (`Padding.cs`), and the same rows measure the `FrozenDictionary` lookup and jump table above the threshold:

```bash
KeryxDispatchPadding=true dotnet run -c Release -f net10.0 -- --filter '*Comparison*Keryx_Generic*'
```

`--filter '*PipelinePrototype*'` runs the hand-written struct pipeline the generated code follows (static behavior methods, struct
`next` links) against nested direct calls at depths 1, 3 and 8: 0 B at every depth, and 0.66x the nested calls' time at depth 3 on the
machine above. On macOS, `--prototype-disasm` runs the same pipelines to Tier 1 for a `DOTNET_JitDisasm` listing (see `Program.cs`).

CI builds the suite, runs `--verify`, runs every benchmark once with `--job Dry`, and runs the pipeline prototype on Linux with
BenchmarkDotNet's `DisassemblyDiagnoser`, uploading the disassembly as a build artifact. Numbers from shared runners are never published.

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
