# ADR 0001: Keryx, a source-generated mediator

Nov 25, 2026 · @David

## Status and decision

**Status:** Accepted, 2026-11-25; amended 2026-11-26 (verification fixes and the decisions recorded in open-questions.md). This replaces the draft "Source-generated mediator library" ADR (working title "Mediator"). Every
placeholder name is replaced with the Keryx name, and every open question in the draft is answered below.

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
- Behaviors can be applied per handler, per assembly or globally, can be limited by generic constraints, and run in a deterministic,
  declared order.
- Request/response, `IAsyncEnumerable` streaming and notifications (publish/subscribe) are all first class.
- Native AOT and trimming safe. No runtime reflection, including in DI registration.

**Non-goals for v1:** runtime-registered handlers, generic request types, behaviors with type-parameter shapes other than those listed
under Behaviors.

## Packaging and repository

### Package and assemblies

One NuGet package, **`Keryx`**, contains:

| Assembly                                                | TFM                              | Contents                                                                                                                                                                                                                                                                                              |
| ------------------------------------------------------- | -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Keryx.dll` (`lib/`)                                    | `netstandard2.0;net10.0;net11.0` | All public types: interfaces, attributes, `Unit`, `Phase`, exceptions, runtime helpers. `IsAotCompatible` on net10+.                                                                                                                                                                                  |
| `Keryx.Analyzers.dll` (`analyzers/dotnet/cs`)           | `netstandard2.0`                 | Incremental generators (`HandlerGenerator`, `HostGenerator`) and `DiagnosticAnalyzer`s. References `Microsoft.CodeAnalysis.CSharp` and `System.Text.Json` (shipped beside it); `Specular.Scanning.Sources` is compiled in as source (`PrivateAssets=all`). No reference to `Keryx.dll` or Workspaces. |
| `Keryx.Analyzers.CodeFixes.dll` (`analyzers/dotnet/cs`) | `netstandard2.0`                 | `CodeFixProvider`s. References `Microsoft.CodeAnalysis.CSharp.Workspaces`.                                                                                                                                                                                                                            |
| `System.Text.Json.dll` (`analyzers/dotnet/cs`)          | `netstandard2.0`                 | Needed at analyzer run time by `Specular.Scanning.Sources` and by the ScanCache JSON context.                                                                                                                                                                                                         |

- Code fixes live in their own assembly because an assembly that contains an analyzer or generator and references Workspaces triggers
  RS1038, and warnings are errors.
- The analyzer assemblies have **no assembly reference** to `Keryx.dll`. They identify Keryx types by metadata name through one internal
  `KnownTypeNames` constants class (for example `"Keryx.IRequest`1"`). A test compiles against the real `Keryx.dll`and checks that every
  entry resolves with`Compilation.GetTypeByMetadataName`.
- Every project that references the `Keryx` package runs the generator, including contracts projects. Contracts projects opt out of
  the "missing handler" rule with `[assembly: KeryxContracts]` (see Diagnostics).
- Analyzers do not flow through a `ProjectReference` (NuGet's default `PrivateAssets` includes `analyzers`). Every project that declares
  requests or handlers, and every host, must reference `Keryx` directly. The host warns (KRX033) about a reference that uses Keryx types
  but carries no manifest.
- The analyzer projects compile against `Microsoft.CodeAnalysis.CSharp` 5.0.0. Consumers need the .NET 10 SDK (Roslyn 5.0) or newer,
  whatever TFM they target. `Specular.Scanning.Sources` sets the same floor and requires C# 14 in the generator project.
- On `netstandard2.0`, `Keryx.dll` depends on `Microsoft.Bcl.AsyncInterfaces` and `System.Threading.Tasks.Extensions`.

### Which projects may target what

- Contracts (requests, stream requests, notifications) and handler libraries may target `netstandard2.0`. Their generated code (the
  handler partial and its attribute) uses only APIs available there.
- A host (`[assembly: KeryxHost]`) must target `net10.0` or newer. Its generated dispatch uses `Unsafe.As`, `FrozenDictionary` and, where
  it helps, `PoolingAsyncValueTaskMethodBuilder`. A host on an older TFM is error KRX031.
- The package's `build/Keryx.props` exposes `<CompilerVisibleProperty Include="RootNamespace;TargetFrameworkIdentifier;TargetFrameworkVersion" />`
  to the generators. KRX031 fires unless `TargetFrameworkIdentifier` is `.NETCoreApp` with version 10.0 or newer. When the properties
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
  `.config/hk.pkl`. Hand-written GitHub workflows.
- Folders: `src/`, `test/`, `samples/` (singular `test`), each with its own `Directory.Build.props`/`.targets`.
- Projects: `src/Keryx`, `src/Keryx.Analyzers`, `src/Keryx.Analyzers.CodeFixes`, `test/Keryx.Tests`, `test/Keryx.Benchmarks`,
  `samples/hosts/*`, `samples/libraries/*`.
- Tests: TUnit on Microsoft.Testing.Platform, `TUnit.Assertions.Should`, `Verify.TUnit` snapshots in `snapshots/`,
  `Rocket.Surgery.Extensions.Testing.SourceGenerators` for generator, analyzer and code-fix tests.
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
- It is not an interface implementation (implicit or explicit), not an override and not generic.
- Either it has a parameter whose type is a request or notification type (for notification handlers, also an interface or base type
  that derives from `INotification`, or `INotification` itself), or it is `static` on a type that directly nests a request type.
- No parameter type is a `ref struct` request or notification (KRX012).

Every other method is ignored, so `Handle` helpers, MediatR handlers kept during a migration and closed behaviors never produce
diagnostics.

### Handler rules

- Declared `private static` (KRX002, with a code fix).
- Return type:
    - request `IRequest<T>`: `ValueTask<T>`, `Task<T>` or synchronous `T`;
    - request `IRequest`: additionally `ValueTask`, `Task`, or synchronous `void` (`async void` is KRX002);
    - stream request `IStreamRequest<T>`: `IAsyncEnumerable<T>` (the method may be an `async` iterator);
    - notification: `ValueTask` or `Task`.
      A wrong return type is KRX002. A response type that differs only in nullability from the request's `TResponse` is warning KRX017.
- Exactly one request/notification parameter (KRX003). For requests it must be the concrete request type, not `IRequest<T>`, an abstract
  request or an interface. For notifications it may be the concrete type or a base type/interface deriving from `INotification`
  (polymorphic handler, see Notifications).
- At most one `CancellationToken`, in any position. `ref`, `in`, `out` and `params` parameters are errors (KRX013).
- Every other parameter is injected from dependency injection (see Injected parameters).
- An `async` iterator stream handler must put `[EnumeratorCancellation]` on its token. The compiler already enforces this (CS8425), and
  warnings are errors. Non-iterator stream handlers (returning an existing `IAsyncEnumerable`) must not use the attribute (CS8424).

### Handler classes

- Every type in the containing chain is `partial` (KRX004, with a code fix).
- In an assembly that is not a host, the handler class and every containing type are `public` (KRX004). A host may keep handlers
  `internal` for its own mediator; those are left out of its manifest.
- The handler class is a `class` or a `record class` without a primary constructor. Structs and record structs are errors (KRX004).
- The class must not declare an instance constructor (KRX007). Static constructors are allowed. A record primary constructor counts as
  an instance constructor.
- A handler class with injected parameters must derive from `object`, or from a base type with an accessible parameterless instance
  constructor, because the generated constructor calls `base()` (KRX007).
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

The host generates explicit factory registrations. There is no reflection-based activation.

| Parameter                            | Generated resolution                                                                                                                                                                                                                           |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| plain `T`                            | `sp.GetRequiredService<T>()`                                                                                                                                                                                                                   |
| `T` with a default value             | reference type: `sp.GetService<T>() ?? <default>`; value type (including `Nullable<T>`): `sp.GetService(typeof(T)) is T __v ? __v : <default>` (no reflection, no allocation). A nullable annotation alone does not make a parameter optional. |
| `[FromKeyedServices(key)] T`         | `sp.GetRequiredKeyedService<T>(key)`; the key must be a compile-time constant (KRX018). The parameterless (`InheritKey`) form is KRX018. With a default value, the same optional forms as above using `GetKeyedService`.                       |
| `IServiceProvider`                   | the scope's provider (counts as an injected parameter)                                                                                                                                                                                         |
| `IMediator`, `ISender`, `IPublisher` | the scoped mediator (no cycle: handlers are resolved lazily)                                                                                                                                                                                   |
| `CancellationToken`                  | the call's token, never injected                                                                                                                                                                                                               |

Keyed services require `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0 or newer in the project that uses them.

## Diagnostics and code fixes

`Keryx.Analyzers` contains two generators and a set of analyzers:

- **`HandlerGenerator`** runs in every project. It emits handler partials and the manifest. It **never** reports diagnostics; it silently
  skips malformed handlers.
- **`HostGenerator`** is active only with `[assembly: KeryxHost]`. It emits the mediator and `AddKeryx()`, and reports the host-only rules
  (KRX005 across assemblies, KRX008/KRX009 for pairs involving referenced behaviors, KRX030–KRX036). Its cached models carry an equatable
  `LocationInfo` (file path, `TextSpan`, `LinePositionSpan`), turned back into a `Location` with `Location.Create` when reporting. Host
  diagnostics are reported at the source location when the symbol is in source, otherwise on the `[assembly: KeryxHost]` attribute.
- **`DiagnosticAnalyzer`s** report every per-assembly rule.

KRX001, same-assembly KRX005 and in-source KRX008 need the whole compilation. Their descriptors carry
`WellKnownDiagnosticTags.CompilationEnd` and are reported from `RegisterCompilationStartAction`/`RegisterCompilationEndAction` (RS1037).
In the IDE they appear only with full-solution analysis or on build; the README says so, and code-fix tests run the analyzer with
compilation-end diagnostics enabled.

Every ID has a help link. IDs are grouped: KRX001–KRX029 per-assembly rules (KRX005, KRX008 and KRX009 are also checked by the host across
assemblies), KRX030–KRX039 host-only rules, KRX040–KRX049 attribute placement rules.

| ID     | Severity             | Reported by                         | Condition                                                                                                                                                                                                                                                                                                                           |
| ------ | -------------------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| KRX001 | Error                | analyzer                            | A request or stream request type has no handler in its assembly. Not reported in assemblies marked `[assembly: KeryxContracts]`, and not reported in a host (`[assembly: KeryxHost]`), where KRX030 covers the host's own request types and every referenced one.                                                                   |
| KRX002 | Error                | analyzer                            | Handler candidate is not `private static`, or has a return type not allowed for its request kind.                                                                                                                                                                                                                                   |
| KRX003 | Error                | analyzer                            | Handler candidate has no request/notification parameter, more than one, or a request parameter typed as `IRequest<T>`, an abstract request or an interface.                                                                                                                                                                         |
| KRX004 | Error                | analyzer                            | Handler class or a containing type is not `partial`, is not `public` outside a host, or is a struct / record struct.                                                                                                                                                                                                                |
| KRX005 | Error                | analyzer, host                      | Two handlers exist for the same request type (same assembly: analyzer; across assemblies: host, naming both handlers and assemblies).                                                                                                                                                                                               |
| KRX006 | Error                | analyzer                            | A class directly contains more than one handler method.                                                                                                                                                                                                                                                                             |
| KRX007 | Error                | analyzer                            | Handler class declares an instance constructor or a record primary constructor, or has injected parameters and a base type without an accessible parameterless instance constructor.                                                                                                                                                |
| KRX008 | Error                | host (analyzer for in-source pairs) | Behavior ordering has a cycle, or `RunsAfter`/`RunsBefore` contradicts phase or order. Checked once per behavior pair.                                                                                                                                                                                                              |
| KRX009 | Error                | analyzer, host                      | A behavior attached by attribute to a method or notification type does not fit its target: wrong behavior kind, or constraints not satisfied.                                                                                                                                                                                       |
| KRX010 | —                    | —                                   | Retired (was "request is not sealed"; dispatch is by exact type, so sealing does not matter).                                                                                                                                                                                                                                       |
| KRX011 | Error                | analyzer                            | A type implements more than one closing of `IRequest<>`/`IStreamRequest<>`, both kinds, or a request interface and `INotification`.                                                                                                                                                                                                 |
| KRX012 | Error                | analyzer                            | A request, stream request or notification type has type parameters, is nested in a generic type, or is a `ref struct`; or a handler class has type parameters.                                                                                                                                                                      |
| KRX013 | Error                | analyzer                            | Handler parameter is `ref`, `in`, `out` or `params`, or there is more than one `CancellationToken`.                                                                                                                                                                                                                                 |
| KRX014 | Error                | analyzer                            | `static` handler class has injected parameters.                                                                                                                                                                                                                                                                                     |
| KRX015 | Error                | analyzer                            | Handler class declares a member named `InvokeAsync` or a `__keryx_` member.                                                                                                                                                                                                                                                         |
| KRX016 | Error                | analyzer                            | Outside a host, a request, response (including generic arguments), notification or behavior type that a pipeline needs is not effectively public.                                                                                                                                                                                   |
| KRX017 | Warning              | analyzer                            | Handler response type differs from the request's `TResponse` only in nullability.                                                                                                                                                                                                                                                   |
| KRX018 | Error                | analyzer                            | `[FromKeyedServices]` key is not a compile-time constant, or uses the parameterless form.                                                                                                                                                                                                                                           |
| KRX019 | Error                | analyzer                            | Behavior class has an unsupported shape (see Behavior shapes), implements more than one Keryx behavior interface closing, is `abstract`, or implements a behavior interface method with a `virtual`, `abstract` or `override` method.                                                                                               |
| KRX020 | Error                | analyzer                            | Behavior class does not have exactly one public constructor.                                                                                                                                                                                                                                                                        |
| KRX021 | Info                 | analyzer                            | Struct request is boxed when sent through `ISender`; only the host's typed overload avoids it.                                                                                                                                                                                                                                      |
| KRX030 | Error (configurable) | host                                | A request or stream request type has no handler anywhere in the composed set. Covers the host's own request types (from its syntax pipeline) and request types in referenced assemblies (from the scan), except types declared in an assembly that is itself a host (see Host control). Severity can be changed in `.editorconfig`. |
| KRX031 | Error                | host                                | A `[KeryxHost]` project targets a framework older than `net10.0`.                                                                                                                                                                                                                                                                   |
| KRX032 | Error                | host                                | A referenced assembly's manifest has a newer `FormatVersion` than this Keryx understands. The message says to upgrade Keryx in this project.                                                                                                                                                                                        |
| KRX033 | Warning              | host                                | A referenced assembly references Keryx but has no `[assembly: KeryxManifest]` (the generator did not run there).                                                                                                                                                                                                                    |
| KRX034 | Error                | host                                | A type named in a manifest cannot be resolved or is not accessible from the host (missing reference). The message names the assembly to reference.                                                                                                                                                                                  |
| KRX035 | Info                 | host                                | Lists every global behavior applied in this host and the assembly it came from.                                                                                                                                                                                                                                                     |
| KRX036 | Warning              | host                                | `ExcludeHandler`/`ExcludeBehavior` names a type that is not part of any pipeline.                                                                                                                                                                                                                                                   |
| KRX037 | Info                 | host                                | A struct notification is delivered to a handler typed as a base interface or `INotification`, so it is boxed once per `Publish` (see Notifications).                                                                                                                                                                                |
| KRX040 | Warning              | analyzer                            | `[HandlerOrder]` on a method that is not a notification handler.                                                                                                                                                                                                                                                                    |
| KRX041 | Warning              | analyzer                            | `[PublishStrategy]` on a type that is not a notification.                                                                                                                                                                                                                                                                           |
| KRX042 | Warning              | analyzer                            | `[Behavior]` on a class or struct that is not a notification type, or on a method that is not a handler candidate; or `[GlobalBehavior]` on a type that does not implement exactly one Keryx behavior interface closing.                                                                                                            |

**Code fixes**

- **KRX001**, two actions:
    - _Add handler stub._ Nested request: the stub goes into the containing class, next to the request. Top-level request: a new
      `public partial class {RequestName}Handler` in a new file `{RequestName}Handler.cs` beside the request, in the request's namespace,
      with a numeric suffix if the name is taken. The stub is
      `private static ValueTask<TResponse> Handle(Request request, CancellationToken ct) => throw new NotImplementedException();`.
      `IRequest` gets a `ValueTask` stub. Stream requests get an `IAsyncEnumerable<TItem>` stub.
    - _Mark this assembly as a contracts assembly_ (adds `[assembly: KeryxContracts]`).
- **KRX002**: make the method `private static`.
- **KRX004**: add the missing `partial` and, outside a host, `public` modifiers.
- **KRX014**: remove `static` from the handler class.

## Behaviors

### Behavior kinds

```csharp
public interface INext<TResponse>
{
    ValueTask<TResponse> InvokeAsync(CancellationToken cancellationToken);
}

public interface INext
{
    ValueTask InvokeAsync(CancellationToken cancellationToken);
}

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>;
}

public interface IStreamBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    ValueTask OnStartAsync(TRequest request, CancellationToken cancellationToken);
    bool OnItem(TRequest request, ref TItem item);            // false drops the item
    ValueTask OnCompleteAsync(TRequest request);
    ValueTask OnErrorAsync(TRequest request, Exception exception); // observe only; the exception is rethrown
}

public interface IStreamWrapBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    IAsyncEnumerable<TItem> Handle(TRequest request, IAsyncEnumerable<TItem> next, CancellationToken cancellationToken);
}

// Wraps one whole Publish call.
public interface INotificationBehavior<TNotification>
    where TNotification : INotification
{
    ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext;
}

// Wraps each notification handler call separately.
public interface INotificationHandlerBehavior<TNotification>
    where TNotification : INotification
{
    ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext;
}
```

Behaviors implement `Handle` (and the stream hooks) implicitly as public, non-virtual methods; the generated code calls them on the
concrete type. An implicit interface implementation compiles to a sealed virtual slot, so the call is direct. A behavior class that is
`abstract`, or whose implementing method is `virtual`, `abstract` or `override`, would turn the call back into a generic virtual dispatch,
and is KRX019.

### Behavior shapes (v1)

A behavior class implements exactly one Keryx behavior interface closing, and is either:

- **open** with the same arity as the interface, its type parameters mapped positionally to the interface's
  (`AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>`,
  `LogEvents<TNotification> : INotificationBehavior<TNotification>`); or
- **closed and non-generic** (`AuditCreate : IPipelineBehavior<CreateOrder.Command, Guid>`).

Anything else (swapped parameters, partially open shapes such as `X<T> : IPipelineBehavior<TReq, Result<T>>`, several closings) is
KRX019. Full type-parameter unification is future work.

Each behavior has exactly one public constructor (KRX020). Its parameters are resolved with the same rules as handler parameters.

### Three ways to attach a behavior

- **Per target:**
    - request and stream behaviors: `[Behavior(typeof(AuditBehavior<,>))]` on the handler method;
    - per-handler notification behaviors: `[Behavior(...)]` on the notification handler method;
    - per-publish notification behaviors: `[Behavior(...)]` on the notification type.
- **Per assembly:** `[assembly: Behavior(typeof(TransactionBehavior<,>))]` applies to every matching target in that assembly.
- **Global:** `[GlobalBehavior]` on the behavior class applies to every matching target in the host's composed set, from any referenced
  assembly.

The applied set for a target is the union of the three scopes, de-duplicated by closed behavior type, minus the host's
`[assembly: ExcludeBehavior(typeof(X<,>))]` entries. Scope decides _whether_ a behavior applies, never _where_ it sits; phases decide
that.

"Matching" means:

- The behavior's kind fits the target. A request behavior never applies to a stream or notification.
- **Open behaviors:** every type parameter is substituted with the target's types (request: `TRequest`, `TResponse`; stream: `TRequest`,
  `TItem`; notification: the concrete published type), and then every constraint must hold. Constraint types that mention other type
  parameters (`where TRequest : IRequest<TResponse>`) are checked after substitution.
- **Closed behaviors:** the interface's type arguments must be identical to the target's (request: `TRequest` and `TResponse`;
  notification: the concrete published type). Assignable is not enough.

Keryx checks constraints itself, because `INamedTypeSymbol.Construct` does not validate them:

- `class`, `struct`, `unmanaged`, `new()` and `notnull` as the compiler defines them. Nullable annotations count for `notnull` and `class`
  (not `class?`), because CS8714/CS8634 are errors when warnings are errors.
- A type constraint holds only when the conversion from the type argument to the constraint type is an identity conversion, an implicit
  reference conversion (`IsImplicit && IsReference`) or a boxing conversion. User-defined, numeric and nullable conversions do not count,
  even though `Compilation.ClassifyConversion` reports them as implicit.

Assembly and global behaviors that do not match are skipped silently. A behavior attached explicitly to a method or notification type
that does not match is KRX009.

### Registration and invocation

- The host registers each **closed** behavior type it uses, as itself, with a generated factory:
  `services.TryAddScoped(static sp => new AuditBehavior<CreateOrder.Command, Guid>(sp.GetRequiredService<IAuditLog>()))`.
  There is no open-generic registration, so value-type responses work under Native AOT.
- Generated code calls `Handle<TNext>` on the concrete behavior type, never through the interface, so there is no generic virtual method
  dispatch. Consequence: a behavior cannot be replaced in DI by registering another implementation of the interface.

### Generated pipeline shape

For each pipeline the host emits:

- a sealed **pipeline context** class, created lazily once per scope, that holds the handler instance and that pipeline's behavior
  instances, resolved in its constructor;
- one small **link** struct per position: `readonly struct Link_k : INext<TResponse> { Context ctx; TRequest request; }`. Its
  `InvokeAsync(ct)` calls `ctx.Behavior_{k+1}.Handle(request, new Link_{k+1}(ctx, request), ct)`; the last link calls the handler's
  `InvokeAsync`.

Links are **flat**: each one holds two fields regardless of pipeline depth, so struct copying does not grow with depth. A behavior may
call `next` more than once (retry). The `CancellationToken` is passed as the `InvokeAsync` argument, never stored in a link.

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

[AttributeUsage(AttributeTargets.Class)]
public sealed class BehaviorOrderAttribute(Phase phase) : Attribute
{
    public Phase Phase { get; } = phase;
    public int Order { get; init; }   // default 0
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsAfterAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunsBeforeAttribute(Type behaviorType) : Attribute { public Type BehaviorType { get; } = behaviorType; }
```

Position belongs to the behavior class. Attachment sites cannot override phase or order in v1. Users can pick values between phases,
such as `(Phase)250`.

**Sort rules**, per pipeline, at compile time:

1. Phase ascending.
2. Within a phase, `Order` ascending.
3. Within equal (phase, order), `RunsAfter`/`RunsBefore` edges are sorted topologically.
4. Any remaining tie is broken by ordinal fully qualified type name.

`RunsAfter`/`RunsBefore` are also checked against every (phase, order) bucket: an edge that agrees with phase and order is a no-op; an
edge that contradicts them is KRX008; a cycle is KRX008. An open `typeof(X<,>)` matches every closing of `X`. An edge to a behavior that is
absent from a given pipeline is ignored. Contradictions are checked once per behavior pair, not once per pipeline.

## Streaming

A stream request implements `IStreamRequest<TItem>`. Its handler follows the normal handler rules and returns `IAsyncEnumerable<TItem>`.

**No stream behaviors:** `Stream` returns the handler's enumerable as is. No extra allocation.

**Fused stream behaviors.** All `IStreamBehavior` hooks of a pipeline are fused into one generated async iterator:

- `OnStartAsync` runs outer → inner, lazily on the first `MoveNextAsync`, not when `Stream()` is called.
- Items travel outward from the handler: `OnItem` runs inner → outer. Returning `false` drops the item, and outer hooks do not see it.
- `OnCompleteAsync` runs inner → outer on normal completion **and** on early dispose (the consumer breaks out).
- `OnErrorAsync` runs inner → outer for any exception from the handler or from a hook, including `OperationCanceledException`. The
  exception is then rethrown.
- `OnCompleteAsync` and `OnErrorAsync` run only for behaviors whose `OnStartAsync` completed.
- Because C# forbids `yield return` inside a `try` with a `catch` (CS1626), the iterator wraps only the `MoveNextAsync`/`Current` step in
  `try/catch` and yields outside it.
- The fused iterator puts `[EnumeratorCancellation]` on its own token, and passes that same token to the handler and to
  `GetAsyncEnumerator`, so the handler never links tokens a second time. It links the `Stream(request, ct)` token and the consumer's
  `WithCancellation` token only when both can be cancelled and differ; that link allocates one `CancellationTokenSource` (a documented
  exception in the Performance contract).

**`IStreamWrapBehavior`** receives and returns the whole `IAsyncEnumerable<TItem>`. It splits fusion at its position: hooks inside it are
fused into one segment, hooks outside it into another. It costs whatever its own enumerable allocates plus, when hooks sit on both sides
of it, one more fused segment iterator.

Null and unknown stream requests are validated synchronously in a non-iterator wrapper before the iterator is returned.

Phases, scopes, constraints and diagnostics work exactly as for request/response behaviors.

## Notifications

A notification implements `INotification`. `IPublisher.Publish` dispatches on the notification's runtime type (see Generic entry points
for how this avoids boxing struct notifications).

**Handlers.** Same rules as request handlers, returning `ValueTask` or `Task`. Any number of handlers per notification, across
assemblies. Zero handlers is valid.

**Zero handlers.** For a notification type the host knows, the per-publish pipeline (`INotificationBehavior`s) still runs when there are
no handlers, so logging and outbox behaviors see every publish. `Publish` completes immediately only when the type has neither handlers
nor per-publish behaviors, or when the runtime type is unknown to the host.

**Polymorphic handlers.** A handler whose parameter is a base class or interface (`Handle(IDomainEvent e)`), or `INotification` itself
(a catch-all), receives every concrete notification that the host knows and that converts to that type. The host expands each concrete
notification's handler list at compile time. A notification type the host does not know at compile time runs no handlers. A struct
notification delivered to a handler typed as an interface or `INotification` is boxed **once per `Publish`**, and that one box is reused
for every such handler (Info KRX037; see open-questions OQ-17).

**Handler order.** `[HandlerOrder(int)]` on the handler method (default 0), ascending; ties by ordinal fully qualified handler class name.
Exact-type and polymorphic handlers share one ordering.

**Publish strategy.** Sequential by default: handlers are awaited one after another in order. A notification opts into concurrent
execution with `[PublishStrategy(PublishStrategy.Parallel)]`:

- Each handler pipeline is started in handler order on the calling thread without awaiting it (no `Task.Run`). A synchronous throw while
  starting a pipeline is caught and recorded, and the remaining pipelines still start.
- Only the `ValueTask`s that have not completed are collected. If none are pending, parallel publish allocates nothing.
- Otherwise Keryx allocates one array plus one `AsTask()` per pending `ValueTask`, and awaits `Task.WhenAll`.
- Failures, synchronous and asynchronous, are combined into the `AggregateException` in handler order.

**Failures.** Every handler runs, whatever the others do. If any handler fails, `Publish` throws one `AggregateException` containing every
failure, in handler order. This holds even when exactly one handler fails: callers always see the same exception shape. The exception
list is allocated only when a failure happens.

**Cancellation.** Cancelling the publish token does not skip handlers. Every handler is still invoked, with the (possibly cancelled)
token, so each can observe it. An `OperationCanceledException` thrown by a handler is collected like any other failure into the
`AggregateException`.

**Behaviors.**

- `INotificationHandlerBehavior<T>` wraps each handler call. Attached per handler method, per assembly (the handler's assembly) or
  globally.
- `INotificationBehavior<T>` wraps the whole `Publish` call, including all handlers and the strategy. Attached on the notification type,
  per assembly (the notification type's assembly) or globally.
- Both use phases, order, constraints and exclusion exactly like request behaviors.
- `TNotification` is always the **concrete published type**, also for a polymorphic handler. Constraints are checked against it.
- Under the parallel strategy each handler's per-handler pipeline runs concurrently; failures go into the `AggregateException`.

```csharp
public enum PublishStrategy { Sequential, Parallel }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class PublishStrategyAttribute(PublishStrategy strategy) : Attribute { public PublishStrategy Strategy { get; } = strategy; }

[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlerOrderAttribute(int order) : Attribute { public int Order { get; } = order; }
```

## Generated host

### Opting in

```csharp
[assembly: KeryxHost]
```

A host must target `net10.0` or newer (KRX031). Generated host types are `internal`, in the host's root namespace (read from the
`RootNamespace` MSBuild property through `CompilerVisibleProperty`, falling back to the assembly name). The fallback is sanitised into a
valid namespace: characters that are not valid in an identifier become `_`, and a segment that starts with a digit gets a `_` prefix.
Several hosts in one solution, including a test project that references the app host, do not collide.

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
  handler's transient dependencies behave as scoped, because the handler is cached per scope.
- **Concurrent first use.** One scope can use the mediator concurrently (parallel publish, handlers that send in parallel, user
  `Task.WhenAll` over `Send`). Contexts are created with
  `Volatile.Read(ref __p17) ?? Interlocked.CompareExchange(ref __p17, new P17(_sp), null) ?? __p17`, so every caller in the scope uses the
  same context; this is allocation-free after warm-up. A losing race may construct, and discard, one extra context, so transient
  dependencies can be created more than once under concurrent first use. A concurrency test covers this.
- **Typed overloads** skip type dispatch. They exist only on the internal host mediator, so they are a host-only optimization; libraries
  call through `IMediator`/`ISender`/`IPublisher`.
- A typed overload for an `IRequest` returns `ValueTask<Unit>`, exactly as the pipeline produces it. There is no second adapter.

### Generic entry points

- `Send<TResponse>(IRequest<TResponse>)` dispatches on `request.GetType() == typeof(X)` (a method-table pointer compare). With more than
  16 request types (threshold to be tuned by benchmark), it looks the type up in a `FrozenDictionary<Type, int>` and then uses a
  `switch` on the index.
- The arm result `ValueTask<X>` is reinterpreted as `ValueTask<TResponse>` with
  `Unsafe.As<ValueTask<X>, ValueTask<TResponse>>(ref vt)`. This is safe because `IRequest<>` is invariant and a request implements
  exactly one closing (KRX011). Never `(ValueTask<TResponse>)(object)vt`, which boxes.
- Class requests are cast with `Unsafe.As<X>(request)`; struct requests are unboxed.
- `Stream<TItem>` uses the same dispatch; the result is a reference type and needs only a reference cast.
- `Publish<TNotification>`: when `typeof(TNotification).IsValueType`, generated arms compare `typeof(TNotification) == typeof(X)` (folded
  by the JIT, because value-type instantiations are not shared) and convert with `Unsafe.As<TNotification, X>(ref notification)`, so a
  struct notification is never boxed by dispatch. Only for a reference-type `TNotification` does dispatch call `notification.GetType()`.
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
- every handler class that has injected parameters;
- every closed behavior type used by any pipeline.

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
    public Type[] Behaviors { get; init; } = [];        // method-attached behaviors, in declared order
    public int Order { get; init; }                    // [HandlerOrder] for notification handlers
    public bool StaticInvoker { get; init; }
}

[AttributeUsage(AttributeTargets.Assembly), EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KeryxManifestAttribute : Attribute
{
    public int FormatVersion { get; init; }
}
```

- Global behaviors need no extra attribute: `[GlobalBehavior]`, `[BehaviorOrder]`, `[RunsAfter]`/`[RunsBefore]` are already on the public
  behavior class. Per-publish behaviors attached to a notification type are on the public notification type. Assembly-level `[Behavior]`
  attributes are read from the assembly.
- Response type, constructor parameters and keyed-service keys are derived from the public request, handler and behavior types; they are
  not duplicated in the attribute.
- Host-internal handlers are not described by a manifest.

Private `Handle` methods and their attributes are not visible across assemblies (Roslyn imports public metadata only, and reference
assemblies strip private members). That is why the generator copies everything the host needs onto the public partial.

### How the host reads it

- The host's **own** handlers come from its syntax pipeline (`ForAttributeWithMetadataName` and syntax providers). A generator never sees
  its own output, so the host does not read its own manifest.
- The host's own request and notification types also come from the syntax pipeline (for KRX030 and polymorphic expansion), not from the
  scan.
- **Referenced** assemblies are read with `Specular.Scanning.Sources`, through **one**
  `context.Scan<KeryxReferenceModel>("Keryx", requests, transform)` call. `requests` is an `IncrementalValuesProvider<ScanRequest>` holding
  three requests in a fixed order (built from one `CompilationProvider.Select` followed by `SelectMany`). Each request is pruned to the
  assemblies that reference Keryx (`DependenciesFromAssemblyOf(<Keryx marker type>)`) and excludes the host itself
  (`NotFromAssembly(compilation.Assembly)`); `Keryx.dll` declares no handlers or requests, so it is not scanned:
    1. types `WithAttribute(KeryxHandlerAttribute)`: handler classes;
    2. types `WithAttribute(GlobalBehaviorAttribute)`: global behaviors;
    3. non-abstract types `AssignableTo` `IRequest<>`, `IStreamRequest<>` or `INotification`: every referenced request and notification
       type, used for KRX030 and for polymorphic notification expansion.
- Inside the transform, results are read by position (`scan.Results[0..2]`). The transform reads attribute arguments and each referenced
  assembly's attributes (`KeryxManifest`, `KeryxHost`, assembly-level `Behavior`), projects them into equatable records, and stores the
  projection per assembly in Specular's **ScanCache** (keyed by MVID) using a source-generated `JsonSerializerContext`. The cache's data
  version is a generator constant, `ProjectionDataVersion`, bumped whenever the projected record, its derivation or its JSON contract
  changes. The referenced assembly's `FormatVersion` is stored as a field inside the projection, so KRX032 works on cache hits. Later
  builds and IDE edits reuse the projection while the reference is unchanged.
- Two `Scan` calls must never share the consumer id `"Keryx"`: each writes the whole cache file.
- Only compile references count (`compilation.References`). A reference hidden from the host (`PrivateAssets`, disabled transitive
  references) hides its handlers too.
- The host merges its own handlers with the referenced manifests, applies `[assembly: ExcludeHandler(typeof(X))]` and
  `[assembly: ExcludeBehavior(typeof(X<,>))]`, composes every pipeline from the three behavior scopes, orders it and emits dispatch.

### Host control

- Global behaviors from every referenced assembly apply automatically. The host removes any of them with `ExcludeBehavior`, and Info
  KRX035 lists every applied global and its source assembly.
- A request handled in two assemblies is KRX005 in the host. To replace a referenced handler (for example in a test host), exclude it with
  `ExcludeHandler` and provide your own.
- A request or stream request type visible to the host with no handler is KRX030, an error whose severity can be lowered in
  `.editorconfig` (for example for a client app that references contracts handled out of process).
- **A host referenced by another host** (typically a test project that references the app host): KRX030 skips request types declared in
  an assembly that carries `[assembly: KeryxHost]`, because that host's own mediator handles them and its internal handlers carry no
  manifest. The referencing host can dispatch such a request only if the referenced host's handler is public (and so has a manifest);
  otherwise the request reaches `KeryxHandlerNotFoundException` at run time. See open-questions OQ-16.

### Versioning

- `KeryxManifest.FormatVersion` starts at 1. A host reads every format version up to its own and reports KRX032 for a newer one.
- Within a major version, manifest attribute shapes and the generated `InvokeAsync`/constructor signatures change only additively.
- The analyzers and `Keryx.dll` ship in one package, so they are always in lockstep within one project.
- `EnablePackageValidation` with a baseline once a stable version exists.

## Attribute reference

| Attribute                                          | Targets                                                           | Purpose                                                                                                         |
| -------------------------------------------------- | ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `[assembly: KeryxHost]`                            | assembly                                                          | This assembly gets a generated mediator and `AddKeryx()`.                                                       |
| `[assembly: KeryxContracts]`                       | assembly                                                          | Turns off KRX001 in this assembly.                                                                              |
| `[Behavior(Type)]`                                 | method (handler), class/struct (notification), assembly; multiple | Attaches a behavior. Elsewhere it is KRX042.                                                                    |
| `[GlobalBehavior]`                                 | class (behavior)                                                  | Applies the behavior to every matching target in every host that references it. On a non-behavior it is KRX042. |
| `[BehaviorOrder(Phase) { Order }]`                 | class                                                             | Behavior position.                                                                                              |
| `[RunsAfter(Type)]`, `[RunsBefore(Type)]`          | class; multiple                                                   | Relative order inside a (phase, order) bucket.                                                                  |
| `[assembly: ExcludeBehavior(Type)]`                | assembly; multiple                                                | Host removes a behavior from all pipelines.                                                                     |
| `[assembly: ExcludeHandler(Type)]`                 | assembly; multiple                                                | Host ignores a referenced handler class.                                                                        |
| `[PublishStrategy(PublishStrategy)]`               | class/struct (notification)                                       | Sequential (default) or parallel publish.                                                                       |
| `[HandlerOrder(int)]`                              | method                                                            | Notification handler order.                                                                                     |
| `[KeryxHandler(...)]`, `[assembly: KeryxManifest]` | generated only                                                    | Manifest, in `Keryx.Internal`.                                                                                  |

## Incremental generation rules

- `IIncrementalGenerator` only. Pipeline models are value-equatable records with equatable arrays. No `Compilation`, `ISymbol`,
  `SyntaxNode` or `Location` in cached models.
- Every pipeline step has a tracking name. The incremental test runs the driver with `trackIncrementalGeneratorSteps: true`, makes an
  unrelated edit (a new file with no Keryx types), and asserts that every named step after the scan step, and every source output, reports
  `Cached` or `Unchanged`. It also asserts the reverse: swapping in a reference with a changed manifest produces `Modified`. Steps fed
  directly by `CompilationProvider` always run and are not asserted.
- Generated code is deterministic (ordinal sorting everywhere, no timestamps or GUIDs), starts with `// <auto-generated/>`, enables
  nullable and uses `global::`-qualified names. `[GeneratedCode("Keryx", version)]` goes on each generated member of a user's partial type,
  and at type level only on types Keryx owns outright (see Handlers, Example). A snapshot test asserts that a user's partial declaration
  carries no `[GeneratedCode]`.
- Hint names are the fully qualified metadata name of the handler class, including containing types, sanitised: drop `global::`, replace
  `+`, `` ` `` and any character outside `[A-Za-z0-9_.]` with `_`, then append `.Keryx.g.cs` (for example `MyApp.Orders.Create.Keryx.g.cs`).
  This keeps `Orders.Create` and `Customers.Create` apart. Host files use fixed names (`KeryxMediator.Keryx.g.cs`,
  `KeryxServiceCollectionExtensions.Keryx.g.cs`).
- No runtime reflection anywhere: no `MakeGenericType`, no `Activator`, no reflection-based DI activation.

## Performance contract

These replace the draft's "minimal allocations" and the handoff's "must not allocate beyond the handler":

- **Send, synchronous handler:** after one warm-up call in the same scope, Keryx allocates 0 bytes per call, at any behavior depth,
  provided the behaviors' own work completes synchronously. Holds for the typed overloads and, for class requests, the generic entry point,
  for value-type and reference-type responses. A struct request sent through `ISender` is boxed at the call site (KRX021); that box is not
  counted against Keryx.
- **Send, yielding handler:** Keryx-generated code allocates nothing beyond the state-machine boxes of async methods the user wrote
  (handler and behaviors). The only generated async path is `UnitAdapter.ToUnit` (non-generic `ValueTask`/`Task` → `ValueTask<Unit>`). It
  uses `PoolingAsyncValueTaskMethodBuilder` on net10+ and must add 0 bytes in steady state; a benchmark checks it. Typed `IRequest`
  overloads return `ValueTask<Unit>`, so they add no second adapter.
- **Stream:** 0 extra allocations with no stream behaviors. With stream behaviors, per `Stream` call: one iterator object per fused hook
  segment (one segment when there is no `IStreamWrapBehavior`), plus whatever each `IStreamWrapBehavior`'s own enumerable allocates.
  Documented exception: one linked `CancellationTokenSource` when the `Stream()` token and the `WithCancellation` token are both
  cancellable and differ. 0 per item.
- **Publish:** 0 extra allocations for a sequential publish with synchronously completing handlers. Parallel: 0 B when every handler
  pipeline completes synchronously; otherwise one array plus one `AsTask()` per pending `ValueTask`, plus what `Task.WhenAll` allocates.
  The exception list only on failure. Documented exception: a struct notification delivered to interface-typed handlers is boxed once per
  `Publish` (KRX037).
- Allocation tests warm up first, measure only a region that completes synchronously (`IsCompletedSuccessfully`, then `.Result`), and
  use `GC.GetAllocatedBytesForCurrentThread`. They cover value-type and reference-type responses through both entry points, class and
  struct requests (asserting the split above), and streams both with a default token on one side and with two distinct cancellable tokens
  (asserting exactly the one linked `CancellationTokenSource`). BenchmarkDotNet `[MemoryDiagnoser]` results are the authoritative
  evidence; the thread-counter tests are regression guards.

### Phase 4 gate

Before behaviors are generated, a hand-written prototype of the struct-based pipeline is benchmarked. It passes when all of these hold:

1. 0 B allocated for a synchronous pipeline at depths 1, 3 and 8;
2. `DisassemblyDiagnoser` output shows the behavior `Handle` called directly on the concrete type (no generic-virtual-method lookup) and
   `next.InvokeAsync` called directly;
3. mean time at depth 3 within 1.25× of a hand-written nested-call baseline.

If it fails, implementation stops: the prototype and its numbers are committed and reported, and the ADR's fallback (generator-inlined
before/after hooks for simple behaviors) is **not** adopted without a decision.

## Benchmarks

`test/Keryx.Benchmarks` (BenchmarkDotNet, net10.0, `[MemoryDiagnoser]`) compares, each library in its own small class library:

- a hand-written direct-call baseline;
- Keryx;
- Mediator (martinothamar) 3.0.2, scoped and singleton lifetimes;
- MediatR 15.x, pinned exactly, run without a license key (allowed for benchmarking), with the `LuckyPennySoftware.MediatR.License` log
  category filtered; no key is ever committed;
- Immediate.Handlers 4.4.0;
- ZeroAlloc.Mediator 7.0.1.

Scenarios: `Send` through the typed overload and the generic entry point; 0, 1, 3 and 8 behaviors; synchronous and `Task.Yield` handlers;
`Stream` of 100 items with 0 and 3 stream behaviors; `Publish` to 1 and 5 handlers. CI builds the benchmarks and runs them with
`--job Dry` only; numbers are never published from shared runners.

## AOT verification

CI publishes the sample host with `PublishAot=true`, treats every IL2xxx/IL3xxx warning as an error, and runs the published binary.

## Alternatives considered

- **Handler interface (`IRequestHandler<,>`):** rejected. It forces a public instance method and hides which methods are handlers from the
  analyzer.
- **`[UnsafeAccessor]` for private handlers:** rejected in favor of generated public partials, which work everywhere including AOT and
  across assemblies.
- **Delegate-chain pipeline:** rejected because it allocates per call.
- **Nested link structs** (each link embeds the next): rejected; struct copying grows with depth. Flat links instead.
- **Behaviors resolved through `IPipelineBehavior<,>` with open-generic DI registration:** rejected; generic virtual dispatch per link and
  not AOT-safe for value-type responses.
- **Polymorphic request dispatch:** rejected; exact-type dispatch is simpler and as fast whether or not a request is sealed.
- **Integer `Order` only:** rejected because numbers collide across method, assembly and global scopes.
- **Open set of user-defined phases:** rejected for v1. A fixed enum with gaps keeps ordering globally consistent.
- **A manifest class with static data, or a JSON/string payload:** rejected. Method bodies and initializers are unreadable from metadata,
  and type names round-trip poorly. Type-level attributes bind `typeof` arguments directly.
- **Specular ScanCache as the manifest itself:** rejected as the _source_. The cache lives in the host's `obj/` and cannot be written by a
  library about itself. It is used as the host-side cache of the attribute-derived manifest.
- **One analyzer + generator + code-fix assembly:** rejected (RS1038).
- **Separate `Keryx.Abstractions` package:** rejected in favor of one `Keryx` package; contracts projects use `[assembly: KeryxContracts]`.

## Consequences

- Handler classes must be `partial` (and `public` outside a host), and users cannot write instance constructors on them.
- Behaviors cannot be swapped through DI; they are resolved by concrete closed type.
- The mediator is scoped. Singletons need a scope to use it.
- Typed `Send` overloads are only reachable inside the host.
- Pipelines produce more generated code, and generic struct specialization adds code size.
- Most mistakes surface as compile errors rather than runtime failures.
- Keryx depends on a prerelease of `Specular.Scanning.Sources` (0.4.3-beta.2) and requires the .NET 10 SDK.
- The generator is the bulk of the work: discovery, manifest reading, ordering and constraint evaluation need thorough tests.

## Answered open questions

The draft listed these as open. They are decided:

| Question                                                 | Answer                                                                                                                                                                                                                                       |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A request in a contracts assembly triggers MED001 there. | `[assembly: KeryxContracts]` turns off KRX001 in that assembly. The host is authoritative: KRX030 reports any request visible to it with no handler (error, severity configurable).                                                          |
| Where does caching sit relative to `Transaction`?        | `Phase.Caching = 450`: after `Default`, before `Transaction`, so a cache hit never opens a transaction.                                                                                                                                      |
| Keyed services as handler parameters.                    | Supported in v1 through `[FromKeyedServices(key)]` with a constant key (KRX018).                                                                                                                                                             |
| Requests with no response.                               | `IRequest : IRequest<Unit>` with `readonly struct Unit`; handlers may return `ValueTask`/`Task`/synchronous `void`; every send path, typed overloads included, returns `ValueTask<Unit>` (decided 2026-11-26).                               |
| Notifications as a v2 feature.                           | In v1: `INotification`, `IPublisher`, sequential default with opt-in parallel, run all then `AggregateException` (even for one failure, and on cancellation), polymorphic handlers, `[HandlerOrder]`, per-publish and per-handler behaviors. |

New questions that come up during implementation go to `docs/adr/open-questions.md`.
