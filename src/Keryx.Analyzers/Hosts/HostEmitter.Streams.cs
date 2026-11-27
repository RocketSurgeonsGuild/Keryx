using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Keryx.Analyzers.GeneratedSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Stream dispatch and stream pipelines (ADR 0001, Streaming): typed <c>Stream</c> overloads, generic <c>Stream&lt;TItem&gt;</c> with
///     type-pattern dispatch on the sealed stream request types, and per stream request a pipeline context holding the handler and the injected services of its stream behaviors
///     (none when it would hold nothing), and one flat <c>IStreamNext&lt;TItem&gt;</c> link struct per stream behavior.
/// </summary>
internal static partial class HostEmitter
{
    private const string AsyncEnumerable = "global::System.Collections.Generic.IAsyncEnumerable";

    private static MethodDeclarationSyntax TypedStream(HostHandlerModel request, int index)
    {
        var statements = new List<StatementSyntax>();
        if (!request.MessageIsValueType)
        {
            statements.Add(Call(Qualified(ThrowIfNull), Id("request")));
        }

        statements.Add(Return(Invoke(Id(StreamCoreName(index)), Id("request"), Id(CancellationTokenName))));
        return Method(
                   Modifiers(SyntaxKind.PublicKeyword),
                   TypeName($"{AsyncEnumerable}<{request.ResponseType}>"),
                   "Stream",
                   Params(Param(TypeName(request.MessageType), "request"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
               )
              .Statements(MemberDepth, statements)
              .Documented(
                   "<summary>",
                   $"    Streams a <c>{Display(request.MessageType)}</c> through its pipeline from its handler, without type dispatch{( request.MessageIsValueType ? " and without boxing it" : "" )}.",
                   "</summary>",
                   "<param name=\"request\">The stream request.</param>",
                   "<param name=\"cancellationToken\">The token passed to the outermost stream behavior, or to the handler when there is none.</param>",
                   "<returns>The items the handler produces, after every stream behavior has seen them.</returns>"
               );
    }

    /// <summary>
    ///     Generic <c>Stream</c>: validates the request and dispatches on its runtime type synchronously, before any behavior runs (ADR 0001,
    ///     Streaming), with a type pattern per sealed stream request type (a jump table above <see cref="DispatchThreshold" />), then calls the
    ///     outermost element of the pipeline directly. The result is a reference type, so reinterpreting
    ///     <c>IAsyncEnumerable&lt;X&gt;</c> as <c>IAsyncEnumerable&lt;TItem&gt;</c> (the same type at run time, because
    ///     <c>IStreamRequest&lt;&gt;</c> is invariant and a stream request implements exactly one closing) needs only a reference cast.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> GenericStream(ImmutableArray<HostHandlerModel> streams)
    {
        yield return Method(
                         Modifiers(SyntaxKind.PublicKeyword),
                         TypeName($"{AsyncEnumerable}<TItem>"),
                         "Stream",
                         Params(Param(TypeName("global::Keryx.IStreamRequest<TItem>"), "request"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
                     )
                    .WithTypeParameterList(TypeParameters("TItem"))
                    .Statements(
                         MemberDepth,
                         [
                             Call(Qualified(ThrowIfNull), Id("request")),
                             .. RequestDispatch(streams, "__StreamRequestIndex", static (stream, i, request) => StreamArm(stream, i, request)),
                         ]
                     )
                    .Documented("<inheritdoc />");

        if (streams.Length > DispatchThreshold)
        {
            foreach (var member in TypeTable("stream request", "__streamRequestTypes", "__StreamRequestIndex", streams.Select(static h => h.MessageType).ToImmutableArray()))
            {
                yield return member;
            }
        }
    }

    /// <summary>
    ///     The result of one section of generic <c>Stream</c>, given the request as its exact type (a class) or as the interface-typed
    ///     <c>request</c> (a struct, unboxed here as the argument, never into a local of the dispatching method; <see cref="RequestDispatch" />).
    /// </summary>
    private static ExpressionSyntax StreamArm(HostHandlerModel stream, int index, ExpressionSyntax request) =>
        Invoke(
            Member(Qualified(Unsafe), Generic("As", TypeName($"{AsyncEnumerable}<TItem>"))),
            Invoke(Id(StreamCoreName(index)), stream.MessageIsValueType ? Exact(stream.MessageType, true, request) : request, Id(CancellationTokenName))
        );

    /// <summary>
    ///     The pipeline of one stream request, without checks: the typed overload and generic dispatch both call it. Without behaviors it
    ///     returns the handler's enumerable as is; with behaviors it calls the outermost stream behavior method directly, so one that is not an
    ///     iterator runs at the <c>Stream</c> call.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> StreamCore(HostPipeline pipeline, int index) =>
        PipelineCore(
            pipeline,
            Method(
                Modifiers(SyntaxKind.PrivateKeyword),
                TypeName($"{AsyncEnumerable}<{pipeline.Handler.ResponseType}>"),
                StreamCoreName(index),
                Params(Param(TypeName(pipeline.Handler.MessageType), "request"), Param(TypeName(CancellationToken), CancellationTokenName))
            ),
            StreamContextName(index),
            StreamContextField(index),
            CreateStreamContextName(index),
            StreamLinkName(0)
        );

    /// <summary>
    ///     The pipeline context of one stream request: its handler (unless the invoker is static) and the injected services of its stream
    ///     behaviors, resolved once per scope, and one flat link struct per stream behavior (<c>StreamLink{k}</c>, the <c>next</c> of behavior
    ///     <c>k</c>). A stream pipeline with nothing to resolve has no context: its links are nested in a static class and take only the
    ///     request.
    /// </summary>
    private static ClassDeclarationSyntax StreamContext(HostPipeline pipeline, int index) =>
        PipelineContext(
            pipeline,
            StreamContextName(index),
            "stream pipeline",
            StreamLinkName,
            $"global::Keryx.IStreamNext<{pipeline.Handler.ResponseType}>",
            $"{AsyncEnumerable}<{pipeline.Handler.ResponseType}>"
        );

    private static string StreamLinkName(int position) => "StreamLink" + Digits(position + 1);

    private static string StreamContextName(int index) => "__StreamPipeline" + Digits(index);

    private static string StreamContextField(int index) => "__streamPipeline" + Digits(index);

    private static string CreateStreamContextName(int index) => "__CreateStreamPipeline" + Digits(index);

    private static string StreamCoreName(int index) => "__Stream" + Digits(index);
}
