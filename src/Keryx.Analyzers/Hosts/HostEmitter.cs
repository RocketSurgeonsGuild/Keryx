using System.Collections.Immutable;
using System.Globalization;
using Keryx.Analyzers.Handlers;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Writes the host's generated types (ADR 0001, Generated host): the scoped <c>KeryxMediator</c> with its typed overloads, generic
///     exact-type dispatch and lazy pipeline contexts, and <c>KeryxServiceCollectionExtensions.AddKeryx()</c> with generated factory
///     registrations.
/// </summary>
internal static class HostEmitter
{
    public const string MediatorHintName = "KeryxMediator.Keryx.g.cs";
    public const string ServiceCollectionExtensionsHintName = "KeryxServiceCollectionExtensions.Keryx.g.cs";

    /// <summary>
    ///     Above this many request types, generic <c>Send</c> looks the runtime type up in a <c>FrozenDictionary&lt;Type, int&gt;</c> and switches
    ///     on the index; up to it, it compares the runtime type with each request type in turn (ADR 0001, Generic entry points). Set from
    ///     <c>DispatchBenchmarks</c> (OQ-15): the lookup costs about 4.5 ns whatever the count, the chain about 0.13 ns per compare, so at 32
    ///     types the chain's worst case is within 1 ns of the lookup and its average is faster.
    /// </summary>
    public const int DispatchThreshold = 32;

    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";
    private const string CancellationToken = "global::System.Threading.CancellationToken";
    private const string ServiceProvider = "global::System.IServiceProvider";
    private const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    private const string ThrowIfNull = "global::System.ArgumentNullException.ThrowIfNull";
    private const string ServiceProviderExtensions = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions";
    private const string KeyedServiceProviderExtensions = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions";
    private const string KeyedServiceProvider = "global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider";
    private const string ServiceCollection = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string DescriptorExtensions = "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";
    private const string NoInlining = "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]";

    /// <summary>The name of the scope's service provider in generated factories and pipeline contexts.</summary>
    private const string ServiceProviderName = "serviceProvider";

    public static string Mediator(HostSettings settings, HostComposition composition)
    {
        var writer = Start(settings);
        writer.Line("/// <summary>");
        writer.Line("///     This host's Keryx mediator: sends each request through its pipeline to its handler. Scoped: it creates each pipeline");
        writer.Line("///     context once per scope, on first use, so nothing is resolved or allocated per call after that. Registered by");
        writer.Line("///     <see cref=\"KeryxServiceCollectionExtensions.AddKeryx\" />.");
        writer.Line("/// </summary>");
        writer.Line(HandlerEmitter.GeneratedCode);
        writer.Line("internal sealed partial class KeryxMediator : global::Keryx.IMediator");
        writer.Open();

        writer.Line($"private readonly {ServiceProvider} __serviceProvider;");
        var requests = composition.Requests;
        for (var i = 0; i < requests.Length; i++)
        {
            if (NeedsContext(requests[i]))
            {
                writer.Line($"private {ContextName(i)}? {ContextField(i)};");
            }
        }

        writer.Line();
        writer.Line("/// <summary>Creates the mediator of one scope.</summary>");
        writer.Line($"/// <param name=\"{ServiceProviderName}\">The scope's service provider. Pipeline contexts resolve handlers from it.</param>");
        writer.Line($"public KeryxMediator({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        writer.Line($"{ThrowIfNull}({ServiceProviderName});");
        writer.Line($"__serviceProvider = {ServiceProviderName};");
        writer.Close();

        for (var i = 0; i < requests.Length; i++)
        {
            writer.Line();
            WriteTypedSend(writer, requests[i], i);
        }

        writer.Line();
        WriteGenericSend(writer, requests);
        writer.Line();
        WriteStream(writer);
        writer.Line();
        WritePublish(writer);

        for (var i = 0; i < requests.Length; i++)
        {
            writer.Line();
            WriteCore(writer, requests[i], i);
        }

        writer.Line();
        writer.Line(NoInlining);
        writer.Line("private static global::Keryx.KeryxHandlerNotFoundException __HandlerNotFound(global::System.Type requestType) => new(requestType);");

        for (var i = 0; i < requests.Length; i++)
        {
            if (NeedsContext(requests[i]))
            {
                writer.Line();
                WriteContext(writer, requests[i], i);
            }
        }

        writer.Close();
        writer.Close();
        return writer.ToString();
    }

    public static string ServiceCollectionExtensions(HostSettings settings, HostComposition composition)
    {
        var writer = Start(settings);
        writer.Line("/// <summary>Registers this host's Keryx mediator and handlers.</summary>");
        writer.Line(HandlerEmitter.GeneratedCode);
        writer.Line("internal static class KeryxServiceCollectionExtensions");
        writer.Open();
        writer.Line("/// <summary>");
        writer.Line("///     Registers the scoped <see cref=\"KeryxMediator\" />, as itself and as <see cref=\"global::Keryx.IMediator\" />,");
        writer.Line("///     <see cref=\"global::Keryx.ISender\" /> and <see cref=\"global::Keryx.IPublisher\" /> (one instance per scope), and every handler");
        writer.Line("///     class that has injected parameters, all scoped and with generated factories. Registrations that already exist are kept, so a");
        writer.Line("///     second call does nothing.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"services\">The service collection.</param>");
        writer.Line("/// <returns>The same service collection.</returns>");
        writer.Line($"public static {ServiceCollection} AddKeryx(this {ServiceCollection} services)");
        writer.Open();
        writer.Line($"{ThrowIfNull}(services);");
        writer.Line($"{DescriptorExtensions}.TryAddScoped(services, static {ServiceProviderName} => new KeryxMediator({ServiceProviderName}));");
        foreach (var service in (string[])["global::Keryx.IMediator", "global::Keryx.ISender", "global::Keryx.IPublisher"])
        {
            writer.Line(
                $"{DescriptorExtensions}.TryAddScoped<{service}>(services, static {ServiceProviderName} => {ServiceProviderExtensions}.GetRequiredService<KeryxMediator>({ServiceProviderName}));"
            );
        }

        foreach (var handler in composition.ConstructedHandlers)
        {
            WriteFactory(writer, handler);
        }

        writer.Line("return services;");
        writer.Close();
        writer.Close();
        writer.Close();
        return writer.ToString();
    }

    private static CodeWriter Start(HostSettings settings)
    {
        var writer = new CodeWriter();
        writer.Line("// <auto-generated/>");
        writer.Line("#nullable enable");
        writer.Line(HandlerEmitter.ObsoletePragma);
        writer.Line();
        writer.Line($"namespace {settings.Namespace}");
        writer.Open();
        return writer;
    }

    private static void WriteTypedSend(CodeWriter writer, HostHandlerModel request, int index)
    {
        writer.Line("/// <summary>");
        writer.Line($"///     Sends a <c>{Display(request.MessageType)}</c> through its pipeline to its handler, without type dispatch{( request.MessageIsValueType ? " and without boxing it" : "" )}.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"request\">The request.</param>");
        writer.Line("/// <param name=\"cancellationToken\">The token passed to the handler.</param>");
        writer.Line("/// <returns>The handler's response.</returns>");
        writer.Line($"public {ValueTask}<{request.ResponseType}> Send({request.MessageType} request, {CancellationToken} cancellationToken = default)");
        writer.Open();
        if (!request.MessageIsValueType)
        {
            writer.Line($"{ThrowIfNull}(request);");
        }

        if (!request.MessageIsSealed)
        {
            // Dispatch is by exact runtime type: a derived request statically typed as this one goes to its own handler (OQ-31).
            writer.Line($"if (request.GetType() != typeof({request.MessageType}))");
            writer.Open();
            writer.Line($"return Send<{request.ResponseType}>((global::Keryx.IRequest<{request.ResponseType}>)request, cancellationToken);");
            writer.Close();
            writer.Line();
        }

        writer.Line($"return {CoreName(index)}(request, cancellationToken);");
        writer.Close();
    }

    private static void WriteGenericSend(CodeWriter writer, ImmutableArray<HostHandlerModel> requests)
    {
        writer.Line("/// <inheritdoc />");
        writer.Line(
            $"public {ValueTask}<TResponse> Send<TResponse>(global::Keryx.IRequest<TResponse> request, {CancellationToken} cancellationToken = default)"
        );
        writer.Open();
        writer.Line($"{ThrowIfNull}(request);");
        writer.Line("var requestType = request.GetType();");
        if (requests.Length > DispatchThreshold)
        {
            writer.Line("switch (__RequestIndex(requestType))");
            writer.Open();
            for (var i = 0; i < requests.Length; i++)
            {
                writer.Line($"case {Number(i)}:");
                writer.Line($"    return {DispatchName(i)}(request, cancellationToken);");
            }

            writer.Line("default:");
            writer.Line("    throw __HandlerNotFound(requestType);");
            writer.Close();
        }
        else
        {
            for (var i = 0; i < requests.Length; i++)
            {
                writer.Line($"if (requestType == typeof({requests[i].MessageType}))");
                writer.Open();
                writer.Line($"return {DispatchName(i)}(request, cancellationToken);");
                writer.Close();
                writer.Line();
            }

            writer.Line("throw __HandlerNotFound(requestType);");
        }

        writer.Close();

        if (requests.Length > DispatchThreshold)
        {
            writer.Line();
            writer.Line("/// <summary>Every request type this host dispatches, mapped to its position in the dispatch switch.</summary>");
            writer.Line("private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, int> __requestTypes =");
            writer.Line("    global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(");
            writer.Line("        new global::System.Collections.Generic.KeyValuePair<global::System.Type, int>[]");
            writer.Line("        {");
            for (var i = 0; i < requests.Length; i++)
            {
                writer.Line($"            new(typeof({requests[i].MessageType}), {Number(i)}),");
            }

            writer.Line("        }");
            writer.Line("    );");
            writer.Line();
            writer.Line("/// <summary>The position of a request type in the dispatch switch, or -1 for a type this host does not dispatch.</summary>");
            writer.Line(NoInlining);
            writer.Line("private static int __RequestIndex(global::System.Type requestType) =>");
            writer.Line("    __requestTypes.TryGetValue(requestType, out var requestIndex) ? requestIndex : -1;");
        }

        for (var i = 0; i < requests.Length; i++)
        {
            writer.Line();
            WriteDispatchArm(writer, requests[i], i);
        }
    }

    /// <summary>
    ///     One arm of generic dispatch: the request is reinterpreted as its exact type (a class with <c>Unsafe.As</c>, a struct unboxed), and
    ///     the arm's <c>ValueTask&lt;X&gt;</c> as <c>ValueTask&lt;TResponse&gt;</c>, which is the same type at run time because
    ///     <c>IRequest&lt;&gt;</c> is invariant and a request implements exactly one closing (KRX011). Never a cast through <c>object</c>,
    ///     which would box the result.
    /// </summary>
    /// <remarks>
    ///     Each arm is its own method, not inlined, so the dispatching method holds no address-taken locals: with one <c>Unsafe.As(ref ...)</c>
    ///     local per arm (or an <c>out</c> lookup local) in one method, the JIT zeroes every arm's <c>ValueTask</c> in the prolog of each call
    ///     and cannot tail-call the arms, which measured about 45 ns per <c>Send</c> under Dynamic PGO whatever the arm taken (OQ-15).
    /// </remarks>
    private static void WriteDispatchArm(CodeWriter writer, HostHandlerModel request, int index)
    {
        var argument = request.MessageIsValueType ? $"({request.MessageType})(object)request" : $"{Unsafe}.As<{request.MessageType}>(request)";
        writer.Line(NoInlining);
        writer.Line(
            $"private {ValueTask}<TResponse> {DispatchName(index)}<TResponse>(global::Keryx.IRequest<TResponse> request, {CancellationToken} cancellationToken)"
        );
        writer.Open();
        writer.Line($"var response = {CoreName(index)}({argument}, cancellationToken);");
        writer.Line($"return {Unsafe}.As<{ValueTask}<{request.ResponseType}>, {ValueTask}<TResponse>>(ref response);");
        writer.Close();
    }

    private static void WriteStream(CodeWriter writer)
    {
        writer.Line("/// <inheritdoc />");
        writer.Line("/// <remarks>This mediator dispatches no stream requests yet, so every stream request is unknown to it.</remarks>");
        writer.Line(
            $"public global::System.Collections.Generic.IAsyncEnumerable<TItem> Stream<TItem>(global::Keryx.IStreamRequest<TItem> request, {CancellationToken} cancellationToken = default)"
        );
        writer.Open();
        writer.Line($"{ThrowIfNull}(request);");
        writer.Line("throw __HandlerNotFound(request.GetType());");
        writer.Close();
    }

    private static void WritePublish(CodeWriter writer)
    {
        writer.Line("/// <inheritdoc />");
        writer.Line("/// <remarks>This mediator dispatches no notifications yet, so every notification type is unknown to it and runs no handlers.</remarks>");
        writer.Line($"public {ValueTask} Publish<TNotification>(TNotification notification, {CancellationToken} cancellationToken = default)");
        writer.Line("    where TNotification : global::Keryx.INotification");
        writer.Open();
        // Not ArgumentNullException.ThrowIfNull(object): that would box a struct notification.
        writer.Line("if (notification is null)");
        writer.Open();
        writer.Line("throw new global::System.ArgumentNullException(nameof(notification));");
        writer.Close();
        writer.Line();
        writer.Line("return default;");
        writer.Close();
    }

    /// <summary>The pipeline of one request, without checks: the typed overload and generic dispatch both call it.</summary>
    private static void WriteCore(CodeWriter writer, HostHandlerModel request, int index)
    {
        var target = NeedsContext(request) ? $"(global::System.Threading.Volatile.Read(ref {ContextField(index)}) ?? {CreateContextName(index)}()).Handler" : request.HandlerClass;
        writer.Line($"private {ValueTask}<{request.ResponseType}> {CoreName(index)}({request.MessageType} request, {CancellationToken} cancellationToken) =>");
        writer.Line($"    {target}.{HandlerAnalysis.InvokerName}(request, cancellationToken);");
        if (!NeedsContext(request))
        {
            return;
        }

        // Concurrent first use in one scope: every caller ends up with the context that won the exchange (ADR 0001, Mediator).
        writer.Line();
        writer.Line(NoInlining);
        writer.Line($"private {ContextName(index)} {CreateContextName(index)}() =>");
        writer.Line(
            $"    global::System.Threading.Interlocked.CompareExchange(ref {ContextField(index)}, new {ContextName(index)}(__serviceProvider), null) ?? {ContextField(index)}!;"
        );
    }

    private static void WriteContext(CodeWriter writer, HostHandlerModel request, int index)
    {
        writer.Line($"/// <summary>The pipeline context of <c>{Display(request.MessageType)}</c>: its handler, resolved once per scope.</summary>");
        writer.Line($"private sealed class {ContextName(index)}");
        writer.Open();
        writer.Line($"public readonly {request.HandlerClass} Handler;");
        writer.Line();
        writer.Line($"public {ContextName(index)}({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        writer.Line($"Handler = {ServiceProviderExtensions}.GetRequiredService<{request.HandlerClass}>({ServiceProviderName});");
        writer.Close();
        writer.Close();
    }

    private static void WriteFactory(CodeWriter writer, HostHandlerModel handler)
    {
        var services = handler.Services;
        if (services.Count == 1)
        {
            writer.Line(
                $"{DescriptorExtensions}.TryAddScoped(services, static {ServiceProviderName} => new {handler.HandlerClass}({Resolve(services[0], 0)}));"
            );
            return;
        }

        writer.Line($"{DescriptorExtensions}.TryAddScoped(");
        writer.Line("    services,");
        writer.Line($"    static {ServiceProviderName} => new {handler.HandlerClass}(");
        for (var i = 0; i < services.Count; i++)
        {
            writer.Line($"        {Resolve(services[i], i)}{( i < services.Count - 1 ? "," : "" )}");
        }

        writer.Line("    )");
        writer.Line(");");
    }

    /// <summary>
    ///     The resolution of one injected parameter (ADR 0001, Injected parameters). No reflection: optional value types are matched with a
    ///     type pattern on <c>GetService(typeof(T))</c>, which neither allocates nor activates anything.
    /// </summary>
    internal static string Resolve(ServiceParameterModel parameter, int index)
    {
        const string provider = ServiceProviderName;
        if (parameter is { ServiceKey: null, IsServiceProvider: true })
        {
            return provider;
        }

        var key = parameter.ServiceKey;
        if (parameter.DefaultValue is not { } defaultValue)
        {
            // Nullable<T> does not satisfy the notnull constraint of GetRequiredService<T>, so it is resolved by type and unboxed.
            return  parameter.NullableUnderlyingType is not null 
                ?  key is null
                    ? $"({parameter.Type}){ServiceProviderExtensions}.GetRequiredService({provider}, typeof({parameter.Type}))"
                    : $"({parameter.Type}){KeyedServiceProviderExtensions}.GetRequiredKeyedService({provider}, typeof({parameter.Type}), {key})" 
                :  key is null
                ? $"{ServiceProviderExtensions}.GetRequiredService<{parameter.ServiceType}>({provider})"
                : $"{KeyedServiceProviderExtensions}.GetRequiredKeyedService<{parameter.ServiceType}>({provider}, {key})";
        }

        if (!parameter.IsValueType)
        {
            // GetService<T> already returns null for a missing service; any other default (including the null! of a parameter that is not
            // annotated) is applied with ??.
            var fallback = defaultValue == "null" ? "" : " ?? " + defaultValue;
            return key is null
                ? $"{ServiceProviderExtensions}.GetService<{parameter.ServiceType}>({provider}){fallback}"
                : $"{KeyedServiceProviderExtensions}.GetKeyedService<{parameter.ServiceType}>({provider}, {key}){fallback}";
        }

        // GetKeyedService(Type, object) is an extension only from Microsoft.Extensions.DependencyInjection.Abstractions 9.0; the interface
        // method exists from 8.0, the floor for keyed services (ADR 0001, Injected parameters).
        var lookup = key is null
            ? $"{provider}.GetService(typeof({parameter.Type}))"
            : $"({provider} as {KeyedServiceProvider})?.GetKeyedService(typeof({parameter.Type}), {key})";
        var variable = "__value" + index.ToString(CultureInfo.InvariantCulture);
        return parameter.NullableUnderlyingType is { } underlying
            ? $"{lookup} is {underlying} {variable} ? ({parameter.Type}){variable} : {defaultValue}"
            : $"{lookup} is {parameter.Type} {variable} ? {variable} : {defaultValue}";
    }

    private static bool NeedsContext(HostHandlerModel request) => !request.StaticInvoker;

    private static string ContextName(int index) => "__Pipeline" + Number(index);

    private static string ContextField(int index) => "__pipeline" + Number(index);

    private static string CreateContextName(int index) => "__CreatePipeline" + Number(index);

    private static string CoreName(int index) => "__Send" + Number(index);

    private static string DispatchName(int index) => "__Dispatch" + Number(index);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A type name for documentation text: without <c>global::</c>.</summary>
    private static string Display(string type) => type.Replace("global::", "");
}

/// <summary>
///     What the host dispatches and registers, composed from the handler models (ADR 0001, How the host reads it).
/// </summary>
/// <param name="Requests">The request handlers, one per request type, ordered by request type.</param>
/// <param name="ConstructedHandlers">The handler classes with injected parameters, ordered by class, each once.</param>
internal sealed record HostComposition(ImmutableArray<HostHandlerModel> Requests, ImmutableArray<HostHandlerModel> ConstructedHandlers)
{
    public static HostComposition Create(IEnumerable<HostHandlerModel> handlers)
    {
        var all = handlers.ToArray();

        // A request with more than one handler is KRX005 (the analyzer reports it in this assembly); the host dispatches neither, so the
        // error is not joined by duplicate members in generated code.
        var requests = all
                      .Where(static h => h.Kind == HandlerKind.Request)
                      .GroupBy(static h => h.MessageType, StringComparer.Ordinal)
                      .Where(static g => g.Count() == 1)
                      .Select(static g => g.Single())
                      .OrderBy(static h => h.MessageType, StringComparer.Ordinal)
                      .ToImmutableArray();

        var constructed = all
                         .Where(static h => !h.StaticInvoker)
                         .GroupBy(static h => h.HandlerClass, StringComparer.Ordinal)
                         .Select(static g => g.First())
                         .OrderBy(static h => h.HandlerClass, StringComparer.Ordinal)
                         .ToImmutableArray();

        return new(requests, constructed);
    }
}
