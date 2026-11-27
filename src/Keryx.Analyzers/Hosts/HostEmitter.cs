using System.Collections.Immutable;
using System.Globalization;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Specular.Scanning;
using static Keryx.Analyzers.GeneratedSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Writes the host's generated types (ADR 0001, Generated host), built with <see cref="SyntaxFactory" /> (<see cref="GeneratedSyntax" />):
///     the scoped <c>KeryxMediator</c> with its typed overloads, generic exact-type dispatch (runtime-type dispatch for notifications) as
///     switch statements, lazy pipeline contexts and flat link structs (ADR 0001, Generated pipeline shape), and
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

    private const string CancellationTokenName = "cancellationToken";

    public static SourceText Mediator(HostSettings settings, HostComposition composition)
    {
        var requests = composition.Requests;
        var streams = composition.Streams;
        var notifications = composition.Notifications.Where(static n => n.IsActive).ToImmutableArray();

        var members = new List<MemberDeclarationSyntax>
        {
            Field(Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.ReadOnlyKeyword), TypeName(ServiceProvider), "__serviceProvider"),
        };
        for (var i = 0; i < requests.Length; i++)
        {
            if (HasContext(requests[i]))
            {
                members.Add(Field(Modifiers(SyntaxKind.PrivateKeyword), NullableType(Id(ContextName(i))), ContextField(i)));
            }
        }

        for (var i = 0; i < streams.Length; i++)
        {
            if (HasContext(streams[i]))
            {
                members.Add(Field(Modifiers(SyntaxKind.PrivateKeyword), NullableType(Id(StreamContextName(i))), StreamContextField(i)));
            }
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            members.Add(Field(Modifiers(SyntaxKind.PrivateKeyword), NullableType(Id(NotificationContextName(i))), NotificationContextField(i)));
        }

        members.Add(
            ConstructorDeclaration(Identifier("KeryxMediator"))
               .WithModifiers(Modifiers(SyntaxKind.PublicKeyword))
               .WithParameterList(Params(Param(TypeName(ServiceProvider), ServiceProviderName)))
               .WithBody(Block(Call(Qualified(ThrowIfNull), Id(ServiceProviderName)), Assign(Id("__serviceProvider"), Id(ServiceProviderName))))
               .Documented(
                    "<summary>Creates the mediator of one scope.</summary>",
                    $"<param name=\"{ServiceProviderName}\">The scope's service provider. Pipeline contexts resolve handlers from it.</param>"
                )
        );

        for (var i = 0; i < requests.Length; i++)
        {
            members.Add(TypedSend(requests[i].Handler, i));
        }

        members.AddRange(GenericSend(requests.Select(static p => p.Handler).ToImmutableArray()));

        for (var i = 0; i < streams.Length; i++)
        {
            members.Add(TypedStream(streams[i].Handler, i));
        }

        members.AddRange(GenericStream(streams.Select(static p => p.Handler).ToImmutableArray()));
        members.AddRange(Publish(composition.Notifications, notifications));

        for (var i = 0; i < requests.Length; i++)
        {
            members.AddRange(Core(requests[i], i));
        }

        for (var i = 0; i < streams.Length; i++)
        {
            members.AddRange(StreamCore(streams[i], i));
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            members.AddRange(PublishCore(notifications[i], i));
        }

        if (notifications.Any(static n => n.Notification.Parallel && !n.Handlers.IsEmpty))
        {
            members.Add(WhenAllHandlers());
        }

        members.Add(
            Method(
                    Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.StaticKeyword),
                    TypeName("global::Keryx.KeryxHandlerNotFoundException"),
                    "__HandlerNotFound",
                    Params(Param(TypeName("global::System.Type"), "requestType"))
                )
               .AddAttributeLists(NoInliningAttribute())
               .Expression(TargetTypedNew(Id("requestType")))
        );

        for (var i = 0; i < requests.Length; i++)
        {
            if (HasContext(requests[i]))
            {
                members.Add(Context(requests[i], i));
            }
        }

        for (var i = 0; i < streams.Length; i++)
        {
            if (HasContext(streams[i]))
            {
                members.Add(StreamContext(streams[i], i));
            }
        }

        for (var i = 0; i < notifications.Length; i++)
        {
            members.Add(NotificationContext(notifications[i], i));
        }

        var mediator = ClassDeclaration("KeryxMediator")
                      .WithModifiers(Modifiers(SyntaxKind.InternalKeyword, SyntaxKind.SealedKeyword, SyntaxKind.PartialKeyword))
                      .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(TypeName("global::Keryx.IMediator")))))
                      .WithMembers(List(members))
                      .AddAttributeLists(GeneratedCodeAttribute())
                      .Documented(
                           "<summary>",
                           "    This host's Keryx mediator: sends each request and stream request through its pipeline to its handler, and publishes each",
                           "    notification through its pipeline to its handlers. Scoped: it creates each pipeline context once per scope, on first use, so",
                           "    nothing is resolved or allocated per call after that. Registered by <see cref=\"KeryxServiceCollectionExtensions.AddKeryx\" />.",
                           "</summary>"
                       );
        return Render(InNamespace(settings, mediator), MediatorPragma);
    }

    public static SourceText ServiceCollectionExtensions(HostSettings settings, HostComposition composition)
    {
        var statements = new List<StatementSyntax>
        {
            Call(Qualified(ThrowIfNull), Id("services")),
            Call(Member(Qualified(DescriptorExtensions), "TryAddScoped"), Id("services"), Factory(New(Id("KeryxMediator"), Id(ServiceProviderName)))),
        };
        foreach (var service in (string[])["global::Keryx.IMediator", "global::Keryx.ISender", "global::Keryx.IPublisher"])
        {
            statements.Add(
                Call(
                    Member(Qualified(DescriptorExtensions), Generic("TryAddScoped", TypeName(service))),
                    Id("services"),
                    Factory(Invoke(Member(Qualified(ServiceProviderExtensions), Generic("GetRequiredService", Id("KeryxMediator"))), Id(ServiceProviderName)))
                )
            );
        }

        foreach (var handler in composition.ConstructedHandlers)
        {
            var arguments = new ExpressionSyntax[handler.Services.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Resolve(handler.Services[i], i);
            }

            statements.Add(Call(Member(Qualified(DescriptorExtensions), "TryAddScoped"), Id("services"), Factory(New(TypeName(handler.HandlerClass), arguments))));
        }

        statements.Add(Return(Id("services")));

        var addKeryx = Method(Modifiers(SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword), TypeName(ServiceCollection), "AddKeryx", Params(Param(TypeName(ServiceCollection), "services").WithModifiers(Modifiers(SyntaxKind.ThisKeyword))))
                      .Statements(statements)
                      .Documented(
                           "<summary>",
                           "    Registers the scoped <see cref=\"KeryxMediator\" />, as itself and as <see cref=\"global::Keryx.IMediator\" />,",
                           "    <see cref=\"global::Keryx.ISender\" /> and <see cref=\"global::Keryx.IPublisher\" /> (one instance per scope), and every",
                           "    handler class that has injected parameters, all scoped and with generated factories. Behaviors are static and are not registered.",
                           "    Registrations that already exist are kept, so a second call does nothing.",
                           "</summary>",
                           "<param name=\"services\">The service collection.</param>",
                           "<returns>The same service collection.</returns>"
                       );

        var extensions = ClassDeclaration("KeryxServiceCollectionExtensions")
                        .WithModifiers(Modifiers(SyntaxKind.InternalKeyword, SyntaxKind.StaticKeyword))
                        .AddMembers(addKeryx)
                        .AddAttributeLists(GeneratedCodeAttribute())
                        .Documented("<summary>Registers this host's Keryx mediator and handlers.</summary>");
        return Render(InNamespace(settings, extensions));
    }

    private static CompilationUnitSyntax InNamespace(HostSettings settings, MemberDeclarationSyntax type) =>
        CompilationUnit().AddMembers(NamespaceDeclaration(Qualified(settings.Namespace)).AddMembers(type));

    /// <summary>A scoped registration's factory: <c>static serviceProvider =&gt; body</c>.</summary>
    private static SimpleLambdaExpressionSyntax Factory(ExpressionSyntax body) =>
        SimpleLambdaExpression(Parameter(Identifier(ServiceProviderName))).WithModifiers(Modifiers(SyntaxKind.StaticKeyword)).WithExpressionBody(body);

    private static MethodDeclarationSyntax TypedSend(HostHandlerModel request, int index)
    {
        var statements = new List<StatementSyntax>();
        if (!request.MessageIsValueType)
        {
            statements.Add(Call(Qualified(ThrowIfNull), Id("request")));
        }

        if (!request.MessageIsSealed)
        {
            // Dispatch is by exact runtime type: a derived request statically typed as this one goes to its own handler (OQ-31).
            statements.Add(
                IfStatement(
                    NotEqual(Invoke(Member(Id("request"), "GetType")), TypeOf(request.MessageType)),
                    Block(
                        Return(
                            Invoke(
                                Generic("Send", TypeName(request.ResponseType!)),
                                Cast(TypeName($"global::Keryx.IRequest<{request.ResponseType}>"), Id("request")),
                                Id(CancellationTokenName)
                            )
                        )
                    )
                )
            );
        }

        statements.Add(Return(Invoke(Id(CoreName(index)), Id("request"), Id(CancellationTokenName))));
        return Method(
                   Modifiers(SyntaxKind.PublicKeyword),
                   TypeName($"{ValueTask}<{request.ResponseType}>"),
                   "Send",
                   Params(Param(TypeName(request.MessageType), "request"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
               )
              .Statements(statements)
              .Documented(
                   "<summary>",
                   $"    Sends a <c>{Display(request.MessageType)}</c> through its pipeline to its handler, without type dispatch{( request.MessageIsValueType ? " and without boxing it" : "" )}.",
                   "</summary>",
                   "<param name=\"request\">The request.</param>",
                   "<param name=\"cancellationToken\">The token passed to the handler.</param>",
                   "<returns>The handler's response.</returns>"
               );
    }

    private static IEnumerable<MemberDeclarationSyntax> GenericSend(ImmutableArray<HostHandlerModel> requests)
    {
        yield return Method(
                         Modifiers(SyntaxKind.PublicKeyword),
                         TypeName($"{ValueTask}<TResponse>"),
                         "Send",
                         Params(Param(TypeName("global::Keryx.IRequest<TResponse>"), "request"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
                     )
                    .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(TypeParameter("TResponse"))))
                    .Statements(
                         [
                             Call(Qualified(ThrowIfNull), Id("request")),
                             Var("requestType", Invoke(Member(Id("request"), "GetType"))),
                             .. Dispatch(
                                 "requestType",
                                 "__RequestIndex",
                                 requests.Select(static r => r.MessageType).ToImmutableArray(),
                                 static (_, i) => Invoke(Id(DispatchName(i)), Id("request"), Id(CancellationTokenName)),
                                 ThrowStatement(Invoke(Id("__HandlerNotFound"), Id("requestType")))
                             ),
                         ]
                     )
                    .Documented("<inheritdoc />");

        if (requests.Length > DispatchThreshold)
        {
            foreach (var member in TypeTable("request", "__requestTypes", "__RequestIndex", requests.Select(static h => h.MessageType).ToImmutableArray()))
            {
                yield return member;
            }
        }

        for (var i = 0; i < requests.Length; i++)
        {
            yield return DispatchArm(requests[i], i);
        }
    }

    /// <summary>
    ///     The statements that dispatch a generic entry point on the exact runtime type (ADR 0001, Generic entry points): up to
    ///     <see cref="DispatchThreshold" /> types, <c>switch (type) { case var _ when type == typeof(X): return ...; }</c>, exact-type
    ///     <c>when</c> guards and never type patterns (<c>case X x:</c> is <c>isinst</c>, which a derived type also matches); above it,
    ///     <c>switch (Index(type)) { case 0: return ...; }</c>, which compiles to a jump table; then the fallback. Without types, the fallback
    ///     alone.
    /// </summary>
    /// <remarks>
    ///     A switch statement with one <c>return</c> per section, not the <c>return type switch { ... };</c> expression of owner decision 7: C#
    ///     lowers a switch expression to one result local that every arm stores and a single return, and the JIT then neither tail-calls the
    ///     arms nor avoids zeroing that <c>ValueTask</c> in the prolog, which measured slower (OQ-67, open). The statement keeps the same
    ///     compares and jump table.
    /// </remarks>
    /// <param name="typeVariable">The local that holds the runtime type.</param>
    /// <param name="lookup">The index method above the threshold.</param>
    /// <param name="types">The dispatched types, fully qualified, in switch order.</param>
    /// <param name="arm">The result of the section of type <c>i</c>.</param>
    /// <param name="fallback">The statement for any other type: a <c>throw</c> or <c>return default;</c>.</param>
    private static StatementSyntax[] Dispatch(
        string typeVariable,
        string lookup,
        ImmutableArray<string> types,
        Func<string, int, ExpressionSyntax> arm,
        StatementSyntax fallback
    )
    {
        if (types.IsEmpty)
        {
            return [fallback];
        }

        var sections = new List<SwitchSectionSyntax>(types.Length);
        if (types.Length > DispatchThreshold)
        {
            for (var i = 0; i < types.Length; i++)
            {
                sections.Add(ConstantSection(i, Return(arm(types[i], i))));
            }

            return [SwitchStatement(Invoke(Id(lookup), Id(typeVariable)), List(sections)), fallback];
        }

        for (var i = 0; i < types.Length; i++)
        {
            sections.Add(WhenSection(Equal(Id(typeVariable), TypeOf(types[i])), Return(arm(types[i], i))));
        }

        return [SwitchStatement(Id(typeVariable), List(sections)), fallback];
    }

    /// <summary>
    ///     The <c>FrozenDictionary</c> generic dispatch uses above <see cref="DispatchThreshold" />, and the non-inlined lookup into it.
    /// </summary>
    /// <param name="kind">"request", "stream request" or "notification", for the documentation.</param>
    /// <param name="table">The name of the static table.</param>
    /// <param name="lookup">The name of the lookup method.</param>
    /// <param name="types">The dispatched types, fully qualified, in switch order.</param>
    private static IEnumerable<MemberDeclarationSyntax> TypeTable(string kind, string table, string lookup, ImmutableArray<string> types)
    {
        const string pair = "global::System.Collections.Generic.KeyValuePair<global::System.Type, int>";
        var entries = ArrayCreationExpression(
            ArrayType(TypeName(pair), SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList<ExpressionSyntax>(types.Select(static (type, i) => TargetTypedNew(TypeOf(type), Number(i))))
            )
        );
        yield return Field(
                         Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.StaticKeyword, SyntaxKind.ReadOnlyKeyword),
                         TypeName("global::System.Collections.Frozen.FrozenDictionary<global::System.Type, int>"),
                         table,
                         Invoke(Member(Qualified("global::System.Collections.Frozen.FrozenDictionary"), "ToFrozenDictionary"), entries)
                     )
                    .Documented($"<summary>Every {kind} type this host dispatches, mapped to its position in the dispatch switch.</summary>");

        yield return Method(Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.StaticKeyword), PredefinedType(Token(SyntaxKind.IntKeyword)), lookup, Params(Param(TypeName("global::System.Type"), "requestType")))
                    .AddAttributeLists(NoInliningAttribute())
                    .Expression(
                         ConditionalExpression(
                             Invoke(Member(Id(table), "TryGetValue"), [Argument(Id("requestType")), OutVarArgument("requestIndex")]),
                             Id("requestIndex"),
                             PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, Number(1))
                         )
                     )
                    .Documented($"<summary>The position of a {kind} type in the dispatch switch, or -1 for a type this host does not dispatch.</summary>");
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
    private static MethodDeclarationSyntax DispatchArm(HostHandlerModel request, int index) =>
        Method(
                Modifiers(SyntaxKind.PrivateKeyword),
                TypeName($"{ValueTask}<TResponse>"),
                DispatchName(index),
                Params(Param(TypeName("global::Keryx.IRequest<TResponse>"), "request"), Param(TypeName(CancellationToken), CancellationTokenName))
            )
           .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(TypeParameter("TResponse"))))
           .AddAttributeLists(NoInliningAttribute())
           .Statements(
                [
                    Var("response", Invoke(Id(CoreName(index)), Exact(request.MessageType, request.MessageIsValueType, Id("request")), Id(CancellationTokenName))),
                    Return(
                        Invoke(
                            Member(Qualified(Unsafe), Generic("As", TypeName($"{ValueTask}<{request.ResponseType}>"), TypeName($"{ValueTask}<TResponse>"))),
                            [RefArgument(Id("response"))]
                        )
                    ),
                ]
            );

    /// <summary>A message typed as an interface, as its exact type: a class reinterpreted with <c>Unsafe.As</c>, a struct unboxed.</summary>
    private static ExpressionSyntax Exact(string type, bool isValueType, ExpressionSyntax message) =>
        isValueType
            ? Cast(TypeName(type), Cast(PredefinedType(Token(SyntaxKind.ObjectKeyword)), message))
            : Invoke(Member(Qualified(Unsafe), Generic("As", TypeName(type))), message);

    /// <summary>
    ///     The pipeline of one request, without checks: the typed overload and generic dispatch both call it. Without behaviors it calls the
    ///     handler's invoker; with behaviors it calls the outermost behavior method directly, with explicit type arguments and the first link.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> Core(HostPipeline pipeline, int index) =>
        PipelineCore(
            pipeline,
            Method(
                Modifiers(SyntaxKind.PrivateKeyword),
                TypeName($"{ValueTask}<{pipeline.Handler.ResponseType}>"),
                CoreName(index),
                Params(Param(TypeName(pipeline.Handler.MessageType), "request"), Param(TypeName(CancellationToken), CancellationTokenName))
            ),
            ContextName(index),
            ContextField(index),
            CreateContextName(index),
            LinkName(0)
        );

    /// <summary>
    ///     The pipeline method of a request or stream request: the handler's invoker without behaviors, else the outermost behavior method
    ///     with the first link, and, when the pipeline has a context, the non-inlined method that creates it.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> PipelineCore(
        HostPipeline pipeline,
        MethodDeclarationSyntax core,
        string contextName,
        string contextField,
        string createName,
        string firstLink
    )
    {
        var request = pipeline.Handler;
        var hasContext = HasContext(pipeline);
        var context = Coalesce(Invoke(Member(Qualified("global::System.Threading.Volatile"), "Read"), [RefArgument(Id(contextField))]), Invoke(Id(createName)));
        if (pipeline.Behaviors.IsEmpty)
        {
            ExpressionSyntax target = hasContext ? Member(ParenthesizedExpression(context), "Handler") : Qualified(request.HandlerClass);
            yield return core.Expression(Invoke(Member(target, HandlerAnalysis.InvokerName), Id("request"), Id(CancellationTokenName)));
        }
        else
        {
            var call = Return(BehaviorCall(pipeline.Behaviors[0], request.ContextServices, TypeName(contextName + "." + firstLink), "request"));
            yield return core.WithBody(Island(Block(Var(PipelineName, context), call), pipeline.Behaviors[0].ObliviousFit));
        }

        if (hasContext)
        {
            yield return CreateContext(contextName, contextField, createName);
        }
    }

    /// <summary>
    ///     The non-inlined method that creates a pipeline context on first use in a scope. Under concurrent first use every caller ends up with
    ///     the context that won the exchange (ADR 0001, Mediator).
    /// </summary>
    private static MethodDeclarationSyntax CreateContext(string contextName, string contextField, string createName) =>
        Method(Modifiers(SyntaxKind.PrivateKeyword), Id(contextName), createName, Params())
           .AddAttributeLists(NoInliningAttribute())
           .Expression(
                Coalesce(
                    Invoke(
                        Member(Qualified("global::System.Threading.Interlocked"), "CompareExchange"),
                        [RefArgument(Id(contextField)), Argument(New(Id(contextName), Id("__serviceProvider"))), Argument(NullLiteral())]
                    ),
                    NotNull(Id(contextField))
                )
            );

    /// <summary>
    ///     The pipeline context of one request: its handler (unless the invoker is static) and the injected services of its behavior methods,
    ///     resolved once per scope, and one flat link struct per behavior (ADR 0001, Generated pipeline shape). <c>Link{k}</c> is the
    ///     <c>next</c> of behavior <c>k</c>: it calls behavior <c>k + 1</c>'s method with <c>Link{k + 1}</c>, and the last link calls the
    ///     handler's invoker. A pipeline with behaviors always has a context, even when it holds nothing but its links (a static invoker and
    ///     no injected services); whether such a pipeline should have none is OQ-65.
    /// </summary>
    private static ClassDeclarationSyntax Context(HostPipeline pipeline, int index) =>
        PipelineContext(pipeline, ContextName(index), "pipeline", LinkName, $"global::Keryx.INext<{pipeline.Handler.ResponseType}>", $"{ValueTask}<{pipeline.Handler.ResponseType}>");

    /// <summary>The pipeline context class of a request or stream request, with its links.</summary>
    private static ClassDeclarationSyntax PipelineContext(
        HostPipeline pipeline,
        string contextName,
        string description,
        Func<int, string> linkName,
        string nextInterface,
        string returnType
    )
    {
        var request = pipeline.Handler;
        var members = new List<MemberDeclarationSyntax>();
        var resolution = new List<StatementSyntax>();
        if (!request.StaticInvoker)
        {
            members.Add(Field(Modifiers(SyntaxKind.PublicKeyword, SyntaxKind.ReadOnlyKeyword), TypeName(request.HandlerClass), "Handler"));
            resolution.Add(
                Assign(Id("Handler"), Invoke(Member(Qualified(ServiceProviderExtensions), Generic("GetRequiredService", TypeName(request.HandlerClass))), Id(ServiceProviderName)))
            );
        }

        members.AddRange(ServiceFields(request.ContextServices));
        resolution.AddRange(ServiceResolution(request.ContextServices));
        members.Add(ContextConstructor(contextName, resolution));

        for (var k = 0; k < pipeline.Behaviors.Length; k++)
        {
            var isLast = k == pipeline.Behaviors.Length - 1;
            ExpressionSyntax body = !isLast
                ? BehaviorCall(pipeline.Behaviors[k + 1], request.ContextServices, Id(linkName(k + 1)), "request")
                : Invoke(
                    Member(request.StaticInvoker ? Qualified(request.HandlerClass) : Member(Id(PipelineName), "Handler"), HandlerAnalysis.InvokerName),
                    Id("request"),
                    Id(CancellationTokenName)
                );
            members.Add(
                Link(
                    linkName(k),
                    contextName,
                    request.MessageType,
                    "request",
                    nextInterface,
                    returnType,
                    body,
                    !isLast && pipeline.Behaviors[k + 1].ObliviousFit,
                    NextSummary(pipeline.Behaviors, k, "the handler")
                )
            );
        }

        return ContextClass(contextName, members, $"<summary>The {description} context of <c>{Display(request.MessageType)}</c>: {Holds(request)}, resolved once per scope.</summary>");
    }

    /// <summary>A pipeline context class: sealed, created once per scope.</summary>
    private static ClassDeclarationSyntax ContextClass(string name, List<MemberDeclarationSyntax> members, string summary) =>
        ClassDeclaration(name)
           .WithModifiers(Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.SealedKeyword))
           .WithMembers(List(members))
           .Documented(summary);

    private static ConstructorDeclarationSyntax ContextConstructor(string contextName, IEnumerable<StatementSyntax> resolution) =>
        ConstructorDeclaration(Identifier(contextName))
           .WithModifiers(Modifiers(SyntaxKind.PublicKeyword))
           .WithParameterList(Params(Param(TypeName(ServiceProvider), ServiceProviderName)))
           .WithBody(Block(resolution));

    /// <summary>
    ///     One link (ADR 0001, Generated pipeline shape): a <c>readonly struct</c> with a primary constructor over the pipeline context and the
    ///     message, and an expression-bodied <c>InvokeAsync</c> that calls the next behavior method (or the handler, or the publish strategy).
    ///     A parameter that <c>InvokeAsync</c> does not read (the context in the last link of a pipeline whose handler's invoker is static;
    ///     both in the last link of a notification type without handlers) is not captured, so the link holds what the links before this
    ///     rework held; the mediator's header suppresses its CS9113 (<see cref="GeneratedSyntax.MediatorPragma" />). A call through a
    ///     nullable-oblivious fit (OQ-58) is written with nullable warnings disabled: generated code names that type without an annotation, so
    ///     the call would warn where the oblivious declaration does not.
    /// </summary>
    private static StructDeclarationSyntax Link(
        string name,
        string contextName,
        string messageType,
        string messageName,
        string nextInterface,
        string returnType,
        ExpressionSyntax body,
        bool obliviousFit,
        string summary
    )
    {
        if (obliviousFit)
        {
            body = body.WithLeadingTrivia(NullableWarnings(disable: true));
        }

        var invoke = Method(Modifiers(SyntaxKind.PublicKeyword), TypeName(returnType), "InvokeAsync", Params(Param(TypeName(CancellationToken), CancellationTokenName)))
           .Expression(body);
        var link = StructDeclaration(name)
                  .WithModifiers(Modifiers(SyntaxKind.PublicKeyword, SyntaxKind.ReadOnlyKeyword))
                  .WithParameterList(Params(Param(Id(contextName), PipelineName), Param(TypeName(messageType), messageName)))
                  .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(TypeName(nextInterface)))));

        if (obliviousFit)
        {
            link = link.WithCloseBraceToken(Token(TriviaList(NullableWarnings(disable: false)), SyntaxKind.CloseBraceToken, TriviaList()));
        }

        return link.AddMembers(invoke).Documented(summary);
    }

    /// <summary>The one-line summary of link <paramref name="position" />: whose <c>next</c> it is, and where it goes.</summary>
    private static string NextSummary(ImmutableArray<PipelineBehaviorModel> behaviors, int position, string end) =>
        position == behaviors.Length - 1
            ? $"<summary>The <c>next</c> of <c>{Display(behaviors[position].Identity)}</c>: {end}.</summary>"
            : $"<summary>The <c>next</c> of <c>{Display(behaviors[position].Identity)}</c>: <c>{Display(behaviors[position + 1].Identity)}</c>.</summary>";

    /// <summary>
    ///     A block whose last statement (the call of a behavior method that fits only through a nullable-oblivious type, OQ-58) is written with
    ///     nullable warnings disabled.
    /// </summary>
    private static BlockSyntax Island(BlockSyntax block, bool obliviousFit)
    {
        if (!obliviousFit)
        {
            return block;
        }

        var last = block.Statements[^1];
        return block
              .ReplaceNode(last, last.WithLeadingTrivia(last.GetLeadingTrivia().Insert(0, NullableWarnings(disable: true))))
              .WithCloseBraceToken(Token(TriviaList(NullableWarnings(disable: false)), SyntaxKind.CloseBraceToken, TriviaList()));
    }

    /// <summary>
    ///     The direct call of a behavior method (ADR 0001, Behavior methods): the class's static method with explicit type arguments
    ///     (<paramref name="link" /> in place of <c>TNext</c>) and the arguments in declared order: the message, the link as a target-typed
    ///     <c>new(pipeline, message)</c>, the call's token, and its injected services read from the context's shared fields.
    /// </summary>
    /// <param name="behavior">The behavior.</param>
    /// <param name="services">The pipeline context's services, which the behavior's <see cref="PipelineBehaviorModel.ServiceSlots" /> index.</param>
    /// <param name="link">The link type its <c>next</c> is, qualified as the call site needs it.</param>
    /// <param name="message">The name of the request or notification.</param>
    private static InvocationExpressionSyntax BehaviorCall(
        PipelineBehaviorModel behavior,
        EquatableArray<ContextServiceModel> services,
        TypeSyntax link,
        string message
    )
    {
        var typeArguments = behavior.TypeArguments.Select(t => t.Length == 0 ? link : TypeName(t)).ToArray();
        var arguments = new List<ExpressionSyntax>(behavior.Parameters.Count);
        var service = 0;
        foreach (var role in behavior.Parameters)
        {
            if (role == BehaviorParameterRole.Service)
            {
                var slot = behavior.ServiceSlots[service];
                arguments.Add(ServiceArgument(services[slot], slot, behavior.Services[service]));
                service++;
                continue;
            }

            arguments.Add(
                role switch
                {
                    BehaviorParameterRole.Message => Id(message),
                    BehaviorParameterRole.Next => TargetTypedNew(Id(PipelineName), Id(message)),
                    BehaviorParameterRole.CancellationToken => Id(CancellationTokenName),
                    _ => DefaultLiteral(),
                }
            );
        }

        return Invoke(Member(Qualified(behavior.Class), Generic(behavior.Method, typeArguments)), [.. arguments]);
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
    private static ExpressionSyntax ServiceArgument(ContextServiceModel service, int slot, ServiceParameterModel parameter)
    {
        var field = Member(Id(PipelineName), ServiceField(slot));
        return service.Required || parameter.DefaultValue is not { } defaultValue || defaultValue == "null"
            ? field
            : service.IsValueType && defaultValue == "default"
            ? Invoke(Member(field, "GetValueOrDefault"))
            : Coalesce(field, Code(defaultValue));
    }

    /// <summary>What a pipeline context holds, for its documentation.</summary>
    private static string Holds(HostHandlerModel handler) =>
        (handler.StaticInvoker, handler.ContextServices.Count > 0) switch
        {
            (false, true) => "its handler and the injected services of its behaviors",
            (false, false) => "its handler",
            (true, true) => "the injected services of its behaviors",
            (true, false) => "nothing but its links",
        };

    /// <summary>
    ///     One field per service the pipeline context shares (ADR 0001, Injected parameters), in order of first use: the service type for a
    ///     service resolved as required, the nullable service type for one whose consumers are all optional (<see langword="null" /> when the
    ///     service is missing; each call site applies its own default).
    /// </summary>
    private static IEnumerable<FieldDeclarationSyntax> ServiceFields(EquatableArray<ContextServiceModel> services) =>
        services.Select(static (service, i) => Field(
                            Modifiers(SyntaxKind.PublicKeyword, SyntaxKind.ReadOnlyKeyword),
                            TypeName(service.Required || service.NullableUnderlyingType is not null ? service.ServiceType : service.ServiceType + "?"),
                            ServiceField(i)
                        )
        );

    /// <summary>Resolves each service the pipeline context shares from the scope, once, in order of first use.</summary>
    private static IEnumerable<StatementSyntax> ServiceResolution(EquatableArray<ContextServiceModel> services) =>
        services.Select(static (service, i) => (StatementSyntax)Assign(Id(ServiceField(i)), ResolveShared(service, i)));

    /// <summary>
    ///     The resolution of a shared service: as required (<see cref="Resolve" /> without a default) when any consumer is required;
    ///     otherwise <c>GetService</c>/<c>GetKeyedService</c>, <see langword="null" /> for a missing service (a value type is matched with
    ///     the type pattern of <see cref="Resolve" />, once, into a <c>Nullable&lt;T&gt;</c> field).
    /// </summary>
    private static ExpressionSyntax ResolveShared(ContextServiceModel service, int slot)
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
        return ConditionalExpression(
            IsPatternExpression(
                ServiceLookup(service.ServiceType, service.ServiceKey),
                DeclarationPattern(TypeName(service.NullableUnderlyingType ?? service.ServiceType), SingleVariableDesignation(Identifier(variable)))
            ),
            Cast(TypeName(nullable), Id(variable)),
            NullLiteral()
        );
    }

    /// <summary>
    ///     <c>serviceProvider.GetService(typeof(T))</c>, or for a keyed service <c>(serviceProvider as IKeyedServiceProvider)?.GetKeyedService(typeof(T), key)</c>.
    ///     <c>GetKeyedService(Type, object)</c> is an extension only from Microsoft.Extensions.DependencyInjection.Abstractions 9.0; the interface
    ///     method exists from 8.0, the floor for keyed services (ADR 0001, Injected parameters).
    /// </summary>
    private static ExpressionSyntax ServiceLookup(string type, string? key) =>
        key is null
            ? Invoke(Member(Id(ServiceProviderName), "GetService"), TypeOf(type))
            : ConditionalAccessExpression(
                ParenthesizedExpression(BinaryExpression(SyntaxKind.AsExpression, Id(ServiceProviderName), TypeName(KeyedServiceProvider))),
                Invoke(MemberBindingExpression(Id("GetKeyedService")), TypeOf(type), Code(key))
            );

    /// <summary>
    ///     The resolution of one injected parameter (ADR 0001, Injected parameters). No reflection: optional value types are matched with a
    ///     type pattern on <c>GetService(typeof(T))</c>, which neither allocates nor activates anything.
    /// </summary>
    internal static ExpressionSyntax Resolve(ServiceParameterModel parameter, int index)
    {
        var provider = Id(ServiceProviderName);
        if (parameter is { ServiceKey: null, IsServiceProvider: true })
        {
            return provider;
        }

        var key = parameter.ServiceKey is { } keyText ? Code(keyText) : null;
        if (parameter.DefaultValue is not { } defaultValue)
        {
            // Nullable<T> does not satisfy the notnull constraint of GetRequiredService<T>, so it is resolved by type and unboxed.
            return parameter.NullableUnderlyingType is not null
                ? key is null
                    ? Cast(TypeName(parameter.Type), Invoke(Member(Qualified(ServiceProviderExtensions), "GetRequiredService"), provider, TypeOf(parameter.Type)))
                    : Cast(TypeName(parameter.Type), Invoke(Member(Qualified(KeyedServiceProviderExtensions), "GetRequiredKeyedService"), provider, TypeOf(parameter.Type), key))
                : key is null
                ? Invoke(Member(Qualified(ServiceProviderExtensions), Generic("GetRequiredService", TypeName(parameter.ServiceType))), provider)
                : Invoke(Member(Qualified(KeyedServiceProviderExtensions), Generic("GetRequiredKeyedService", TypeName(parameter.ServiceType))), provider, key);
        }

        if (!parameter.IsValueType)
        {
            // GetService<T> already returns null for a missing service; any other default (including the null! of a parameter that is not
            // annotated) is applied with ??.
            var lookup = key is null
                ? Invoke(Member(Qualified(ServiceProviderExtensions), Generic("GetService", TypeName(parameter.ServiceType))), provider)
                : Invoke(Member(Qualified(KeyedServiceProviderExtensions), Generic("GetKeyedService", TypeName(parameter.ServiceType))), provider, key);
            return defaultValue == "null" ? lookup : Coalesce(lookup, Code(defaultValue));
        }

        var variable = "__value" + Digits(index);
        return parameter.NullableUnderlyingType is { } underlying
            ? ConditionalExpression(
                IsPatternExpression(ServiceLookup(parameter.Type, parameter.ServiceKey), DeclarationPattern(TypeName(underlying), SingleVariableDesignation(Identifier(variable)))),
                Cast(TypeName(parameter.Type), Id(variable)),
                Code(defaultValue)
            )
            : ConditionalExpression(
                IsPatternExpression(ServiceLookup(parameter.Type, parameter.ServiceKey), DeclarationPattern(TypeName(parameter.Type), SingleVariableDesignation(Identifier(variable)))),
                Id(variable),
                Code(defaultValue)
            );
    }

    /// <summary>
    ///     Whether a request or stream pipeline has a context (created once per scope): its handler is resolved from the scope, or it has
    ///     behaviors (the context nests their links). A pipeline without behaviors whose handler's invoker is static calls the invoker
    ///     directly.
    /// </summary>
    private static bool HasContext(HostPipeline pipeline) => !pipeline.Handler.StaticInvoker || !pipeline.Behaviors.IsEmpty;

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
