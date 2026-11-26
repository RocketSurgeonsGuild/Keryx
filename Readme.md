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
- Requests, notifications and handler libraries may target `netstandard2.0`. A **host** (the project with the mediator) targets
  `net10.0` or later ([KRX031](#krx031)).
- The `Keryx` package depends on `Microsoft.Extensions.DependencyInjection.Abstractions` (8.0.x for `netstandard2.0`, 10.0.x for
  `net10.0`, 11.0.x for `net11.0`), which the generated host code uses.
- Analyzers do not flow through a `ProjectReference`: every project that declares requests or handlers, and every host, references the
  `Keryx` package directly.

## Quick start

```csharp
// Program.cs
using Keryx;
using Microsoft.Extensions.DependencyInjection;
using MyApp; // the host's root namespace, where AddKeryx() is generated

[assembly: KeryxHost] // this project gets the generated KeryxMediator and AddKeryx()

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
`AddKeryx()`, which registers the mediator, the handler class and every behavior with generated factories.

## Requests and handlers

A **request** implements exactly one of `IRequest<TResponse>`, `IRequest` (no response; it is `IRequest<Unit>`) or
`IStreamRequest<TItem>`. A **notification** implements `INotification`. Requests may be classes, records or structs; they must not be
generic, nested in a generic type or `ref struct`s ([KRX012](#krx012)). Dispatch is by exact runtime type: a handler for a base request
never receives a derived one, and every concrete request needs its own handler.

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

Each request has one handler in the whole application ([KRX005](#krx005)); a request without one is [KRX001](#krx001) in its assembly
(code fixes: add a handler stub, or mark the assembly as a contracts assembly) and [KRX030](#krx030) in the host.

**Injected parameters** are resolved by generated code, never by reflection:

| Parameter                                 | Resolved with                                                                       |
| ----------------------------------------- | ----------------------------------------------------------------------------------- |
| `T`                                       | `GetRequiredService<T>()`                                                           |
| `T` with a default value                  | `GetService<T>()`, falling back to the default                                      |
| `[FromKeyedServices("key")] T`            | `GetRequiredKeyedService<T>("key")`; the key is a constant ([KRX018](#krx018))      |
| `IServiceProvider`                        | the scope's provider                                                                |
| `IMediator`, `ISender`, `IPublisher`      | the scoped mediator                                                                 |
| `CancellationToken`                       | the token of the call                                                               |

## Testing handlers

Every handler class gets a public **invoker**, `InvokeAsync`, which runs the handler and nothing else: no behaviors, no container.
Unit tests construct the class with fakes and call it:

```csharp
var handler = new CreateOrder(new FakeOrderRepository());
var id = await handler.InvokeAsync(new CreateOrder.Command("apple", 3), CancellationToken.None);
```

A handler with nothing injected gets a `static InvokeAsync`. To test whole pipelines (behaviors, notification fan-out), make the test
project a host with `[assembly: KeryxHost]`, call `AddKeryx()` on a `ServiceCollection` and send through the mediator. A test host can
reference the app host; to replace one of its handlers, exclude it with `[assembly: ExcludeHandler(typeof(TheHandler))]` and write your
own.

## Behaviors

A **behavior** wraps a handler call. There is one interface per pipeline:

| Interface                                         | Wraps                                                  |
| ------------------------------------------------- | ------------------------------------------------------ |
| `IRequestPipelineBehavior<TRequest, TResponse>`   | one `Send`                                             |
| `IStreamPipelineBehavior<TRequest, TItem>`        | one `Stream` (wraps the stream; see [Streaming](#streaming)) |
| `IStreamHookBehavior<TRequest, TItem>`            | one `Stream`, through start/item/complete/error hooks  |
| `INotificationPipelineBehavior<TNotification>`    | one whole `Publish`                                    |
| `INotificationHandlerPipelineBehavior<TNotification>` | each notification handler call                     |

```csharp
[BehaviorOrder(Phase.Observability)]
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        return await next.InvokeAsync(cancellationToken);
    }
}
```

`next` is a generated struct, and the generated pipeline calls `Handle` on the concrete behavior type, so there is no delegate, no
closure and no generic virtual call per step. A behavior may call `next` more than once (retry) or not at all (short-circuit).

**Shapes.** A behavior implements exactly one behavior interface closing, and is either open with the interface's type parameters in
order (`LoggingBehavior<TRequest, TResponse>`) or closed and non-generic (`AuditCreate : IRequestPipelineBehavior<CreateOrder.Command,
Guid>`). It is not `abstract`, its `Handle` is not `virtual` ([KRX019](#krx019)), and it has exactly one public constructor, whose
parameters are injected like a handler's ([KRX020](#krx020)).

**Attaching.** A behavior applies through any of three scopes, and the union (without duplicates) runs:

- per target: `[Behavior(typeof(LoggingBehavior<,>))]` on a handler method, or, for per-publish notification behaviors, on the
  notification type;
- per assembly: `[assembly: Behavior(typeof(TransactionBehavior<,>))]` applies to every matching target in that assembly;
- global: `[GlobalBehavior]` on the behavior class applies to every matching target in every host that references it. A host removes
  one with `[assembly: ExcludeBehavior(typeof(LoggingBehavior<,>))]`; Info [KRX035](#krx035) lists every global behavior a host applies.

An open behavior applies where its generic constraints hold for the target's types (`where TRequest : ICommand` limits it to commands).
Assembly and global behaviors that do not fit are skipped; one attached explicitly that does not fit is [KRX009](#krx009).

**Ordering.** Lower runs first (further out). Behaviors sort by `[BehaviorOrder(Phase)]` (`Outermost` 0, `Observability` 100, `Security`
200, `Validation` 300, `Default` 400, `Caching` 450, `Transaction` 500, `Innermost` 600; any value in between works), then by its
`Order`, then by `[RunsAfter]`/`[RunsBefore]` edges (`typeof` form or generic form, which mean the same), then by type name. A cycle,
or an edge that contradicts phase or order, is [KRX008](#krx008).

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

With no stream behaviors, `Stream` returns the handler's enumerable as is. Two kinds of stream behavior sort together in one list:

- `IStreamPipelineBehavior` **wraps** the stream: `Handle` gets the inner stream from `next.InvokeAsync(ct)` and returns the stream the
  caller sees, typically by re-yielding it from its own iterator. It can filter, map, buffer, retry or serve items without calling `next`.
- `IStreamHookBehavior` **observes** the stream through `OnStartAsync`, `OnItem` (return `false` to drop an item), `OnCompleteAsync`
  and `OnErrorAsync`. Hook behaviors next to each other in the pipeline are fused into one generated iterator, so three hooks cost one
  iterator instead of three.

Per `Stream` call, Keryx allocates one iterator per wrap behavior and one per run of adjacent hook behaviors, nothing per item, and one
linked `CancellationTokenSource` only when both the `Stream` token and the `WithCancellation` token can be cancelled and differ.

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
  that converts to it.
- Handlers run in `[HandlerOrder]` order (default 0), then by class name.
- Publishing is sequential by default. `[PublishStrategy(PublishStrategy.Parallel)]` on the notification type starts every handler
  before awaiting any.
- Every handler runs, whatever the others do. If any fail, `Publish` throws one `AggregateException` with every failure in handler
  order, even for a single failure. Cancelling the token does not skip handlers; each sees the cancelled token.
- `INotificationPipelineBehavior` wraps the whole publish (and runs even when there are no handlers);
  `INotificationHandlerPipelineBehavior` wraps each handler.
- A struct notification delivered to a handler typed as an interface is boxed once per `Publish` (Info [KRX037](#krx037)).

## Hosts

`[assembly: KeryxHost]` turns a project (`net10.0` or later) into a **host**. The host composes every pipeline at compile time from its
own handlers and from the handlers, behaviors and notification types of every assembly it references, and generates, in its root
namespace:

- `internal sealed partial class KeryxMediator : IMediator`, with `ISender.Send`, `ISender.Stream` and `IPublisher.Publish`, plus a typed
  overload for each request and notification (`ValueTask<Guid> Send(CreateOrder.Command)`), which skips type dispatch;
- `internal static IServiceCollection AddKeryx(this IServiceCollection services)`, which registers `KeryxMediator` and `IMediator`,
  `ISender` and `IPublisher` (one scoped instance), every handler class with injected parameters, and every closed behavior type used,
  all with generated factories and `TryAdd`.

The mediator is **scoped**. It creates each pipeline's handler and behavior instances once per scope, lazily, and from then on a `Send`
allocates nothing. Singletons and hosted services that need it create a scope with `IServiceScopeFactory`. A handler's transient
dependencies therefore live as long as the scope.

The generic `Send<TResponse>(IRequest<TResponse>)` dispatches on the exact runtime type with type compares (a `FrozenDictionary` lookup
above 32 request types) and never boxes the response. A struct request is boxed when sent through `ISender`; the typed overload avoids
it (Info [KRX021](#krx021)). An unknown request type throws `KeryxHandlerNotFoundException`.

Hosts are Native AOT and trimming safe: CI publishes the [sample host](samples/hosts/Keryx.Samples.Host) with `PublishAot=true`, fails
on any trim or AOT warning and runs it.

## Contracts and handler libraries

Requests, notifications and handlers can live in any project that references `Keryx`, including `netstandard2.0` libraries. Each such
assembly carries a **manifest** in its metadata (`[assembly: KeryxManifest]` and one `[KeryxHandler]` attribute per public handler class),
which every host that references it reads, whether the reference is a project or a package.

- A **contracts assembly** holds requests whose handlers live elsewhere. Mark it `[assembly: KeryxContracts]` to turn off
  [KRX001](#krx001) there.
- A **handler library** contains `public` handler classes, and may declare `[GlobalBehavior]`s that every referencing host applies.
- In the host, a request handled in two assemblies is [KRX005](#krx005); `[assembly: ExcludeHandler(typeof(X))]` drops a referenced
  handler. A request the host can see without a handler anywhere is [KRX030](#krx030). A host that references contracts handled out of
  process names each such request with `[assembly: ExternallyHandled(typeof(X))]`; `.editorconfig` can also lower KRX030 for the whole
  host.
- A host referenced by another host (a test project referencing the app) gets only the handlers the referenced host keeps `public`; the
  referencing host reports KRX030 for every request of the referenced host without such a handler, unless it names it with
  `ExternallyHandled`.
- `ExternallyHandled`, `ExcludeHandler` and `ExcludeBehavior` are read only by a host; anywhere else they are [KRX043](#krx043).

See [samples/libraries](samples/libraries) (a `netstandard2.0` contracts library and a handler library with a global behavior and
notification handlers) and [samples/hosts](samples/hosts).

## Diagnostics

Every diagnostic links here. KRX001, KRX005 (within one assembly) and KRX008 (within one assembly) need the whole compilation: the IDE
shows them with full-solution analysis on, and every build reports them.

| ID                             | Severity | Meaning                                                                                                                                                        |
| ------------------------------ | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| <a id="krx001"></a>KRX001      | Error    | A request or stream request has no handler in its assembly. Not reported in a contracts assembly or a host. Code fixes: add a handler stub; mark the assembly `[assembly: KeryxContracts]`. |
| <a id="krx002"></a>KRX002      | Error    | A handler is not `private static`, or returns a type its request kind does not allow (including `async void`). Code fix: make it `private static`.           |
| <a id="krx003"></a>KRX003      | Error    | A handler has no request or notification parameter, more than one, or a request parameter typed as `IRequest<T>`, an abstract request or an interface.        |
| <a id="krx004"></a>KRX004      | Error    | A handler class or a containing type is not `partial`, is not `public` outside a host, or is a struct. Code fix: add `partial` (and `public`).                |
| <a id="krx005"></a>KRX005      | Error    | A request has two handlers (in one assembly, or across the host's assemblies).                                                                                |
| <a id="krx006"></a>KRX006      | Error    | A class directly contains more than one handler.                                                                                                              |
| <a id="krx007"></a>KRX007      | Error    | A handler class declares an instance or primary constructor, or has injected parameters and a base type without an accessible parameterless constructor.      |
| <a id="krx008"></a>KRX008      | Error    | Behavior ordering has a cycle, or `RunsAfter`/`RunsBefore` contradicts phase or order.                                                                         |
| <a id="krx009"></a>KRX009      | Error    | A behavior attached to a handler or notification type does not fit it: wrong behavior kind, or its constraints do not hold.                                    |
| <a id="krx011"></a>KRX011      | Error    | A type implements more than one request closing, both request kinds, or a request interface and `INotification`.                                              |
| <a id="krx012"></a>KRX012      | Error    | A request, stream request or notification is generic, nested in a generic type or a `ref struct`; or a handler class is generic.                             |
| <a id="krx013"></a>KRX013      | Error    | A handler parameter is `ref`, `in`, `out` or `params`, or there is more than one `CancellationToken`.                                                          |
| <a id="krx014"></a>KRX014      | Error    | A `static` handler class has injected parameters. Code fix: remove `static`.                                                                                  |
| <a id="krx015"></a>KRX015      | Error    | A handler class declares a member named `InvokeAsync` or starting with `__keryx_`.                                                                            |
| <a id="krx016"></a>KRX016      | Error    | Outside a host, a type a pipeline needs (request, response, notification, behavior) is not public.                                                            |
| <a id="krx017"></a>KRX017      | Warning  | A handler's response type differs from the request's only in nullability.                                                                                     |
| <a id="krx018"></a>KRX018      | Error    | A `[FromKeyedServices]` key is not a compile-time constant, or uses the parameterless form.                                                                   |
| <a id="krx019"></a>KRX019      | Error    | A behavior class has an unsupported shape, implements more than one behavior interface closing, is `abstract`, or implements `Handle` as `virtual`/`override`. |
| <a id="krx020"></a>KRX020      | Error    | A behavior class does not have exactly one public constructor.                                                                                                |
| <a id="krx021"></a>KRX021      | Info     | A struct request is boxed when sent through `ISender`; the host's typed overload avoids it.                                                                   |
| <a id="krx022"></a>KRX022      | Error    | The project compiles with C# below 14. Remove the `LangVersion` setting or set it to 14 or later. The generators emit nothing until then.                     |
| <a id="krx030"></a>KRX030      | Error    | A request the host can see has no handler anywhere in the host, unless `[assembly: ExternallyHandled(typeof(X))]` names it. Severity can be changed in `.editorconfig`. |
| <a id="krx031"></a>KRX031      | Error    | A `[KeryxHost]` project targets a framework older than `net10.0`.                                                                                              |
| <a id="krx032"></a>KRX032      | Error    | A referenced assembly's manifest is newer than this Keryx understands. Upgrade Keryx in the host.                                                             |
| <a id="krx033"></a>KRX033      | Warning  | A referenced assembly uses Keryx but has no manifest (the generator did not run there; reference the `Keryx` package directly).                               |
| <a id="krx034"></a>KRX034      | Error    | A type a referenced manifest names cannot be resolved from the host. Reference the assembly the message names.                                                |
| <a id="krx035"></a>KRX035      | Info     | Lists each global behavior the host applies and the assembly it comes from.                                                                                   |
| <a id="krx036"></a>KRX036      | Warning  | `ExcludeHandler` or `ExcludeBehavior` names a type that is not part of any pipeline, or `ExternallyHandled` names a type the host handles or that is not a request or stream request. |
| <a id="krx037"></a>KRX037      | Info     | A struct notification is delivered to a handler typed as an interface or `INotification`, so it is boxed once per `Publish`.                                 |
| <a id="krx040"></a>KRX040      | Warning  | `[HandlerOrder]` on a method that is not a notification handler.                                                                                              |
| <a id="krx041"></a>KRX041      | Warning  | `[PublishStrategy]` on a type that is not a notification.                                                                                                     |
| <a id="krx042"></a>KRX042      | Warning  | `[Behavior]` where it has no effect, or `[GlobalBehavior]` on a type that is not a behavior.                                                                  |
| <a id="krx043"></a>KRX043      | Warning  | `[assembly: ExternallyHandled]`, `[assembly: ExcludeHandler]` or `[assembly: ExcludeBehavior]` in an assembly without `[assembly: KeryxHost]`, where it has no effect. |

## Benchmarks

`test/Keryx.Benchmarks` compares Keryx with a hand-written direct call and with the other source-generated and reflection-based
mediators, each in its own small project under `test/benchmark-subjects`, all running the same requests, handler bodies and
pass-through behaviors. Every benchmark is one call on a mediator resolved beforehand (scoped ones from a scope that lives for the whole
run); behaviors complete synchronously. "Typed" is a library's per-request entry point, `IMediator` its generic method; n/a marks a
scenario the library has no API for. How each library is set up is recorded in
[open-questions.md](docs/adr/open-questions.md) (OQ-51).

Each cell is the mean time per call and the bytes allocated per call. Local runs, 2026-11-26, all with the settings below (the `IRequest`
and Publish tables come from a second run, after the Publish direct call was made non-async):

```
BenchmarkDotNet v0.15.8, macOS 27.2.1 (26B131) [Darwin 27.2.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100, .NET 10.0.15 (10.0.1526.53008), Arm64 RyuJIT armv8.0-a, Dynamic PGO on
IterationCount=15  IterationTime=200ms  LaunchCount=1  WarmupCount=5
```

How to read them:

- Times under about 0.5 ns are below what BenchmarkDotNet resolves: the JIT inlined the whole call, pipeline included, into the
  benchmark (the direct call, Keryx's typed overloads and Mediator's singleton `Mediator` class at low depths). Compare those rows by
  order of magnitude.
- With a yielding handler, the time is the thread-pool hop of `Task.Yield()` and varies by a few hundred nanoseconds from run to run;
  the allocation column is the meaningful one. The direct call's 112 to 113 B (averaged over thread-pool work) is the handler's own
  async state machine, so a library that shows the same adds nothing.
- In the stream scenarios the direct call's 120 B is the handler's iterator. Each wrap behavior adds its own iterator; Keryx's three
  hook behaviors are fused into one generated iterator.

### Send, synchronous handler

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 0.31 ns · 0 B | 0.02 ns · 0 B | 0.02 ns · 0 B | 0.00 ns · 0 B |
| **Keryx**, typed overload | 0.00 ns · 0 B | 0.00 ns · 0 B | 0.18 ns · 0 B | 2.16 ns · 0 B |
| **Keryx**, `IMediator` | 2.95 ns · 0 B | 3.28 ns · 0 B | 3.44 ns · 0 B | 4.75 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 25.1 ns · 64 B | 45.5 ns · 160 B | 67.4 ns · 352 B | 122 ns · 832 B |
| Mediator 3.0.2 scoped, `IMediator` | 27.4 ns · 64 B | 45.9 ns · 160 B | 62.9 ns · 352 B | 118 ns · 832 B |
| Mediator 3.0.2 singleton, `Mediator` class | 0.38 ns · 0 B | 1.22 ns · 0 B | 5.90 ns · 0 B | 14.0 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 4.09 ns · 0 B | 4.61 ns · 0 B | 9.24 ns · 0 B | 20.3 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 31.1 ns · 200 B | 67.4 ns · 440 B | 102 ns · 728 B | 187 ns · 1,448 B |
| Immediate.Handlers 4.4.0, `Handler` class | 8.06 ns · 0 B | 7.76 ns · 0 B | 10.2 ns · 0 B | 13.7 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 11.6 ns · 0 B | 9.00 ns · 0 B | 10.9 ns · 0 B | 14.1 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 19.8 ns · 24 B | 22.6 ns · 24 B | 24.5 ns · 24 B | 22.0 ns · 24 B |

### Send, handler awaits `Task.Yield()`

| Subject | 0 behaviors | 1 behavior | 3 behaviors | 8 behaviors |
| --- | ---: | ---: | ---: | ---: |
| Hand-written direct call | 1,177 ns · 113 B | 1,112 ns · 113 B | 1,137 ns · 113 B | 899 ns · 113 B |
| **Keryx**, typed overload | 863 ns · 113 B | 877 ns · 113 B | 890 ns · 113 B | 981 ns · 112 B |
| **Keryx**, `IMediator` | 1,005 ns · 112 B | 1,188 ns · 113 B | 878 ns · 113 B | 1,051 ns · 113 B |
| Mediator 3.0.2 scoped, `Mediator` class | 1,163 ns · 176 B | 945 ns · 272 B | 1,238 ns · 465 B | 938 ns · 944 B |
| Mediator 3.0.2 scoped, `IMediator` | 856 ns · 176 B | 1,277 ns · 272 B | 920 ns · 464 B | 1,222 ns · 944 B |
| Mediator 3.0.2 singleton, `Mediator` class | 1,149 ns · 112 B | 925 ns · 113 B | 1,166 ns · 113 B | 874 ns · 113 B |
| Mediator 3.0.2 singleton, `IMediator` | 810 ns · 113 B | 1,067 ns · 112 B | 923 ns · 113 B | 1,026 ns · 113 B |
| MediatR 15.0.0, `IMediator` | 842 ns · 232 B | 878 ns · 472 B | 1,093 ns · 760 B | 978 ns · 1,480 B |
| Immediate.Handlers 4.4.0, `Handler` class | 1,356 ns · 373 B | 881 ns · 372 B | 890 ns · 373 B | 831 ns · 374 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 841 ns · 245 B | 1,324 ns · 246 B | 956 ns · 245 B | 1,192 ns · 246 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 880 ns · 279 B | 929 ns · 278 B | 1,237 ns · 279 B | 1,006 ns · 278 B |

### Stream of 100 items

| Subject | 0 stream behaviors | 3 stream behaviors |
| --- | ---: | ---: |
| Hand-written direct call | 633 ns · 120 B | 650 ns · 120 B |
| **Keryx**, typed overload | 641 ns · 120 B | 2,708 ns · 680 B |
| **Keryx**, `IMediator` | 631 ns · 120 B | 2,725 ns · 680 B |
| **Keryx** (hook behaviors), typed overload | n/a | 1,407 ns · 352 B |
| **Keryx** (hook behaviors), `IMediator` | n/a | 1,386 ns · 352 B |
| Mediator 3.0.2 scoped, `Mediator` class | 669 ns · 184 B | 2,793 ns · 1,048 B |
| Mediator 3.0.2 scoped, `IMediator` | 655 ns · 184 B | 2,803 ns · 1,048 B |
| Mediator 3.0.2 singleton, `Mediator` class | 633 ns · 120 B | 2,719 ns · 696 B |
| Mediator 3.0.2 singleton, `IMediator` | 637 ns · 120 B | 2,771 ns · 696 B |
| MediatR 15.0.0, `IMediator` | 1,309 ns · 512 B | 7,617 ns · 2,336 B |
| Immediate.Handlers 4.4.0, `Handler` class | 623 ns · 120 B | 2,711 ns · 672 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 637 ns · 120 B | n/a |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 641 ns · 144 B | n/a |

### Send of an `IRequest`, handler awaits `Task.Yield()` (Keryx only)

The handler of a request with no response returns a non-generic `ValueTask` or `Task`, so Keryx adapts it to `ValueTask<Unit>` through
its one generated async path (`UnitAdapter.ToUnit`, pooled builder on net10+). The direct call awaits the same handler body, so its
allocation is the handler's own state machine; the Performance contract says the adapter adds 0 bytes to it in steady state. The other
libraries are left out: this checks Keryx's contract, not a comparison.

| Subject | `async ValueTask` handler | `async Task` handler |
| --- | ---: | ---: |
| Hand-written direct call | 1,257 ns · 96 B | 1,146 ns · 96 B |
| **Keryx**, typed overload | 1,394 ns · 96 B | 1,303 ns · 96 B |
| **Keryx**, `IMediator` | 1,402 ns · 96 B | 1,319 ns · 96 B |

### Publish, sequential, synchronous handlers

The direct call calls the handlers in turn without an `async` method of its own (it only falls back to one when a handler does not
complete synchronously), so it is the floor. With one handler the JIT inlines all of it and the time is below what BenchmarkDotNet
resolves.

| Subject | 1 handler | 5 handlers |
| --- | ---: | ---: |
| Hand-written direct call | 0.00 ns · 0 B | 0.64 ns · 0 B |
| **Keryx**, typed overload | 0.68 ns · 0 B | 0.74 ns · 0 B |
| **Keryx**, `IMediator` | 2.86 ns · 0 B | 3.05 ns · 0 B |
| Mediator 3.0.2 scoped, `Mediator` class | 17.8 ns · 0 B | 32.6 ns · 0 B |
| Mediator 3.0.2 scoped, `IMediator` | 17.8 ns · 0 B | 27.4 ns · 0 B |
| Mediator 3.0.2 singleton, `Mediator` class | 3.21 ns · 0 B | 15.4 ns · 0 B |
| Mediator 3.0.2 singleton, `IMediator` | 5.07 ns · 0 B | 17.3 ns · 0 B |
| MediatR 15.0.0, `IMediator` | 121 ns · 768 B | 299 ns · 1,728 B |
| ZeroAlloc.Mediator 7.0.1, static `Mediator` | 7.57 ns · 0 B | 15.9 ns · 0 B |
| ZeroAlloc.Mediator 7.0.1, `IMediator` | 28.2 ns · 24 B | 58.4 ns · 120 B |

Run them yourself (the full set takes about 15 minutes with these settings; `--verify` checks that every library really runs each
scenario's handlers and behaviors):

```bash
cd test/Keryx.Benchmarks
dotnet run -c Release -f net10.0 -- --verify
dotnet run -c Release -f net10.0 -- --filter '*Comparison*' --launchCount 1 --warmupCount 5 --iterationCount 15 --iterationTime 200
```

CI only builds the suite, runs `--verify` and runs every benchmark once with `--job Dry`; numbers from shared runners are never published.

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
