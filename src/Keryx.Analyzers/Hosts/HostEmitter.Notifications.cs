using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Keryx.Analyzers.GeneratedSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Notification dispatch and publish pipelines (ADR 0001, Notifications): typed <c>Publish</c> overloads, generic
///     <c>Publish&lt;TNotification&gt;</c> that never boxes a struct notification, and per notification type a pipeline context holding its
///     handlers and the injected services of its notification behaviors, one flat <c>INext</c> link struct per notification behavior, and the
///     publish strategy (sequential or parallel, run all, then one <c>AggregateException</c>), which calls the handlers directly.
/// </summary>
internal static partial class HostEmitter
{
    private const string TaskType = "global::System.Threading.Tasks.Task";
    private const string ExceptionType = "global::System.Exception";
    private const string AggregateExceptionType = "global::System.AggregateException";
    private const string ExceptionList = "global::System.Collections.Generic.List<global::System.Exception>";
    private const string NotificationInterface = "global::Keryx.INotification";

    /// <summary>
    ///     The typed overloads, one per notification type the host knows, then generic <c>Publish</c>.
    /// </summary>
    /// <param name="all">Every notification type the host knows.</param>
    /// <param name="active">The ones something runs for, in core and context order.</param>
    private static IEnumerable<MemberDeclarationSyntax> Publish(ImmutableArray<NotificationPipeline> all, ImmutableArray<NotificationPipeline> active)
    {
        foreach (var pipeline in all)
        {
            yield return TypedPublish(pipeline, active.IndexOf(pipeline));
        }

        foreach (var member in GenericPublish(active))
        {
            yield return member;
        }
    }

    private static MethodDeclarationSyntax TypedPublish(NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        var statements = new List<StatementSyntax>();
        if (!notification.IsValueType)
        {
            statements.Add(Call(Qualified(ThrowIfNull), Id("notification")));
        }

        if (!notification.IsSealed)
        {
            // Dispatch is by runtime type: a derived notification statically typed as this one goes to its own pipeline.
            statements.Add(
                IfStatement(
                    NotEqual(Invoke(Member(Id("notification"), "GetType")), TypeOf(notification.Type)),
                    Block(Return(Invoke(Generic("Publish", TypeName(notification.Type)), Id("notification"), Id(CancellationTokenName))))
                )
            );
        }

        statements.Add(Return(index < 0 ? DefaultLiteral() : Invoke(Id(PublishCoreName(index)), Id("notification"), Id(CancellationTokenName))));
        return Method(
                   Modifiers(SyntaxKind.PublicKeyword),
                   TypeName(ValueTask),
                   "Publish",
                   Params(Param(TypeName(notification.Type), "notification"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
               )
              .Statements(statements)
              .Documented(
                   "<summary>",
                   index < 0
                       ? $"    Publishes a <c>{Display(notification.Type)}</c>. Nothing runs for it in this host: it has neither handlers nor notification behaviors."
                       : $"    Publishes a <c>{Display(notification.Type)}</c> to its handlers, without type dispatch{( notification.IsValueType ? " and without boxing it" : "" )}.",
                   "</summary>",
                   "<param name=\"notification\">The notification.</param>",
                   "<param name=\"cancellationToken\">The token passed to every behavior and handler. Cancelling it does not skip handlers.</param>",
                   "<returns>A task that completes when every handler has run; it faults with one <see cref=\"global::System.AggregateException\" /> when any handler fails.</returns>"
               );
    }

    /// <summary>
    ///     Generic <c>Publish</c> (ADR 0001, Generic entry points): with a value-type <c>TNotification</c>, the arms compare
    ///     <c>typeof(TNotification)</c>, which the JIT folds because value-type instantiations are not shared, and convert with
    ///     <c>Unsafe.As&lt;TNotification, X&gt;</c>, so dispatch never boxes a struct notification. Only a reference-type
    ///     <c>TNotification</c> dispatches on <c>GetType()</c> (above <see cref="DispatchThreshold" /> through a <c>FrozenDictionary</c>); a
    ///     struct that arrives boxed (typed as an interface) is unboxed there. Notification types need not be sealed, so that branch keeps
    ///     exact-type guards rather than type patterns (<see cref="ExactTypeDispatch" />). Each switch ends with <c>default: return default;</c>:
    ///     an unknown runtime type completes immediately.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> GenericPublish(ImmutableArray<NotificationPipeline> active)
    {
        var statements = new List<StatementSyntax>
        {
            // Not ArgumentNullException.ThrowIfNull(object): that would box a struct notification.
            IfStatement(
                IsPatternExpression(Id("notification"), ConstantPattern(NullLiteral())),
                Block(ThrowStatement(New(TypeName("global::System.ArgumentNullException"), Invoke(Id("nameof"), Id("notification")))))
            ),
        };

        if (active.IsEmpty)
        {
            // Every notification type is unknown to this host or has nothing to run.
            statements.Add(Return(DefaultLiteral()));
        }
        else
        {
            var valueTypeSections = new List<SwitchSectionSyntax>();
            for (var i = 0; i < active.Length; i++)
            {
                var type = active[i].Notification.Type;
                if (active[i].Notification.IsValueType)
                {
                    valueTypeSections.Add(
                        WhenSection(
                            Equal(TypeOf("TNotification"), TypeOf(type)),
                            Return(
                                Invoke(
                                    Id(PublishCoreName(i)),
                                    [
                                        Argument(Invoke(Member(Qualified(Unsafe), Generic("As", TypeName("TNotification"), TypeName(type))), [RefArgument(Id("notification"))])),
                                        Argument(Id(CancellationTokenName)),
                                    ]
                                )
                            )
                        )
                    );
                }
            }

            StatementSyntax valueTypeDispatch = Return(DefaultLiteral());
            if (valueTypeSections.Count > 0)
            {
                valueTypeSections.Add(DefaultSection(Return(DefaultLiteral())));
                valueTypeDispatch = SwitchStatement(TypeOf("TNotification"), List(valueTypeSections));
            }

            statements.Add(IfStatement(Member(TypeOf("TNotification"), "IsValueType"), Block(valueTypeDispatch)));
            statements.Add(Var("notificationType", Invoke(Member(Id("notification"), "GetType"))));
            statements.AddRange(
                ExactTypeDispatch(
                    "notificationType",
                    "__NotificationIndex",
                    active.Select(static n => n.Notification.Type).ToImmutableArray(),
                    (_, i) => PublishArm(active[i], i),
                    Return(DefaultLiteral())
                )
            );
        }

        yield return Method(
                         Modifiers(SyntaxKind.PublicKeyword),
                         TypeName(ValueTask),
                         "Publish",
                         Params(Param(TypeName("TNotification"), "notification"), Param(TypeName(CancellationToken), CancellationTokenName, DefaultLiteral()))
                     )
                    .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(TypeParameter("TNotification"))))
                    .WithConstraintClauses(
                         SingletonList(
                             TypeParameterConstraintClause(Id("TNotification"))
                                .WithConstraints(SingletonSeparatedList<TypeParameterConstraintSyntax>(TypeConstraint(TypeName(NotificationInterface))))
                         )
                     )
                    .Statements(statements)
                    .Documented("<inheritdoc />");

        if (active.Length > DispatchThreshold)
        {
            foreach (var member in TypeTable("notification", "__notificationTypes", "__NotificationIndex", active.Select(static n => n.Notification.Type).ToImmutableArray()))
            {
                yield return member;
            }
        }
    }

    /// <summary>
    ///     The arm of the reference-type branch of generic <c>Publish</c>: a class is reinterpreted with <c>Unsafe.As</c>; a struct arrives boxed
    ///     (the caller's box, through an interface-typed <c>TNotification</c>) and is unboxed.
    /// </summary>
    private static ExpressionSyntax PublishArm(NotificationPipeline pipeline, int index)
    {
        var type = pipeline.Notification.Type;
        var boxed = Cast(PredefinedType(Token(SyntaxKind.ObjectKeyword)), Id("notification"));
        ExpressionSyntax argument = pipeline.Notification.IsValueType ? Cast(TypeName(type), boxed) : Invoke(Member(Qualified(Unsafe), Generic("As", TypeName(type))), boxed);
        return Invoke(Id(PublishCoreName(index)), argument, Id(CancellationTokenName));
    }

    /// <summary>
    ///     The publish pipeline of one notification type, without checks: the typed overload and generic dispatch both call it. It calls the
    ///     outermost notification behavior method directly with the first link, or, without notification behaviors, runs the handlers with the
    ///     publish strategy.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> PublishCore(NotificationPipeline pipeline, int index)
    {
        var core = Method(
            Modifiers(SyntaxKind.PrivateKeyword),
            TypeName(ValueTask),
            PublishCoreName(index),
            Params(Param(TypeName(pipeline.Notification.Type), "notification"), Param(TypeName(CancellationToken), CancellationTokenName))
        );
        var call = Return(
            pipeline.Behaviors.IsEmpty
                ? Invoke(Member(Id(PipelineName), "RunHandlers"), Id("notification"), Id(CancellationTokenName))
                : BehaviorCall(
                    pipeline.Behaviors[0],
                    pipeline.Notification.ContextServices,
                    TypeName(NotificationContextName(index) + "." + LinkName(0)),
                    "notification"
                )
        );
        var context = Coalesce(
            Invoke(Member(Qualified("global::System.Threading.Volatile"), "Read"), [RefArgument(Id(NotificationContextField(index)))]),
            Invoke(Id(CreateNotificationContextName(index)))
        );
        yield return core.WithBody(Block(Var(PipelineName, context), call));
        yield return CreateContext(NotificationContextName(index), NotificationContextField(index), CreateNotificationContextName(index));
    }

    /// <summary>
    ///     The parallel strategy's slow path (ADR 0001, Publish strategy): awaits the handlers that had not completed successfully when they
    ///     were started, then throws one <c>AggregateException</c> with every failure, in handler order.
    /// </summary>
    private static MethodDeclarationSyntax WhenAllHandlers()
    {
        var collected = Block().WithCloseBraceToken(
            Token(TriviaList(Comment("// Every failure is collected below, in handler order.")), SyntaxKind.CloseBraceToken, TriviaList())
        );
        return Method(
                   Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.StaticKeyword, SyntaxKind.AsyncKeyword),
                   TypeName(ValueTask),
                   "__WhenAllHandlers",
                   Params(Param(ArrayType(TypeName(TaskType), SingletonList(ArrayRankSpecifier())), "tasks"))
               )
              .AddAttributeLists(PoolingBuilderAttribute())
              .Statements(
                   [
                       TryStatement(
                           Block(ExpressionStatement(AwaitExpression(ConfigureAwaitFalse(Invoke(Member(Qualified(TaskType), "WhenAll"), Id("tasks")))))),
                           SingletonList(CatchClause().WithDeclaration(CatchDeclaration(TypeName(ExceptionType))).WithBlock(collected)),
                           null
                       ),
                       Local(NullableType(TypeName(ExceptionList)), "exceptions", NullLiteral()),
                       ForEachStatement(
                           Id("var"),
                           Identifier("task"),
                           Id("tasks"),
                           Block(
                               IfStatement(
                                   Member(Id("task"), "IsFaulted"),
                                   Block(ExpressionStatement(Invoke(Member(Exceptions(), "AddRange"), Member(NotNull(Member(Id("task"), "Exception")), "InnerExceptions")))),
                                   ElseClause(
                                       IfStatement(
                                           Member(Id("task"), "IsCanceled"),
                                           Block(
                                               TryStatement(
                                                   Block(ExpressionStatement(Invoke(Member(Invoke(Member(Id("task"), "GetAwaiter")), "GetResult")))),
                                                   SingletonList(CollectException()),
                                                   null
                                               )
                                           )
                                       )
                                   )
                               )
                           )
                       ),
                       ThrowCollected(),
                   ]
               )
              .Documented("<summary>Awaits the pending handlers of a parallel publish and throws every failure, in handler order.</summary>");
    }

    /// <summary><c>(exceptions ??= new())</c>, the failures collected so far.</summary>
    private static ParenthesizedExpressionSyntax Exceptions() =>
        ParenthesizedExpression(AssignmentExpression(SyntaxKind.CoalesceAssignmentExpression, Id("exceptions"), TargetTypedNew()));

    /// <summary><c>catch (Exception exception) { (exceptions ??= new()).Add(exception); }</c>, then <paramref name="more" />.</summary>
    private static CatchClauseSyntax CollectException(params StatementSyntax[] more) =>
        CatchClause()
           .WithDeclaration(CatchDeclaration(TypeName(ExceptionType), Identifier("exception")))
           .WithBlock(Block(new StatementSyntax[] { ExpressionStatement(Invoke(Member(Exceptions(), "Add"), Id("exception"))) }.Concat(more)));

    /// <summary><c>if (exceptions is not null) { throw new AggregateException(exceptions); }</c>.</summary>
    private static IfStatementSyntax ThrowCollected() =>
        IfStatement(
            IsPatternExpression(Id("exceptions"), UnaryPattern(Token(SyntaxKind.NotKeyword), ConstantPattern(NullLiteral()))),
            Block(ThrowStatement(New(TypeName(AggregateExceptionType), Id("exceptions"))))
        );

    private static InvocationExpressionSyntax ConfigureAwaitFalse(ExpressionSyntax awaitable) => Invoke(Member(awaitable, "ConfigureAwait"), BooleanLiteral(false));

    /// <summary>
    ///     The pipeline context of one notification type: its handlers that have injected parameters and the injected services of its
    ///     notification behaviors, resolved once per scope; the publish strategy, which calls the handlers directly; and one flat link struct
    ///     per notification behavior (<c>Link{k}</c>, whose last one runs the strategy). A type with notification behaviors but no handlers
    ///     still has a context, which then holds only its links and any injected services; whether it should have none is OQ-65.
    /// </summary>
    private static ClassDeclarationSyntax NotificationContext(NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        var context = NotificationContextName(index);
        var members = new List<MemberDeclarationSyntax>();
        var resolution = new List<StatementSyntax>();
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            var handler = pipeline.Handlers[i].Handler;
            if (!handler.StaticInvoker)
            {
                members.Add(Field(Modifiers(SyntaxKind.PublicKeyword, SyntaxKind.ReadOnlyKeyword), TypeName(handler.HandlerClass), HandlerField(i)));
                resolution.Add(
                    Assign(Id(HandlerField(i)), Invoke(Member(Qualified(ServiceProviderExtensions), Generic("GetRequiredService", TypeName(handler.HandlerClass))), Id(ServiceProviderName)))
                );
            }
        }

        members.AddRange(ServiceFields(notification.ContextServices));
        resolution.AddRange(ServiceResolution(notification.ContextServices));
        members.Add(ContextConstructor(context, resolution));

        if (!pipeline.Handlers.IsEmpty)
        {
            if (notification.Parallel)
            {
                members.Add(ParallelStrategy(pipeline));
            }
            else
            {
                members.AddRange(SequentialStrategy(pipeline));
            }
        }

        for (var k = 0; k < pipeline.Behaviors.Length; k++)
        {
            var isLast = k == pipeline.Behaviors.Length - 1;
            ExpressionSyntax body = !isLast
                ? BehaviorCall(pipeline.Behaviors[k + 1], notification.ContextServices, Id(LinkName(k + 1)), "notification")
                : pipeline.Handlers.IsEmpty
                ? DefaultLiteral()
                : Invoke(Member(Id(PipelineName), "RunHandlers"), Id("notification"), Id(CancellationTokenName));
            members.Add(
                Link(
                    LinkName(k),
                    context,
                    notification.Type,
                    "notification",
                    "global::Keryx.INext",
                    ValueTask,
                    body,
                    obliviousFit: false,
                    NextSummary(pipeline.Behaviors, k, pipeline.Handlers.IsEmpty ? "nothing (no handlers)" : "the handlers")
                )
            );
        }

        return ContextClass(
            context,
            members,
            $"<summary>The publish pipeline context of <c>{Display(notification.Type)}</c>: its handlers and the injected services of its behaviors, resolved once per scope, and its publish strategy.</summary>"
        );
    }

    /// <summary>
    ///     The sequential strategy (ADR 0001, Publish strategy, Failures): each handler is called directly and awaited in handler order, every
    ///     one runs whatever the others do, and the failures are combined into one <c>AggregateException</c>. Handlers that complete synchronously and
    ///     successfully take a path that allocates nothing; the first one that does not hands over to <c>RunHandlersAsync</c>, which awaits it
    ///     and runs the rest.
    /// </summary>
    private static IEnumerable<MemberDeclarationSyntax> SequentialStrategy(NotificationPipeline pipeline)
    {
        var notification = pipeline.Notification;
        var statements = new List<StatementSyntax>();
        statements.AddRange(Box(pipeline));
        statements.Add(Local(NullableType(TypeName(ExceptionList)), "exceptions", NullLiteral()));
        statements.Add(Local(TypeName(ValueTask), "task"));
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            var pending = new List<ExpressionSyntax> { Id("notification") };
            if (pipeline.Boxes)
            {
                pending.Add(Id("boxed"));
            }

            pending.AddRange([Id("task"), Number(i), Id("exceptions"), Id(CancellationTokenName)]);
            statements.Add(
                TryStatement(
                    Block(Assign(Id("task"), HandlerCall(pipeline, i))),
                    SingletonList(CollectException(Assign(Id("task"), DefaultLiteral()))),
                    null
                )
            );
            statements.Add(
                IfStatement(PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Member(Id("task"), "IsCompletedSuccessfully")), Block(Return(Invoke(Id("RunHandlersAsync"), [.. pending]))))
            );
            statements.Add(ExpressionStatement(Invoke(Member(Invoke(Member(Id("task"), "GetAwaiter")), "GetResult"))));
        }

        statements.Add(
            Return(
                ConditionalExpression(
                    IsPatternExpression(Id("exceptions"), ConstantPattern(NullLiteral())),
                    DefaultLiteral(),
                    Invoke(Member(Qualified(ValueTask), "FromException"), New(TypeName(AggregateExceptionType), Id("exceptions")))
                )
            )
        );

        yield return Method(
                         Modifiers(SyntaxKind.PublicKeyword),
                         TypeName(ValueTask),
                         "RunHandlers",
                         Params(Param(TypeName(notification.Type), "notification"), Param(TypeName(CancellationToken), CancellationTokenName))
                     )
                    .Statements(statements)
                    .Documented("<summary>Runs every handler in handler order (sequential), then reports every failure in one AggregateException.</summary>");

        var parameters = new List<ParameterSyntax> { Param(TypeName(notification.Type), "notification") };
        if (pipeline.Boxes)
        {
            parameters.Add(Param(TypeName(NotificationInterface), "boxed"));
        }

        parameters.AddRange(
            [
                Param(TypeName(ValueTask), "pending"),
                Param(PredefinedType(Token(SyntaxKind.IntKeyword)), "position"),
                Param(NullableType(TypeName(ExceptionList)), "exceptions"),
                Param(TypeName(CancellationToken), CancellationTokenName),
            ]
        );

        var rest = new List<StatementSyntax>
        {
            TryStatement(Block(ExpressionStatement(AwaitExpression(ConfigureAwaitFalse(Id("pending"))))), SingletonList(CollectException()), null),
        };
        for (var i = 1; i < pipeline.Handlers.Length; i++)
        {
            rest.Add(
                IfStatement(
                    BinaryExpression(SyntaxKind.LessThanExpression, Id("position"), Number(i)),
                    Block(
                        TryStatement(
                            Block(ExpressionStatement(AwaitExpression(ConfigureAwaitFalse(HandlerCall(pipeline, i))))),
                            SingletonList(CollectException()),
                            null
                        )
                    )
                )
            );
        }

        rest.Add(ThrowCollected());

        yield return Method(Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.AsyncKeyword), TypeName(ValueTask), "RunHandlersAsync", ParameterList(SeparatedList(parameters)))
                    .AddAttributeLists(PoolingBuilderAttribute())
                    .Statements(rest)
                    .Documented("<summary>Awaits the handler at <paramref name=\"position\" />, runs the ones after it in order, then reports every failure.</summary>");
    }

    /// <summary>
    ///     The parallel strategy (ADR 0001, Publish strategy): every handler is started in handler order on the calling thread without
    ///     awaiting it (no <c>Task.Run</c>), a synchronous throw is recorded as a faulted task and the rest still start. When every handler
    ///     completed successfully, nothing is allocated; otherwise one array plus one <c>AsTask()</c> per pipeline that did not, awaited
    ///     together, with every failure in handler order.
    /// </summary>
    private static MethodDeclarationSyntax ParallelStrategy(NotificationPipeline pipeline)
    {
        var notification = pipeline.Notification;
        var count = pipeline.Handlers.Length;
        var statements = new List<StatementSyntax>();
        statements.AddRange(Box(pipeline));
        for (var i = 0; i < count; i++)
        {
            var task = "task" + Digits(i);
            statements.Add(Local(TypeName(ValueTask), task));
            statements.Add(
                TryStatement(
                    Block(Assign(Id(task), HandlerCall(pipeline, i))),
                    SingletonList(
                        CatchClause()
                           .WithDeclaration(CatchDeclaration(TypeName(ExceptionType), Identifier("exception")))
                           .WithBlock(Block(Assign(Id(task), Invoke(Member(Qualified(ValueTask), "FromException"), Id("exception")))))
                    ),
                    null
                )
            );
        }

        // Each status is read once: a pending handler may complete between two reads.
        for (var i = 0; i < count; i++)
        {
            statements.Add(Var("done" + Digits(i), Member(Id("task" + Digits(i)), "IsCompletedSuccessfully")));
            statements.Add(IfStatement(Id("done" + Digits(i)), Block(ExpressionStatement(Invoke(Member(Invoke(Member(Id("task" + Digits(i)), "GetAwaiter")), "GetResult"))))));
        }

        ExpressionSyntax pending = Pending(0);
        for (var i = 1; i < count; i++)
        {
            pending = BinaryExpression(SyntaxKind.AddExpression, pending, Pending(i));
        }

        statements.Add(Var("pending", pending));
        statements.Add(IfStatement(Equal(Id("pending"), Number(0)), Block(Return(DefaultLiteral()))));
        statements.Add(
            Var("tasks", ArrayCreationExpression(ArrayType(TypeName(TaskType), SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(Id("pending")))))))
        );
        statements.Add(Var("next", Number(0)));
        for (var i = 0; i < count; i++)
        {
            statements.Add(
                IfStatement(
                    PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, Id("done" + Digits(i))),
                    Block(
                        Assign(
                            ElementAccessExpression(Id("tasks"), BracketedArgumentList(SingletonSeparatedList(Argument(PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, Id("next")))))),
                            Invoke(Member(Id("task" + Digits(i)), "AsTask"))
                        )
                    )
                )
            );
        }

        statements.Add(Return(Invoke(Id("__WhenAllHandlers"), Id("tasks"))));

        return Method(
                   Modifiers(SyntaxKind.PublicKeyword),
                   TypeName(ValueTask),
                   "RunHandlers",
                   Params(Param(TypeName(notification.Type), "notification"), Param(TypeName(CancellationToken), CancellationTokenName))
               )
              .Statements(statements)
              .Documented("<summary>Starts every handler in handler order without awaiting it (parallel), then awaits the pending ones together.</summary>");

        static ParenthesizedExpressionSyntax Pending(int i) => ParenthesizedExpression(ConditionalExpression(Id("done" + Digits(i)), Number(0), Number(1)));
    }

    /// <summary>The one box of a struct notification per publish, shared by every handler typed as an interface (OQ-17, KRX037).</summary>
    private static IEnumerable<StatementSyntax> Box(NotificationPipeline pipeline)
    {
        if (pipeline.Boxes)
        {
            yield return Local(TypeName(NotificationInterface), "boxed", Id("notification"));
        }
    }

    /// <summary>
    ///     The direct call of one handler's invoker, from inside the context: handlers have no behaviors of their own (ADR 0001,
    ///     Notifications). It is called on the class for a static invoker, else on the handler instance the context holds; it receives the
    ///     notification itself (a class converts to the handler's base type or interface implicitly), or, for a struct notification and an
    ///     interface-typed handler, the publish's one box.
    /// </summary>
    private static InvocationExpressionSyntax HandlerCall(NotificationPipeline pipeline, int handler)
    {
        var entry = pipeline.Handlers[handler];
        ExpressionSyntax target = entry.Handler.StaticInvoker ? Qualified(entry.Handler.HandlerClass) : Id(HandlerField(handler));
        ExpressionSyntax argument = entry.Boxed
            ? Invoke(Member(Qualified(Unsafe), Generic("As", TypeName(entry.Handler.MessageType))), Id("boxed"))
            : Id("notification");
        return Invoke(Member(target, HandlerAnalysis.InvokerName), argument, Id(CancellationTokenName));
    }

    private static string HandlerField(int handler) => "Handler" + Digits(handler);

    private static string NotificationContextName(int index) => "__Notification" + Digits(index);

    private static string NotificationContextField(int index) => "__notification" + Digits(index);

    private static string CreateNotificationContextName(int index) => "__CreateNotification" + Digits(index);

    private static string PublishCoreName(int index) => "__Publish" + Digits(index);
}
