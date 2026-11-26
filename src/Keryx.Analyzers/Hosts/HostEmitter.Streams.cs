using System.Collections.Immutable;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Stream dispatch and stream pipelines (ADR 0001, Streaming): typed <c>Stream</c> overloads, generic <c>Stream&lt;TItem&gt;</c> with
///     exact-type dispatch, and per stream request a pipeline context holding the handler and the behaviors, one flat
///     <c>IStreamNext&lt;TItem&gt;</c> link struct per wrap behavior, and one fused hook segment iterator per run of adjacent hook behaviors.
/// </summary>
internal static partial class HostEmitter
{
    private const string AsyncEnumerable = "global::System.Collections.Generic.IAsyncEnumerable";
    private const string AsyncEnumerator = "global::System.Collections.Generic.IAsyncEnumerator";
    private const string EnumeratorCancellation = "[global::System.Runtime.CompilerServices.EnumeratorCancellation]";

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
    ///     returns the handler's enumerable as is; with behaviors it calls the outermost element (a wrap behavior's <c>Handle</c> on its
    ///     concrete type, or the first fused hook segment) directly, so a wrap behavior that is not an iterator runs at the <c>Stream</c>
    ///     call.
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
            writer.Line($"return {InvokeElement(pipeline, StreamElements(pipeline.Behaviors), 0, "pipeline", "request", StreamContextName(index) + ".")};");
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
    ///     The pipeline context of one stream request: its handler (unless the invoker is static) and its behaviors, wrap and hook, in pipeline
    ///     order, resolved once per scope; one flat link struct per wrap behavior (<c>StreamLink{k}</c>, the <c>next</c> of behavior <c>k</c>);
    ///     and one fused hook segment iterator per run of adjacent hook behaviors.
    /// </summary>
    private static void WriteStreamContext(CodeWriter writer, HostPipeline pipeline, int index)
    {
        var request = pipeline.Handler;
        var behaviors = pipeline.Behaviors;
        var elements = StreamElements(behaviors);
        var holds = request.StaticInvoker ? "its behaviors" : behaviors.IsEmpty ? "its handler" : "its handler and behaviors";
        writer.Line($"/// <summary>The stream pipeline context of <c>{Display(request.MessageType)}</c>: {holds}, resolved once per scope.</summary>");
        writer.Line($"private sealed class {StreamContextName(index)}");
        writer.Open();
        if (!request.StaticInvoker)
        {
            writer.Line($"public readonly {request.HandlerClass} Handler;");
        }

        for (var k = 0; k < behaviors.Length; k++)
        {
            writer.Line($"public readonly {behaviors[k].Type} {BehaviorField(k)};");
        }

        writer.Line();
        writer.Line($"public {StreamContextName(index)}({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        if (!request.StaticInvoker)
        {
            writer.Line($"Handler = {ServiceProviderExtensions}.GetRequiredService<{request.HandlerClass}>({ServiceProviderName});");
        }

        for (var k = 0; k < behaviors.Length; k++)
        {
            writer.Line($"{BehaviorField(k)} = {ServiceProviderExtensions}.GetRequiredService<{behaviors[k].Type}>({ServiceProviderName});");
        }

        writer.Close();

        for (var e = 0; e < elements.Length; e++)
        {
            writer.Line();
            if (elements[e].IsSegment)
            {
                WriteSegment(writer, pipeline, elements, e, index);
            }
            else
            {
                WriteStreamLink(writer, pipeline, elements, e, index);
            }
        }

        writer.Close();
    }

    /// <summary>
    ///     The <c>next</c> of one wrap behavior: <c>InvokeAsync</c> calls the next element (a wrap behavior's <c>Handle</c> on its concrete
    ///     type, a fused hook segment, or the handler's invoker). Holds the context and the request; the token is an argument, never stored.
    /// </summary>
    private static void WriteStreamLink(CodeWriter writer, HostPipeline pipeline, ImmutableArray<StreamElement> elements, int element, int index)
    {
        var request = pipeline.Handler;
        var context = StreamContextName(index);
        var position = elements[element].First;
        var link = StreamLinkName(position);
        var isLast = element == elements.Length - 1;
        writer.Line($"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: {Describe(pipeline, elements, element + 1)}.</summary>");
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
        writer.Line($"    {InvokeElement(pipeline, elements, element + 1, "_pipeline", "_request", "")};");
        writer.Close();
    }

    /// <summary>
    ///     One fused hook segment (ADR 0001, Streaming, Hook semantics): one async iterator that runs every hook behavior of a run of adjacent
    ///     hook behaviors. <c>OnStartAsync</c> runs outer to inner on the first <c>MoveNextAsync</c>, then the segment gets its inner stream;
    ///     <c>OnItem</c> runs inner to outer and <c>false</c> drops the item; when the segment ends, the inner enumerator is disposed and every
    ///     hook whose <c>OnStartAsync</c> completed gets <c>OnCompleteAsync</c> (normal completion or early dispose) or <c>OnErrorAsync</c>
    ///     (an exception from the guarded region), inner to outer. The guarded region covers <c>OnStartAsync</c>, getting the inner stream and
    ///     its enumerator, each <c>MoveNextAsync</c>, <c>Current</c>, <c>OnItem</c> and the final dispose of the inner enumerator; only
    ///     <c>yield return</c> stays outside it (CS1626). The terminal calls nest like <c>using</c> statements, so each started hook gets its
    ///     terminal call even when an inner one throws (OQ-38). Every await uses <c>ConfigureAwait(false)</c> (OQ-39). The segment puts <c>[EnumeratorCancellation]</c> on its token and passes that
    ///     token to the hooks and to the inner stream, so when it is outermost the compiler links the <c>Stream</c> token with the
    ///     consumer's, once, and only when both can be cancelled and differ.
    /// </summary>
    private static void WriteSegment(CodeWriter writer, HostPipeline pipeline, ImmutableArray<StreamElement> elements, int element, int index)
    {
        var request = pipeline.Handler;
        var item = request.ResponseType;
        var segment = elements[element];
        var hooks = segment.Last - segment.First + 1;
        var names = string.Join(", ", Enumerable.Range(segment.First, hooks).Select(k => $"<c>{Display(pipeline.Behaviors[k].Identity)}</c>"));
        writer.Line(
            $"/// <summary>The fused hook segment <c>{SegmentName(elements, element)}</c>: {names} (outer to inner), over {Describe(pipeline, elements, element + 1)}.</summary>"
        );
        writer.Line(
            $"public async {AsyncEnumerable}<{item}> {SegmentName(elements, element)}({request.MessageType} request, {EnumeratorCancellation} {CancellationToken} cancellationToken)"
        );
        writer.Open();
        writer.Line("// The number of hooks, outermost first, whose OnStartAsync completed: only they get OnCompleteAsync or OnErrorAsync.");
        writer.Line("var started = 0;");
        writer.Line("global::System.Exception? error = null;");
        writer.Line($"{AsyncEnumerator}<{item}>? items = null;");
        writer.Line("try");
        writer.Open();
        writer.Line("try");
        writer.Open();
        for (var h = 0; h < hooks; h++)
        {
            writer.Line($"await {BehaviorField(segment.First + h)}.OnStartAsync(request, cancellationToken).ConfigureAwait(false);");
            writer.Line($"started = {Number(h + 1)};");
        }

        writer.Line($"items = {InvokeElement(pipeline, elements, element + 1, "this", "request", "")}.GetAsyncEnumerator(cancellationToken);");
        writer.Close();
        WriteRecordError(writer);
        writer.Line();
        writer.Line("while (true)");
        writer.Open();
        writer.Line($"{item} item;");
        writer.Line("try");
        writer.Open();
        writer.Line("if (!await items.MoveNextAsync().ConfigureAwait(false))");
        writer.Open();
        writer.Line("var finished = items;");
        writer.Line("items = null;");
        writer.Line("await finished.DisposeAsync().ConfigureAwait(false);");
        writer.Line("break;");
        writer.Close();
        writer.Line();
        writer.Line("item = items.Current;");
        // Inner to outer; the first hook that returns false drops the item, so the outer hooks never see it.
        var onItem = string.Join(" || ", Enumerable.Range(0, hooks).Reverse().Select(h => $"!{BehaviorField(segment.First + h)}.OnItem(request, ref item)"));
        writer.Line($"if ({onItem})");
        writer.Open();
        writer.Line("continue;");
        writer.Close();
        writer.Close();
        WriteRecordError(writer);
        writer.Line();
        writer.Line("yield return item;");
        writer.Close();
        writer.Close();
        writer.Line("finally");
        writer.Open();
        writer.Line("try");
        writer.Open();
        writer.Line("if (items is not null)");
        writer.Open();
        writer.Line("await items.DisposeAsync().ConfigureAwait(false);");
        writer.Close();
        writer.Close();
        writer.Line("finally");
        writer.Open();
        WriteTerminal(writer, segment, hooks - 1);
        writer.Close();
        writer.Close();
        writer.Close();
    }

    private static void WriteRecordError(CodeWriter writer)
    {
        writer.Line("catch (global::System.Exception exception)");
        writer.Open();
        writer.Line("error = exception;");
        writer.Line("throw;");
        writer.Close();
    }

    /// <summary>
    ///     The terminal call of hook <paramref name="hook" /> (0 is the segment's outermost) and, nested in its <c>finally</c>, of every hook
    ///     outside it: inner to outer, each only when its <c>OnStartAsync</c> completed.
    /// </summary>
    private static void WriteTerminal(CodeWriter writer, StreamElement segment, int hook)
    {
        var field = BehaviorField(segment.First + hook);
        var call =
            $"await (error is null ? {field}.OnCompleteAsync(request) : {field}.OnErrorAsync(request, error)).ConfigureAwait(false);";
        if (hook == 0)
        {
            writer.Line("if (started > 0)");
            writer.Open();
            writer.Line(call);
            writer.Close();
            return;
        }

        writer.Line("try");
        writer.Open();
        writer.Line($"if (started > {Number(hook)})");
        writer.Open();
        writer.Line(call);
        writer.Close();
        writer.Close();
        writer.Line("finally");
        writer.Open();
        WriteTerminal(writer, segment, hook - 1);
        writer.Close();
    }

    /// <summary>
    ///     The expression that gets the stream of element <paramref name="element" /> (the handler when it is past the last element).
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="elements">Its elements.</param>
    /// <param name="element">The element to call.</param>
    /// <param name="context">The pipeline context expression (<c>this</c> inside the context).</param>
    /// <param name="request">The request expression.</param>
    /// <param name="linkPrefix">What qualifies a link type name outside the context (<c>__StreamPipeline0.</c>), or nothing inside it.</param>
    private static string InvokeElement(HostPipeline pipeline, ImmutableArray<StreamElement> elements, int element, string context, string request, string linkPrefix)
    {
        if (element == elements.Length)
        {
            var handler = pipeline.Handler;
            return handler.StaticInvoker
                ? $"{handler.HandlerClass}.{HandlerAnalysis.InvokerName}({request}, cancellationToken)"
                : $"{context}.Handler.{HandlerAnalysis.InvokerName}({request}, cancellationToken)";
        }

        var target = elements[element];
        if (target.IsSegment)
        {
            var segment = SegmentName(elements, element);
            return context == "this" ? $"{segment}({request}, cancellationToken)" : $"{context}.{segment}({request}, cancellationToken)";
        }

        var behavior = context == "this" ? BehaviorField(target.First) : $"{context}.{BehaviorField(target.First)}";
        return $"{behavior}.Handle<{linkPrefix}{StreamLinkName(target.First)}>({request}, new({context}, {request}), cancellationToken)";
    }

    /// <summary>What element <paramref name="element" /> is, for documentation.</summary>
    private static string Describe(HostPipeline pipeline, ImmutableArray<StreamElement> elements, int element) =>
        element == elements.Length
            ? "the handler"
            : elements[element].IsSegment
                ? $"the fused hook segment <c>{SegmentName(elements, element)}</c>"
                : $"<c>{Display(pipeline.Behaviors[elements[element].First].Identity)}</c>";

    /// <summary>
    ///     Splits a sorted stream pipeline into its elements, outermost first: each wrap behavior on its own, and each run of adjacent hook
    ///     behaviors as one fused hook segment (ADR 0001, Streaming).
    /// </summary>
    internal static ImmutableArray<StreamElement> StreamElements(ImmutableArray<PipelineBehaviorModel> behaviors)
    {
        var elements = ImmutableArray.CreateBuilder<StreamElement>();
        for (var k = 0; k < behaviors.Length; k++)
        {
            if (behaviors[k].Kind != BehaviorKind.StreamHook)
            {
                elements.Add(new(false, k, k));
                continue;
            }

            var first = k;
            while (k + 1 < behaviors.Length && behaviors[k + 1].Kind == BehaviorKind.StreamHook)
            {
                k++;
            }

            elements.Add(new(true, first, k));
        }

        return elements.ToImmutable();
    }

    /// <summary>The segment iterator's name: <c>Segment{n}</c>, numbered from 1 in pipeline order.</summary>
    private static string SegmentName(ImmutableArray<StreamElement> elements, int element)
    {
        var number = 0;
        for (var e = 0; e <= element; e++)
        {
            if (elements[e].IsSegment)
            {
                number++;
            }
        }

        return "Segment" + Number(number);
    }

    private static string StreamLinkName(int position) => "StreamLink" + Number(position + 1);

    private static string StreamContextName(int index) => "__StreamPipeline" + Number(index);

    private static string StreamContextField(int index) => "__streamPipeline" + Number(index);

    private static string CreateStreamContextName(int index) => "__CreateStreamPipeline" + Number(index);

    private static string StreamCoreName(int index) => "__Stream" + Number(index);
}

/// <summary>
///     One element of a stream pipeline: a wrap behavior, or a fused hook segment of adjacent hook behaviors.
/// </summary>
/// <param name="IsSegment">Whether it is a fused hook segment.</param>
/// <param name="First">The position of its first (outermost) behavior in the sorted pipeline.</param>
/// <param name="Last">The position of its last (innermost) behavior; equal to <paramref name="First" /> for a wrap behavior.</param>
internal readonly record struct StreamElement(bool IsSegment, int First, int Last);
