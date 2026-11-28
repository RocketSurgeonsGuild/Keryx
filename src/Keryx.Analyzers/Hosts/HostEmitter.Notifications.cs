using System.Collections.Immutable;
using System.Text;
using Keryx.Analyzers.Handlers;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Notification dispatch and publish pipelines (ADR 0001, Notifications): typed <c>Publish</c> overloads, generic
///     <c>Publish&lt;TNotification&gt;</c> that never boxes a struct notification, and per notification type a pipeline context holding what
///     its handlers need (the services of the host's own handlers, referenced handlers) and the injected services of its notification behaviors, one flat <c>INext</c> link struct per notification behavior, and the
///     publish strategy (sequential or parallel, run all, then one <c>AggregateException</c>), which calls the handlers directly. A type that
///     resolves nothing (every handler's invoker is static, or it has no handlers, and its behaviors inject nothing) has no context: its
///     strategy and links are static members of a static class (OQ-70).
/// </summary>
internal static partial class HostEmitter
{
    private const string TaskType = "global::System.Threading.Tasks.Task";
    private const string ExceptionType = "global::System.Exception";
    private const string AggregateExceptionType = "global::System.AggregateException";
    private const string ExceptionList = "global::System.Collections.Generic.List<global::System.Exception>";
    private const string NotificationInterface = "global::Keryx.INotification";

    /// <summary><c>(exceptions ??= new())</c>, the failures collected so far.</summary>
    private const string Exceptions = "(exceptions ??= new())";

    /// <summary>
    ///     The typed overloads, one per notification type the host knows, then generic <c>Publish</c>.
    /// </summary>
    /// <param name="writer">The writer, in the mediator's body.</param>
    /// <param name="all">Every notification type the host knows.</param>
    /// <param name="active">The ones something runs for, in core and context order.</param>
    private static void WritePublish(CodeWriter writer, ImmutableArray<NotificationPipeline> all, ImmutableArray<NotificationPipeline> active)
    {
        foreach (var pipeline in all)
        {
            WriteTypedPublish(writer, pipeline, active.IndexOf(pipeline));
        }

        WriteGenericPublish(writer, active);
    }

    private static void WriteTypedPublish(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        writer.Member();
        writer.Documentation(
            "<summary>",
            index < 0
                ? $"    Publishes a <c>{Display(notification.Type)}</c>. Nothing runs for it in this host: it has neither handlers nor notification behaviors."
                : $"    Publishes a <c>{Display(notification.Type)}</c> to its handlers, without type dispatch{( notification.IsValueType ? " and without boxing it" : "" )}.",
            "</summary>",
            "<param name=\"notification\">The notification.</param>",
            "<param name=\"cancellationToken\">The token passed to every behavior and handler. Cancelling it does not skip handlers.</param>",
            "<returns>A task that completes when every handler has run; it faults with one <see cref=\"global::System.AggregateException\" /> when any handler fails.</returns>"
        );
        writer.Line($"public {ValueTask} Publish({notification.Type} notification, {OptionalTokenParameter})");
        writer.Open();
        if (!notification.IsValueType)
        {
            writer.Line($"{ThrowIfNull}(notification);");
        }

        if (!notification.IsSealed)
        {
            // Dispatch is by runtime type: a derived notification statically typed as this one goes to its own pipeline.
            writer.Line($"if (notification.GetType() != typeof({notification.Type}))");
            writer.Open();
            writer.Line($"return Publish<{notification.Type}>(notification, cancellationToken);");
            writer.CloseStatement();
        }

        writer.Line(index < 0 ? "return default;" : $"return {PublishCoreName(index)}(notification, cancellationToken);");
        writer.Close();
    }

    /// <summary>
    ///     Generic <c>Publish</c> (ADR 0001, Generic entry points): with a value-type <c>TNotification</c>, the arms compare
    ///     <c>typeof(TNotification)</c>, which the JIT folds because value-type instantiations are not shared, and convert with
    ///     <c>Unsafe.As&lt;TNotification, X&gt;</c>, so dispatch never boxes a struct notification. Only a reference-type
    ///     <c>TNotification</c> dispatches on <c>GetType()</c> (above <see cref="DispatchThreshold" /> through a <c>FrozenDictionary</c>); a
    ///     struct that arrives boxed (typed as an interface) is unboxed there. Notification types need not be sealed, so that branch keeps
    ///     exact-type guards rather than type patterns (<see cref="WriteExactTypeDispatch" />). Each switch ends with <c>default: return default;</c>:
    ///     an unknown runtime type completes immediately.
    /// </summary>
    private static void WriteGenericPublish(CodeWriter writer, ImmutableArray<NotificationPipeline> active)
    {
        writer.Member();
        writer.Documentation("<inheritdoc />");
        writer.Line($"public {ValueTask} Publish<TNotification>(TNotification notification, {OptionalTokenParameter})");
        writer.Line($"    where TNotification : {NotificationInterface}");
        writer.Open();

        // Not ArgumentNullException.ThrowIfNull(object): that would box a struct notification.
        writer.Line("if (notification is null)");
        writer.Open();
        writer.Line("throw new global::System.ArgumentNullException(nameof(notification));");
        writer.CloseStatement();

        if (active.IsEmpty)
        {
            // Every notification type is unknown to this host or has nothing to run.
            writer.Line("return default;");
        }
        else
        {
            writer.Line("if (typeof(TNotification).IsValueType)");
            writer.Open();
            if (active.Any(static n => n.Notification.IsValueType))
            {
                writer.Switch("typeof(TNotification)");
                for (var i = 0; i < active.Length; i++)
                {
                    var type = active[i].Notification.Type;
                    if (active[i].Notification.IsValueType)
                    {
                        writer.Section(
                            $"case var _ when typeof(TNotification) == typeof({type}):",
                            $"return {PublishCoreName(i)}({Unsafe}.As<TNotification, {type}>(ref notification), cancellationToken);"
                        );
                    }
                }

                writer.Section("default:", "return default;");
                writer.CloseStatement();
            }
            else
            {
                writer.Line("return default;");
            }

            writer.CloseStatement();
            writer.Line("var notificationType = notification.GetType();");
            WriteExactTypeDispatch(
                writer,
                "notificationType",
                "__NotificationIndex",
                active.Select(static n => n.Notification.Type).ToImmutableArray(),
                (_, i) => PublishArm(active[i], i),
                "return default;"
            );
        }

        writer.Close();

        if (active.Length > DispatchThreshold)
        {
            WriteTypeTable(writer, "notification", "__notificationTypes", "__NotificationIndex", active.Select(static n => n.Notification.Type).ToImmutableArray());
        }
    }

    /// <summary>
    ///     The arm of the reference-type branch of generic <c>Publish</c>: a class is reinterpreted with <c>Unsafe.As</c>; a struct arrives boxed
    ///     (the caller's box, through an interface-typed <c>TNotification</c>) and is unboxed.
    /// </summary>
    private static string PublishArm(NotificationPipeline pipeline, int index)
    {
        var type = pipeline.Notification.Type;
        var argument = pipeline.Notification.IsValueType ? $"({type})(object)notification" : $"{Unsafe}.As<{type}>((object)notification)";
        return $"{PublishCoreName(index)}({argument}, cancellationToken)";
    }

    /// <summary>
    ///     The publish pipeline of one notification type, without checks: the typed overload and generic dispatch both call it. It calls the
    ///     outermost notification behavior method directly with the first link, or, without notification behaviors, runs the handlers with the
    ///     publish strategy: on the context, or, for a pipeline without one, the static <c>__NotificationN.RunHandlers</c>.
    /// </summary>
    private static void WritePublishCore(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var hasContext = HasContext(pipeline);
        var context = ContextRead(GetNotificationContextName(index));
        var declaration = $"private {ValueTask} {PublishCoreName(index)}({pipeline.Notification.Type} notification, {TokenParameter})";
        writer.Member();
        if (pipeline.Behaviors.IsEmpty && !hasContext)
        {
            writer.Arrow(declaration, $"{NotificationContextName(index)}.RunHandlers(notification, cancellationToken)");
        }
        else if (pipeline.Behaviors.IsEmpty)
        {
            writer.Line(declaration);
            writer.Open();
            writer.Line($"var {PipelineName} = {context};");
            writer.Line($"return {PipelineName}.RunHandlers(notification, cancellationToken);");
            writer.Close();
        }
        else
        {
            writer.Line(declaration);
            var links = LinkCaptures(pipeline.Behaviors, End(pipeline));
            var call = BehaviorCall(
                pipeline.Behaviors[0],
                pipeline.Services,
                NotificationContextName(index) + "." + LinkName(0),
                links[0],
                "notification"
            );
            WriteEntry(writer, "return " + call + ";", CallCaptures(pipeline.Behaviors[0], links[0]), context, obliviousFit: false);
        }

        if (hasContext)
        {
            WriteContextAccess(
                writer,
                NotificationContextName(index),
                NotificationContextField(index),
                GetNotificationContextName(index),
                CreateNotificationContextName(index)
            );
        }
    }

    /// <summary>
    ///     Whether a publish pipeline has a context (created once per scope): only when it resolves something, a service its handlers or its
    ///     behaviors inject (the context holds the shared services and its referenced handlers), the unkeyed <c>IServiceProvider</c> included
    ///     (ADR 0001, Generated pipeline shape; OQ-70, OQ-63). Otherwise its publish strategy and its links are static members of a static
    ///     class, and the scope state has no field for it.
    /// </summary>
    private static bool HasContext(NotificationPipeline pipeline) =>
        pipeline.Services.Count > 0
     || pipeline.ServiceProvider
     || pipeline.Handlers.Any(static entry => !entry.Handler.StaticInvoker);

    /// <summary>
    ///     What the end of a publish pipeline reads: the notification, for its publish strategy, and the context when the strategy runs on it;
    ///     nothing without handlers (the last link completes at once).
    /// </summary>
    private static Captures End(NotificationPipeline pipeline) =>
        pipeline.Handlers.IsEmpty ? Captures.None : Captures.Message | ( HasContext(pipeline) ? Captures.Pipeline : Captures.None );

    /// <summary>
    ///     The parallel strategy's slow path (ADR 0001, Publish strategy): awaits the handlers that had not completed successfully when they
    ///     were started, then throws one <c>AggregateException</c> with every failure, in handler order.
    /// </summary>
    private static void WriteWhenAllHandlers(CodeWriter writer)
    {
        writer.Member();
        writer.Documentation("<summary>Awaits the pending handlers of a parallel publish and throws every failure, in handler order.</summary>");
        writer.Line(CodeWriter.PoolingBuilderAttribute);
        writer.Line($"private static async {ValueTask} __WhenAllHandlers({TaskType}[] tasks)");
        writer.Open();
        writer.Line("try");
        writer.Open();
        writer.Line($"await {TaskType}.WhenAll(tasks).ConfigureAwait(false);");
        writer.Close();
        writer.Line($"catch ({ExceptionType})");
        writer.Open();
        writer.Line("// Every failure is collected below, in handler order.");
        writer.CloseStatement();
        writer.Line($"{ExceptionList}? exceptions = null;");
        writer.Line("foreach (var task in tasks)");
        writer.Open();
        writer.Line("if (task.IsFaulted)");
        writer.Open();
        writer.Line($"{Exceptions}.AddRange(task.Exception!.InnerExceptions);");
        writer.Close();
        writer.Line("else if (task.IsCanceled)");
        writer.Open();
        WriteTryCollect(writer, "task.GetAwaiter().GetResult();");
        writer.CloseStatement();
        writer.CloseStatement();
        WriteThrowCollected(writer);
        writer.Close();
    }

    /// <summary>
    ///     <c>try { statement } catch (Exception exception) { (exceptions ??= new()).Add(exception); more }</c>: one statement whose failure is
    ///     collected.
    /// </summary>
    private static void WriteTryCollect(CodeWriter writer, string statement, string? more = null)
    {
        writer.Line("try");
        writer.Open();
        writer.Line(statement);
        writer.Close();
        writer.Line($"catch ({ExceptionType} exception)");
        writer.Open();
        writer.Line($"{Exceptions}.Add(exception);");
        if (more is not null)
        {
            writer.Line(more);
        }

        writer.CloseStatement();
    }

    /// <summary><c>if (exceptions is not null) { throw new AggregateException(exceptions); }</c>.</summary>
    private static void WriteThrowCollected(CodeWriter writer)
    {
        writer.Line("if (exceptions is not null)");
        writer.Open();
        writer.Line($"throw new {AggregateExceptionType}(exceptions);");
        writer.CloseStatement();
    }

    /// <summary>
    ///     The pipeline context of one notification type: the services its handlers and its notification behaviors inject, resolved once per
    ///     scope and shared among them (owner decision 43; the host's own handlers receive them through their static entries, decision 44),
    ///     and each referenced handler, constructed with <c>new</c> over them (owner decision 42); the publish strategy, which calls the
    ///     handlers directly; and one flat link struct per notification behavior (<c>Link{k}</c>, whose last one runs the strategy). A type that resolves nothing has no context: its
    ///     publish strategy (static) and its links are nested in a static class of the same name; its last link takes the notification, or
    ///     nothing when there are no handlers.
    /// </summary>
    private static void WriteNotificationContext(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        var context = NotificationContextName(index);
        var hasContext = HasContext(pipeline);
        writer.Member();
        if (hasContext)
        {
            writer.Documentation(
                pipeline.Handlers.IsEmpty
                    ? $"<summary>The publish pipeline context of <c>{Display(notification.Type)}</c>: the injected services of its behaviors, resolved once per scope (it has no handlers).</summary>"
                    : $"<summary>The publish pipeline context of <c>{Display(notification.Type)}</c>: {HandlersHeld(pipeline)}, resolved once per scope, and its publish strategy.</summary>"
            );
            writer.Line($"private sealed class {context}");
            writer.Open();
            var resolution = new List<string>(pipeline.Handlers.Length + pipeline.Services.Count + 1);
            WriteServiceFields(writer, pipeline.Services, pipeline.ServiceProvider, resolution);
            for (var i = 0; i < pipeline.Handlers.Length; i++)
            {
                var entry = pipeline.Handlers[i];
                WriteHandlerField(writer, entry.Handler, entry.ServiceSlots, pipeline.Services, HandlerField(i), resolution);
            }

            WriteContextConstructor(writer, context, resolution);
        }
        else
        {
            writer.Documentation(
                pipeline.Handlers.IsEmpty
                    ? $"<summary>The links of the publish pipeline of <c>{Display(notification.Type)}</c>, which has no context: it has no handlers and its behaviors inject nothing.</summary>"
                    : pipeline.Behaviors.IsEmpty
                    ? $"<summary>The publish strategy of <c>{Display(notification.Type)}</c>, which has no context: its handlers' invokers are static and it has no behaviors.</summary>"
                    : $"<summary>The publish strategy and links of <c>{Display(notification.Type)}</c>, which has no context: its handlers' invokers are static and its behaviors inject nothing.</summary>"
            );
            writer.Line($"private static class {context}");
            writer.Open();
        }

        if (!pipeline.Handlers.IsEmpty)
        {
            if (notification.Parallel)
            {
                WriteParallelStrategy(writer, pipeline, hasContext);
            }
            else
            {
                WriteSequentialStrategy(writer, pipeline, hasContext);
            }
        }

        var links = pipeline.Behaviors.IsEmpty ? [] : LinkCaptures(pipeline.Behaviors, End(pipeline));
        for (var k = 0; k < pipeline.Behaviors.Length; k++)
        {
            var isLast = k == pipeline.Behaviors.Length - 1;
            var body = !isLast
                ? BehaviorCall(pipeline.Behaviors[k + 1], pipeline.Services, LinkName(k + 1), links[k + 1], "notification")
                : pipeline.Handlers.IsEmpty
                ? "default"
                : $"{( hasContext ? PipelineName : context )}.RunHandlers(notification, cancellationToken)";
            WriteLink(
                writer,
                LinkName(k),
                context,
                links[k],
                notification.Type,
                "notification",
                "global::Keryx.INext",
                ValueTask,
                body,
                obliviousFit: false,
                NextSummary(pipeline.Behaviors, k, pipeline.Handlers.IsEmpty ? "nothing (no handlers)" : "the handlers")
            );
        }

        writer.Close();
    }

    /// <summary>
    ///     The sequential strategy (ADR 0001, Publish strategy, Failures): each handler is called directly and awaited in handler order, every
    ///     one runs whatever the others do, and the failures are combined into one <c>AggregateException</c>. Handlers that complete synchronously and
    ///     successfully take a path that allocates nothing; the first one that does not hands over to <c>RunHandlersAsync</c>, which awaits it
    ///     and runs the rest. Both are instance methods of the context, or static members of the static class of a pipeline without one.
    /// </summary>
    private static void WriteSequentialStrategy(CodeWriter writer, NotificationPipeline pipeline, bool hasContext)
    {
        var notification = pipeline.Notification;
        var @static = hasContext ? "" : "static ";
        writer.Member();
        writer.Documentation("<summary>Runs every handler in handler order (sequential), then reports every failure in one AggregateException.</summary>");
        writer.Line($"public {@static}{ValueTask} RunHandlers({notification.Type} notification, {TokenParameter})");
        writer.Open();
        WriteBox(writer, pipeline);
        writer.Line($"{ExceptionList}? exceptions = null;");
        writer.Line($"{ValueTask} task;");
        var boxed = pipeline.Boxes ? "boxed, " : "";
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            WriteTryCollect(writer, $"task = {HandlerCall(pipeline, i)};", "task = default;");
            writer.Line("if (!task.IsCompletedSuccessfully)");
            writer.Open();
            writer.Line($"return RunHandlersAsync(notification, {boxed}task, {Digits(i)}, exceptions, cancellationToken);");
            writer.CloseStatement();
            writer.Line("task.GetAwaiter().GetResult();");
        }

        writer.Line($"return exceptions is null ? default : {ValueTask}.FromException(new {AggregateExceptionType}(exceptions));");
        writer.Close();

        var parameters = pipeline.Boxes
            ? $"{notification.Type} notification, {NotificationInterface} boxed, {ValueTask} pending, int position, {ExceptionList}? exceptions, {TokenParameter}"
            : $"{notification.Type} notification, {ValueTask} pending, int position, {ExceptionList}? exceptions, {TokenParameter}";
        writer.Member();
        writer.Documentation("<summary>Awaits the handler at <paramref name=\"position\" />, runs the ones after it in order, then reports every failure.</summary>");
        writer.Line(CodeWriter.PoolingBuilderAttribute);
        writer.Line($"private {@static}async {ValueTask} RunHandlersAsync({parameters})");
        writer.Open();
        WriteTryCollect(writer, "await pending.ConfigureAwait(false);");
        for (var i = 1; i < pipeline.Handlers.Length; i++)
        {
            writer.Line($"if (position < {Digits(i)})");
            writer.Open();
            WriteTryCollect(writer, $"await {HandlerCall(pipeline, i)}.ConfigureAwait(false);");
            writer.CloseStatement();
        }

        WriteThrowCollected(writer);
        writer.Close();
    }

    /// <summary>
    ///     The parallel strategy (ADR 0001, Publish strategy): every handler is started in handler order on the calling thread without
    ///     awaiting it (no <c>Task.Run</c>), a synchronous throw is recorded as a faulted task and the rest still start. When every handler
    ///     completed successfully, nothing is allocated; otherwise one array plus one <c>AsTask()</c> per pipeline that did not, awaited
    ///     together, with every failure in handler order. An instance method of the context, or a static member of the static class of a
    ///     pipeline without one.
    /// </summary>
    private static void WriteParallelStrategy(CodeWriter writer, NotificationPipeline pipeline, bool hasContext)
    {
        var count = pipeline.Handlers.Length;
        writer.Member();
        writer.Documentation("<summary>Starts every handler in handler order without awaiting it (parallel), then awaits the pending ones together.</summary>");
        writer.Line($"public {( hasContext ? "" : "static " )}{ValueTask} RunHandlers({pipeline.Notification.Type} notification, {TokenParameter})");
        writer.Open();
        WriteBox(writer, pipeline);
        for (var i = 0; i < count; i++)
        {
            var task = "task" + Digits(i);
            writer.Line($"{ValueTask} {task};");
            writer.Line("try");
            writer.Open();
            writer.Line($"{task} = {HandlerCall(pipeline, i)};");
            writer.Close();
            writer.Line($"catch ({ExceptionType} exception)");
            writer.Open();
            writer.Line($"{task} = {ValueTask}.FromException(exception);");
            writer.CloseStatement();
        }

        // Each status is read once: a pending handler may complete between two reads.
        for (var i = 0; i < count; i++)
        {
            writer.Line($"var done{Digits(i)} = task{Digits(i)}.IsCompletedSuccessfully;");
            writer.Line($"if (done{Digits(i)})");
            writer.Open();
            writer.Line($"task{Digits(i)}.GetAwaiter().GetResult();");
            writer.CloseStatement();
        }

        var pending = new StringBuilder(16 * count);
        for (var i = 0; i < count; i++)
        {
            pending.Append(i == 0 ? "(done" : " + (done").Append(Digits(i)).Append(" ? 0 : 1)");
        }

        writer.Line($"var pending = {pending};");
        writer.Line("if (pending == 0)");
        writer.Open();
        writer.Line("return default;");
        writer.CloseStatement();
        writer.Line($"var tasks = new {TaskType}[pending];");
        writer.Line("var next = 0;");
        for (var i = 0; i < count; i++)
        {
            writer.Line($"if (!done{Digits(i)})");
            writer.Open();
            writer.Line($"tasks[next++] = task{Digits(i)}.AsTask();");
            writer.CloseStatement();
        }

        writer.Line("return __WhenAllHandlers(tasks);");
        writer.Close();
    }

    /// <summary>The one box of a struct notification per publish, shared by every handler typed as an interface (OQ-17, KRX037).</summary>
    private static void WriteBox(CodeWriter writer, NotificationPipeline pipeline)
    {
        if (pipeline.Boxes)
        {
            writer.Line($"{NotificationInterface} boxed = notification;");
        }
    }

    /// <summary>
    ///     The direct call of one handler, from inside the context: handlers have no behaviors of their own (ADR 0001, Notifications). A static
    ///     invoker and the host's own handler's static entry (with the context's shared services it injects) are called on the class, a
    ///     referenced handler on the instance the context holds (<see cref="HandlerInvocation" />); it receives the notification itself (a
    ///     class converts to the handler's base type or interface implicitly), or, for a struct notification and an interface-typed handler,
    ///     the publish's one box.
    /// </summary>
    private static string HandlerCall(NotificationPipeline pipeline, int handler)
    {
        var entry = pipeline.Handlers[handler];
        var argument = entry.Boxed ? $"{Unsafe}.As<{entry.Handler.MessageType}>(boxed)" : "notification";
        return HandlerInvocation(entry.Handler, entry.ServiceSlots, pipeline.Services, "", HandlerField(handler), argument);
    }

    /// <summary>
    ///     What a publish pipeline context with handlers holds, for its documentation: its referenced handlers, and the services its handlers
    ///     and behaviors inject, shared among them.
    /// </summary>
    private static string HandlersHeld(NotificationPipeline pipeline)
    {
        var handlers = pipeline.Handlers.Any(static h => !h.Handler.StaticInvoker);
        var referenced = pipeline.Handlers.Any(static h => h.Handler is { StaticInvoker: false, StaticEntry: false });
        var behaviors = pipeline.Behaviors.Any(static b => b.Services.Count > 0);
        var injected = handlers && behaviors ? "the services its handlers and its behaviors inject"
            : handlers ? "the services its handlers inject"
            : "the injected services of its behaviors";
        return referenced ? "its referenced handlers and " + injected : injected;
    }

    private static string HandlerField(int handler) => "Handler" + Digits(handler);

    private static string NotificationContextName(int index) => "__Notification" + Digits(index);

    private static string NotificationContextField(int index) => "__notification" + Digits(index);

    private static string GetNotificationContextName(int index) => "__GetNotification" + Digits(index);

    private static string CreateNotificationContextName(int index) => "__CreateNotification" + Digits(index);

    private static string PublishCoreName(int index) => "__Publish" + Digits(index);
}
