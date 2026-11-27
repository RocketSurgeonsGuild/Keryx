# ADR 0001: Keryx, a source-generated mediator

Nov 25, 2026 · @David

## Status and decision

**Status:** Accepted, 2026-11-25; amended 2026-11-26 (verification fixes and the decisions recorded in open-questions.md, including the
`I…PipelineBehavior` interface names, the generic `RunsAfter<T>`/`RunsBefore<T>` forms, one analyzer assembly with the code fixes in it,
the C# 11 floor, stream behaviors that wrap the stream with optional fused hook behaviors, and a Phase 4 gate that reports instead of
stopping; the interface names, generic ordering forms and hook behaviors were replaced by the second amendment); amended again
2026-11-26 after a review of the whole design: behaviors are `static` classes marked with kind attributes and called as static methods,
replacing the behavior interfaces and their instances (KRX020 retired); stream hook behaviors are removed; ordering attributes have only
the `typeof` form; a host can mark request types as handled elsewhere with `[assembly: ExternallyHandled]` (removed by the fifth amendment); the `Keryx` package depends
on `Microsoft.Extensions.DependencyInjection.Abstractions`; the dispatch threshold is 32; and every open question up to OQ-51 is folded
in. Amended a third time 2026-11-26, after the owner's second round of decisions: only one notification behavior kind remains,
`[NotificationBehavior]` (the per-publish kind, until then `[PublishBehavior]`), and per-handler notification behaviors
(`[NotificationHandlerBehavior]`) are removed; an open behavior method must constrain its message type parameter to the kind's message
interface (KRX019); the language floor rises to C# 14, the .NET 10 SDK's default (OQ-52); and the host-only assembly attributes outside a host are warning KRX043 (OQ-54).
Amended a fourth time 2026-11-26 (OQ-56 to OQ-58): the `net11.0` dependency group is empty, because the .NET 11 shared framework carries
`Microsoft.Extensions.DependencyInjection.Abstractions`; KRX034 and KRX035 are per behavior kind; a message constraint through a derived
interface counts; and an open behavior method may write its response or item type parameter annotated (`TResponse?`), matched with
nullability. Amended a fifth time 2026-11-26, the owner's second rework: `[assembly: KeryxContracts]` and
`[assembly: ExternallyHandled]` are removed (their designs are kept in [future-requirements.md](../future-requirements.md)); a request's
or stream request's handler lives in the request's own assembly, so KRX001 applies in every assembly, hosts included, a handler of a
request declared in another assembly is the new error KRX023, and host KRX030 covers only referenced requests, as a safety net. The same
rework removes `[assembly: KeryxHost]`: a project is a host when its `KeryxHost` MSBuild property is `true`, which the package's
`build/Keryx.targets` defaults for executables and test projects, and host diagnostics without a source location of their own are reported
without a location.
The replaced designs are listed under Alternatives considered. This replaces the draft "Source-generated mediator library" ADR (working
title "Mediator"). Every placeholder name is replaced with the Keryx name, and every open question in the draft is answered below.

**Decision:** Build a .NET mediator named **Keryx**. Handlers are plain `private static` methods on `partial` classes. A Roslyn
analyzer and source generator, shipped in the same NuGet package, emits all dispatch code. Pipelines are generated as straight-line
code with no runtime reflection, no delegate chains and no closures. Discovery across assemblies uses type-level attributes that the
generator emits, found in the host with `Specular.Scanning.Sources`.

**Why:** Reflection-based mediators resolve handlers at run time and wrap each behavior in a delegate, which allocates on every call.
A source generator can resolve all of that at compile time and still give developers behaviors, ordering and constraints.

**Goals**

- A measurable allocation contract per `Send`, `Stream` and `Publish`, with behaviors included (see Performance contract).
- Handlers are ordinary methods. There is no handler interface to implement.
- Compile-time errors for missing, malformed or conflicting handlers, with code fixes.
- Behaviors can be applied per target (handler or notification type), per assembly or globally, can be limited by generic constraints, and
  run in a deterministic, declared order.
- Request/response, `IAsyncEnumerable` streaming and notifications (publish/subscribe) are all first class.
- Native AOT and trimming safe. No runtime reflection, including in DI registration.

**Non-goals for v1:** runtime-registered handlers, generic request types, behaviors with type-parameter shapes other than those listed
under Behaviors.

## Packaging and repository

### Package and assemblies

One NuGet package, **`Keryx`**, contains:

| Assembly                                       | TFM                              | Contents                                                                                                                                                                                                                                                                                                                                                                  |
| ---------------------------------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Keryx.dll` (`lib/`)                           | `netstandard2.0;net10.0;net11.0` | All public types: interfaces, attributes, `Unit`, `Phase`, exceptions, runtime helpers. `IsAotCompatible` on net10+.                                                                                                                                                                                                                                                      |
| `Keryx.Analyzers.dll` (`analyzers/dotnet/cs`)  | `netstandard2.0`                 | Incremental generators (`HandlerGenerator`, `HostGenerator`), `DiagnosticAnalyzer`s and `CodeFixProvider`s. References `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces` (code fixes only) and `System.Text.Json` (shipped beside it); `Specular.Scanning.Sources` is compiled in as source (`PrivateAssets=all`). No reference to `Keryx.dll`. |
| `System.Text.Json.dll` (`analyzers/dotnet/cs`) | `netstandard2.0`                 | Needed at analyzer run time by `Specular.Scanning.Sources` and by the ScanCache JSON context.                                                                                                                                                                                                                                                                             |

- The code fixes live in `Keryx.Analyzers.dll` with the generators and analyzers (decided 2026-11-26). An assembly that contains an
  analyzer or generator and references Workspaces triggers RS1038; `Keryx.Analyzers.csproj` suppresses it in `NoWarn`, with a comment
  giving the reason. The risk RS1038 guards against is real: the command-line compiler does not ship
  `Microsoft.CodeAnalysis.Workspaces`, and only the IDE instantiates a `CodeFixProvider`. So code-fix types (the `CodeFixes` folder) are
  never touched from a generator or analyzer code path: no shared static state, no shared base types, no signatures that mention a
  Workspaces type. Code fixes may use generator and analyzer types, never the reverse. Breaking the rule fails only on the command line,
  with a `FileNotFoundException` for the Workspaces assembly.
- `Keryx.Analyzers.dll` has **no assembly reference** to `Keryx.dll`. It identifies Keryx types by metadata name through one internal
  `KnownTypeNames` constants class (for example `"Keryx.IRequest`1"`). A test compiles against the real `Keryx.dll`and checks that every
  entry resolves with`Compilation.GetTypeByMetadataName`.
- Every project that references the `Keryx` package runs the generator, including contracts projects. A request's handler lives in the
  request's own assembly, so a project that declares requests handles them (KRX001; a handler elsewhere is KRX023). A contracts project
  declares notifications and shared types, which may be handled in any assembly.
- Analyzers do not flow through a package that depends on Keryx (NuGet's default `PrivateAssets` includes `analyzers`, so the
  dependency is packed with `exclude="Build,Analyzers"`); through a `ProjectReference` to a project that references the package they do
  (measured 2026-11-26 with the .NET 11 SDK). The package's `buildTransitive/` files flow in both cases, so a transitive consumer gets
  the same `LangVersion` and `KeryxHost` defaults (Opting in). Every project that declares requests or handlers, and every host, must
  still reference `Keryx` directly. The host warns (KRX033) about a reference that uses Keryx types
  but carries no manifest.
- The analyzer project compiles against `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.0.0, while
  the tests use the newer Roslyn they need. The analyzer assembly is packed to `analyzers/dotnet/cs`, with no Roslyn-versioned folder
  and no multi-Roslyn `*.supports` projects in v1. Consumers need the .NET 10 SDK (Roslyn 5.0) or newer, whatever TFM they target.
  `Specular.Scanning.Sources` sets the same floor and requires C# 14 in the generator project.
- Every project that references Keryx compiles with **C# 14 or later**, the default language of the .NET 10 SDK that Keryx already
  requires (open-questions OQ-20 and OQ-52). `netstandard2.0` stays a supported target for contracts and handler libraries; only the
  language version rises. Generated code and Keryx's own source may use any C# 14 feature. The package's `build/Keryx.props` sets
  `LangVersion` to `latest` when the project leaves it empty (a `netstandard2.0` project would otherwise get C# 7.3), the same way
  `Specular.Scanning.Sources` defaults its own. A `LangVersion` the project or its `Directory.Build.props` sets still wins; one below C# 14
  is error KRX022, and the generators emit nothing in that project.
- `Keryx.dll` depends on `Microsoft.Extensions.DependencyInjection.Abstractions` for `net10.0` (10.0.x) and `netstandard2.0` (8.0.x, the
  oldest version with keyed services). Generated host code uses `IServiceCollection`, the `IServiceProvider` extensions and the
  keyed-service APIs, so every host gets them through the package. `net11.0` gets the assembly from the shared framework:
  `Microsoft.NETCore.App` 11.0 (runtime and reference pack) carries `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, so the
  SDK's package pruning drops the reference and the package's `net11.0` dependency group is empty (open-questions OQ-56).
- On `netstandard2.0`, `Keryx.dll` also depends on `Microsoft.Bcl.AsyncInterfaces` and `System.Threading.Tasks.Extensions`.

### Which projects may target what

- Contracts (notifications and shared types) and handler libraries (requests and stream requests with their handlers, notification
  handlers) may target `netstandard2.0`. Their generated code (the
  handler partial and its attribute) uses only APIs available there.
- A host (a project whose `KeryxHost` MSBuild property is `true`, see Opting in) must target `net10.0` or newer. Its generated dispatch uses `Unsafe.As`, `FrozenDictionary` and, where
  it helps, `PoolingAsyncValueTaskMethodBuilder`. A host on an older TFM is error KRX031.
- The package's `build/Keryx.props` exposes `<CompilerVisibleProperty Include="KeryxHost;RootNamespace;TargetFrameworkIdentifier;TargetFrameworkVersion" />`
  to the generators and analyzers. KRX031 fires unless `TargetFrameworkIdentifier` is `.NETCoreApp` with version 10.0 or newer. When the properties
  are absent (for example in a test harness), the host generator falls back to the referenced `System.Runtime` assembly version
  (10.0 or newer passes).

### Repository layout

The repository follows the current Specular layout (`RocketSurgeonsGuild/Specular` `origin/main`, commit 92746cd), not the older
Indago-era branch:

- `global.json`: .NET 11 SDK (`11.0.100`, `rollForward: latestFeature`, `allowPrerelease: true`), `test.runner:
  Microsoft.Testing.Platform`, `msbuild-sdks` entry for `Rocket.Surgery.Sdk`.
- Root `Directory.Build.props` imports `Rocket.Surgery.Sdk` and sets package metadata. **`TreatWarningsAsErrors` is `true` everywhere,
  always** (src, test, samples, benchmarks, local and CI).
- Central package management in `Directory.Packages.props`; every package pinned exactly.
- Build: `build/Build.cs` file-based ModularPipelines app run with `mise run build`. Tooling in `.config/mise.toml`, git hooks in
  `.config/hk.pkl`, whose C# formatting step is `dotnet format whitespace` only (decided 2026-11-26; style comes from the build's
  analyzers). Hand-written GitHub workflows.
- Folders: `src/`, `test/`, `samples/` (singular `test`), each with its own `Directory.Build.props`/`.targets`.
- Projects: `src/Keryx`, `src/Keryx.Analyzers` (generators, analyzers and, in its `CodeFixes` folder, code fixes), `test/Keryx.Tests`,
  `test/Keryx.Benchmarks`, `samples/hosts/*`, `samples/libraries/*`.
- Tests: TUnit on Microsoft.Testing.Platform, `TUnit.Assertions.Should`, `Verify.TUnit` snapshots in `snapshots/`,
  `Rocket.Surgery.Extensions.Testing.SourceGenerators` (11.0.2 or later) for generator, analyzer and code-fix tests. Every test
  compilation goes through it, including referenced libraries, compilation options, severity options and incremental reruns; no test
  drives a `CSharpGeneratorDriver` or emits a compilation by hand (decided 2026-11-26). Each kind of generated output has one snapshot
  shape: the host generator's is the object `{ Mediator, AddKeryx }`, the handler generator's is `Verify(result)`.
- The analyzer project has `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` from the start (RS2008).

Specular-specific parts are not copied: the Astro docs site, the multi-Roslyn `*.supports` projects, Uno/MAUI/Blazor samples and
workloads, Specular icons, spec-kit files.

## Core types

All public types live in the `Keryx` namespace. Types used only by generated code live in `Keryx.Internal` and are marked
`[EditorBrowsable(EditorBrowsableState.Never)]`.

```csharp
namespace Keryx;

public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    public static readonly Unit Value = default;
    // Equals, GetHashCode, CompareTo, ==, !=, ToString() => "()"
}

// Requests are invariant on purpose: generated dispatch reinterprets ValueTask<TExact> as ValueTask<TResponse>.
public interface IRequest<TResponse> { }
public interface IRequest : IRequest<Unit> { }
public interface IStreamRequest<TItem> { }
public interface INotification { }

public interface ISender
{
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TItem> Stream<TItem>(IStreamRequest<TItem> request, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}

public interface IMediator : ISender, IPublisher { }

public sealed class KeryxHandlerNotFoundException : InvalidOperationException
{
    public KeryxHandlerNotFoundException(Type requestType);
    public Type RequestType { get; }
}
```

## Requests and notifications

**What counts as a request.** A request is a non-abstract class, record or struct, with no type parameters and not nested in a generic
type, whose interfaces contain **exactly one** closing of `IRequest<>` or `IStreamRequest<>` (never both). Abstract types and interfaces
that derive from these interfaces are never requests and never need handlers; they are fine as shared base types.

**What counts as a notification.** A non-abstract class, record or struct, not generic, that implements `INotification` and is not also a
request (KRX011).

A `ref struct` cannot be boxed, stored in a link or captured by an async method, so a `ref struct` that implements a request or
notification interface is KRX012 and is never a request, notification or handler parameter.

**Where a request is handled.** A request's or stream request's one handler lives in the assembly that declares the request: KRX001
reports a request without a handler in its assembly (in every assembly, hosts included), and a handler of a request declared in another
assembly is KRX023. Notifications are not affected: a notification may be declared in one assembly and handled in any number of others.
Designs that let a request be handled in another assembly (`[assembly: KeryxContracts]`, `[assembly: ExternallyHandled]`) were removed
on 2026-11-26 and are kept in [future-requirements.md](../future-requirements.md).

**Dispatch is by exact runtime type.** Every concrete request type needs its own handler. A handler for a base request never receives a
derived request. Requests do not need to be `sealed`; the draft's MED010 is retired.

**Struct requests** are allowed. The generic `ISender.Send` path boxes them at the call site; only the host's typed overloads avoid it
(Info KRX021). That box is the caller's, and is not counted against Keryx in the Performance contract.

**Generic requests** (`GetById<T>`) are not supported in v1 (KRX012). The supported pattern is a generic abstract base plus sealed closed
subtypes, each with its own handler. Generic _response_ types (`IRequest<List<Order>>`, `IRequest<Result<Order>>`) are fully supported.

**Requests with no response** implement `IRequest`, which is `IRequest<Unit>`. Their handlers may return `ValueTask`, `Task`,
`ValueTask<Unit>`, `Task<Unit>` or synchronous `void`. Behaviors see `TResponse = Unit`, so one behavior shape covers both cases. Every
send path, typed overloads included, returns `ValueTask<Unit>` for them.

## Handlers

### Which methods are handler candidates

A method is a **handler candidate** when all of these hold:

- It is named `Handle` or `HandleAsync` (equivalent; the code fix emits `Handle`).
- It is not an interface implementation (implicit or explicit), not an override, not `abstract` (an interface's own members and
  abstract class members declare a contract and have no body to call) and not generic.
- Either it has a parameter whose type is a request or notification type (for notification handlers, also an interface or base type
  that derives from `INotification`, or `INotification` itself), or it is `static` on a type that directly nests a request type.
- No parameter type is a `ref struct` request or notification (KRX012).
- It is not a behavior method (a method of a class that carries a behavior kind attribute; see Behaviors).

Every other method is ignored, so `Handle` helpers, MediatR handlers kept during a migration and behavior methods never produce
handler diagnostics.

A request that implements `IRequest<Unit>` directly counts as an `IRequest`: its handler may also return `ValueTask`, `Task` or
synchronous `void`.

### Handler rules

- Declared `private static` (KRX002, with a code fix).
- Return type:
    - request `IRequest<T>`: `ValueTask<T>`, `Task<T>` or synchronous `T`;
    - request `IRequest`: additionally `ValueTask`, `Task`, or synchronous `void` (`async void` is KRX002);
    - stream request `IStreamRequest<T>`: `IAsyncEnumerable<T>` (the method may be an `async` iterator);
    - notification: `ValueTask` or `Task`.
      A wrong return type is KRX002. A response type that differs only in nullability from the request's `TResponse` is warning KRX017.
- Exactly one request/notification parameter (KRX003). For requests it must be the concrete request type, not `IRequest<T>`, an abstract
  request or an interface, declared in the handler's own assembly (KRX023). For notifications it may be the concrete type or a base type/interface deriving from `INotification`
  (polymorphic handler, see Notifications).
- At most one `CancellationToken`, in any position. `ref`, `in`, `out` and `params` parameters are errors (KRX013).
- Every other parameter is injected from dependency injection (see Injected parameters).
- An `async` iterator stream handler must put `[EnumeratorCancellation]` on its token. The compiler already enforces this (CS8425), and
  warnings are errors. Non-iterator stream handlers (returning an existing `IAsyncEnumerable`) must not use the attribute (CS8424).

### Handler classes

- Every type in the containing chain is `partial` (KRX004, with a code fix). No type in the chain is file-local (`file`), because Keryx
  cannot generate the other half of a file-local type from another file (KRX004).
- In an assembly that is not a host, the handler class and every containing type are `public` (KRX004). A host may keep handlers
  `internal` for its own mediator; those are left out of its manifest. In a host, a handler class or containing type narrower than
  `internal` (`private`, `protected`, `private protected`) is KRX004 too, because the generated mediator cannot reach it;
  `protected internal` counts as `internal`. The handler generator skips such a handler.
- The handler class is a `class` or a `record class` without a primary constructor. Structs, record structs and interfaces are errors
  (KRX004). A handler class that has type parameters or is nested in a generic type is KRX012, because the host cannot construct it.
- The class must not declare an instance constructor (KRX007). Static constructors are allowed. A record primary constructor counts as
  an instance constructor.
- A handler class with injected parameters is constructed by the host's generated factory, so it must not be `abstract`, must not
  declare or inherit `required` members (the generated constructor does not set them, and carries no `[SetsRequiredMembers]`), and must
  derive from `object` or from a base type with an accessible parameterless instance constructor, because the generated constructor
  calls `base()` (KRX007). The handler generator skips such a handler, and KRX001 and the host count its request as handled by a
  malformed handler, so no KRX001 or KRX030 is added. A static invoker (no injected parameters) is never constructed, so none of this applies to it.
- Generated member accessibility follows the handler's signature alone: the constructor and `InvokeAsync` are `public` only when every
  type they mention is effectively public. Outside a host that is required (KRX016 covers injected parameter types and a polymorphic
  handler's parameter type too). In a host, a public handler class whose request, response or injected types are `internal` gets
  `internal` generated members and no `[KeryxHandler]` attribute, like the host's internal handlers; a handler whose signature uses a
  type narrower than `internal` is skipped and KRX016 reports that type ("must be at least internal"), and its request still counts as
  handled.
- `static partial` classes are allowed when the handler has no injected parameters. A static class with injected parameters is KRX014
  (code fix: remove `static`).
- One handler method per class, counting direct members only (KRX006). Grouping is done with nested classes, for example a
  `public static partial class Orders` containing `public partial class Create` and `public partial class Cancel`.
- One handler per request type per application (KRX005). Notifications may have any number of handlers, including none.
- Where the handler lives is a convention of the code fix only. Any class in the same assembly may host a request's handler.
- A user member named `InvokeAsync` in a handler class is an error (KRX015). Generated fields use reserved names
  (`__keryx_<parameterName>`).

### Example

```csharp
public partial class CreateOrder
{
    public sealed record Command(string Sku, int Quantity) : IRequest<Guid>;

    private static async ValueTask<Guid> Handle(Command request, IOrderRepository orders, CancellationToken ct)
    {
        // ...
    }
}
```

The generator emits the other half of the class, with `RegisterSourceOutput` (never `RegisterImplementationSourceOutput`, so the IDE sees
it in other projects):

```csharp
// <auto-generated/>
#nullable enable
[global::Keryx.Internal.KeryxHandler(typeof(global::CreateOrder.Command), global::Keryx.Internal.HandlerKind.Request)]
public partial class CreateOrder
{
    [global::System.CodeDom.Compiler.GeneratedCode("Keryx", "1.0.0")]
    private readonly global::IOrderRepository __keryx_orders;

    [global::System.CodeDom.Compiler.GeneratedCode("Keryx", "1.0.0")]
    public CreateOrder(global::IOrderRepository orders) => __keryx_orders = orders;

    /// <summary>Runs the handler only. No behaviors run.</summary>
    [global::System.CodeDom.Compiler.GeneratedCode("Keryx", "1.0.0")]
    public global::System.Threading.Tasks.ValueTask<global::System.Guid> InvokeAsync(
        global::CreateOrder.Command request, global::System.Threading.CancellationToken cancellationToken)
        => Handle(request, __keryx_orders, cancellationToken);
}
```

- `[GeneratedCode]` goes on each generated member, never on the partial declaration of a user type. On a partial declaration it would
  apply to the whole type and mark the user's own `Handle` as generated code, hiding it from analyzers and coverage. Types Keryx owns
  outright (`KeryxMediator`, pipeline contexts, link structs, `KeryxServiceCollectionExtensions`) carry it at type level.
- A handler with no injected parameters gets a `public static InvokeAsync` and no constructor. No-dependency handler classes are not
  registered in DI.
- `InvokeAsync` is supported public API: "runs the handler only, no behaviors". It has no `[EditorBrowsable(Never)]`. Unit tests call it
  directly (`new CreateOrder(fakeRepo).InvokeAsync(cmd, ct)`). Pipeline tests make the test project a host.
- Return adaptation happens once, in `InvokeAsync`:
    - `Task<T>` → `new ValueTask<T>(task)` (no allocation);
    - synchronous `T` → `new ValueTask<T>(result)`;
    - synchronous `void` for `IRequest` → call the handler, then return `new ValueTask<Unit>(Unit.Value)` (no allocation);
    - non-generic `ValueTask`/`Task` for `IRequest` → `ValueTask<Unit>` through `Keryx.Internal.UnitAdapter.ToUnit(...)`, which returns a
      completed `ValueTask<Unit>` without allocating when the source already completed successfully, and otherwise awaits through an async
      helper (pooled builder on net10+, plain builder on netstandard2.0);
    - notification handlers' `Task` → `new ValueTask(task)`.
- Constructor parameters copy `[FromKeyedServices(key)]` and default values from the handler's parameters, so the host can read them from
  metadata.

### Injected parameters

The host generates explicit factory registrations for handlers, and resolves behavior methods' injected parameters in generated
pipeline contexts (see Behaviors). There is no reflection-based activation. The same rules apply to every injected parameter of a
handler and of a behavior method.

| Parameter                            | Generated resolution                                                                                                                                                                                                                           |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| plain `T`                            | `sp.GetRequiredService<T>()`                                                                                                                                                                                                                   |
| `T` with a default value             | reference type: `sp.GetService<T>() ?? <default>`; value type (including `Nullable<T>`): `sp.GetService(typeof(T)) is T __v ? __v : <default>` (no reflection, no allocation). A nullable annotation alone does not make a parameter optional. |
| `[FromKeyedServices(key)] T`         | `sp.GetRequiredKeyedService<T>(key)`; the key must be a compile-time constant (KRX018). The parameterless (`InheritKey`) form is KRX018. With a default value, the same optional forms as above using `GetKeyedService`.                       |
| `IServiceProvider`                   | the scope's provider (counts as an injected parameter)                                                                                                                                                                                         |
| `IMediator`, `ISender`, `IPublisher` | the scoped mediator (no cycle: handlers are resolved lazily)                                                                                                                                                                                   |
| `CancellationToken`                  | the call's token, never injected                                                                                                                                                                                                               |

An injected service is stored in a field (of the handler class, or of the pipeline context for a behavior method), so an injected
parameter that is a `ref struct` (`Span<T>`, `TypedReference`, any `ref struct`), a pointer or a function pointer is KRX013 and the
handler or behavior is skipped. A parameter that breaks KRX013 for any reason does not count as an injected service, so a static
handler class does not also get KRX014 and the base-constructor part of KRX007 does not apply.

**Shared services in a pipeline context.** A pipeline context (request, stream or publish; see Generated pipeline shape) resolves each
service its behavior methods inject **once**: it has one field per distinct service, not one per behavior parameter. A service is
identified by its type (without a top-level nullable annotation) and its `[FromKeyedServices]` key, so keyed and unkeyed services of one
type, or two keys, stay separate. When the consumers of a service differ:

- if **any** consumer is required (no default value), the context resolves it as required (`GetRequiredService`/`GetRequiredKeyedService`)
  and every consumer, optional ones included, receives that instance;
- if **all** consumers are optional, the context resolves it once with `GetService`/`GetKeyedService` into a nullable field (`T?`, a
  `Nullable<T>` for a value type, matched with the type pattern above), and each call site applies its own parameter's default:
  `pipeline.Service0 ?? <default>`.

The fields are named `Service0`, `Service1`, ... in order of first use (the pipeline's behaviors outermost first, each one's injected
parameters in declared order). An edit that changes neither the set of services nor the order in which they are first used keeps the
names (adding a behavior that injects only services the pipeline already has, in an order that does not move any first use, for
example); an edit that keeps the set but changes that order renumbers them (open-questions OQ-64). The model computes
the shared services (each behavior's parameters refer to a service by index); the emitter only writes them out. A behavior's unkeyed
`IServiceProvider` is the scope's provider the context is built with, held in a field like any shared service and never resolved
(open-questions OQ-63).

**Consequence:** a transient service that several behavior methods of one pipeline inject is **one instance per pipeline context**, that
is per scope and pipeline, not one per behavior method. Behaviors that need separate instances inject a factory (or
`IServiceProvider`) and create them.

Keyed services need `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0 or newer, which the `Keryx` package brings on `net10.0`
and `netstandard2.0` and the shared framework provides on `net11.0` (see Package and assemblies).

## Diagnostics and code fixes

`Keryx.Analyzers` contains two generators, a set of analyzers and the code fixes:

- **`HandlerGenerator`** runs in every project. It emits handler partials and the manifest. It **never** reports diagnostics; it silently
  skips malformed handlers.
- **`HostGenerator`** is active only in a host (`build_property.KeryxHost` is `true`, see Opting in). It emits the mediator and `AddKeryx()`, and reports the host-only
  rules (KRX005 across assemblies, KRX008 for pairs involving referenced behaviors, KRX009 for behaviors a referenced handler's manifest
  attaches, KRX030–KRX037). Its cached models carry an equatable `LocationInfo` (file path,
  `TextSpan`, `LinePositionSpan`). The host computes its diagnostics from the cached models in a step of their own; only the output that
  reports them is combined with the compilation, to find the syntax tree by file path and turn the `LocationInfo` back into a `Location`
  on that tree (`Location.Create(tree, span)`). Per-file `.editorconfig` severities then apply to host diagnostics (a `Location` without
  a tree would see only global options). The source output does not read the compilation and stays cached on unrelated edits. Host
  diagnostics are reported at the source location when the symbol is in source, otherwise without a location (`Location.None`): a host
  is an MSBuild property, so there is no attribute to report on. A diagnostic without a location takes only the project-wide severity (a
  global AnalyzerConfig file such as `.globalconfig`, `<NoWarn>`, `<WarningsAsErrors>`), not a per-file `.editorconfig` section.
- **`DiagnosticAnalyzer`s** report every per-assembly rule.
- **`CodeFixProvider`s** (the `CodeFixes` folder) offer the code fixes below. Only the IDE loads them (see Package and assemblies).

KRX001, same-assembly KRX005 and in-source KRX008 need the whole compilation. Their descriptors carry
`WellKnownDiagnosticTags.CompilationEnd` and are reported from `RegisterCompilationStartAction`/`RegisterCompilationEndAction` (RS1037).
KRX022 is reported once per compilation from the parse options, from `RegisterCompilationAction` with `Location.None`, so it carries
`CompilationEnd` too. In the IDE these appear only with full-solution analysis or on build; the README says so, and code-fix tests run
the analyzer with compilation-end diagnostics enabled.

The host reports KRX009 for referenced handlers too: each behavior that a referenced request or stream handler's manifest attaches
(`KeryxHandler.Behaviors`; a notification handler attaches none) and that does not fit the target as the host resolves it is KRX009 (the
analyzer's descriptor, not a compilation-end diagnostic), with the analyzer's reasons, without a location, naming the target
`HandlerClass.Handle` (the method is private, so its signature is not visible). The behavior is skipped and the handler kept. A behavior
class that the behavior rules reject is the library's KRX019 and is not reported again. The same holds for a `[Behavior]` on a
referenced notification type (open-questions OQ-55): when the behavior does not fit the notification type as the host resolves it, the
host reports KRX009 without a location, naming the notification type and the behavior, skips the behavior and keeps the rest of
the publish pipeline.

Every ID has a help link. IDs are grouped: KRX001–KRX029 per-assembly rules (KRX005, KRX008 and KRX009 are also checked by the host across
assemblies), KRX030–KRX039 host-only rules, KRX040–KRX049 attribute placement rules.

| ID     | Severity             | Reported by                         | Condition                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| ------ | -------------------- | ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| KRX001 | Error                | analyzer                            | A request or stream request type has no handler in its assembly. Reported in every assembly, hosts included: a request's handler lives in the request's own assembly (see Requests and notifications; a handler elsewhere is KRX023).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| KRX002 | Error                | analyzer                            | Handler candidate is not `private static`, or has a return type not allowed for its request kind.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| KRX003 | Error                | analyzer                            | Handler candidate has no request/notification parameter, more than one, or a request parameter typed as `IRequest<T>`, an abstract request or an interface.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| KRX004 | Error                | analyzer                            | Handler class or a containing type is not `partial`, is file-local, is not `public` outside a host or is narrower than `internal` in a host, or is a struct, record struct or interface.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| KRX005 | Error                | analyzer, host                      | Two handlers exist for the same request type. Same assembly: analyzer (compilation end). Across assemblies: host, with its own message ("Request type 'X' has more than one handler in this host: 'A' (assembly 'LibA'), 'B' (assembly 'LibB')"), without a location (or on the host's own handler when one is the host's); the host dispatches none of them. Duplicates inside one referenced assembly are that assembly's analyzer's to report. Since KRX023, generated manifests cannot make two assemblies handle one request, so the host check is a safety net for manifests Keryx did not generate (open-questions OQ-60).                                                                                                                                                                                                                                                                                                                                                                                                                                |
| KRX006 | Error                | analyzer                            | A class directly contains more than one handler method.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| KRX007 | Error                | analyzer                            | Handler class declares an instance constructor or a record primary constructor, or has injected parameters and is `abstract`, declares or inherits `required` members, or has a base type without an accessible parameterless instance constructor.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| KRX008 | Error                | host (analyzer for in-source pairs) | Behavior ordering has a cycle, or `RunsAfter`/`RunsBefore` contradicts phase or order. Checked once per pair of behavior classes (see Ordering).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| KRX009 | Error                | analyzer, host                      | A behavior attached by attribute to a request or stream handler method or a notification type does not fit its target: the type is not a behavior class, the class has no behavior method of the target's kind, the method's constraints do not hold, or a closed method's message or response (or item) type is not identical to the target's.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| KRX010 | —                    | —                                   | Retired (was "request is not sealed"; dispatch is by exact type, so sealing does not matter).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| KRX011 | Error                | analyzer                            | A type implements more than one closing of `IRequest<>`/`IStreamRequest<>`, both kinds, or a request interface and `INotification`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| KRX012 | Error                | analyzer                            | A request, stream request or notification type has type parameters, is nested in a generic type, or is a `ref struct`; or a handler class has type parameters or is nested in a generic type.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| KRX013 | Error                | analyzer                            | A handler or behavior method parameter is `ref`, `in`, `out` or `params`, an injected parameter is a ref struct or a pointer, or there is more than one `CancellationToken`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| KRX014 | Error                | analyzer                            | `static` handler class has injected parameters.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| KRX015 | Error                | analyzer                            | Handler class declares a member named `InvokeAsync` or a `__keryx_` member.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| KRX016 | Error                | analyzer                            | Outside a host, a request, response (including generic arguments), notification, behavior class or behavior method that a pipeline needs, or a type the generated constructor or invoker mentions (injected parameter types, a polymorphic handler's parameter type), is not effectively public. In a host, such a type or behavior is narrower than `internal` ("must be at least internal"); file-local types count as narrower than `internal`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| KRX017 | Warning              | analyzer                            | Handler response type differs from the request's `TResponse` only in nullability.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| KRX018 | Error                | analyzer                            | `[FromKeyedServices]` key on a handler or behavior method parameter is not a compile-time constant, or uses the parameterless form.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| KRX019 | Error                | analyzer                            | Behavior class has an unsupported shape (see Behavior methods): the class is not `static` or is generic; a kind attribute has no valid behavior method of that kind; the class declares more than one behavior method of one kind; a behavior method is partially open, has other type parameters, has a `next` parameter that is not a method type parameter constrained `struct` and the kind's `next` interface, has a `next` constraint and message type of different kinds, or has a return type the kind does not allow; an open behavior method's message type parameter is not constrained to the kind's message interface (`TRequest : IRequest<TResponse>`, `TRequest : IStreamRequest<TItem>`, `TNotification : INotification`) or to an interface that derives from it with the same response or item type parameter (`TRequest : ICommand<TResponse>`); or a closed behavior method's `next` does not carry the message's response or item type. Writing the response or item type parameter annotated (`TResponse?`) is not KRX019 (see Matching). |
| KRX020 | —                    | —                                   | Retired (was "behavior class does not have exactly one public constructor"; behaviors are static classes and have no constructors).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| KRX021 | Info                 | analyzer                            | A struct request is boxed when sent through `ISender`; only the host's typed overload avoids it. Reported at the call site, on the request argument of an `ISender.Send`/`ISender.Stream` call (also through `IMediator`) that converts a value type to the interface.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| KRX022 | Error                | analyzer                            | The project compiles with a C# language version below 14 (Keryx needs C# 14 or later; see Package and assemblies). Reported once per compilation (compilation end), in every project that references Keryx; the generators emit nothing there. Fix: remove the `LangVersion` setting (the package then defaults it to `latest`) or set it to 14 or later.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| KRX023 | Error                | analyzer                            | A handler takes a request or stream request type declared in another assembly. A request's one handler lives in the assembly that declares it, so that assembly checks it is handled (KRX001) and every host composes the same handler. Reported on the handler's request parameter, in libraries and hosts alike; the generators skip the handler, and a host counts it as handling its request (no KRX030 on top). Notifications are not affected.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| KRX030 | Error (configurable) | host                                | A **referenced** request or stream request type has no handler anywhere in the composed set: a safety net, since each assembly checks its own requests (KRX001). Covers request types in referenced assemblies (from the scan), including types declared in another host; it catches a referenced handler the host excluded, an internal handler of a referenced host, or a library built with KRX001 suppressed. The host's own request types are KRX001's. Reported without a location (a referenced type has no source location), so its severity is set for the whole host: a global AnalyzerConfig file (`.globalconfig`, `is_global = true`), `<NoWarn>` or `<WarningsAsErrors>`, not a per-file `.editorconfig` section.                                                                                                                                                                                                                                                                                                                                  |
| KRX031 | Error                | host                                | A host (`KeryxHost` is `true`) targets a framework older than `net10.0`; the message suggests `<KeryxHost>false</KeryxHost>`. Reported without a location. The host generator emits nothing there (KRX030 is still reported).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| KRX032 | Error                | host                                | A referenced assembly's manifest has a newer `FormatVersion` than this Keryx understands. The message says to upgrade Keryx in this project.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| KRX033 | Warning              | host                                | A referenced assembly references Keryx but has no `[assembly: KeryxManifest]` (the generator did not run there).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| KRX034 | Error                | host                                | A type named in a manifest, or one the generated code derives from it (a referenced handler's response, item or constructor parameter type, an injected parameter type of a referenced behavior method), cannot be resolved, has a type argument that cannot be resolved, or is not accessible from the host (missing reference). Reported once per type and manifest assembly, without a location, naming the assembly to reference. For a behavior method's injected parameter type it is reported per behavior method, naming the method and its kind; the class stays in the pipelines of its other kinds. A second message covers a manifest entry that resolves but does not fit the types the host resolves (version skew, for example a notification type that is no longer a notification in the contracts assembly the host references). See How the host reads it.                                                                                                                                                                                    |
| KRX035 | Info                 | host                                | Lists every global behavior applied in this host and the assembly it came from, once per behavior class and kind it makes global ("Global request behavior 'Audit' …"): every `[GlobalBehavior]` the behavior rules accept and no `ExcludeBehavior` removes, whether or not it matches a target, except a kind whose behavior method KRX034 leaves out. Reported on the `[GlobalBehavior]` attribute when the behavior is in source, otherwise without a location.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| KRX036 | Warning              | host                                | An exclusion has no effect: `ExcludeHandler` names a type that is not a referenced handler class the host would compose, or `ExcludeBehavior` names a type that is not part of any pipeline.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| KRX037 | Info                 | host                                | A struct notification is delivered to a handler typed as a base interface or `INotification`, so it is boxed once per `Publish` (see Notifications).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| KRX040 | Warning              | analyzer                            | `[HandlerOrder]` on a method that is not a notification handler.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| KRX041 | Warning              | analyzer                            | `[PublishStrategy]` on a type that is not a notification.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| KRX042 | Warning              | analyzer                            | `[Behavior]` on a class or struct that is not a notification type, on a method that is not a handler candidate, or on a notification handler method (notification behaviors wrap the whole publish, so it has no effect there); or `[GlobalBehavior]` on a class without a behavior kind attribute, or on a method that is not a behavior method.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| KRX043 | Warning              | analyzer                            | `[assembly: ExcludeHandler]` or `[assembly: ExcludeBehavior]` in a project that is not a host (`KeryxHost` is not `true`). Only a host reads them, so they have no effect there. Reported on the attribute.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |

**Code fixes**

- **KRX001**: _Add handler stub_, in libraries and hosts alike. Nested request: the stub goes into the containing class, next to the
  request. Top-level request: a new `public partial class {RequestName}Handler` in a new file `{RequestName}Handler.cs` beside the
  request, in the request's namespace, with a numeric suffix if the name is taken. The stub is
  `private static ValueTask<TResponse> Handle(Request request, CancellationToken ct) => throw new NotImplementedException();`. `IRequest`
  gets a `ValueTask` stub. Stream requests get an `IAsyncEnumerable<TItem>` stub. (The second action, _Mark this assembly as a contracts
  assembly_, went with `[assembly: KeryxContracts]`; see [future-requirements.md](../future-requirements.md).)
- **KRX002**: make the method `private static`.
- **KRX004**: add the missing `partial` modifiers and, outside a host (or in a host, for a type narrower than `internal`), `public`.
- **KRX014**: remove `static` from the handler class.

## Behaviors

### Behavior kinds

A behavior is a `static` class marked with one or more **kind attributes**. There are three kinds. Each kind attribute says which
pipeline the class's behavior method of that kind wraps:

| Kind attribute           | Wraps                                | Message type parameter (open method) | `next` constraint            | Return type               |
| ------------------------ | ------------------------------------ | ------------------------------------ | ---------------------------- | ------------------------- |
| `[RequestBehavior]`      | one `Send` of a request              | `TRequest : IRequest<TResponse>`     | `struct, INext<TResponse>`   | `ValueTask<TResponse>`    |
| `[StreamBehavior]`       | the inner stream of a stream request | `TRequest : IStreamRequest<TItem>`   | `struct, IStreamNext<TItem>` | `IAsyncEnumerable<TItem>` |
| `[NotificationBehavior]` | one whole `Publish` call             | `TNotification : INotification`      | `struct, INext`              | `ValueTask`               |

Notification handlers have no behaviors of their own: the handlers of one publish are called without per-handler pipelines (see
Notifications).

```csharp
public interface INext<TResponse>
{
    ValueTask<TResponse> InvokeAsync(CancellationToken cancellationToken);
}

public interface INext
{
    ValueTask InvokeAsync(CancellationToken cancellationToken);
}

public interface IStreamNext<TItem>
{
    IAsyncEnumerable<TItem> InvokeAsync(CancellationToken cancellationToken);
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequestBehaviorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class StreamBehaviorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class NotificationBehaviorAttribute : Attribute;
```

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
public static class CheckStock
{
    public static ValueTask<Guid> Handle<TNext>(CreateOrder.Command request, TNext next, IStock stock, CancellationToken ct)
        where TNext : struct, INext<Guid>
        => stock.Has(request.Sku, request.Quantity) ? next.InvokeAsync(ct) : throw new OutOfStockException(request.Sku);
}
```

Requests with no response are `IRequest<Unit>`, so a request behavior sees `TResponse = Unit` and one shape covers both cases.

### Behavior methods

A **behavior method** is a `static` method named `Handle` or `HandleAsync` (equivalent) in a class that carries a kind attribute. The
user writes it; the host's generated pipeline calls it directly. There is no generated half and no `partial` requirement.

- **The class** is `static` and not generic, and carries at least one kind attribute. One class may carry several. For each kind
  attribute it carries, it declares exactly one behavior method of that kind.
- **A method's kind** follows from its signature alone: its `next` constraint (`INext<TResponse>` for the request kind,
  `IStreamNext<TItem>` for the stream kind, `INext` for the notification kind) and its message type, which is the kind's message (a
  concrete request, stream request or notification type for a closed method, or a type parameter constrained to the kind's message
  interface for an open one). Each kind has its own method shape, so the methods of a class that carries several kind attributes never
  compete for one kind. A method counts only for the kinds its class's attributes name.
- **Parameters** mirror a handler's: the message, `next` and an optional `CancellationToken` are found by type, in any order. `next` is
  a method type parameter constrained `struct` and the kind's `next` interface (table above). Every other parameter is an injected
  service, resolved with the handler parameter rules (Injected parameters): `[FromKeyedServices]` with a constant key (KRX018), and no
  `ref`, `in`, `out`, `params`, ref struct or pointer parameters and no second `CancellationToken` (KRX013).
- **Return type:** `ValueTask<TResponse>` (request), `IAsyncEnumerable<TItem>` (stream) or `ValueTask` (notification). The method may
  be synchronous or `async` (an `async` iterator for the stream kind).
- **Generic shape.** The method carries the type parameters; the class does not. A behavior method is either:
    - **fully open:** every message and response (or item) position is a method type parameter, and the method has no other type
      parameters besides `TNext`: `Handle<TRequest, TResponse, TNext>` (request), `Handle<TRequest, TItem, TNext>` (stream),
      `Handle<TNotification, TNext>` (notification). The message type parameter must be constrained to the kind's message interface
      (table above): `where TRequest : IRequest<TResponse>`, `where TRequest : IStreamRequest<TItem>` or
      `where TNotification : INotification`; without it the method is KRX019. A constraint to an interface that derives from it counts
      (open-questions OQ-57): `where TRequest : ICommand<TResponse>` with `ICommand<T> : IRequest<T>`, `where TRequest : IListing<TItem>`
      with `IListing<T> : IStreamRequest<T>`, `where TNotification : IDomainEvent` with `IDomainEvent : INotification`; the derived
      interface must pass on the method's response or item type parameter, and it narrows the method to the targets that implement it.
      The compiler guarantees the same conversion either way. A constraint to an unrelated interface is KRX019. The response (or item)
      type parameter may be written annotated, `TResponse?` (`TItem?`), in `next`, the return type and the message constraint
      (open-questions OQ-58); on a type parameter constrained to `struct`, `TResponse?` is `Nullable<TResponse>`. Matching substitutes
      the annotated type there and checks it with its nullability (see Matching). Further constraints on those type parameters narrow
      where it applies (see Matching); or
    - **fully closed:** generic only in `TNext`. The message parameter is a concrete request, stream request or notification type, and
      the `next` constraint carries exactly that request's response or item type (`INext<Unit>` for an `IRequest`).

    A partially open method (some positions open and some closed, or an open position inside a constructed type such as
    `INext<Result<T>>`) is KRX019. Full type-parameter unification is future work.

- **Accessibility.** A behavior used from another assembly (a library's attached, assembly or global behavior read by a host) has a
  `public` class and method. Inside a host, `internal` is enough for the host's own pipelines. A behavior class or method narrower than
  that is KRX016, and the host skips it; file-local types count as narrower than `internal`.
  Any other shape is KRX019: a class that is not `static` or is generic, a kind attribute without a valid behavior method of that kind,
  two behavior methods of one kind, a partially open method or one with other type parameters, an open method whose message type
  parameter lacks the kind's message constraint (directly or through an interface that derives from it), a `next` parameter without the `struct` and interface constraint, a return type the
  kind does not allow, and a `static` `Handle`/`HandleAsync` method whose kind the class does not carry, that has no valid `next`
  parameter, or whose `next` constraint and message type belong to different kinds. C# does not allow two methods of one class that
  differ only in their constraints (CS0111), so a class that holds behaviors of two kinds with the same parameter list names one
  `Handle` and the other `HandleAsync`.

No behavior instance ever exists, so nothing is allocated for a behavior itself, per scope or per call. A behavior method's injected
parameters are resolved once per DI scope into the generated pipeline context (see Generated pipeline shape), which already holds the
handler, and the link calls the method as
`Audit.Handle<CreateOrder.Command, Guid, Link2>(request, new(pipeline, request), pipeline.Service0, ct)`, with the arguments in the method's
declared order. The call is a direct static call: no interface, no virtual or generic virtual dispatch. A transient service injected into a
behavior is created once per scope and pipeline, however many behavior methods of the pipeline inject it: they share the context's one
instance (Injected parameters, Shared services in a pipeline context). Nothing is registered in DI for a behavior, so a behavior cannot be
replaced through DI; exclude it with `ExcludeBehavior` and attach another.

### Three ways to attach a behavior

- **Per target:**
    - request and stream behaviors: `[Behavior(typeof(Audit))]` on the handler method;
    - notification behaviors: `[Behavior(...)]` on the notification type. `[Behavior]` on a notification handler method has no effect
      and is KRX042, because a notification handler has no pipeline of its own.
- **Per assembly:** `[assembly: Behavior(typeof(Transaction))]` applies to every matching target in that assembly (for a notification
  behavior, the notification types declared there).
- **Global:** `[GlobalBehavior]` on the behavior class applies each of its behaviors to every matching target in the host's composed
  set, from any referenced assembly. `[GlobalBehavior]` on a behavior method makes only that method's kind global; on both the class and
  a method, every kind is global.

`typeof(X)` in `[Behavior]`, `[ExcludeBehavior]`, `[RunsAfter]` and `[RunsBefore]` always names the behavior class. It means "the
behavior of X whose kind fits the target (or pipeline)": `[Behavior(typeof(X))]` attaches the method of X whose kind matches the
target, and every reference names the whole class, never one closing of a method.

The applied set for a target is the union of the three scopes, de-duplicated by behavior class, minus the host's
`[assembly: ExcludeBehavior(typeof(X))]` entries, each of which removes X from every pipeline of every kind. Scope decides _whether_ a
behavior applies, never _where_ it sits; phases decide that.

### Matching

A behavior matches a target as follows:

- The class has a behavior method whose kind fits the target: `[RequestBehavior]` for a request, `[StreamBehavior]` for a stream
  request, `[NotificationBehavior]` for a notification type's publish pipeline.
- **Open methods:** the message and response (or item) type parameters are substituted with the target's types (request: `TRequest`,
  `TResponse`; stream: `TRequest`, `TItem`; notification: the concrete published type), and then every constraint must hold. Constraint
  types that mention other type parameters (`where TRequest : IRequest<TResponse>`) are checked after substitution, with nullability:
  their type arguments must match the target's own base type or interface with the same annotations (`IRequest<>` and `INext<>` are
  invariant; `out` and `in` type parameters, and `IEquatable<T>` as the compiler treats it, allow the usual widening). `TNext` is always
  the generated link and is not part of matching. The generated link implements `INext<TResponse>` (`IStreamNext<TItem>`) over the
  target's exact response or item type, so the method's `next` and the type argument of its return type, after substitution, must be
  that type with the same nullability.
- **Annotated response or item type parameters** (open-questions OQ-58). Where the method writes `TResponse?` (`TItem?`), the
  substituted type is annotated: `string?` for `string`, while a value type stays as it is (`T?` on an unconstrained `T` substituted with
  `int` is `int`). `TResponse` itself is the target's response without its annotation, as C# infers it (or the response as it is, when
  that is what fits); on a type parameter constrained to `struct`, `TResponse?` is `Nullable<TResponse>`, and `TResponse` is the
  underlying type of a nullable value type response, which must match exactly. So a method that writes `TResponse?` fits a request
  whose response is annotated (`string?`), nullable-oblivious (declared under `#nullable disable`) or a value type (`int`, and `int?`),
  and does not fit a non-annotated reference response (`string` in a nullable-enabled context); a `struct`-constrained one fits only a
  nullable value type response (`int?`). A method that does not fit follows the rules below: a global or assembly behavior is skipped
  for that target, an attached one is KRX009. Generated code enables nullable and names an oblivious response without an annotation, so
  the call of a method that fits only through an oblivious response is emitted between `#nullable disable warnings` and
  `#nullable restore warnings`, as the oblivious declaration has them; every other fit compiles without nullability warnings
  (CS8619, CS8620, CS8631). The match is stored in the host's incremental model as text and flags, never as symbols.
- **Closed methods:** the message type and the response (or item) type must be identical to the target's (request: `TRequest` and
  `TResponse`; stream: `TRequest` and `TItem`; notification: the concrete published type). Assignable is not enough. The method's
  `next` and the type argument of its return type are checked with nullability like an open method's after substitution: annotations
  count when both sides are annotated (a method over `string` does not fit a request that responds with `string?`, because the link
  implements `INext<string?>` and the mismatch would be a nullability error in generated code, and a method that returns
  `ValueTask<string?>` does not fit a request that responds with `string`); an oblivious annotation on the method's side matches anything,
  and an annotated one over an oblivious response fits and is called with nullable warnings disabled, as above (OQ-58).

Keryx checks constraints itself, because `IMethodSymbol.Construct` does not validate them:

- `class`, `struct`, `unmanaged`, `new()` and `notnull` as the compiler defines them. Nullable annotations count for `notnull` and `class`
  (not `class?`), because CS8714/CS8634 are errors when warnings are errors.
- A type constraint holds only when the conversion from the type argument to the constraint type is an identity conversion, an implicit
  reference conversion (`IsImplicit && IsReference`) or a boxing conversion. User-defined, numeric and nullable conversions do not count,
  even though `Compilation.ClassifyConversion` reports them as implicit.
- A constraint type that is an interface never holds for `Nullable<T>` (CS0313), although `ClassifyConversion` reports a boxing
  conversion from `T?` to `T`'s interfaces.
- A type constraint that is not annotated (`where TResponse : IEquatable<TResponse>`) does not hold for an annotated argument
  (`string?`), because the compiler reports CS8631 although `ClassifyConversion` ignores nullability. An oblivious constraint accepts
  anything; a constraint that is a type parameter takes its argument's annotation.
- A type with `required` members (declared on it or a base type) does not satisfy `new()` (CS9040) unless its parameterless constructor
  carries `[SetsRequiredMembers]`.
- Constraint types nested in generic types (`Outer<TResponse>.IMarker`, `Outer<TResponse>.Inner<TRequest>`) are substituted through
  their containing type.

Assembly and global behaviors that do not match are skipped silently, and `[assembly: Behavior(typeof(X))]` naming a type that is not a
behavior class reports nothing (it matches no target). A behavior attached explicitly to a request or stream handler method or a
notification type that does not match is KRX009.

### Generated pipeline shape

For each pipeline the host emits:

- a sealed **pipeline context** class, created lazily once per scope, that holds the handler instance (for a publish pipeline, those of
  the notification type's handlers, and the publish strategy that runs them) and each distinct service the pipeline's behavior methods
  inject, once (one `ServiceN` field per service type and key, shared by every behavior method that injects it; see Injected
  parameters), resolved in its constructor. Every pipeline with behaviors has one, even when it holds nothing but its links (a static
  invoker and no injected services, or a notification type without handlers whose behaviors inject nothing); a request or stream
  pipeline without behaviors whose handler's invoker is static has none and calls the invoker directly. Dropping the contexts that hold
  nothing is proposed (open-questions OQ-65, open);
- one small **link** struct per position, a `readonly struct` with a primary constructor and an expression-bodied `InvokeAsync`, with a
  one-line summary that names where its `next` goes (decided 2026-11-26):

    ```csharp
    /// <summary>The <c>next</c> of <c>Sample.Step1</c>: <c>Sample.Step2</c>.</summary>
    public readonly struct Link1(__Pipeline0 pipeline, global::Sample.Ping request) : global::Keryx.INext<string>
    {
        public global::System.Threading.Tasks.ValueTask<string> InvokeAsync(global::System.Threading.CancellationToken cancellationToken) =>
            global::Sample.Step2.Handle<global::Sample.Ping, string, Link2>(request, new(pipeline, request), cancellationToken, pipeline.Service0);
    }
    ```

    Its `InvokeAsync(ct)` calls the behavior method at position k+1 with a target-typed `new(pipeline, request)`, the next link, as
    `next` (the link type stays the explicit type argument for `TNext`) and that method's injected services read from the context; the last
    link calls the handler's `InvokeAsync` (in a publish pipeline, it runs the handlers under the publish strategy). The entry of the
    pipeline calls the outermost behavior method the same way. Every link takes the context and the message (`notification` in a publish
    pipeline). A primary constructor parameter that `InvokeAsync` does not read is not captured, so a link holds what it held before the
    primary constructors: the last link of a pipeline whose handler's invoker is static holds the request alone, and the last link of a
    publish pipeline without handlers holds nothing. Such a parameter is warning CS9113, which the mediator's header suppresses
    (`#pragma warning disable CS0612, CS0618, CS9113`). Taking only what a link reads or passes on is proposed with OQ-65 (open). Types are
    `global::`-qualified; there are no `using` aliases.

Links are **flat**: each one holds at most two fields regardless of pipeline depth, so struct copying does not grow with depth. A behavior
may call `next` more than once (retry). The `CancellationToken` is passed as the `InvokeAsync` argument, never stored in a link. The
spelling does not change the machine code: the Phase 4 gate prototype, written the same way, still allocates nothing and inlines every
link and behavior method (open-questions, "Links with primary constructors").

Stream pipelines use the same shape with `IStreamNext<TItem>` links (see Streaming), and the publish pipeline of a notification type
with `INext` links (see Notifications).

## Ordering

Behaviors are ordered by a fixed `Phase` enum with gaps, then by an optional number inside the phase, then by explicit relationships.
Lower values run first, so the lowest phase is the outermost wrapper.

```csharp
public enum Phase
{
    Outermost     = 0,    // exception handling
    Observability = 100,  // logging, tracing, metrics
    Security      = 200,  // authorization
    Validation    = 300,
    Default       = 400,  // used when no phase is declared
    Caching       = 450,
    Transaction   = 500,
    Innermost     = 600
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BehaviorOrderAttribute(Phase phase) : Attribute
{
    public Phase Phase { get; } = phase;
    public int Order { get; init; }   // default 0
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RunsAfterAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RunsBeforeAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }
```

`[BehaviorOrder]`, `[RunsAfter]` and `[RunsBefore]` go on the behavior class and apply to every behavior method it holds. The same
attribute on a behavior method overrides the class's for that method's kind only: a method's `[BehaviorOrder]` replaces the class's,
and a method's `[RunsAfter]` (or `[RunsBefore]`) list replaces the class's list of that attribute. `typeof(X)` names the behavior class
`X`; in a pipeline it means `X`'s behavior of that pipeline's kind. There is no generic attribute form: a static class cannot be a type
argument (CS0718).

Position belongs to the behavior. Attachment sites cannot override phase or order in v1. Users can pick values between phases, such as
`(Phase)250`.

**Sort rules**, per pipeline, at compile time:

1. Phase ascending.
2. Within a phase, `Order` ascending.
3. Within equal (phase, order), `RunsAfter`/`RunsBefore` edges are sorted topologically.
4. Any remaining tie is broken by ordinal fully qualified behavior class name.

`RunsAfter`/`RunsBefore` are also checked against every (phase, order) bucket: an edge that agrees with phase and order is a no-op; an
edge that contradicts them is KRX008; a cycle is KRX008. An edge to a behavior class that has no behavior in a given pipeline is
ignored. Contradictions are checked once per pair of behavior classes (and kind, where a method overrides the class's ordering), not
once per pipeline, and reported once per pair whichever side declares the edge. A cycle is reported once per set of behavior classes:
it is a strongly connected component of the edges left unsorted, so a behavior that only waits on a cycle is not named in it. The
analyzer reports pairs and cycles of in-source behaviors; the host reports those that involve a referenced behavior, and a cycle of
in-source behaviors stays the analyzer's even when a referenced behavior waits on it.

## Streaming

A stream request implements `IStreamRequest<TItem>`. Its handler follows the normal handler rules and returns `IAsyncEnumerable<TItem>`.

**No stream behaviors:** `Stream` returns the handler's enumerable as is. No extra allocation.

**Stream behaviors wrap the stream.** A `[StreamBehavior]` follows the Immediate.Handlers `StreamingBehavior` model, adapted to struct
links. Its behavior method calls `next.InvokeAsync(ct)` to get the inner `IAsyncEnumerable<TItem>` and returns the stream the outer
behavior or the caller enumerates, usually by re-yielding the inner stream with `await foreach` in an `async` iterator. It can do
anything an iterator can: filter, map, buffer, catch, retry by calling `next` again, or not call `next` at all (for example to serve
cached items).

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

**Links.** As for requests, the host emits one flat `readonly struct StreamLink_k(Context pipeline, TRequest request) : IStreamNext<TItem>`
per position, with an expression-bodied `InvokeAsync(ct)` that returns the result of the stream behavior method at position k+1, called
with a target-typed `new(pipeline, request)` as `next` and its injected services from the pipeline context, or the handler's
`InvokeAsync` at the end. Every link takes the context and the request; the last link of a pipeline whose handler's invoker is static
does not read the context, which is then not captured (see Generated pipeline shape). The token is an argument, never stored in a link.

**Cancellation.** The outermost element of the pipeline receives the `Stream(request, ct)` token as its argument and the consumer's
`WithCancellation` token through `GetAsyncEnumerator`; every inner element receives one token, as its argument, and is enumerated
without a second one (a stream behavior enumerates `next.InvokeAsync(ct)` without `WithCancellation`). So two tokens meet in exactly
one place:

- A stream behavior written as an `async` iterator puts `[EnumeratorCancellation]` on its `cancellationToken` (CS8425 warns when it is
  missing) and passes that token to `next.InvokeAsync`. When it is outermost, the compiler-generated iterator links the two tokens only
  when both can be cancelled and differ; that link allocates one `CancellationTokenSource` (a documented exception in the Performance
  contract).
- With no stream behaviors the handler is outermost, and an `async` iterator handler links the two tokens the same way in its own
  iterator. Below a stream behavior, the handler is called with the token its caller passed on, so it never links tokens a second time.

Null and unknown stream requests are validated synchronously in a non-iterator wrapper before the first behavior is called. `Stream()`
then calls the outermost element directly, so a non-iterator stream behavior's method (and any non-iterator element it calls eagerly,
down to the first iterator) runs, and can throw, at the `Stream()` call. Iterator stream behaviors and iterator handlers stay lazy until
the first `MoveNextAsync`. Deferring the outermost call would need one more iterator per `Stream` call, which the Performance contract
does not allow.

Phases, scopes, constraints, ordering and diagnostics work exactly as for request behaviors.

## Notifications

A notification implements `INotification`. `IPublisher.Publish` dispatches on the notification's runtime type (see Generic entry points
for how this avoids boxing struct notifications). `Publish` has only the generic form,
`Publish<TNotification>(TNotification, CancellationToken) where TNotification : INotification`: a non-generic `Publish(INotification)`
overload would box every struct notification. Base-typed calls (`Publish<IDomainEvent>(e)`) work through the `GetType()` path for a
reference-type `TNotification`.

**Handlers.** Same rules as request handlers, returning `ValueTask` or `Task`. Any number of handlers per notification, across
assemblies. Zero handlers is valid.

**Known notification types.** The host knows:

- every concrete notification type declared in its own source that generated code can reach (at least `internal`; narrower and
  file-local types are skipped, like handlers);
- the public concrete notification types of every referenced assembly that references Keryx (the third scan in How the host reads it);
- the parameter type of each of its own and referenced notification handlers when that is itself a concrete notification, wherever it
  is declared.

A typed `Publish` overload is generated for every known type, also for one that has neither handlers nor notification behaviors (it
completes immediately). A known type declared in another assembly gets its notification behaviors from `[Behavior]` on the type, the
`[assembly: Behavior]`s of its own assembly and the host's globals.

**Zero handlers.** For a notification type the host knows, the publish pipeline (its notification behaviors) still runs when there are no
handlers, so logging and outbox behaviors see every publish. `Publish` completes immediately only when the type has neither handlers nor
notification behaviors, or when the runtime type is unknown to the host. A notification whose runtime type is unknown runs nothing, even
when it derives from a known type with handlers: the typed overload of a type that is not sealed checks the runtime type and redispatches,
as typed `Send` does (see Mediator).

**Polymorphic handlers.** A handler whose parameter is a base class or interface (`Handle(IDomainEvent e)`), or `INotification` itself
(a catch-all), receives every known concrete notification that converts to that type. The host expands each concrete notification's
handler list at compile time. A struct notification delivered to a handler typed as an interface or `INotification` is boxed **once per
`Publish`**, and that one box is reused for every such handler (Info KRX037). Making that case an error instead would narrow polymorphic
delivery for struct notifications, so the box is a documented exception in the Performance contract.

**Handler order.** `[HandlerOrder(int)]` on the handler method (default 0), ascending; ties by ordinal fully qualified handler class name.
Exact-type and polymorphic handlers share one ordering.

**Publish strategy.** Sequential by default: handlers are awaited one after another in order. A notification opts into concurrent
execution with `[PublishStrategy(PublishStrategy.Parallel)]`:

- Each handler is started in handler order on the calling thread without awaiting it (no `Task.Run`). A synchronous throw while
  starting a handler is caught and recorded, and the remaining handlers still start.
- Only the `ValueTask`s that have not completed are collected. If none are pending, parallel publish allocates nothing.
- Otherwise Keryx allocates one array plus one `AsTask()` per pending `ValueTask`, and awaits `Task.WhenAll`.
- Failures, synchronous and asynchronous, are combined into the `AggregateException` in handler order.

Every await in generated publish code (the sequential strategy's slow path after the first handler that did not complete
synchronously, and the parallel strategy's `Task.WhenAll`) uses `ConfigureAwait(false)`: after a handler yields, the next handlers of a
sequential publish may run on a thread-pool thread. Handlers and behaviors await however they choose, and the caller's own
`await Publish(...)` still resumes on its context.

**Failures.** Every handler runs, whatever the others do. If any handler fails, `Publish` throws one `AggregateException` containing every
failure, in handler order. This holds even when exactly one handler fails: callers always see the same exception shape. The exception
list is allocated only when a failure happens. A notification behavior wraps the whole publish, so an exception it throws (or lets
through from `next`, which is already the `AggregateException`) propagates as it is, not wrapped again; a synchronous throw from the
outermost notification behavior throws from `Publish` itself, like a request behavior's from `Send`.

**Cancellation.** Cancelling the publish token does not skip handlers. Every handler is still invoked, with the (possibly cancelled)
token, so each can observe it. An `OperationCanceledException` thrown by a handler is collected like any other failure into the
`AggregateException`.

**Behaviors.**

- A `[NotificationBehavior]` wraps the whole `Publish` call, including all handlers and the strategy, once per publish (also with zero
  handlers). The last link of the publish pipeline runs the handlers under the publish strategy. Attached with `[Behavior]` on the
  notification type, per assembly (`[assembly: Behavior]` in the notification type's assembly) or globally.
- It uses the non-generic `INext` and returns plain `ValueTask`, rather than `INext<Unit>` and `ValueTask<Unit>`.
- It uses phases, order, constraints and exclusion exactly like request behaviors.
- `TNotification` is always the **concrete published type**. Constraints are checked against it.
- Handlers in a publish are called directly, without per-handler pipelines, under either strategy. There is no per-handler behavior:
  `[Behavior]` on a notification handler method has no effect and is KRX042. A handler that needs a per-handler concern, such as a
  retry, does it itself.

```csharp
public enum PublishStrategy { Sequential, Parallel }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class PublishStrategyAttribute(PublishStrategy strategy) : Attribute { public PublishStrategy Strategy { get; } = strategy; }

[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlerOrderAttribute(int order) : Attribute { public int Order { get; } = order; }
```

## Generated host

### Opting in

A project is a host when its `KeryxHost` MSBuild property is `true`. The package's `build/Keryx.targets` sets it when the project leaves it
empty, after the project body (NuGet imports a package's targets from `nuget.g.targets`), so `OutputType` and `IsTestProject` are known:
`true` for an executable (`OutputType` `Exe` or `WinExe`) and for a test project (`IsTestProject` is `true`), `false` otherwise, class
libraries included. A value the project or its `Directory.Build.props` sets wins:

```xml
<!-- A class library that is a host (the benchmark subjects' Keryx host does this). An executable or test project that references
     Keryx but is not a host sets it to false. -->
<PropertyGroup>
    <KeryxHost>true</KeryxHost>
</PropertyGroup>
```

`build/Keryx.props` makes it compiler visible, and every generator and analyzer reads `build_property.KeryxHost`; unset (a compilation
without the package's build files, such as a hand-wired analyzer) or anything but `true` (case-insensitive) is not a host. The package
ships `buildTransitive/` files that import `build/`, so a project that gets Keryx only through another package or project, whose
analyzers flow to it, gets the same default; `buildMultiTargeting/` imports `build/` for the outer build of a multi-targeting project.

A host must target `net10.0` or newer (KRX031). Generated host types are `internal`, in the host's root namespace (read from the
`RootNamespace` MSBuild property through `CompilerVisibleProperty`, falling back to the assembly name). Both are sanitised into a valid
namespace (a valid one is unchanged): characters that are not valid in an identifier become `_`, a segment that starts with a digit gets
a `_` prefix, an empty segment becomes `_` (`My..App` → `My._.App`), and a segment that is a C# keyword is escaped with `@`
(`Contoso.@namespace`). Several hosts in one solution, including a test project that references the app host, do not collide. An
explicit namespace property (for example `KeryxHostNamespace`) can be added later if needed.

In a host that targets an older framework the host generator emits nothing and reports KRX031 (KRX030 for referenced requests is still
reported), so the user sees KRX031 instead of compiler errors in generated files. The handler generator is unaffected.

### Mediator

```csharp
internal sealed partial class KeryxMediator : IMediator
{
    public KeryxMediator(IServiceProvider serviceProvider);

    // ISender / IPublisher members, plus typed overloads per request and notification:
    public ValueTask<Guid> Send(CreateOrder.Command request, CancellationToken cancellationToken = default);
    public ValueTask<Unit> Send(DeleteOrder.Command request, CancellationToken cancellationToken = default);   // IRequest
    public IAsyncEnumerable<Order> Stream(ListOrders.Query request, CancellationToken cancellationToken = default);
    public ValueTask Publish(OrderCreated notification, CancellationToken cancellationToken = default);
}
```

- **Lifetime: scoped.** The mediator holds one lazily created pipeline context per pipeline. After the first call in a scope, nothing is
  resolved or allocated per call. Singletons and hosted services that need the mediator create a scope with `IServiceScopeFactory`. A
  handler's transient dependencies behave as scoped, because the handler is cached per scope, and so do a behavior method's, because
  its injected services are cached in the pipeline context (once per scope and pipeline, one instance for every behavior method of the
  pipeline that injects the service).
- **Concurrent first use.** One scope can use the mediator concurrently (parallel publish, handlers that send in parallel, user
  `Task.WhenAll` over `Send`). Contexts are created with
  `Volatile.Read(ref __p17) ?? Interlocked.CompareExchange(ref __p17, new P17(_sp), null) ?? __p17`, so every caller in the scope uses the
  same context; this is allocation-free after warm-up. A losing race may construct, and discard, one extra context, so transient
  dependencies can be created more than once under concurrent first use. A concurrency test covers this.
- **Typed overloads** skip type dispatch. They exist only on the internal host mediator, so they are a host-only optimization; libraries
  call through `IMediator`/`ISender`/`IPublisher`.
- A typed overload for a request or notification class that is not sealed checks the runtime type first: it compares
  `request.GetType()` with its own type (a method-table compare, no allocation) and, when they differ, goes through the generic entry
  point, which dispatches on the exact runtime type (or throws `KeryxHandlerNotFoundException`). So an `Animal`-typed variable that
  holds a `Dog` reaches `Dog`'s handler, never `Animal`'s. Sealed classes and structs skip the check.
- A typed overload for an `IRequest` returns `ValueTask<Unit>`, exactly as the pipeline produces it. There is no second adapter.

### Generic entry points

- `Send<TResponse>(IRequest<TResponse>)` dispatches on `request.GetType() == typeof(X)` (a method-table pointer compare), with one
  exact-type `when` guard per request type. Never type patterns: `X x` compiles to `isinst`, which a type derived from `X` also matches,
  and would break exact-type dispatch. With more than 32 request types, it looks the type up in a `FrozenDictionary<Type, int>` and
  switches on the index, which compiles to a jump table. Owner decision 7 (2026-11-26) asks for switch expressions,
  `return requestType switch { _ when requestType == typeof(X) => __Dispatch0(request, cancellationToken), ..., _ => throw __HandlerNotFound(requestType) };`
  and `return __RequestIndex(requestType) switch { 0 => ..., _ => throw ... };`, and says the change must not regress. A switch
  expression returns through one result local, so the JIT does not tail-call its arms and measured slower; until open-questions OQ-67
  is decided the generated code is a switch statement of the same sections with one `return` each,
  `switch (requestType) { case var _ when requestType == typeof(X): return __Dispatch0(request, cancellationToken); ... }` and
  `switch (__RequestIndex(requestType)) { case 0: return ...; }`, followed by `throw __HandlerNotFound(requestType);`. The threshold
  comes from the Phase 3 benchmark (open-questions OQ-15): the lookup costs a flat 4.5 ns, the chain about 0.13 ns per compare, and at
  32 types the chain's worst case is 0.6 ns slower than the lookup and its average 1.5 ns faster.
- Each arm is its own non-inlined `__Dispatch{n}<TResponse>` method that holds the `Unsafe.As` below, and the lookup is a non-inlined
  `__RequestIndex(Type)` that returns -1 for an unknown type, so the generic `Send` holds no address-taken locals. Arms written inline
  in `Send` made the Tier 1 code zero a frame with one `ValueTask` per arm in the prolog of every call and kept it from tail-calling the
  arms (about 45 ns per call under Dynamic PGO, for every request count).
- The arm result `ValueTask<X>` is reinterpreted as `ValueTask<TResponse>` with
  `Unsafe.As<ValueTask<X>, ValueTask<TResponse>>(ref vt)`. This is safe because `IRequest<>` is invariant and a request implements
  exactly one closing (KRX011). Never `(ValueTask<TResponse>)(object)vt`, which boxes.
- Class requests are cast with `Unsafe.As<X>(request)`; struct requests are unboxed.
- `Stream<TItem>` uses the same dispatch; the result is a reference type and needs only a reference cast.
- `Publish<TNotification>`: when `typeof(TNotification).IsValueType`, generated arms compare `typeof(TNotification) == typeof(X)` (folded
  by the JIT, because value-type instantiations are not shared) and convert with `Unsafe.As<TNotification, X>(ref notification)`, so a
  struct notification is never boxed by dispatch
  (`switch (typeof(TNotification)) { case var _ when typeof(TNotification) == typeof(X): return ...; } return default;`, a switch
  expression ending with `_ => default` under decision 7; OQ-67). Only for a reference-type `TNotification` does dispatch call
  `notification.GetType()`, in the shape `Send` uses, ending with `return default;`.
- Above the threshold (for `Publish`, with a reference-type `TNotification`) the lookup is by exact type as well: a type derived from a
  dispatched type, with no handler of its own, is not in the table, so it takes the default arm (not found for a request or a stream
  request, nothing to run for a notification) and never reaches the base type's handler. Tests cover both sides of the threshold for
  `Send`, `Stream` and `Publish<TNotification>`, in the generated syntax, in the compiled IL (a `switch` opcode above the threshold, none
  at or below it) and at run time (decided 2026-11-26).
- A `null` argument throws `ArgumentNullException`. An unknown request type throws `KeryxHandlerNotFoundException` naming the type, with
  a hint to reference the handler's assembly from the host. Both throw synchronously, before any behavior runs; behaviors never observe
  them.

### DI registration

```csharp
internal static class KeryxServiceCollectionExtensions
{
    public static IServiceCollection AddKeryx(this IServiceCollection services);
}
```

`AddKeryx()` uses `TryAdd*`, so a second call does nothing. It registers, all scoped and all with generated factory lambdas:

- `KeryxMediator`, and `IMediator`, `ISender`, `IPublisher` forwarding to that same instance;
- every handler class that has injected parameters.

Behaviors are not registered: they are static, and the pipeline contexts resolve their injected services.

## Cross-assembly discovery

### Manifest

Each assembly that runs the generator carries its manifest **in compiled metadata**, so it works for project references and for NuGet
packages built elsewhere:

- `[assembly: KeryxManifest(FormatVersion = 1)]`.
- On every generated public handler partial, one type-level attribute:

```csharp
namespace Keryx.Internal;

public enum HandlerKind { Request, Stream, Notification }

[AttributeUsage(AttributeTargets.Class), EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KeryxHandlerAttribute(Type targetType, HandlerKind kind) : Attribute
{
    public Type TargetType { get; } = targetType;     // request, stream request, or notification parameter type (may be a base/interface)
    public HandlerKind Kind { get; } = kind;
    public Type[] Behaviors { get; init; } = [];        // attached behavior classes, in declared order; none for notification handlers
    public int Order { get; init; }                    // [HandlerOrder] for notification handlers
    public bool StaticInvoker { get; init; }
}

[AttributeUsage(AttributeTargets.Assembly), EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KeryxManifestAttribute : Attribute
{
    public int FormatVersion { get; init; }
}
```

- Global behaviors need no extra attribute: the kind attributes, `[GlobalBehavior]`, `[BehaviorOrder]` and `[RunsAfter]`/`[RunsBefore]`
  are already on the public behavior class and its public behavior methods. Notification behaviors attached to a notification type are
  on the public notification type. Assembly-level `[Behavior]` attributes are read from the assembly.
- Response type, handler constructor parameters, behavior methods' injected parameters and keyed-service keys are derived from the public
  request, handler and behavior types; they are not duplicated in the attribute.
- Host-internal handlers are not described by a manifest.

Private `Handle` methods and their attributes are not visible across assemblies (Roslyn imports public metadata only, and reference
assemblies strip private members). That is why the generator copies everything the host needs onto the public partial.

### How the host reads it

- The host's **own** handlers come from its syntax pipeline (`ForAttributeWithMetadataName` and syntax providers). A generator never sees
  its own output, so the host does not read its own manifest.
- The host's own notification types also come from the syntax pipeline (for polymorphic expansion), not from the scan. Its own request
  types are KRX001's (the analyzer), so the host does not collect them.
- **Referenced** assemblies are read with `Specular.Scanning.Sources`, through **one**
  `context.Scan<KeryxReferenceModel>("Keryx", requests, transform)` call. `requests` is an `IncrementalValuesProvider<ScanRequest>` holding
  three requests in a fixed order (built from one `CompilationProvider.Select` followed by `SelectMany`). Each request is pruned to the
  assemblies that reference Keryx (`DependenciesFromAssemblyOf(<Keryx marker type>)`) and excludes the host itself
  (`NotFromAssembly(compilation.Assembly)`); `Keryx.dll` declares no handlers or requests, so it is not scanned:
    1. types `WithAttribute(KeryxHandlerAttribute)`: handler classes;
    2. global behaviors: behavior classes that carry `[GlobalBehavior]` on the class or on a behavior method;
    3. non-abstract types `AssignableTo` `IRequest<>`, `IStreamRequest<>` or `INotification`: every referenced request and notification
       type, used for KRX030 (referenced request types) and for polymorphic notification expansion.
- Inside the transform, results are read by position (`scan.Results[0..2]`). The transform reads attribute arguments and each referenced
  assembly's attributes (`KeryxManifest`, assembly-level `Behavior`), projects them into equatable records, and stores the
  projection per assembly in Specular's **ScanCache** (keyed by MVID) using a source-generated `JsonSerializerContext`. The cache's data
  version is a generator constant, `ProjectionDataVersion`, bumped whenever the projected record, its derivation or its JSON contract
  changes. The referenced assembly's `FormatVersion` is stored as a field inside the projection, so KRX032 works on cache hits. Later
  builds and IDE edits reuse the projection while the reference is unchanged.
- The projection names types by assembly and metadata name: handler classes with their `[KeryxHandler]` arguments, global behaviors,
  request and notification types, the assembly's `[assembly: Behavior]`s and the manifest's format version. Whether a referenced
  assembly is a host is not visible in its metadata (a host is an MSBuild property) and the host does not need it. An assembly whose manifest has a newer format version (KRX032) or that references Keryx without a manifest (KRX033) contributes
  nothing: no handlers, behaviors, request types or notification types, so its request types get no KRX030 and its notification types
  are unknown to the host.
- **One composition step** combines the host's settings, its own handler entries and notification types from the syntax pipeline, and
  the projections with the compilation. It resolves the names, closes and matches behaviors, expands polymorphic handlers, and composes
  every pipeline, own and referenced, into one equatable `HostModel`. Composing needs symbols, and the host's own handlers need the
  references' global behaviors and notification types, which a syntax transform cannot see. The step is fed by the compilation, so it
  runs on every compilation; the source output stays cached while the model is unchanged. The syntax pipeline's handler step carries only
  what identifies a handler (the request it handles, its class's metadata name and its location).
- Two `Scan` calls must never share the consumer id `"Keryx"`: each writes the whole cache file.
- Only compile references count (`compilation.References`). A reference hidden from the host (`PrivateAssets`, disabled transitive
  references) hides its handlers too.
- The host merges its own handlers with the referenced manifests, applies `[assembly: ExcludeHandler(typeof(X))]` and
  `[assembly: ExcludeBehavior(typeof(X))]`, composes every pipeline from the three behavior scopes, orders it and emits dispatch.
- Every type a referenced manifest names (the handler class, `TargetType`, `Behaviors`, global behaviors, `[assembly: Behavior]` types,
  request and notification types), and every type the generated code derives from it (a referenced handler's response or item type and
  constructor parameter types, the injected parameter types of a referenced behavior method the host calls, including behaviors from
  `[Behavior]` on a referenced notification type), must resolve and be accessible from the host; otherwise it is KRX034. The handler is
  left out of the host; when its request type resolves, the request still counts as handled, so KRX030 does not add a second error.
  Injected parameter types are checked per behavior method, that is per kind (open-questions OQ-57): KRX034 names the behavior method
  and its kind, and the class is left out of the pipelines of that kind only, staying in those of the kinds whose methods resolve. A
  global or `[assembly: Behavior]` class is resolved before the host knows which of its kinds any pipeline uses, so every behavior method
  of the kinds it applies to is checked (for a global, the kinds its `[GlobalBehavior]` makes global), whatever targets exist; KRX035
  then lists the global for the kinds that resolve. An attached behavior is checked for the kind of the handler it is attached to (a
  `[Behavior]` on a referenced notification type, for the notification kind); one that fails leaves its handler out too, so the
  handler's pipeline does not change silently. A referenced handler whose entry resolves but no longer
  describes a handler the host can compose (its `Kind` does not fit the target, for example a notification type that is no longer a
  notification in the contracts assembly the host references; a request handler cannot drift this way, since it lives in its request's
  assembly) is KRX034 with its own message, and is left out the same way.

### Host control

- Global behaviors from every referenced assembly apply automatically. The host removes any of them with `ExcludeBehavior`, and Info
  KRX035 lists every applied global and its source assembly, once per kind it makes global.
- A request handled in two assemblies is KRX005 in the host (a safety net since KRX023; open-questions OQ-60). `ExcludeHandler` removes a
  referenced handler (for example in a test host without the handler's infrastructure). A referenced notification handler can be
  replaced by the host's own; a referenced request handler cannot, because a host's handler of a referenced request is KRX023
  (open-questions OQ-59). `ExcludeHandler` removes only handler classes that come from references (their `[KeryxHandler]` entries); naming the host's own handler class, or any type that is not a referenced handler class the host would
  otherwise compose, has no effect and is KRX036. The exclusion is matched against the manifest's name of the handler class (assembly
  and metadata name) before anything else of the entry is resolved, so an excluded handler whose types the host cannot resolve reports
  no KRX034, and the exclusion counts as used (no KRX036). Excluding a referenced request handler leaves its request unhandled (KRX030).
- A referenced request or stream request type with no handler in the host's composed set is KRX030, an error; the host's own request
  types are KRX001's. Each assembly handles its own requests, so KRX030 is a safety net: it catches an excluded handler, an internal
  handler of a referenced host, or a library built with KRX001 suppressed. It has no location, so its severity is changed for the whole
  host, in a global AnalyzerConfig file (`.globalconfig`) or with `<NoWarn>`; there is no per-type relief (`ExternallyHandled` was removed on 2026-11-26, see [future-requirements.md](../future-requirements.md)).
- **A host referenced by another host** (typically a test project that references the app host): the referenced host's internal
  handlers carry no manifest, so the referencing host composes only the handlers the referenced host keeps `public`. A host that is
  referenced by another host keeps the handlers it wants composed `public`. KRX030 enforces it: the referencing host reports every
  request type declared in the referenced host that has no handler it can compose, as for any other reference. Nothing extra is reported
  in the referenced host.
- `[assembly: ExcludeHandler]` and `[assembly: ExcludeBehavior]` are read only by a host. In a
  project that is not a host they have no effect, and the analyzer reports warning KRX043 on each of them.

### Versioning

- `KeryxManifest.FormatVersion` starts at 1. A host reads every format version up to its own and reports KRX032 for a newer one.
- Within a major version, manifest attribute shapes and the generated `InvokeAsync`/constructor signatures change only additively.
- The analyzers and `Keryx.dll` ship in one package, so they are always in lockstep within one project.
- `EnablePackageValidation` with a baseline once a stable version exists.

## Attribute reference

| Attribute                                                         | Targets                                                                             | Purpose                                                                                                                                                |
| ----------------------------------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `[RequestBehavior]`, `[StreamBehavior]`, `[NotificationBehavior]` | class (static behavior class)                                                       | Behavior kind: the class declares one behavior method of each kind it carries. Shape errors are KRX019.                                                |
| `[Behavior(Type)]`                                                | method (request or stream handler), class/struct (notification), assembly; multiple | Attaches the named behavior class's behavior of the target's kind. Elsewhere, including on a notification handler method, it is KRX042.                |
| `[GlobalBehavior]`                                                | class (behavior), method (behavior method)                                          | Applies the class's behaviors (on a method: that kind only) to every matching target in every host that references it. On a non-behavior it is KRX042. |
| `[BehaviorOrder(Phase) { Order }]`                                | class (behavior), method (behavior method)                                          | Behavior position. On a method it overrides the class's for that kind.                                                                                 |
| `[RunsAfter(Type)]`, `[RunsBefore(Type)]`                         | class (behavior), method (behavior method); multiple                                | Relative order inside a (phase, order) bucket, naming a behavior class. On a method the list overrides the class's for that kind.                      |
| `[assembly: ExcludeBehavior(Type)]`                               | assembly; multiple                                                                  | Host removes a behavior class from all pipelines of every kind. Outside a host: no effect (KRX043).                                                    |
| `[assembly: ExcludeHandler(Type)]`                                | assembly; multiple                                                                  | Host ignores a referenced handler class. Outside a host: no effect (KRX043).                                                                           |
| `[PublishStrategy(PublishStrategy)]`                              | class/struct (notification)                                                         | Sequential (default) or parallel publish.                                                                                                              |
| `[HandlerOrder(int)]`                                             | method                                                                              | Notification handler order.                                                                                                                            |
| `[KeryxHandler(...)]`, `[assembly: KeryxManifest]`                | generated only                                                                      | Manifest, in `Keryx.Internal`.                                                                                                                         |

The attributes not declared in an earlier section:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BehaviorAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class GlobalBehaviorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeBehaviorAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExcludeHandlerAttribute(Type handlerType) : Attribute { public Type HandlerType { get; } = handlerType; }
```

Every public attribute is sealed. `AllowMultiple` follows the table ("multiple"); `Inherited` keeps its default (`true`) everywhere. The
generator reads attributes through Roslyn, which ignores `Inherited`, so it has no effect on Keryx itself.

## Incremental generation rules

- `IIncrementalGenerator` only. Pipeline models are value-equatable records with equatable arrays. No `Compilation`, `ISymbol`,
  `SyntaxNode` or `Location` in cached models.
- Every pipeline step has a tracking name. The incremental test runs the driver with `trackIncrementalGeneratorSteps: true`, makes an
  unrelated edit (a new file with no Keryx types), and asserts that every named step after the scan step, and every source output, reports
  `Cached` or `Unchanged`. It also asserts the reverse: swapping in a reference with a changed manifest produces `Modified`. Steps fed
  directly by `CompilationProvider` always run and are not asserted.
- Generated code is built with Roslyn's `SyntaxFactory` through shared helpers (`GeneratedSyntax`), the pattern Dovetail's generators use,
  never as concatenated text (decided 2026-11-26). Syntax is built only inside `RegisterSourceOutput`: the models carry strings, never
  syntax. Each file's compilation unit is normalized once (`NormalizeWhitespace`, four spaces, `\n` line endings), then a layout pass fixes
  what normalization alone gets wrong or hard to read (`GeneratedSyntax.Render`: blank lines between members, a break after `=>`, the
  closing brace of a switch expression on its own line, `is` after `)`, no space after the `?` of a nullable type unless a name follows:
  `typeof(int?)`, `string?[]`), and the header (`// <auto-generated/>`, `#nullable enable`, the `#pragma` for obsolete types, and in the
  mediator for CS9113, the unread link parameters) is added last. No type declaration carries a stray `;`, and a nullable type renders `T?`.
  On a very large host the one normalization dominates the generator's time (open-questions OQ-66, open).
- Generated code is deterministic (ordinal sorting everywhere, no timestamps or GUIDs), starts with `// <auto-generated/>`, enables
  nullable and uses `global::`-qualified names. `[GeneratedCode("Keryx", version)]` goes on each generated member of a user's partial type,
  and at type level only on types Keryx owns outright (see Handlers, Example). A snapshot test asserts that a user's partial declaration
  carries no `[GeneratedCode]`.
- Hint names are the fully qualified metadata name of the handler class, including containing types, sanitised (drop `global::`,
  replace `+`, `` ` `` and any character outside `[A-Za-z0-9_.]` with `_`), then 8 lowercase hex digits of the 32-bit FNV-1a hash of the
  UTF-8 bytes of the exact (ordinal) metadata name, then `.Keryx.g.cs`. Class `Create` in namespace `MyApp.Orders` gets
  `MyApp.Orders.Create.<hash>.Keryx.g.cs`; class `Create` nested in `public static partial class Orders` (`MyApp.Orders+Create`) gets
  `MyApp.Orders_Create.<hash>.Keryx.g.cs`. The sanitised prefix keeps the name readable and keeps `Orders.Create` and
  `Customers.Create` apart; the hash keeps apart names the sanitising merges (a nested `Orders.Cancel` and a top-level `Orders_Cancel`)
  and names that differ only in case, which Roslyn compares case-insensitively. Escaping alone would still collide on case. The
  manifest goes to `KeryxManifest.g.cs`, and host files use fixed names (`KeryxMediator.Keryx.g.cs`,
  `KeryxServiceCollectionExtensions.Keryx.g.cs`); neither can collide with a handler hint name.
- No runtime reflection anywhere: no `MakeGenericType`, no `Activator`, no reflection-based DI activation.

## Performance contract

These replace the draft's "minimal allocations" and the handoff's "must not allocate beyond the handler":

- **Send, synchronous handler:** after one warm-up call in the same scope, Keryx allocates 0 bytes per call, at any behavior depth,
  provided the behaviors' own work completes synchronously. Holds for the typed overloads and, for class requests, the generic entry point,
  for value-type and reference-type responses. A struct request sent through `ISender` is boxed at the call site (KRX021); that box is not
  counted against Keryx. Behaviors are static methods, so no behavior instance is allocated, per scope or per call; the pipeline context,
  created once per scope and pipeline, holds the handler and the behaviors' injected services.
- **Send, yielding handler:** Keryx-generated code allocates nothing beyond the state-machine boxes of async methods the user wrote
  (handler and behaviors). The only generated async path is `UnitAdapter.ToUnit` (non-generic `ValueTask`/`Task` → `ValueTask<Unit>`). It
  uses `PoolingAsyncValueTaskMethodBuilder` on net10+ and must add 0 bytes in steady state; a benchmark checks it. Typed `IRequest`
  overloads return `ValueTask<Unit>`, so they add no second adapter.
- **Stream:** with no stream behaviors, `Stream` returns the handler's enumerable as is: 0 extra allocations. With stream behaviors, per
  `Stream` call: one iterator per stream behavior (its own `async` iterator). Links are structs and add nothing. Documented exception: one
  linked `CancellationTokenSource` when the `Stream()` token and the `WithCancellation` token are both cancellable and differ. 0 per item.
  The count assumes each stream behavior calls `next` once and allocates nothing else itself; a stream behavior that calls `next` again
  also creates the inner iterators again, and what a stream behavior allocates beyond its own iterator is its own.
- **Publish:** 0 extra allocations for a sequential publish with synchronously completing handlers. Notification behaviors wrap the
  whole publish once, and handlers are called without per-handler pipelines. Parallel: 0 B when every handler completes synchronously;
  otherwise one array plus one `AsTask()` per pending `ValueTask`, plus what `Task.WhenAll` allocates.
  The exception list only on failure. Documented exception: a struct notification delivered to interface-typed handlers is boxed once per
  `Publish` (KRX037).
- Allocation tests warm up first, measure only a region that completes synchronously (`IsCompletedSuccessfully`, then `.Result`), and
  use `GC.GetAllocatedBytesForCurrentThread`. They cover value-type and reference-type responses through both entry points, class and
  struct requests (asserting the split above), and streams both with a default token on one side and with two distinct cancellable tokens
  (asserting exactly the one linked `CancellationTokenSource` in total, with a stream behavior outermost). Tests of `async` behaviors
  compile the test host with optimizations on: a Debug build compiles an `async` method's state machine to a class, allocated on every
  call even when it completes synchronously, and that allocation is the behavior's own. BenchmarkDotNet `[MemoryDiagnoser]` results are
  the authoritative evidence; the thread-counter tests are regression guards.

### Phase 4 gate

Before behaviors are generated, a hand-written prototype of the struct-based pipeline is benchmarked. It passes when all of these hold:

1. 0 B allocated for a synchronous pipeline at depths 1, 3 and 8;
2. `DisassemblyDiagnoser` output shows the behavior method and `next.InvokeAsync` called directly (no generic-virtual-method lookup);
3. mean time at depth 3 within 1.25× of a hand-written nested-call baseline.

Where BenchmarkDotNet's `DisassemblyDiagnoser` does not run (it supports only Windows and Linux), a `DOTNET_JitDisasm` listing of the same
pipelines run to Tier 1 (`Keryx.Benchmarks --prototype-disasm`) is equivalent evidence for criterion 2. The benchmarks CI job re-runs the
prototype (`--filter *PipelinePrototype*`) on a Linux runner with `DisassemblyDiagnoser`.

The gate passed on all three criteria with the instance behaviors of Phases 4 to 8 (open-questions OQ-36), and criterion 1 also held for
the generated pipelines. It was re-run after the static-behavior rework, with the prototype's behaviors written as static
behavior methods, and passed on all three criteria again (OQ-36): 0 B at every depth, every behavior method and link inlined with no
generic-virtual-method lookup, and depth 3 at 0.66× the baseline. Static calls inline at least as well as the instance behaviors did.
It was re-run once more after the links moved to primary constructors and target-typed `new(pipeline, request)` (decided 2026-11-26),
with the prototype written in that spelling: the Tier 1 code of every depth is byte for byte the same, it allocates nothing, and depth 3
runs at 0.67× the baseline.

A failed gate does **not** stop implementation (decided 2026-11-26). The measured numbers and the disassembly findings are recorded in
`docs/adr/open-questions.md`, the work is committed with the struct-based pipeline as designed, and work continues. The ADR's fallback
(generator-inlined before/after hooks for simple behaviors) is **not** adopted without a decision.

## Benchmarks

`test/Keryx.Benchmarks` (BenchmarkDotNet, net10.0, `[MemoryDiagnoser]`) compares, each library in its own small class library
(`test/benchmark-subjects`):

- a hand-written direct-call baseline;
- Keryx;
- Mediator (martinothamar) 3.0.2, scoped and singleton lifetimes;
- MediatR 15.x, pinned exactly, run without a license key (allowed for benchmarking), with the `LuckyPennySoftware.MediatR.License` log
  category filtered; no key is ever committed;
- Immediate.Handlers 4.4.0;
- ZeroAlloc.Mediator 7.0.1 (its generator, `ZeroAlloc.Mediator.Generator`, pinned to the same version).

Scenarios: `Send` through the typed overload and the generic entry point; 0, 1, 3 and 8 behaviors; synchronous and `Task.Yield` handlers;
`Stream` of 100 items with 0 and 3 stream behaviors; `Publish` to 1 and 5 handlers. Keryx also runs `SendUnitYieldValueTask` and
`SendUnitYieldTask` (an `IRequest` whose `ValueTask`/`Task` handler yields, against the same handler awaited directly), the benchmark the
Performance contract names for `UnitAdapter.ToUnit`.

Setup rules, which every number depends on:

- Every library sends the same `sealed record` class requests and notifications, with the same handler bodies (ZeroAlloc.Mediator's
  ZAM003, which asks for a `readonly record struct`, is suppressed in its project).
- Lifetimes are each library's defaults, except Mediator, which runs scoped and singleton. Scoped mediators and handlers are resolved
  once, from one scope that lives for the whole benchmark; per-scope setup is not measured.
- Behaviors are pass-through and complete synchronously. Each library attaches them its own way (Keryx and Immediate.Handlers per
  handler, Mediator and MediatR as constrained open behaviors selected by marker interfaces, ZeroAlloc.Mediator with one
  `[PipelineBehavior(AppliesTo = ...)]` class per request and position).
- The baseline calls the handler directly at every depth and never models behaviors, so the ratio is the whole cost of the mediator and
  its behaviors. The Publish baseline calls the handlers in turn from a non-async method, with an `async` fallback only when a handler
  does not complete synchronously.
- "Typed" is each library's per-request entry point, "generic" its `IMediator`/`ISender` generic method. A scenario a library has no API
  for is left out and shown as n/a.

CI builds the benchmarks, runs `Keryx.Benchmarks --verify` (every comparison benchmark is called with a call counter on, and the run
fails when a library does not run the scenario's handlers and behaviors), and runs every benchmark with `--job Dry` only; numbers are
never published from shared runners. The README's numbers come from local runs, with the machine and job stated.

## AOT verification

CI publishes the sample host with `PublishAot=true`, treats every IL2xxx/IL3xxx warning as an error, and runs the published binary.

## Alternatives considered

- **Handler interface (`IRequestHandler<,>`):** rejected. It forces a public instance method and hides which methods are handlers from the
  analyzer.
- **`[UnsafeAccessor]` for private handlers:** rejected in favor of generated public partials, which work everywhere including AOT and
  across assemblies.
- **Delegate-chain pipeline:** rejected because it allocates per call.
- **Nested link structs** (each link embeds the next): rejected; struct copying grows with depth. Flat links instead.
- **Behaviors resolved through `IRequestPipelineBehavior<,>` with open-generic DI registration:** rejected; generic virtual dispatch per
  link and not AOT-safe for value-type responses.
- **Polymorphic request dispatch:** rejected; exact-type dispatch is simpler and as fast whether or not a request is sealed.
- **Integer `Order` only:** rejected because numbers collide across method, assembly and global scopes.
- **Open set of user-defined phases:** rejected for v1. A fixed enum with gaps keeps ordering globally consistent.
- **A manifest class with static data, or a JSON/string payload:** rejected. Method bodies and initializers are unreadable from metadata,
  and type names round-trip poorly. Type-level attributes bind `typeof` arguments directly.
- **Specular ScanCache as the manifest itself:** rejected as the _source_. The cache lives in the host's `obj/` and cannot be written by a
  library about itself. It is used as the host-side cache of the attribute-derived manifest.
- **A separate `Keryx.Analyzers.CodeFixes` assembly for the code fixes:** the original choice, to keep RS1038 quiet. Replaced on
  2026-11-26 by one analyzer assembly that holds the code fixes too; RS1038 is suppressed and the rule in Package and assemblies keeps
  code-fix types off every command-line code path.
- **MediatR-style interface names (`IPipelineBehavior`, `INotificationBehavior`, `IStreamBehavior`, ...):** used until 2026-11-26, then
  renamed to one pattern, `I{Kind}PipelineBehavior` (`IRequestPipelineBehavior`, `INotificationPipelineBehavior`,
  `INotificationHandlerPipelineBehavior`, `IStreamPipelineBehavior`, `IStreamWrapPipelineBehavior`), so every behavior interface follows
  the same pattern and names the pipeline it belongs to. The stream interfaces were reshaped later the same day (next entry), and all
  five interfaces were removed later the same day (Interface-based instance behaviors, below).
- **Hook-first stream behaviors** (`IStreamPipelineBehavior` as the fused `OnStart`/`OnItem`/`OnComplete`/`OnError` hooks, plus a
  separate `IStreamWrapPipelineBehavior` whose `Handle(request, IAsyncEnumerable<TItem> next, ct)` received the inner stream already
  built): used until 2026-11-26, then replaced by the Immediate.Handlers `StreamingBehavior` model. `IStreamPipelineBehavior` became the
  wrap shape with a struct `IStreamNext<TItem>` link, consistent with `INext<T>`, so a behavior decides when, whether and how often the
  rest of the pipeline is built; `IStreamWrapPipelineBehavior` is gone; the hooks stay as the optional `IStreamHookBehavior`, fused per
  segment. The hooks were removed later the same day (Stream hook behaviors, below).
- **Stopping implementation when the Phase 4 gate fails:** the original rule, replaced on 2026-11-26. A failed gate is recorded and
  reported, and work continues (see Phase 4 gate).
- **`set` instead of `init` on attribute properties, to lower the language floor to C# 8:** rejected 2026-11-26 (open-questions OQ-20).
  The generic ordering attributes needed C# 11 regardless at the time, and `build/Keryx.props` defaults `LangVersion` to `latest`.
- **Keeping the C# 11 floor, or lowering it to C# 9, once the generic ordering attributes were gone** (open-questions OQ-52): rejected
  2026-11-26. With the generic attributes removed (Generic `RunsAfter<T>`/`RunsBefore<T>`, below), only the `init` properties (C# 9)
  still need a newer language. The owner raised the floor to C# 14 instead: Keryx already requires the .NET 10 SDK, whose default
  language is C# 14, so the floor matches the toolchain and generated code may use every feature it offers. `netstandard2.0` targets
  stay supported; dropping them was considered and rejected.
- **Separate `Keryx.Abstractions` package:** rejected in favor of one `Keryx` package; contracts projects reference it too.
- **Interface-based instance behaviors** (`IRequestPipelineBehavior<TRequest, TResponse>`, `IStreamPipelineBehavior<TRequest, TItem>`,
  `INotificationPipelineBehavior<TNotification>`, `INotificationHandlerPipelineBehavior<TNotification>`): the design of Phases 4 to 8,
  replaced on 2026-11-26 by static behaviors. A behavior was a class implementing one interface closing, open with the interface's arity
  or closed and non-generic, registered in DI as each closed type it was used with, resolved once per scope into the pipeline context,
  and called through `Handle<TNext>` on the concrete type (implicit, non-virtual implementations, so the sealed interface slot gave a
  direct call; `abstract` classes and `virtual` implementations were KRX019). Exclusions and ordering edges could name an open
  `typeof(X<,>)`, matching every closing, or one closing, matching only that one. Replaced because a static behavior needs no instance:
  nothing is registered, resolved or allocated for the behavior itself per scope or per call, and the call is a plain static call. One
  class can hold the behaviors of several kinds, generics move to the method, and every reference names the whole class, so the closed
  forms of exclusion and edges are gone.
- **Exactly one public behavior constructor (KRX020):** the rule for instance behaviors, because the host emitted an explicit factory
  per behavior. Picking the constructor with the most parameters, as MS.DI does, was the alternative (open-questions OQ-11). Retired
  with instance behaviors on 2026-11-26: a static behavior has no constructor, and its method's parameters are the injected services.
- **Stream hook behaviors** (`IStreamHookBehavior<TRequest, TItem>` with `OnStartAsync`, `OnItem`, `OnCompleteAsync` and
  `OnErrorAsync`; each run of adjacent hook behaviors fused into one generated iterator, a fused hook segment): an optional
  zero-extra-iterator form beside the wrap behaviors, removed on 2026-11-26. Each segment needed rules of its own (exceptions raised
  outside the segment, terminal hook calls that throw, `ConfigureAwait` in the generated iterator; open-questions OQ-21, OQ-38, OQ-39),
  generated code of its own and a second stream behavior shape, while a wrap behavior does everything a hook did for one iterator.
- **Generic `RunsAfter<T>`/`RunsBefore<T>`:** added on 2026-11-26 beside the `typeof` forms and removed the same day. A static class
  cannot be a type argument (CS0718), so the generic form cannot name a static behavior.
- **KRX030 relief per referenced assembly** (`[assembly: KeryxIgnoreUnhandledFrom(typeof(MarkerInThatAssembly))]`, open-questions OQ-10):
  rejected 2026-11-26 in favor of naming each request type with `[assembly: ExternallyHandled(typeof(X))]`, which kept the error for
  every other request in the same assembly. `ExternallyHandled` itself was removed later the same day (below).
- **Requests handled in another assembly** (`[assembly: KeryxContracts]`, which turned KRX001 off in a contracts assembly that declared
  requests handled elsewhere, with a KRX001 code-fix action that added it; and `[assembly: ExternallyHandled(typeof(X))]`, which turned
  KRX030 off in a host for a request handled outside it, with KRX036 for a handled type or a non-request; open-questions OQ-10, OQ-54):
  the design until the owner's second rework on 2026-11-26, which put every request's handler in the request's own assembly (KRX001
  everywhere, KRX023). With that rule there is nothing for either attribute to turn off. Both designs are kept in
  [future-requirements.md](../future-requirements.md).
- **KRX030 skipping request types declared in a referenced host:** the interim rule until 2026-11-26 (open-questions OQ-16). A test host
  that referenced an app host got no error for an app request whose handler was internal, and the request failed at run time with
  `KeryxHandlerNotFoundException`. Replaced by reporting KRX030 for them like for any other reference.
- **Per-handler notification behaviors** (`[NotificationHandlerBehavior]`, before that
  `INotificationHandlerPipelineBehavior<TNotification>`), beside the per-publish kind (`[PublishBehavior]`, before that
  `INotificationPipelineBehavior<TNotification>`): the design until 2026-11-26, when the per-handler kind was removed and the per-publish
  kind was renamed `[NotificationBehavior]`. A per-handler behavior wrapped each handler call, was attached with `[Behavior]` on the handler
  method or per assembly in the handler's assembly, and was closed once per concrete type a polymorphic handler received (open-questions
  OQ-40). Removed because one notification kind keeps a behavior method's kind inferred purely from its signature (both kinds had the same
  `INext` method shape, open-questions OQ-53), publish-wide concerns run once per publish instead of once per handler, and a handler that
  needs a retry can do it itself.
- **Ignoring the host-only assembly attributes outside a host** (open-questions OQ-54): the interim rule until 2026-11-26. In a library
  or contracts assembly, `ExternallyHandled` (since removed), `ExcludeHandler` and `ExcludeBehavior` had no effect and nothing was
  reported. Replaced by warning KRX043.
- **Generated source written as text** (a line-by-line `CodeWriter` of interpolated strings): the emitters until the owner's second rework
  on 2026-11-26 (decision 5), replaced by syntax built with `SyntaxFactory` and normalized once, the pattern Dovetail's generators use
  (Incremental generation rules).
- **`if` chains for generic dispatch** (`if (requestType == typeof(X)) { return __Dispatch0(...); }` per type, and a `switch` statement on
  the index above the threshold): the generated shape until 2026-11-26. Decision 7 of the second rework replaces them with switch
  expressions with exact-type `when` guards and an index switch expression; because those lose the arms' tail calls, the generated
  code uses switch statements of the same sections until OQ-67 is decided (Generic entry points). The compiled dispatch is the same: a
  chain of method-table compares, or a jump table. Type patterns (`X x`) were rejected: they compile to `isinst`, which a derived type
  also matches.
- **Other link shapes** (owner decision 11 of the second rework, 2026-11-26): stage-generic links (one generic link type per position,
  closed over the next stage), a runtime-provided generic link in `Keryx`, and composed chain types (the whole pipeline as nested
  generic type arguments) all share `__Canon` code over reference types, so a behavior method the JIT does not inline pays a
  generic-dictionary lookup per call; a single index link (one struct holding the position, whose `InvokeAsync` switches on it) is
  recursive and is not inlined. One non-generic link struct per position, as before, written with a primary constructor and
  target-typed `new(pipeline, request)` (Generated pipeline shape). The earlier spelling, with explicit `_pipeline`/`_request` fields, a
  constructor and `new LinkK(...)`, compiled to the same machine code and was dropped as noise.
- **`[assembly: KeryxHost]`**, an assembly attribute that made the assembly a host: the design until the owner's second rework on
  2026-11-26, replaced by the `KeryxHost` MSBuild property (Opting in), the pattern Dovetail uses. The property needs no source edit in the
  common case (an executable or test project is a host by default), a project opts out without touching code, and the build decides
  before compilation, so the generators and analyzers read one analyzer config option instead of the compilation's attributes. Host
  diagnostics without a source location were reported on the attribute, where a per-file `.editorconfig` section applied to them; they
  are now reported without a location.

## Consequences

- Handler classes must be `partial` (and `public` outside a host), and users cannot write instance constructors on them.
- A request's handler lives in the request's own assembly (KRX023): requests cannot be shared through a contracts assembly and handled
  elsewhere, and a host cannot replace a referenced request handler with its own (open-questions OQ-59). Notifications can.
- Behaviors are `static` classes whose methods are called directly; they cannot be swapped through DI (exclude one and attach another).
- Notification behaviors wrap a whole publish; a notification handler has no behaviors of its own, so per-handler concerns such as a
  retry go in the handler.
- The `Keryx` package brings `Microsoft.Extensions.DependencyInjection.Abstractions` into every project that references it, except on
  `net11.0`, where the shared framework carries it (OQ-56).
- An executable or test project that references Keryx is a host unless it sets `<KeryxHost>false</KeryxHost>`, and a class library that
  should be one sets `<KeryxHost>true</KeryxHost>`. A host diagnostic without a source location takes only the project-wide severity.
- The mediator is scoped. Singletons need a scope to use it.
- Typed `Send` overloads are only reachable inside the host.
- Pipelines produce more generated code, and generic struct specialization adds code size.
- Most mistakes surface as compile errors rather than runtime failures.
- Keryx depends on a prerelease of `Specular.Scanning.Sources` (0.4.3-beta.2) and requires the .NET 10 SDK.
- Every project that references Keryx compiles with C# 14 or later (KRX022), on any supported target framework; the package defaults `LangVersion` to `latest`.
- The code fixes share an assembly with the generators, so a code-fix type reached from a generator or analyzer path breaks command-line
  builds; the rule in Package and assemblies, not a compiler check, prevents it.
- The generator is the bulk of the work: discovery, manifest reading, ordering and constraint evaluation need thorough tests.

## Answered open questions

The draft listed these as open. They are decided:

| Question                                                 | Answer                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A request in a contracts assembly triggers MED001 there. | A request's handler lives in the request's own assembly, so a request is never declared without its handler: KRX001 applies in every assembly, hosts included, and a handler elsewhere is KRX023 (2026-11-26; the earlier answer, `[assembly: KeryxContracts]`, is in future-requirements.md). Contracts assemblies declare notifications. The host reports KRX030 for a referenced request it cannot compose a handler for (error, severity configurable). |
| Where does caching sit relative to `Transaction`?        | `Phase.Caching = 450`: after `Default`, before `Transaction`, so a cache hit never opens a transaction.                                                                                                                                                                                                                                                                                                                                                     |
| Keyed services as handler parameters.                    | Supported in v1 through `[FromKeyedServices(key)]` with a constant key (KRX018).                                                                                                                                                                                                                                                                                                                                                                            |
| Requests with no response.                               | `IRequest : IRequest<Unit>` with `readonly struct Unit`; handlers may return `ValueTask`/`Task`/synchronous `void`; every send path, typed overloads included, returns `ValueTask<Unit>` (decided 2026-11-26).                                                                                                                                                                                                                                              |
| Notifications as a v2 feature.                           | In v1: `INotification`, `IPublisher`, sequential default with opt-in parallel, run all then `AggregateException` (even for one failure, and on cancellation), polymorphic handlers, `[HandlerOrder]`, notification behaviors that wrap each publish.                                                                                                                                                                                                        |

New questions that come up during implementation go to `docs/adr/open-questions.md`.
