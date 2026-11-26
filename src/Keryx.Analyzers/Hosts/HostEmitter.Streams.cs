using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Stream dispatch and stream pipelines (ADR 0001, Streaming): typed <c>Stream</c> overloads, generic <c>Stream&lt;TItem&gt;</c> with
///     exact-type dispatch, and per stream request a pipeline context holding the handler and the injected services of its stream behaviors,
///     and one flat <c>IStreamNext&lt;TItem&gt;</c> link struct per stream behavior.
/// </summary>
internal static partial class HostEmitter
{
    private const string AsyncEnumerable = "global::System.Collections.Generic.IAsyncEnumerable";

    private static void WriteTypedStream(CodeWriter writer, HostHandlerModel request, int index)
    {
        writer.Line("/// <summary>");
        writer.Line(
            $"///     Streams a <c>{Display(request.MessageType)}</c> through its pipeline from its handler, without type dispatch{( request.MessageIsValueType ? " and without boxing it" : "" )}."
        );
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"request\">The stream request.</param>");
        writer.Line("/// <param name=\"cancellationToken\">The token passed to the outermost stream behavior, or to the handler when there is none.</param>");
        writer.Line("/// <returns>The items the handler produces, after every stream behavior has seen them.</returns>");
        writer.Line($"public {AsyncEnumerable}<{request.ResponseType}> Stream({request.MessageType} request, {CancellationToken} cancellationToken = default)");
        writer.Open();
        if (!request.MessageIsValueType)
        {
            writer.Line($"{ThrowIfNull}(request);");
        }

        if (!request.MessageIsSealed)
        {
            // Dispatch is by exact runtime type: a derived stream request statically typed as this one goes to its own handler (OQ-31).
            writer.Line($"if (request.GetType() != typeof({request.MessageType}))");
            writer.Open();
            writer.Line($"return Stream<{request.ResponseType}>((global::Keryx.IStreamRequest<{request.ResponseType}>)request, cancellationToken);");
            writer.Close();
            writer.Line();
        }

        writer.Line($"return {StreamCoreName(index)}(request, cancellationToken);");
        writer.Close();
    }

    /// <summary>
    ///     Generic <c>Stream</c>: validates the request and dispatches on its exact runtime type synchronously, before any behavior runs (ADR
    ///     0001, Streaming), then calls the outermost element of the pipeline directly. The result is a reference type, so reinterpreting
    ///     <c>IAsyncEnumerable&lt;X&gt;</c> as <c>IAsyncEnumerable&lt;TItem&gt;</c> (the same type at run time, because
    ///     <c>IStreamRequest&lt;&gt;</c> is invariant and a stream request implements exactly one closing) needs only a reference cast.
    /// </summary>
    private static void WriteGenericStream(CodeWriter writer, ImmutableArray<HostHandlerModel> streams)
    {
        writer.Line("/// <inheritdoc />");
        writer.Line(
            $"public {AsyncEnumerable}<TItem> Stream<TItem>(global::Keryx.IStreamRequest<TItem> request, {CancellationToken} cancellationToken = default)"
        );
        writer.Open();
        writer.Line($"{ThrowIfNull}(request);");
        writer.Line("var requestType = request.GetType();");
        if (streams.Length > DispatchThreshold)
        {
            writer.Line("switch (__StreamRequestIndex(requestType))");
            writer.Open();
            for (var i = 0; i < streams.Length; i++)
            {
                writer.Line($"case {Number(i)}:");
                writer.Line($"    return {StreamArm(streams[i], i)};");
            }

            writer.Line("default:");
            writer.Line("    throw __HandlerNotFound(requestType);");
            writer.Close();
        }
        else
        {
            for (var i = 0; i < streams.Length; i++)
            {
                writer.Line($"if (requestType == typeof({streams[i].MessageType}))");
                writer.Open();
                writer.Line($"return {StreamArm(streams[i], i)};");
                writer.Close();
                writer.Line();
            }

            writer.Line("throw __HandlerNotFound(requestType);");
        }

        writer.Close();

        if (streams.Length > DispatchThreshold)
        {
            writer.Line();
            WriteTypeTable(writer, "stream request", "__streamRequestTypes", "__StreamRequestIndex", streams);
        }
    }

    private static string StreamArm(HostHandlerModel stream, int index)
    {
        var argument = stream.MessageIsValueType ? $"({stream.MessageType})(object)request" : $"{Unsafe}.As<{stream.MessageType}>(request)";
        return $"{Unsafe}.As<{AsyncEnumerable}<TItem>>({StreamCoreName(index)}({argument}, cancellationToken))";
    }

    /// <summary>
    ///     The pipeline of one stream request, without checks: the typed overload and generic dispatch both call it. Without behaviors it
    ///     returns the handler's enumerable as is; with behaviors it calls the outermost stream behavior method directly, so one that is not an
    ///     iterator runs at the <c>Stream</c> call.
    /// </summary>
    private static void WriteStreamCore(CodeWriter writer, HostPipeline pipeline, int index)
    {
        var request = pipeline.Handler;
        var context = $"global::System.Threading.Volatile.Read(ref {StreamContextField(index)}) ?? {CreateStreamContextName(index)}()";
        var signature = $"private {AsyncEnumerable}<{request.ResponseType}> {StreamCoreName(index)}({request.MessageType} request, {CancellationToken} cancellationToken)";
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
            writer.Line($"return {BehaviorCall(pipeline.Behaviors[0], 0, StreamContextName(index) + "." + StreamLinkName(0), "pipeline", "request")};");
            writer.Close();
        }

        if (!NeedsContext(pipeline))
        {
            return;
        }

        writer.Line();
        writer.Line(NoInlining);
        writer.Line($"private {StreamContextName(index)} {CreateStreamContextName(index)}() =>");
        writer.Line(
            $"    global::System.Threading.Interlocked.CompareExchange(ref {StreamContextField(index)}, new {StreamContextName(index)}(__serviceProvider), null) ?? {StreamContextField(index)}!;"
        );
    }

    /// <summary>
    ///     The pipeline context of one stream request: its handler (unless the invoker is static) and the injected services of its stream
    ///     behaviors, resolved once per scope, and one flat link struct per stream behavior (<c>StreamLink{k}</c>, the <c>next</c> of behavior
    ///     <c>k</c>).
    /// </summary>
    private static void WriteStreamContext(CodeWriter writer, HostPipeline pipeline, int index)
    {
        var request = pipeline.Handler;
        var behaviors = pipeline.Behaviors;
        writer.Line($"/// <summary>The stream pipeline context of <c>{Display(request.MessageType)}</c>: {Holds(request, behaviors)}, resolved once per scope.</summary>");
        writer.Line($"private sealed class {StreamContextName(index)}");
        writer.Open();
        if (!request.StaticInvoker)
        {
            writer.Line($"public readonly {request.HandlerClass} Handler;");
        }

        WriteServiceFields(writer, behaviors);
        writer.Line();
        writer.Line($"public {StreamContextName(index)}({ServiceProvider} {ServiceProviderName})");
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
            WriteStreamLink(writer, pipeline, k, index);
        }

        writer.Close();
    }

    /// <summary>
    ///     The <c>next</c> of one stream behavior: <c>InvokeAsync</c> calls the next stream behavior method directly, or the handler's invoker.
    ///     Holds the context and the request; the token is an argument, never stored.
    /// </summary>
    private static void WriteStreamLink(CodeWriter writer, HostPipeline pipeline, int position, int index)
    {
        var request = pipeline.Handler;
        var context = StreamContextName(index);
        var link = StreamLinkName(position);
        var isLast = position == pipeline.Behaviors.Length - 1;
        writer.Line(
            isLast
                ? $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: the handler.</summary>"
                : $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: <c>{Display(pipeline.Behaviors[position + 1].Identity)}</c>.</summary>"
        );
        // The last link of a pipeline whose handler has a static invoker needs only the request.
        var holdsContext = !isLast || !request.StaticInvoker;
        writer.Line($"public readonly struct {link} : global::Keryx.IStreamNext<{request.ResponseType}>");
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
        writer.Line($"public {AsyncEnumerable}<{request.ResponseType}> InvokeAsync({CancellationToken} cancellationToken) =>");
        if (!isLast)
        {
            writer.Line($"    {BehaviorCall(pipeline.Behaviors[position + 1], position + 1, StreamLinkName(position + 1), "_pipeline", "_request")};");
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

    private static string StreamLinkName(int position) => "StreamLink" + Number(position + 1);

    private static string StreamContextName(int index) => "__StreamPipeline" + Number(index);

    private static string StreamContextField(int index) => "__streamPipeline" + Number(index);

    private static string CreateStreamContextName(int index) => "__CreateStreamPipeline" + Number(index);

    private static string StreamCoreName(int index) => "__Stream" + Number(index);
}
