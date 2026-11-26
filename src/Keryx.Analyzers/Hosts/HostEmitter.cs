using System.Collections.Immutable;
using System.Globalization;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Writes the host's generated types (ADR 0001, Generated host): the scoped <c>KeryxMediator</c> with its typed overloads, generic
///     exact-type dispatch (runtime-type dispatch for notifications), lazy pipeline contexts and flat link structs (ADR 0001, Generated pipeline shape), and
///     <c>KeryxServiceCollectionExtensions.AddKeryx()</c> with generated factory registrations for handlers. Behaviors are static: links call
///     their behavior methods directly, and the pipeline contexts resolve their injected services.
/// </summary>
internal static partial class HostEmitter
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
        writer.Line("///     This host's Keryx mediator: sends each request and stream request through its pipeline to its handler, and publishes each");
        writer.Line("///     notification through its pipeline to its handlers. Scoped: it creates each pipeline context once per scope, on first use, so");
        writer.Line("///     nothing is resolved or allocated per call after that. Registered by <see cref=\"KeryxServiceCollectionExtensions.AddKeryx\" />.");
        writer.Line("/// </summary>");
        writer.Line(HandlerEmitter.GeneratedCode);
        writer.Line("internal sealed partial class KeryxMediator : global::Keryx.IMediator");
        writer.Open();

        writer.Line($"private readonly {ServiceProvider} __serviceProvider;");
        var requests = composition.Requests;
        var handlers = requests.Select(static p => p.Handler).ToImmutableArray();
        for (var i = 0; i < requests.Length; i++)
        {
            if (NeedsContext(requests[i]))
            {
                writer.Line($"private {ContextName(i)}? {ContextField(i)};");
            }
        }

        var streams = composition.Streams;
        for (var i = 0; i < streams.Length; i++)
        {
            if (NeedsContext(streams[i]))
            {
                writer.Line($"private {StreamContextName(i)}? {StreamContextField(i)};");
            }
        }

        var notifications = composition.Notifications.Where(static n => n.IsActive).ToImmutableArray();
        for (var i = 0; i < notifications.Length; i++)
        {
            writer.Line($"private {NotificationContextName(i)}? {NotificationContextField(i)};");
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
            WriteTypedSend(writer, requests[i].Handler, i);
        }

        writer.Line();
        WriteGenericSend(writer, handlers);

        for (var i = 0; i < streams.Length; i++)
        {
            writer.Line();
            WriteTypedStream(writer, streams[i].Handler, i);
        }

        writer.Line();
        WriteGenericStream(writer, streams.Select(static p => p.Handler).ToImmutableArray());
        WritePublish(writer, composition.Notifications, notifications);

        for (var i = 0; i < requests.Length; i++)
        {
            writer.Line();
            WriteCore(writer, requests[i], i);
        }

        for (var i = 0; i < streams.Length; i++)
        {
            writer.Line();
            WriteStreamCore(writer, streams[i], i);
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            writer.Line();
            WritePublishCore(writer, notifications[i], i);
        }

        if (notifications.Any(static n => n.Notification.Parallel && !n.Handlers.IsEmpty))
        {
            writer.Line();
            WriteWhenAllHandlers(writer);
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

        for (var i = 0; i < streams.Length; i++)
        {
            if (NeedsContext(streams[i]))
            {
                writer.Line();
                WriteStreamContext(writer, streams[i], i);
            }
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            writer.Line();
            WriteNotificationContext(writer, notifications[i], i);
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
        writer.Line("///     <see cref=\"global::Keryx.ISender\" /> and <see cref=\"global::Keryx.IPublisher\" /> (one instance per scope), and every");
        writer.Line("///     handler class that has injected parameters, all scoped and with generated factories. Behaviors are static and are not registered.");
        writer.Line("///     Registrations that already exist are kept, so a second call does nothing.");
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
            WriteFactory(writer, handler.HandlerClass, handler.Services);
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
            WriteTypeTable(writer, "request", "__requestTypes", "__RequestIndex", requests);
        }

        for (var i = 0; i < requests.Length; i++)
        {
            writer.Line();
            WriteDispatchArm(writer, requests[i], i);
        }
    }

    /// <summary>
    ///     The <c>FrozenDictionary</c> generic dispatch uses above <see cref="DispatchThreshold" />, and the non-inlined lookup into it.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="kind">"request" or "stream request", for the documentation.</param>
    /// <param name="table">The name of the static table.</param>
    /// <param name="lookup">The name of the lookup method.</param>
    /// <param name="handlers">The dispatched handlers, in switch order.</param>
    private static void WriteTypeTable(CodeWriter writer, string kind, string table, string lookup, ImmutableArray<HostHandlerModel> handlers) =>
        WriteTypeTable(writer, kind, table, lookup, handlers.Select(static h => h.MessageType).ToImmutableArray());

    /// <summary>
    ///     The <c>FrozenDictionary</c> generic dispatch uses above <see cref="DispatchThreshold" />, and the non-inlined lookup into it.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="kind">"request", "stream request" or "notification", for the documentation.</param>
    /// <param name="table">The name of the static table.</param>
    /// <param name="lookup">The name of the lookup method.</param>
    /// <param name="types">The dispatched types, fully qualified, in switch order.</param>
    private static void WriteTypeTable(CodeWriter writer, string kind, string table, string lookup, ImmutableArray<string> types)
    {
        writer.Line($"/// <summary>Every {kind} type this host dispatches, mapped to its position in the dispatch switch.</summary>");
        writer.Line($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, int> {table} =");
        writer.Line("    global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(");
        writer.Line("        new global::System.Collections.Generic.KeyValuePair<global::System.Type, int>[]");
        writer.Line("        {");
        for (var i = 0; i < types.Length; i++)
        {
            writer.Line($"            new(typeof({types[i]}), {Number(i)}),");
        }

        writer.Line("        }");
        writer.Line("    );");
        writer.Line();
        writer.Line($"/// <summary>The position of a {kind} type in the dispatch switch, or -1 for a type this host does not dispatch.</summary>");
        writer.Line(NoInlining);
        writer.Line($"private static int {lookup}(global::System.Type requestType) =>");
        writer.Line($"    {table}.TryGetValue(requestType, out var requestIndex) ? requestIndex : -1;");
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

    /// <summary>
    ///     The pipeline of one request, without checks: the typed overload and generic dispatch both call it. Without behaviors it calls the
    ///     handler's invoker; with behaviors it calls the outermost behavior method directly, with explicit type arguments and the first link.
    /// </summary>
    private static void WriteCore(CodeWriter writer, HostPipeline pipeline, int index)
    {
        var request = pipeline.Handler;
        var context = $"global::System.Threading.Volatile.Read(ref {ContextField(index)}) ?? {CreateContextName(index)}()";
        var signature = $"private {ValueTask}<{request.ResponseType}> {CoreName(index)}({request.MessageType} request, {CancellationToken} cancellationToken)";
        if (pipeline.Behaviors.IsEmpty)
        {
            var target = NeedsContext(pipeline) ? $"({context}).Handler" : request.HandlerClass;
            writer.Line(signature + " =>");
            writer.Line($"    {target}.{HandlerAnalysis.InvokerName}(request, cancellationToken);");
        }
        else
        {
            writer.Line(signature);
            writer.Open();
            writer.Line($"var pipeline = {context};");
            writer.Line($"return {BehaviorCall(pipeline.Behaviors[0], 0, ContextName(index) + "." + LinkName(0), "pipeline", "request")};");
            writer.Close();
        }

        if (!NeedsContext(pipeline))
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

    /// <summary>
    ///     The pipeline context of one request: its handler (unless the invoker is static) and the injected services of its behavior methods,
    ///     resolved once per scope, and one flat link struct per behavior (ADR 0001, Generated pipeline shape). <c>Link{k}</c> is the
    ///     <c>next</c> of behavior <c>k</c>: it calls behavior <c>k + 1</c>'s method with <c>Link{k + 1}</c>, and the last link calls the
    ///     handler's invoker. Each link holds the context and the request, whatever the depth; the token is an argument, never stored.
    /// </summary>
    private static void WriteContext(CodeWriter writer, HostPipeline pipeline, int index)
    {
        var request = pipeline.Handler;
        var behaviors = pipeline.Behaviors;
        writer.Line($"/// <summary>The pipeline context of <c>{Display(request.MessageType)}</c>: {Holds(request, behaviors)}, resolved once per scope.</summary>");
        writer.Line($"private sealed class {ContextName(index)}");
        writer.Open();
        if (!request.StaticInvoker)
        {
            writer.Line($"public readonly {request.HandlerClass} Handler;");
        }

        WriteServiceFields(writer, behaviors);
        writer.Line();
        writer.Line($"public {ContextName(index)}({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        if (!request.StaticInvoker)
        {
            writer.Line($"Handler = {ServiceProviderExtensions}.GetRequiredService<{request.HandlerClass}>({ServiceProviderName});");
        }

        WriteServiceResolution(writer, behaviors);
        writer.Close();

        for (var k = 0; k < behaviors.Length; k++)
        {
            writer.Line();
            WriteLink(writer, pipeline, index, k);
        }

        writer.Close();
    }

    private static void WriteLink(CodeWriter writer, HostPipeline pipeline, int index, int position)
    {
        var request = pipeline.Handler;
        var context = ContextName(index);
        var link = LinkName(position);
        var isLast = position == pipeline.Behaviors.Length - 1;
        writer.Line(
            isLast
                ? $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: the handler.</summary>"
                : $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: <c>{Display(pipeline.Behaviors[position + 1].Identity)}</c>.</summary>"
        );
        // The last link of a pipeline whose handler has a static invoker needs only the request.
        var holdsContext = !isLast || !request.StaticInvoker;
        writer.Line($"public readonly struct {link} : global::Keryx.INext<{request.ResponseType}>");
        writer.Open();
        if (holdsContext)
        {
            writer.Line($"private readonly {context} _pipeline;");
        }

        writer.Line($"private readonly {request.MessageType} _request;");
        writer.Line();
        writer.Line($"public {link}({context} pipeline, {request.MessageType} request)");
        writer.Open();
        if (holdsContext)
        {
            writer.Line("_pipeline = pipeline;");
        }

        writer.Line("_request = request;");
        writer.Close();
        writer.Line();
        writer.Line($"public {ValueTask}<{request.ResponseType}> InvokeAsync({CancellationToken} cancellationToken) =>");
        if (!isLast)
        {
            writer.Line($"    {BehaviorCall(pipeline.Behaviors[position + 1], position + 1, LinkName(position + 1), "_pipeline", "_request")};");
        }
        else if (request.StaticInvoker)
        {
            writer.Line($"    {request.HandlerClass}.{HandlerAnalysis.InvokerName}(_request, cancellationToken);");
        }
        else
        {
            writer.Line($"    _pipeline.Handler.{HandlerAnalysis.InvokerName}(_request, cancellationToken);");
        }

        writer.Close();
    }

    /// <summary>
    ///     The direct call of the behavior method at <paramref name="position" /> (ADR 0001, Behavior methods): the class's static method with
    ///     explicit type arguments (<paramref name="link" /> in place of <c>TNext</c>) and the arguments in declared order: the message, a new
    ///     <paramref name="link" /> over the context and the message, the call's token, and its injected services read from the context.
    /// </summary>
    /// <param name="behavior">The behavior.</param>
    /// <param name="position">Its position in the pipeline, which names its service fields.</param>
    /// <param name="link">The link type its <c>next</c> is, qualified as the call site needs it.</param>
    /// <param name="context">The pipeline context expression.</param>
    /// <param name="message">The request or notification expression.</param>
    private static string BehaviorCall(PipelineBehaviorModel behavior, int position, string link, string context, string message)
    {
        var typeArguments = string.Join(", ", behavior.TypeArguments.Select(t => t.Length == 0 ? link : t));
        var arguments = new List<string>(behavior.Parameters.Count);
        var service = 0;
        foreach (var role in behavior.Parameters)
        {
            arguments.Add(
                role switch
                {
                    BehaviorParameterRole.Message => message,
                    BehaviorParameterRole.Next => $"new {link}({context}, {message})",
                    BehaviorParameterRole.CancellationToken => "cancellationToken",
                    BehaviorParameterRole.Service => $"{context}.{ServiceField(position, service++)}",
                    _ => "default",
                }
            );
        }

        return $"{behavior.Class}.{behavior.Method}<{typeArguments}>({string.Join(", ", arguments)})";
    }

    /// <summary>What a pipeline context holds, for its documentation.</summary>
    private static string Holds(HostHandlerModel handler, ImmutableArray<PipelineBehaviorModel> behaviors)
    {
        var services = behaviors.Any(static b => b.Services.Count > 0);
        return (handler.StaticInvoker, services) switch
        {
            (false, true) => "its handler and the injected services of its behaviors",
            (false, false) => "its handler",
            (true, true) => "the injected services of its behaviors",
            (true, false) => "nothing but its links",
        };
    }

    /// <summary>One field per injected service of each behavior method, in pipeline and declared order.</summary>
    private static void WriteServiceFields(CodeWriter writer, ImmutableArray<PipelineBehaviorModel> behaviors)
    {
        for (var k = 0; k < behaviors.Length; k++)
        {
            for (var j = 0; j < behaviors[k].Services.Count; j++)
            {
                writer.Line($"public readonly {behaviors[k].Services[j].Type} {ServiceField(k, j)};");
            }
        }
    }

    /// <summary>Resolves each injected service of each behavior method from the scope, in pipeline and declared order.</summary>
    private static void WriteServiceResolution(CodeWriter writer, ImmutableArray<PipelineBehaviorModel> behaviors)
    {
        var variable = 0;
        for (var k = 0; k < behaviors.Length; k++)
        {
            for (var j = 0; j < behaviors[k].Services.Count; j++)
            {
                writer.Line($"{ServiceField(k, j)} = {Resolve(behaviors[k].Services[j], variable++)};");
            }
        }
    }

    private static void WriteFactory(CodeWriter writer, string type, Specular.Scanning.EquatableArray<ServiceParameterModel> services)
    {
        if (services.Count <= 1)
        {
            var arguments = services.Count == 1 ? Resolve(services[0], 0) : "";
            writer.Line($"{DescriptorExtensions}.TryAddScoped(services, static {ServiceProviderName} => new {type}({arguments}));");
            return;
        }

        writer.Line($"{DescriptorExtensions}.TryAddScoped(");
        writer.Line("    services,");
        writer.Line($"    static {ServiceProviderName} => new {type}(");
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
            return parameter.NullableUnderlyingType is not null
                ? key is null
                    ? $"({parameter.Type}){ServiceProviderExtensions}.GetRequiredService({provider}, typeof({parameter.Type}))"
                    : $"({parameter.Type}){KeyedServiceProviderExtensions}.GetRequiredKeyedService({provider}, typeof({parameter.Type}), {key})"
                : key is null
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

    private static bool NeedsContext(HostPipeline pipeline) => !pipeline.Handler.StaticInvoker || !pipeline.Behaviors.IsEmpty;

    /// <summary>The context field of injected service <paramref name="service" /> of the behavior at <paramref name="position" />.</summary>
    private static string ServiceField(int position, int service) => "Behavior" + Number(position + 1) + "Service" + Number(service);

    private static string LinkName(int position) => "Link" + Number(position + 1);

    private static string ContextName(int index) => "__Pipeline" + Number(index);

    private static string ContextField(int index) => "__pipeline" + Number(index);

    private static string CreateContextName(int index) => "__CreatePipeline" + Number(index);

    private static string CoreName(int index) => "__Send" + Number(index);

    private static string DispatchName(int index) => "__Dispatch" + Number(index);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A type name for documentation text: without <c>global::</c>, with the angle brackets of type arguments escaped for XML.</summary>
    private static string Display(string type) => type.Replace("global::", "").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>
///     One request pipeline: the handler and its behaviors, sorted outermost first (ADR 0001, Ordering).
/// </summary>
/// <param name="Handler">The request handler.</param>
/// <param name="Behaviors">The behaviors, outermost first.</param>
internal sealed record HostPipeline(HostHandlerModel Handler, ImmutableArray<PipelineBehaviorModel> Behaviors);

/// <summary>
///     What the host dispatches and registers, composed from the handler models (ADR 0001, How the host reads it).
/// </summary>
/// <param name="Requests">The request pipelines, one per request type, ordered by request type.</param>
/// <param name="Streams">The stream pipelines, one per stream request type, ordered by stream request type.</param>
/// <param name="Notifications">
///     The notification pipelines, one per concrete notification type the host knows (whether or not anything runs for it), ordered by
///     notification type.
/// </param>
/// <param name="ConstructedHandlers">The handler classes with injected parameters, ordered by class, each once.</param>
/// <param name="Cycles">The behaviors left in a <c>RunsAfter</c>/<c>RunsBefore</c> cycle, per pipeline and bucket (for KRX008).</param>
internal sealed record HostComposition(
    ImmutableArray<HostPipeline> Requests,
    ImmutableArray<HostPipeline> Streams,
    ImmutableArray<NotificationPipeline> Notifications,
    ImmutableArray<HostHandlerModel> ConstructedHandlers,
    ImmutableArray<ImmutableArray<PipelineBehaviorModel>> Cycles
)
{
    /// <summary>Every sorted behavior list of every pipeline: requests, streams and publishes.</summary>
    public IEnumerable<ImmutableArray<PipelineBehaviorModel>> BehaviorLists =>
        Requests
           .Concat(Streams)
           .Select(static p => p.Behaviors)
           .Concat(Notifications.Select(static n => n.Behaviors));

    public static HostComposition Create(IEnumerable<HostHandlerModel> handlers, IEnumerable<HostNotificationModel> notifications)
    {
        var all = handlers.ToArray();

        var cycles = new List<ImmutableArray<PipelineBehaviorModel>>();
        var requests = Pipelines(all, HandlerKind.Request, cycles);
        var streams = Pipelines(all, HandlerKind.Stream, cycles);
        var notificationPipelines = NotificationPipelines(all, notifications, cycles);

        var constructed = all
                         .Where(static h => !h.StaticInvoker)
                         .GroupBy(static h => h.HandlerClass, StringComparer.Ordinal)
                         .Select(static g => g.First())
                         .OrderBy(static h => h.HandlerClass, StringComparer.Ordinal)
                         .ToImmutableArray();

        return new(requests, streams, notificationPipelines, constructed, [.. cycles]);
    }

    /// <summary>
    ///     The pipelines of one kind of handler, one per message type, ordered by message type, each with its behaviors sorted (ADR 0001,
    ///     Ordering). A message type with more than one handler is KRX005 (the analyzer reports it in this assembly); the host dispatches
    ///     neither, so the error is not joined by duplicate members in generated code.
    /// </summary>
    private static ImmutableArray<HostPipeline> Pipelines(HostHandlerModel[] all, HandlerKind kind, List<ImmutableArray<PipelineBehaviorModel>> cycles) =>
        all
           .Where(h => h.Kind == kind)
           .GroupBy(static h => h.MessageType, StringComparer.Ordinal)
           .Where(static g => g.Count() == 1)
           .Select(static g => g.Single())
           .OrderBy(static h => h.MessageType, StringComparer.Ordinal)
           .Select(h => new HostPipeline(h, BehaviorOrdering.Sort(h.Behaviors, static b => b.Key, cycles)))
           .ToImmutableArray();

    /// <summary>
    ///     One pipeline per concrete notification type the host knows (declared in its source, or delivered to one of its handlers), ordered
    ///     by type: its notification behaviors sorted, and every handler that receives it (exact-type and polymorphic), ordered by
    ///     <c>[HandlerOrder]</c>, then by the ordinal fully qualified handler class name (ADR 0001, Handler order).
    /// </summary>
    private static ImmutableArray<NotificationPipeline> NotificationPipelines(
        HostHandlerModel[] all,
        IEnumerable<HostNotificationModel> declared,
        List<ImmutableArray<PipelineBehaviorModel>> cycles
    )
    {
        var handlers = all.Where(static h => h.Kind == HandlerKind.Notification).ToArray();
        var known = new SortedDictionary<string, HostNotificationModel>(StringComparer.Ordinal);
        foreach (var notification in declared.Concat(handlers.SelectMany(static h => h.Deliveries)))
        {
            if (!known.ContainsKey(notification.Type))
            {
                known.Add(notification.Type, notification);
            }
        }

        var received = new Dictionary<string, List<NotificationHandlerPipeline>>(StringComparer.Ordinal);
        foreach (var handler in handlers.OrderBy(static h => h.Order).ThenBy(static h => h.HandlerClass, StringComparer.Ordinal))
        {
            foreach (var delivery in handler.Deliveries)
            {
                if (!received.TryGetValue(delivery.Type, out var list))
                {
                    list = [];
                    received.Add(delivery.Type, list);
                }

                list.Add(new(handler, delivery.IsValueType && handler.MessageType != delivery.Type));
            }
        }

        var pipelines = ImmutableArray.CreateBuilder<NotificationPipeline>(known.Count);
        foreach (var notification in known.Values)
        {
            pipelines.Add(
                new(
                    notification,
                    Sorted(notification.Behaviors, cycles),
                    received.TryGetValue(notification.Type, out var list) ? [.. list] : []
                )
            );
        }

        return pipelines.MoveToImmutable();
    }

    private static ImmutableArray<PipelineBehaviorModel> Sorted(
        Specular.Scanning.EquatableArray<PipelineBehaviorModel> behaviors,
        List<ImmutableArray<PipelineBehaviorModel>> cycles
    ) =>
        BehaviorOrdering.Sort(behaviors, static b => b.Key, cycles);
}

/// <summary>
///     One notification type's publish pipeline: its notification behaviors, sorted outermost first, around its publish strategy over its
///     handlers (ADR 0001, Notifications).
/// </summary>
/// <param name="Notification">The concrete notification type.</param>
/// <param name="Behaviors">The notification behaviors, outermost first.</param>
/// <param name="Handlers">The handlers, in handler order.</param>
internal sealed record NotificationPipeline(
    HostNotificationModel Notification,
    ImmutableArray<PipelineBehaviorModel> Behaviors,
    ImmutableArray<NotificationHandlerPipeline> Handlers
)
{
    /// <summary>
    ///     Whether anything runs when the type is published: a handler or a notification behavior (OQ-18). Otherwise <c>Publish</c> completes
    ///     immediately.
    /// </summary>
    public bool IsActive => !Handlers.IsEmpty || !Behaviors.IsEmpty;

    /// <summary>Whether a publish boxes the notification once, for handlers typed as an interface (KRX037).</summary>
    public bool Boxes => Handlers.Any(static h => h.Boxed);
}

/// <summary>
///     One handler of a notification type. Handlers have no behaviors of their own: the publish strategy calls them directly.
/// </summary>
/// <param name="Handler">The handler.</param>
/// <param name="Boxed">
///     Whether the notification is a struct and the handler's parameter an interface or <c>INotification</c>, so the handler receives the
///     publish's one box (OQ-17).
/// </param>
internal sealed record NotificationHandlerPipeline(HostHandlerModel Handler, bool Boxed);
