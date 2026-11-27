using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Keryx.Analyzers.GeneratedSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Stream dispatch and stream pipelines (ADR 0001, Streaming): typed <c>Stream</c> overloads, generic <c>Stream&lt;TItem&gt;</c> with
///     exact-type dispatch, and per stream request a pipeline context holding the handler and the injected services of its stream behaviors,
///     and one flat <c>IStreamNext&lt;TItem&gt;</c> link struct per stream behavior.
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

        if (!request.MessageIsSealed)
        {
            // Dispatch is by exact runtime type: a derived stream request statically typed as this one goes to its own handler (OQ-31).
            statements.Add(
                IfStatement(
                    NotEqual(Invoke(Member(Id("request"), "GetType")), TypeOf(request.MessageType)),
                    Block(
                        Return(
                            Invoke(
                                Generic("Stream", TypeName(request.ResponseType!)),
                                Cast(TypeName($"global::Keryx.IStreamRequest<{request.ResponseType}>"), Id("request")),
                                Id(CancellationTokenName)
                            )
                        )
                    )
                )
            );
        }

        statements.Add(Return(Invoke(Id(StreamCoreName(index)), Id("request"), Id(CancellationTokenName))));
        return Method(
                   Modifiers(SyntaxKind.PublicKeyword),
                   TypeName($"{AsyncEnumerable}<{request.ResponseType}>"),
                   "Stream",
                   Params(Param(TypeName(request.MessageType), "request"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
               )
              .Statements(statements)
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
    ///     Generic <c>Stream</c>: validates the request and dispatches on its exact runtime type synchronously, before any behavior runs (ADR
    ///     0001, Streaming), then calls the outermost element of the pipeline directly. The result is a reference type, so reinterpreting
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
                    .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(TypeParameter("TItem"))))
                    .Statements(
                         [
                             Call(Qualified(ThrowIfNull), Id("request")),
                             Var("requestType", Invoke(Member(Id("request"), "GetType"))),
                             .. Dispatch(
                                 "requestType",
                                 "__StreamRequestIndex",
                                 streams.Select(static s => s.MessageType).ToImmutableArray(),
                                 (_, i) => StreamArm(streams[i], i),
                                 ThrowStatement(Invoke(Id("__HandlerNotFound"), Id("requestType")))
                             ),
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

    private static ExpressionSyntax StreamArm(HostHandlerModel stream, int index) =>
        Invoke(
            Member(Qualified(Unsafe), Generic("As", TypeName($"{AsyncEnumerable}<TItem>"))),
            Invoke(Id(StreamCoreName(index)), Exact(stream.MessageType, stream.MessageIsValueType, Id("request")), Id(CancellationTokenName))
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
    ///     <c>k</c>).
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
