using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis.Text;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Writes the host's generated types (ADR 0001, Generated host), as text through a <see cref="CodeWriter" />: the scoped
///     <c>KeryxMediator</c> with its typed overloads, generic dispatch as switch statements (type patterns on the sealed request and stream
///     request types, exact-type guards on notification types, a jump table above the threshold), lazy pipeline contexts for the pipelines
///     that resolve something and flat link structs that take only what they read or pass on (ADR 0001, Generated pipeline shape), and
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

    /// <summary>The name of the scope's service provider in generated factories and pipeline contexts.</summary>
    private const string ServiceProviderName = "serviceProvider";

    /// <summary>The name of the pipeline context, as a local at the entry of a pipeline and as a parameter of its links.</summary>
    private const string PipelineName = "pipeline";

    /// <summary>The parameters every pipeline method, typed overload and link ends with, after the message.</summary>
    private const string TokenParameter = CancellationToken + " cancellationToken";

    /// <summary>The token parameter of a public entry point, which defaults.</summary>
    private const string OptionalTokenParameter = TokenParameter + " = default";

    public static SourceText Mediator(HostSettings settings, HostComposition composition)
    {
        var requests = composition.Requests;
        var streams = composition.Streams;
        var notifications = composition.Notifications.Where(static n => n.IsActive).ToImmutableArray();

        // About 2.5 KB of text per pipeline with a few behaviors, so the builder rarely grows.
        var writer = new CodeWriter(capacity: 8192 + 2560 * ( requests.Length + streams.Length + composition.Notifications.Length ));
        writer.Namespace(settings.Namespace);
        writer.Member();
        writer.Documentation(
            "<summary>",
            "    This host's Keryx mediator: sends each request and stream request through its pipeline to its handler, and publishes each",
            "    notification through its pipeline to its handlers. Scoped: it creates each pipeline context once per scope, on first use, so",
            "    nothing is resolved or allocated per call after that. Registered by <see cref=\"KeryxServiceCollectionExtensions.AddKeryx\" />.",
            "</summary>"
        );
        writer.Line(CodeWriter.GeneratedCodeAttribute);
        writer.Line("internal sealed partial class KeryxMediator : global::Keryx.IMediator");
        writer.Open();

        writer.Member(plainField: true);
        writer.Line($"private readonly {ServiceProvider} __serviceProvider;");
        for (var i = 0; i < requests.Length; i++)
        {
            if (HasContext(requests[i]))
            {
                writer.Member(plainField: true);
                writer.Line($"private {ContextName(i)}? {ContextField(i)};");
            }
        }

        for (var i = 0; i < streams.Length; i++)
        {
            if (HasContext(streams[i]))
            {
                writer.Member(plainField: true);
                writer.Line($"private {StreamContextName(i)}? {StreamContextField(i)};");
            }
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            if (HasContext(notifications[i]))
            {
                writer.Member(plainField: true);
                writer.Line($"private {NotificationContextName(i)}? {NotificationContextField(i)};");
            }
        }

        writer.Member();
        writer.Documentation(
            "<summary>Creates the mediator of one scope.</summary>",
            $"<param name=\"{ServiceProviderName}\">The scope's service provider. Pipeline contexts resolve handlers from it.</param>"
        );
        writer.Line($"public KeryxMediator({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        writer.Line($"{ThrowIfNull}({ServiceProviderName});");
        writer.Line($"__serviceProvider = {ServiceProviderName};");
        writer.Close();

        for (var i = 0; i < requests.Length; i++)
        {
            WriteTypedSend(writer, requests[i].Handler, i);
        }

        WriteGenericSend(writer, requests.Select(static p => p.Handler).ToImmutableArray());

        for (var i = 0; i < streams.Length; i++)
        {
            WriteTypedStream(writer, streams[i].Handler, i);
        }

        WriteGenericStream(writer, streams.Select(static p => p.Handler).ToImmutableArray());
        WritePublish(writer, composition.Notifications, notifications);

        for (var i = 0; i < requests.Length; i++)
        {
            WriteCore(writer, requests[i], i);
        }

        for (var i = 0; i < streams.Length; i++)
        {
            WriteStreamCore(writer, streams[i], i);
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            WritePublishCore(writer, notifications[i], i);
        }

        if (notifications.Any(static n => n.Notification.Parallel && !n.Handlers.IsEmpty))
        {
            WriteWhenAllHandlers(writer);
        }

        writer.Member();
        writer.Line(CodeWriter.NoInliningAttribute);
        writer.Arrow("private static global::Keryx.KeryxHandlerNotFoundException __HandlerNotFound(global::System.Type requestType)", "new(requestType)");

        for (var i = 0; i < requests.Length; i++)
        {
            if (HasContext(requests[i]) || !requests[i].Behaviors.IsEmpty)
            {
                WriteContext(writer, requests[i], i);
            }
        }

        for (var i = 0; i < streams.Length; i++)
        {
            if (HasContext(streams[i]) || !streams[i].Behaviors.IsEmpty)
            {
                WriteStreamContext(writer, streams[i], i);
            }
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            WriteNotificationContext(writer, notifications[i], i);
        }

        writer.Close();
        writer.Close();
        return writer.ToSourceText();
    }

    public static SourceText ServiceCollectionExtensions(HostSettings settings, HostComposition composition)
    {
        var writer = new CodeWriter(capacity: 4096 + 512 * composition.ConstructedHandlers.Length);
        writer.Namespace(settings.Namespace);
        writer.Member();
        writer.Documentation("<summary>Registers this host's Keryx mediator and handlers.</summary>");
        writer.Line(CodeWriter.GeneratedCodeAttribute);
        writer.Line("internal static class KeryxServiceCollectionExtensions");
        writer.Open();
        writer.Member();
        writer.Documentation(
            "<summary>",
            "    Registers the scoped <see cref=\"KeryxMediator\" />, as itself and as <see cref=\"global::Keryx.IMediator\" />,",
            "    <see cref=\"global::Keryx.ISender\" /> and <see cref=\"global::Keryx.IPublisher\" /> (one instance per scope), and every",
            "    handler class that has injected parameters, all scoped and with generated factories. Behaviors are static and are not registered.",
            "    Registrations that already exist are kept, so a second call does nothing.",
            "</summary>",
            "<param name=\"services\">The service collection.</param>",
            "<returns>The same service collection.</returns>"
        );
        writer.Line($"public static {ServiceCollection} AddKeryx(this {ServiceCollection} services)");
        writer.Open();
        writer.Line($"{ThrowIfNull}(services);");
        writer.Line($"{DescriptorExtensions}.TryAddScoped(services, {Factory($"new KeryxMediator({ServiceProviderName})")});");
        foreach (var service in (string[])["global::Keryx.IMediator", "global::Keryx.ISender", "global::Keryx.IPublisher"])
        {
            writer.Line(
                $"{DescriptorExtensions}.TryAddScoped<{service}>(services, {Factory($"{ServiceProviderExtensions}.GetRequiredService<KeryxMediator>({ServiceProviderName})")});"
            );
        }

        foreach (var handler in composition.ConstructedHandlers)
        {
            var creation = new StringBuilder(256).Append("new ").Append(handler.HandlerClass).Append('(');
            for (var i = 0; i < handler.Services.Count; i++)
            {
                creation.Append(i == 0 ? "" : ", ").Append(Resolve(handler.Services[i], i));
            }

            writer.Line($"{DescriptorExtensions}.TryAddScoped(services, {Factory(creation.Append(')').ToString())});");
        }

        writer.Line("return services;");
        writer.Close();
        writer.Close();
        writer.Close();
        return writer.ToSourceText();
    }

    /// <summary>A scoped registration's factory: <c>static serviceProvider =&gt; body</c>.</summary>
    private static string Factory(string body) => $"static {ServiceProviderName} => {body}";

    private static void WriteTypedSend(CodeWriter writer, HostHandlerModel request, int index)
    {
        writer.Member();
        writer.Documentation(
            "<summary>",
            $"    Sends a <c>{Display(request.MessageType)}</c> through its pipeline to its handler, without type dispatch{( request.MessageIsValueType ? " and without boxing it" : "" )}.",
            "</summary>",
            "<param name=\"request\">The request.</param>",
            "<param name=\"cancellationToken\">The token passed to the handler.</param>",
            "<returns>The handler's response.</returns>"
        );
        writer.Line($"public {ValueTask}<{request.ResponseType}> Send({request.MessageType} request, {OptionalTokenParameter})");
        writer.Open();
        if (!request.MessageIsValueType)
        {
            writer.Line($"{ThrowIfNull}(request);");
        }

        writer.Line($"return {CoreName(index)}(request, cancellationToken);");
        writer.Close();
    }

    private static void WriteGenericSend(CodeWriter writer, ImmutableArray<HostHandlerModel> requests)
    {
        writer.Member();
        writer.Documentation("<inheritdoc />");
        writer.Line($"public {ValueTask}<TResponse> Send<TResponse>(global::Keryx.IRequest<TResponse> request, {OptionalTokenParameter})");
        writer.Open();
        writer.Line($"{ThrowIfNull}(request);");
        WriteRequestDispatch(writer, requests, "__RequestIndex", static (_, i, request) => $"{DispatchName(i)}<TResponse>({request}, cancellationToken)");
        writer.Close();

        if (requests.Length > DispatchThreshold)
        {
            WriteTypeTable(writer, "request", "__requestTypes", "__RequestIndex", requests.Select(static h => h.MessageType).ToImmutableArray());
        }

        for (var i = 0; i < requests.Length; i++)
        {
            WriteDispatchArm(writer, requests[i], i);
        }
    }

    /// <summary>
    ///     The statements that dispatch generic <c>Send</c> or <c>Stream</c> on the request's runtime type (ADR 0001, Generic entry points). Up
    ///     to <see cref="DispatchThreshold" /> types, a type pattern per request type, <c>switch (request) { case global::X r: return ...;
    ///     default: throw __HandlerNotFound(request.GetType()); }</c>: every request type the host composes is sealed (KRX024, KRX038), so the
    ///     pattern matches the exact type only, and the JIT compiles it to one method-table compare. Above the threshold,
    ///     <c>var requestType = request.GetType();</c> and <c>switch (Index(requestType)) { case 0: return ...; default: throw ...; }</c>, which
    ///     compiles to a jump table on the exact type. Without types, the throw alone.
    /// </summary>
    /// <remarks>
    ///     A switch statement with one <c>return</c> per section, not a switch expression: C# lowers a switch expression to one result local
    ///     that every arm stores and a single return, and the JIT then neither tail-calls the arms nor avoids zeroing that result in the prolog
    ///     (OQ-67). A struct request's section has no designation (<c>case global::S:</c>) and its arm gets <c>request</c> as the interface and
    ///     unboxes it itself: a pattern variable would unbox the struct into a local of the dispatching method, which the JIT zeroes in the
    ///     prolog of every call (it holds references) and whose copy keeps that arm from being a tail call.
    /// </remarks>
    /// <param name="writer">The writer, in the dispatching method's body.</param>
    /// <param name="requests">The dispatched request or stream request types, in switch order.</param>
    /// <param name="lookup">The index method above the threshold.</param>
    /// <param name="arm">
    ///     The result of the section of type <c>i</c>, given the request: as its exact type for a class, as the interface-typed <c>request</c>
    ///     for a struct.
    /// </param>
    private static void WriteRequestDispatch(
        CodeWriter writer,
        ImmutableArray<HostHandlerModel> requests,
        string lookup,
        Func<HostHandlerModel, int, string, string> arm
    )
    {
        if (requests.IsEmpty)
        {
            writer.Line("throw __HandlerNotFound(request.GetType());");
            return;
        }

        if (requests.Length > DispatchThreshold)
        {
            writer.Line("var requestType = request.GetType();");
            writer.Switch(lookup + "(requestType)");
            for (var i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                writer.Section(Case(i), $"return {arm(request, i, request.MessageIsValueType ? "request" : Exact(request.MessageType, false, "request"))};");
            }

            writer.Section("default:", "throw __HandlerNotFound(requestType);");
            writer.CloseStatement();
            return;
        }

        writer.Switch("request");
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            if (request.MessageIsValueType)
            {
                writer.Section($"case {request.MessageType}:", $"return {arm(request, i, "request")};");
            }
            else
            {
                writer.Section($"case {request.MessageType} r:", $"return {arm(request, i, "r")};");
            }
        }

        writer.Section("default:", "throw __HandlerNotFound(request.GetType());");
        writer.CloseStatement();
    }

    /// <summary>
    ///     The statements that dispatch generic <c>Publish</c> on the exact runtime type of a notification held as a reference (ADR 0001,
    ///     Generic entry points): notification types are not sealed (a handler of a base type receives derived notifications through the
    ///     host's expansion, so a type pattern would match a derived type the host does not know), so up to <see cref="DispatchThreshold" />
    ///     types the sections keep exact-type guards, <c>switch (type) { case var _ when type == typeof(X): return ...; default: ... }</c>;
    ///     above it, <c>switch (Index(type)) { case 0: return ...; default: ... }</c>, which compiles to a jump table.
    /// </summary>
    /// <param name="writer">The writer, in the dispatching method's body.</param>
    /// <param name="typeVariable">The local that holds the runtime type.</param>
    /// <param name="lookup">The index method above the threshold.</param>
    /// <param name="types">The dispatched types, fully qualified, in switch order.</param>
    /// <param name="arm">The result of the section of type <c>i</c>.</param>
    /// <param name="fallback">The statement of the <c>default</c> section.</param>
    private static void WriteExactTypeDispatch(
        CodeWriter writer,
        string typeVariable,
        string lookup,
        ImmutableArray<string> types,
        Func<string, int, string> arm,
        string fallback
    )
    {
        if (types.IsEmpty)
        {
            writer.Line(fallback);
            return;
        }

        if (types.Length > DispatchThreshold)
        {
            writer.Switch($"{lookup}({typeVariable})");
            for (var i = 0; i < types.Length; i++)
            {
                writer.Section(Case(i), $"return {arm(types[i], i)};");
            }
        }
        else
        {
            writer.Switch(typeVariable);
            for (var i = 0; i < types.Length; i++)
            {
                writer.Section($"case var _ when {typeVariable} == typeof({types[i]}):", $"return {arm(types[i], i)};");
            }
        }

        writer.Section("default:", fallback);
        writer.CloseStatement();
    }

    /// <summary>
    ///     The <c>FrozenDictionary</c> generic dispatch uses above <see cref="DispatchThreshold" />, and the non-inlined lookup into it.
    /// </summary>
    /// <param name="writer">The writer, in the mediator's body.</param>
    /// <param name="kind">"request", "stream request" or "notification", for the documentation.</param>
    /// <param name="table">The name of the static table.</param>
    /// <param name="lookup">The name of the lookup method.</param>
    /// <param name="types">The dispatched types, fully qualified, in switch order.</param>
    private static void WriteTypeTable(CodeWriter writer, string kind, string table, string lookup, ImmutableArray<string> types)
    {
        var entries = new StringBuilder(64 * types.Length + 64).Append("new global::System.Collections.Generic.KeyValuePair<global::System.Type, int>[] { ");
        for (var i = 0; i < types.Length; i++)
        {
            entries.Append(i == 0 ? "new(typeof(" : ", new(typeof(").Append(types[i]).Append("), ").Append(Digits(i)).Append(')');
        }

        entries.Append(" }");

        // A field without attributes: no blank line would separate it from another such field, but it follows a method.
        writer.Member(plainField: true);
        writer.Documentation($"<summary>Every {kind} type this host dispatches, mapped to its position in the dispatch switch.</summary>");
        writer.Line(
            $"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::System.Type, int> {table} = global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary({entries});"
        );

        writer.Member();
        writer.Documentation($"<summary>The position of a {kind} type in the dispatch switch, or -1 for a type this host does not dispatch.</summary>");
        writer.Line(CodeWriter.NoInliningAttribute);
        writer.Arrow($"private static int {lookup}(global::System.Type requestType)", $"{table}.TryGetValue(requestType, out var requestIndex) ? requestIndex : -1");
    }

    /// <summary>
    ///     One arm of generic dispatch: a class request arrives as its exact type (the switch section's type pattern, or above the threshold
    ///     the request reinterpreted with <c>Unsafe.As</c>); a struct request arrives as <c>IRequest&lt;TResponse&gt;</c> and is unboxed here,
    ///     so the dispatching method holds no struct local (<see cref="WriteRequestDispatch" />). It reinterprets the arm's
    ///     <c>ValueTask&lt;X&gt;</c> as <c>ValueTask&lt;TResponse&gt;</c>, which is the same type at run time because <c>IRequest&lt;&gt;</c>
    ///     is invariant and a request implements exactly one closing (KRX011). Never a cast through <c>object</c>, which would box the result.
    /// </summary>
    /// <remarks>
    ///     Each arm is its own method, not inlined, so the dispatching method holds no address-taken locals: with one <c>Unsafe.As(ref ...)</c>
    ///     local per arm (or an <c>out</c> lookup local) in one method, the JIT zeroes every arm's <c>ValueTask</c> in the prolog of each call
    ///     and cannot tail-call the arms, which measured about 45 ns per <c>Send</c> under Dynamic PGO whatever the arm taken (OQ-15).
    /// </remarks>
    private static void WriteDispatchArm(CodeWriter writer, HostHandlerModel request, int index)
    {
        var parameter = request.MessageIsValueType ? "global::Keryx.IRequest<TResponse>" : request.MessageType;
        var argument = request.MessageIsValueType ? Exact(request.MessageType, true, "request") : "request";
        writer.Member();
        writer.Line(CodeWriter.NoInliningAttribute);
        writer.Line($"private {ValueTask}<TResponse> {DispatchName(index)}<TResponse>({parameter} request, {TokenParameter})");
        writer.Open();
        writer.Line($"var response = {CoreName(index)}({argument}, cancellationToken);");
        writer.Line($"return {Unsafe}.As<{ValueTask}<{request.ResponseType}>, {ValueTask}<TResponse>>(ref response);");
        writer.Close();
    }

    /// <summary>A message typed as an interface, as its exact type: a class reinterpreted with <c>Unsafe.As</c>, a struct unboxed.</summary>
    private static string Exact(string type, bool isValueType, string message) =>
        isValueType ? $"({type})(object){message}" : $"{Unsafe}.As<{type}>({message})";

    /// <summary>
    ///     The pipeline of one request, without checks: the typed overload and generic dispatch both call it. Without behaviors it calls the
    ///     handler's invoker; with behaviors it calls the outermost behavior method directly, with explicit type arguments and the first link.
    /// </summary>
    private static void WriteCore(CodeWriter writer, HostPipeline pipeline, int index) =>
        WritePipelineCore(
            writer,
            pipeline,
            $"private {ValueTask}<{pipeline.Handler.ResponseType}> {CoreName(index)}({pipeline.Handler.MessageType} request, {TokenParameter})",
            ContextName(index),
            ContextField(index),
            CreateContextName(index),
            LinkName(0)
        );

    /// <summary>
    ///     The pipeline method of a request or stream request: the handler's invoker without behaviors, else the outermost behavior method
    ///     with the first link, and, when the pipeline has a context, the non-inlined method that creates it. A pipeline without a context
    ///     (<see cref="HasContext(HostPipeline)" />) reads no field: the method calls the outermost behavior method with <c>new(request)</c>.
    /// </summary>
    private static void WritePipelineCore(
        CodeWriter writer,
        HostPipeline pipeline,
        string declaration,
        string contextName,
        string contextField,
        string createName,
        string firstLink
    )
    {
        var request = pipeline.Handler;
        var hasContext = HasContext(pipeline);
        var context = ContextRead(contextField, createName);
        writer.Member();
        if (pipeline.Behaviors.IsEmpty)
        {
            var target = hasContext ? $"({context}).Handler" : request.HandlerClass;
            writer.Arrow(declaration, $"{target}.{HandlerAnalysis.InvokerName}(request, cancellationToken)");
        }
        else
        {
            var links = LinkCaptures(pipeline.Behaviors, End(pipeline));
            writer.Line(declaration);
            WriteEntry(
                writer,
                "return " + BehaviorCall(pipeline.Behaviors[0], request.ContextServices, contextName + "." + firstLink, links[0], "request") + ";",
                CallCaptures(pipeline.Behaviors[0], links[0]),
                context,
                pipeline.Behaviors[0].ObliviousFit
            );
        }

        if (hasContext)
        {
            WriteCreateContext(writer, contextName, contextField, createName);
        }
    }

    /// <summary><c>Volatile.Read(ref field) ?? Create()</c>: the pipeline context of the scope, created on first use.</summary>
    private static string ContextRead(string contextField, string createName) => $"global::System.Threading.Volatile.Read(ref {contextField}) ?? {createName}()";

    /// <summary>
    ///     The body of a pipeline method with behaviors: the call of the outermost behavior method, after <c>var pipeline = ...;</c> when
    ///     that call reads the context or passes it on; with nullable warnings disabled around the call through an oblivious fit (OQ-58).
    /// </summary>
    private static void WriteEntry(CodeWriter writer, string call, Captures captures, string context, bool obliviousFit)
    {
        writer.Open();
        if (( captures & Captures.Pipeline ) != 0)
        {
            writer.Line($"var {PipelineName} = {context};");
        }

        if (obliviousFit)
        {
            writer.Directive("#nullable disable warnings");
            writer.Line(call);
            writer.Directive("#nullable restore warnings");
        }
        else
        {
            writer.Line(call);
        }

        writer.Close();
    }

    /// <summary>
    ///     What a link or a behavior method call needs from where it is written (ADR 0001, Generated pipeline shape): the pipeline context, the
    ///     message, both or neither. A link takes exactly the parameters it reads or passes on, so it captures nothing it does not use and has
    ///     no unread primary constructor parameter (CS9113).
    /// </summary>
    [Flags]
    private enum Captures
    {
        None = 0,
        Pipeline = 1,
        Message = 2,
    }

    /// <summary>
    ///     What the end of a request or stream pipeline reads: the request, and the context when the handler is resolved into it (the
    ///     invoker is not static).
    /// </summary>
    private static Captures End(HostPipeline pipeline) => Captures.Message | ( pipeline.Handler.StaticInvoker ? Captures.None : Captures.Pipeline );

    /// <summary>
    ///     What each link reads or passes on, outermost first: the last link what the end of the pipeline reads (<paramref name="end" />),
    ///     every other link what its call of the next behavior method needs (<see cref="CallCaptures" />).
    /// </summary>
    private static Captures[] LinkCaptures(ImmutableArray<PipelineBehaviorModel> behaviors, Captures end)
    {
        var links = new Captures[behaviors.Length];
        links[^1] = end;
        for (var k = behaviors.Length - 2; k >= 0; k--)
        {
            links[k] = CallCaptures(behaviors[k + 1], links[k + 1]);
        }

        return links;
    }

    /// <summary>
    ///     What a call of <paramref name="behavior" />'s method needs: the message when the method takes it, the context when it injects a
    ///     service (read from the context's fields), and what its link <paramref name="next" /> takes, which the call constructs.
    /// </summary>
    private static Captures CallCaptures(PipelineBehaviorModel behavior, Captures next)
    {
        var captures = Captures.None;
        foreach (var role in behavior.Parameters)
        {
            captures |= role switch
            {
                BehaviorParameterRole.Message => Captures.Message,
                BehaviorParameterRole.Service => Captures.Pipeline,
                BehaviorParameterRole.Next => next,
                _ => Captures.None,
            };
        }

        return captures;
    }

    /// <summary>
    ///     The non-inlined method that creates a pipeline context on first use in a scope. Under concurrent first use every caller ends up with
    ///     the context that won the exchange (ADR 0001, Mediator).
    /// </summary>
    private static void WriteCreateContext(CodeWriter writer, string contextName, string contextField, string createName)
    {
        writer.Member();
        writer.Line(CodeWriter.NoInliningAttribute);
        writer.Arrow(
            $"private {contextName} {createName}()",
            $"global::System.Threading.Interlocked.CompareExchange(ref {contextField}, new {contextName}(__serviceProvider), null) ?? {contextField}!"
        );
    }

    /// <summary>
    ///     The pipeline context of one request: its handler (unless the invoker is static) and the injected services of its behavior methods,
    ///     resolved once per scope, and one flat link struct per behavior (ADR 0001, Generated pipeline shape). <c>Link{k}</c> is the
    ///     <c>next</c> of behavior <c>k</c>: it calls behavior <c>k + 1</c>'s method with <c>Link{k + 1}</c>, and the last link calls the
    ///     handler's invoker. A pipeline with nothing to resolve (a static invoker and no injected services) has no context: its links are
    ///     nested in a static class of the same name and take only the request.
    /// </summary>
    private static void WriteContext(CodeWriter writer, HostPipeline pipeline, int index) =>
        WritePipelineContext(
            writer,
            pipeline,
            ContextName(index),
            "pipeline",
            LinkName,
            $"global::Keryx.INext<{pipeline.Handler.ResponseType}>",
            $"{ValueTask}<{pipeline.Handler.ResponseType}>"
        );

    /// <summary>
    ///     The pipeline context class of a request or stream request, with its links; for a pipeline without a context, the static class
    ///     that holds its links.
    /// </summary>
    private static void WritePipelineContext(
        CodeWriter writer,
        HostPipeline pipeline,
        string contextName,
        string description,
        Func<int, string> linkName,
        string nextInterface,
        string returnType
    )
    {
        var request = pipeline.Handler;
        writer.Member();
        if (HasContext(pipeline))
        {
            writer.Documentation($"<summary>The {description} context of <c>{Display(request.MessageType)}</c>: {Holds(request)}, resolved once per scope.</summary>");
            writer.Line($"private sealed class {contextName}");
            writer.Open();
            var resolution = new List<string>(request.ContextServices.Count + 1);
            if (!request.StaticInvoker)
            {
                writer.Member(plainField: true);
                writer.Line($"public readonly {request.HandlerClass} Handler;");
                resolution.Add($"Handler = {ServiceProviderExtensions}.GetRequiredService<{request.HandlerClass}>({ServiceProviderName});");
            }

            WriteServiceFields(writer, request.ContextServices, resolution);
            WriteContextConstructor(writer, contextName, resolution);
        }
        else
        {
            writer.Documentation(
                $"<summary>The links of the {description} of <c>{Display(request.MessageType)}</c>, which has no context: its handler's invoker is static and its behaviors inject nothing.</summary>"
            );
            writer.Line($"private static class {contextName}");
            writer.Open();
        }

        var links = pipeline.Behaviors.IsEmpty ? [] : LinkCaptures(pipeline.Behaviors, End(pipeline));
        for (var k = 0; k < pipeline.Behaviors.Length; k++)
        {
            var isLast = k == pipeline.Behaviors.Length - 1;
            var body = !isLast
                ? BehaviorCall(pipeline.Behaviors[k + 1], request.ContextServices, linkName(k + 1), links[k + 1], "request")
                : $"{( request.StaticInvoker ? request.HandlerClass : PipelineName + ".Handler" )}.{HandlerAnalysis.InvokerName}(request, cancellationToken)";
            WriteLink(
                writer,
                linkName(k),
                contextName,
                links[k],
                request.MessageType,
                "request",
                nextInterface,
                returnType,
                body,
                !isLast && pipeline.Behaviors[k + 1].ObliviousFit,
                NextSummary(pipeline.Behaviors, k, "the handler")
            );
        }

        writer.Close();
    }

    /// <summary>The constructor of a pipeline context, which resolves what it holds from the scope.</summary>
    private static void WriteContextConstructor(CodeWriter writer, string contextName, List<string> resolution)
    {
        writer.Member();
        writer.Line($"public {contextName}({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        foreach (var statement in resolution)
        {
            writer.Line(statement);
        }

        writer.Close();
    }

    /// <summary>
    ///     One link (ADR 0001, Generated pipeline shape): a <c>readonly struct</c> with a primary constructor over what its <c>InvokeAsync</c>
    ///     reads or passes on (<paramref name="captures" />: the pipeline context, the message, both, or neither, then without a parameter
    ///     list), and an expression-bodied <c>InvokeAsync</c> that calls the next behavior method (or the handler, or the publish strategy).
    ///     A call through a nullable-oblivious fit (OQ-58) is written with nullable warnings disabled: generated code names that type without
    ///     an annotation, so the call would warn where the oblivious declaration does not.
    /// </summary>
    private static void WriteLink(
        CodeWriter writer,
        string name,
        string contextName,
        Captures captures,
        string messageType,
        string messageName,
        string nextInterface,
        string returnType,
        string body,
        bool obliviousFit,
        string summary
    )
    {
        var parameters = ( captures & ( Captures.Pipeline | Captures.Message ) ) switch
        {
            Captures.Pipeline | Captures.Message => $"({contextName} {PipelineName}, {messageType} {messageName})",
            Captures.Pipeline => $"({contextName} {PipelineName})",
            Captures.Message => $"({messageType} {messageName})",
            _ => "",
        };

        writer.Member();
        writer.Documentation(summary);
        writer.Line($"public readonly struct {name}{parameters} : {nextInterface}");
        writer.Open();
        writer.Member();
        var invoke = $"public {returnType} InvokeAsync({TokenParameter})";
        if (obliviousFit)
        {
            writer.NullableObliviousArrow(invoke, body);
            writer.Directive("#nullable restore warnings");
        }
        else
        {
            writer.Arrow(invoke, body);
        }

        writer.Close();
    }

    /// <summary>The one-line summary of link <paramref name="position" />: whose <c>next</c> it is, and where it goes.</summary>
    private static string NextSummary(ImmutableArray<PipelineBehaviorModel> behaviors, int position, string end) =>
        position == behaviors.Length - 1
            ? $"<summary>The <c>next</c> of <c>{Display(behaviors[position].Identity)}</c>: {end}.</summary>"
            : $"<summary>The <c>next</c> of <c>{Display(behaviors[position].Identity)}</c>: <c>{Display(behaviors[position + 1].Identity)}</c>.</summary>";

    /// <summary>
    ///     The direct call of a behavior method (ADR 0001, Behavior methods): the class's static method with explicit type arguments
    ///     (<paramref name="link" /> in place of <c>TNext</c>) and the arguments in declared order: the message, the link as a target-typed
    ///     <c>new(...)</c> over what it takes (<c>new(pipeline, message)</c>, <c>new(message)</c>, <c>new(pipeline)</c> or <c>new()</c>), the
    ///     call's token, and its injected services read from the context's shared fields.
    /// </summary>
    /// <param name="behavior">The behavior.</param>
    /// <param name="services">The pipeline context's services, which the behavior's <see cref="PipelineBehaviorModel.ServiceSlots" /> index.</param>
    /// <param name="link">The link type its <c>next</c> is, qualified as the call site needs it.</param>
    /// <param name="linkCaptures">What that link takes (<see cref="LinkCaptures" />).</param>
    /// <param name="message">The name of the request or notification.</param>
    private static string BehaviorCall(
        PipelineBehaviorModel behavior,
        EquatableArray<ContextServiceModel> services,
        string link,
        Captures linkCaptures,
        string message
    )
    {
        var call = new StringBuilder(256).Append(behavior.Class).Append('.').Append(behavior.Method).Append('<');
        for (var i = 0; i < behavior.TypeArguments.Count; i++)
        {
            var argument = behavior.TypeArguments[i];
            call.Append(i == 0 ? "" : ", ").Append(argument.Length == 0 ? link : argument);
        }

        call.Append(">(");
        var service = 0;
        for (var i = 0; i < behavior.Parameters.Count; i++)
        {
            call.Append(i == 0 ? "" : ", ");
            switch (behavior.Parameters[i])
            {
                case BehaviorParameterRole.Service:
                    var slot = behavior.ServiceSlots[service];
                    call.Append(ServiceArgument(services[slot], slot, behavior.Services[service]));
                    service++;
                    break;
                case BehaviorParameterRole.Message:
                    call.Append(message);
                    break;
                case BehaviorParameterRole.Next:
                    call.Append(
                        ( linkCaptures & ( Captures.Pipeline | Captures.Message ) ) switch
                        {
                            Captures.Pipeline | Captures.Message => $"new({PipelineName}, {message})",
                            Captures.Pipeline => $"new({PipelineName})",
                            Captures.Message => $"new({message})",
                            _ => "new()",
                        }
                    );
                    break;
                case BehaviorParameterRole.CancellationToken:
                    call.Append("cancellationToken");
                    break;
                default:
                    call.Append("default");
                    break;
            }
        }

        return call.Append(')').ToString();
    }

    /// <summary>
    ///     What one injected parameter of a behavior method receives: the context's field for its service. A service resolved as required
    ///     is passed as is to every consumer; a service whose consumers are all optional is held in a nullable field (<see langword="null" />
    ///     when missing), and each call site applies its own parameter's default with <c>??</c> (<c>GetValueOrDefault()</c> for a
    ///     <c>default</c> value type).
    /// </summary>
    /// <param name="service">The parameter's service.</param>
    /// <param name="slot">Its index in the context.</param>
    /// <param name="parameter">The parameter.</param>
    private static string ServiceArgument(ContextServiceModel service, int slot, ServiceParameterModel parameter)
    {
        var field = PipelineName + "." + ServiceField(slot);
        return service.Required || parameter.DefaultValue is not { } defaultValue || defaultValue == "null"
            ? field
            : service.IsValueType && defaultValue == "default"
            ? field + ".GetValueOrDefault()"
            : $"{field} ?? {defaultValue}";
    }

    /// <summary>What a pipeline context holds, for its documentation (a pipeline that would hold nothing has no context).</summary>
    private static string Holds(HostHandlerModel handler) =>
        handler.StaticInvoker ? "the injected services of its behaviors"
        : handler.ContextServices.Count > 0 ? "its handler and the injected services of its behaviors"
        : "its handler";

    /// <summary>
    ///     One field per service the pipeline context shares (ADR 0001, Injected parameters), in order of first use: the service type for a
    ///     service resolved as required, the nullable service type for one whose consumers are all optional (<see langword="null" /> when the
    ///     service is missing; each call site applies its own default). Adds the statement that resolves each from the scope, once, to
    ///     <paramref name="resolution" />.
    /// </summary>
    private static void WriteServiceFields(CodeWriter writer, EquatableArray<ContextServiceModel> services, List<string> resolution)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var service = services[i];
            writer.Member(plainField: true);
            writer.Line(
                $"public readonly {( service.Required || service.NullableUnderlyingType is not null ? service.ServiceType : service.ServiceType + "?" )} {ServiceField(i)};"
            );
            resolution.Add($"{ServiceField(i)} = {ResolveShared(service, i)};");
        }
    }

    /// <summary>
    ///     The resolution of a shared service: as required (<see cref="Resolve" /> without a default) when any consumer is required;
    ///     otherwise <c>GetService</c>/<c>GetKeyedService</c>, <see langword="null" /> for a missing service (a value type is matched with
    ///     the type pattern of <see cref="Resolve" />, once, into a <c>Nullable&lt;T&gt;</c> field).
    /// </summary>
    private static string ResolveShared(ContextServiceModel service, int slot)
    {
        if (service.Required || !service.IsValueType)
        {
            return Resolve(
                new(
                    service.ServiceType,
                    service.ServiceType,
                    service.ServiceKey,
                    service.Required ? null : "null",
                    service.IsValueType,
                    service.NullableUnderlyingType,
                    service.IsServiceProvider
                ),
                slot
            );
        }

        var variable = "__value" + Digits(slot);
        var nullable = service.NullableUnderlyingType is null ? service.ServiceType + "?" : service.ServiceType;
        return $"{ServiceLookup(service.ServiceType, service.ServiceKey)} is {service.NullableUnderlyingType ?? service.ServiceType} {variable} ? ({nullable}){variable} : null";
    }

    /// <summary>
    ///     <c>serviceProvider.GetService(typeof(T))</c>, or for a keyed service <c>(serviceProvider as IKeyedServiceProvider)?.GetKeyedService(typeof(T), key)</c>.
    ///     <c>GetKeyedService(Type, object)</c> is an extension only from Microsoft.Extensions.DependencyInjection.Abstractions 9.0; the interface
    ///     method exists from 8.0, the floor for keyed services (ADR 0001, Injected parameters).
    /// </summary>
    private static string ServiceLookup(string type, string? key) =>
        key is null
            ? $"{ServiceProviderName}.GetService(typeof({type}))"
            : $"({ServiceProviderName} as {KeyedServiceProvider})?.GetKeyedService(typeof({type}), {key})";

    /// <summary>
    ///     The resolution of one injected parameter (ADR 0001, Injected parameters). No reflection: optional value types are matched with a
    ///     type pattern on <c>GetService(typeof(T))</c>, which neither allocates nor activates anything.
    /// </summary>
    internal static string Resolve(ServiceParameterModel parameter, int index)
    {
        if (parameter is { ServiceKey: null, IsServiceProvider: true })
        {
            return ServiceProviderName;
        }

        var key = parameter.ServiceKey;
        if (parameter.DefaultValue is not { } defaultValue)
        {
            // Nullable<T> does not satisfy the notnull constraint of GetRequiredService<T>, so it is resolved by type and unboxed.
            return parameter.NullableUnderlyingType is not null
                ? key is null
                    ? $"({parameter.Type}){ServiceProviderExtensions}.GetRequiredService({ServiceProviderName}, typeof({parameter.Type}))"
                    : $"({parameter.Type}){KeyedServiceProviderExtensions}.GetRequiredKeyedService({ServiceProviderName}, typeof({parameter.Type}), {key})"
                : key is null
                ? $"{ServiceProviderExtensions}.GetRequiredService<{parameter.ServiceType}>({ServiceProviderName})"
                : $"{KeyedServiceProviderExtensions}.GetRequiredKeyedService<{parameter.ServiceType}>({ServiceProviderName}, {key})";
        }

        if (!parameter.IsValueType)
        {
            // GetService<T> already returns null for a missing service; any other default (including the null! of a parameter that is not
            // annotated) is applied with ??.
            var lookup = key is null
                ? $"{ServiceProviderExtensions}.GetService<{parameter.ServiceType}>({ServiceProviderName})"
                : $"{KeyedServiceProviderExtensions}.GetKeyedService<{parameter.ServiceType}>({ServiceProviderName}, {key})";
            return defaultValue == "null" ? lookup : $"{lookup} ?? {defaultValue}";
        }

        var variable = "__value" + Digits(index);
        return parameter.NullableUnderlyingType is { } underlying
            ? $"{ServiceLookup(parameter.Type, key)} is {underlying} {variable} ? ({parameter.Type}){variable} : {defaultValue}"
            : $"{ServiceLookup(parameter.Type, key)} is {parameter.Type} {variable} ? {variable} : {defaultValue}";
    }

    /// <summary>
    ///     Whether a request or stream pipeline has a context (created once per scope): it resolves something, its handler (the invoker is
    ///     not static) or an injected service of its behaviors (ADR 0001, Generated pipeline shape). A pipeline with nothing to resolve has
    ///     none: without behaviors it calls the static invoker directly, with behaviors it calls the outermost behavior method directly and
    ///     its links, nested in a static class, take only the request.
    /// </summary>
    private static bool HasContext(HostPipeline pipeline) => !pipeline.Handler.StaticInvoker || pipeline.Handler.ContextServices.Count > 0;

    /// <summary>The label of the switch section of jump-table position <paramref name="position" />.</summary>
    private static string Case(int position) => "case " + Digits(position) + ":";

    /// <summary>The context field of the shared service at <paramref name="slot" />.</summary>
    private static string ServiceField(int slot) => "Service" + Digits(slot);

    private static string LinkName(int position) => "Link" + Digits(position + 1);

    private static string ContextName(int index) => "__Pipeline" + Digits(index);

    private static string ContextField(int index) => "__pipeline" + Digits(index);

    private static string CreateContextName(int index) => "__CreatePipeline" + Digits(index);

    private static string CoreName(int index) => "__Send" + Digits(index);

    private static string DispatchName(int index) => "__Dispatch" + Digits(index);

    private static string Digits(int value) => value.ToString(CultureInfo.InvariantCulture);

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
