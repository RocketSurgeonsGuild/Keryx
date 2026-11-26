using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;

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
    /// <param name="writer">The writer.</param>
    /// <param name="all">Every notification type the host knows.</param>
    /// <param name="active">The ones something runs for, in core and context order.</param>
    private static void WritePublish(CodeWriter writer, ImmutableArray<NotificationPipeline> all, ImmutableArray<NotificationPipeline> active)
    {
        foreach (var pipeline in all)
        {
            writer.Line();
            WriteTypedPublish(writer, pipeline, active.IndexOf(pipeline));
        }

        writer.Line();
        WriteGenericPublish(writer, active);
    }

    private static void WriteTypedPublish(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        writer.Line("/// <summary>");
        writer.Line(
            index < 0
                ? $"///     Publishes a <c>{Display(notification.Type)}</c>. Nothing runs for it in this host: it has neither handlers nor notification behaviors."
                : $"///     Publishes a <c>{Display(notification.Type)}</c> to its handlers, without type dispatch{( notification.IsValueType ? " and without boxing it" : "" )}."
        );
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"notification\">The notification.</param>");
        writer.Line("/// <param name=\"cancellationToken\">The token passed to every behavior and handler. Cancelling it does not skip handlers.</param>");
        writer.Line("/// <returns>A task that completes when every handler has run; it faults with one <see cref=\"global::System.AggregateException\" /> when any handler fails.</returns>");
        writer.Line($"public {ValueTask} Publish({notification.Type} notification, {CancellationToken} cancellationToken = default)");
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
            writer.Close();
            writer.Line();
        }

        writer.Line(index < 0 ? "return default;" : $"return {PublishCoreName(index)}(notification, cancellationToken);");
        writer.Close();
    }

    /// <summary>
    ///     Generic <c>Publish</c> (ADR 0001, Generic entry points): with a value-type <c>TNotification</c>, the arms compare
    ///     <c>typeof(TNotification)</c>, which the JIT folds because value-type instantiations are not shared, and convert with
    ///     <c>Unsafe.As&lt;TNotification, X&gt;</c>, so dispatch never boxes a struct notification. Only a reference-type
    ///     <c>TNotification</c> dispatches on <c>GetType()</c> (above <see cref="DispatchThreshold" /> through a <c>FrozenDictionary</c>); a
    ///     struct that arrives boxed (typed as an interface) is unboxed there. An unknown runtime type completes immediately.
    /// </summary>
    private static void WriteGenericPublish(CodeWriter writer, ImmutableArray<NotificationPipeline> active)
    {
        writer.Line("/// <inheritdoc />");
        writer.Line($"public {ValueTask} Publish<TNotification>(TNotification notification, {CancellationToken} cancellationToken = default)");
        writer.Line($"    where TNotification : {NotificationInterface}");
        writer.Open();
        // Not ArgumentNullException.ThrowIfNull(object): that would box a struct notification.
        writer.Line("if (notification is null)");
        writer.Open();
        writer.Line("throw new global::System.ArgumentNullException(nameof(notification));");
        writer.Close();
        writer.Line();
        if (active.IsEmpty)
        {
            // Every notification type is unknown to this host or has nothing to run.
            writer.Line("return default;");
            writer.Close();
            return;
        }

        writer.Line("if (typeof(TNotification).IsValueType)");
        writer.Open();
        for (var i = 0; i < active.Length; i++)
        {
            var type = active[i].Notification.Type;
            if (active[i].Notification.IsValueType)
            {
                writer.Line($"if (typeof(TNotification) == typeof({type}))");
                writer.Open();
                writer.Line($"return {PublishCoreName(i)}({Unsafe}.As<TNotification, {type}>(ref notification), cancellationToken);");
                writer.Close();
                writer.Line();
            }
        }

        writer.Line("return default;");
        writer.Close();
        writer.Line();
        writer.Line("var notificationType = notification.GetType();");
        if (active.Length > DispatchThreshold)
        {
            writer.Line("switch (__NotificationIndex(notificationType))");
            writer.Open();
            for (var i = 0; i < active.Length; i++)
            {
                writer.Line($"case {Number(i)}:");
                writer.Line($"    return {PublishArm(active[i], i)};");
            }

            writer.Line("default:");
            writer.Line("    return default;");
            writer.Close();
        }
        else
        {
            for (var i = 0; i < active.Length; i++)
            {
                writer.Line($"if (notificationType == typeof({active[i].Notification.Type}))");
                writer.Open();
                writer.Line($"return {PublishArm(active[i], i)};");
                writer.Close();
                writer.Line();
            }

            writer.Line("return default;");
        }

        writer.Close();

        if (active.Length > DispatchThreshold)
        {
            writer.Line();
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
    ///     publish strategy.
    /// </summary>
    private static void WritePublishCore(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        writer.Line($"private {ValueTask} {PublishCoreName(index)}({notification.Type} notification, {CancellationToken} cancellationToken)");
        writer.Open();
        writer.Line($"var pipeline = global::System.Threading.Volatile.Read(ref {NotificationContextField(index)}) ?? {CreateNotificationContextName(index)}();");
        writer.Line(
            pipeline.Behaviors.IsEmpty
                ? "return pipeline.RunHandlers(notification, cancellationToken);"
                : $"return {BehaviorCall(pipeline.Behaviors[0], 0, NotificationContextName(index) + "." + LinkName(0), "pipeline", "notification")};"
        );
        writer.Close();

        // Concurrent first use in one scope: every caller ends up with the context that won the exchange (ADR 0001, Mediator).
        writer.Line();
        writer.Line(NoInlining);
        writer.Line($"private {NotificationContextName(index)} {CreateNotificationContextName(index)}() =>");
        writer.Line(
            $"    global::System.Threading.Interlocked.CompareExchange(ref {NotificationContextField(index)}, new {NotificationContextName(index)}(__serviceProvider), null) ?? {NotificationContextField(index)}!;"
        );
    }

    /// <summary>
    ///     The parallel strategy's slow path (ADR 0001, Publish strategy): awaits the handlers that had not completed successfully when they
    ///     were started, then throws one <c>AggregateException</c> with every failure, in handler order.
    /// </summary>
    private static void WriteWhenAllHandlers(CodeWriter writer)
    {
        writer.Line("/// <summary>Awaits the pending handlers of a parallel publish and throws every failure, in handler order.</summary>");
        writer.Line("[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]");
        writer.Line($"private static async {ValueTask} __WhenAllHandlers({TaskType}[] tasks)");
        writer.Open();
        writer.Line("try");
        writer.Open();
        writer.Line($"await {TaskType}.WhenAll(tasks).ConfigureAwait(false);");
        writer.Close();
        writer.Line($"catch ({ExceptionType})");
        writer.Open();
        writer.Line("// Every failure is collected below, in handler order.");
        writer.Close();
        writer.Line();
        writer.Line($"{ExceptionList}? exceptions = null;");
        writer.Line("foreach (var task in tasks)");
        writer.Open();
        writer.Line("if (task.IsFaulted)");
        writer.Open();
        writer.Line("(exceptions ??= new()).AddRange(task.Exception!.InnerExceptions);");
        writer.Close();
        writer.Line("else if (task.IsCanceled)");
        writer.Open();
        writer.Line("try");
        writer.Open();
        writer.Line("task.GetAwaiter().GetResult();");
        writer.Close();
        writer.Line($"catch ({ExceptionType} exception)");
        writer.Open();
        writer.Line("(exceptions ??= new()).Add(exception);");
        writer.Close();
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("if (exceptions is not null)");
        writer.Open();
        writer.Line($"throw new {AggregateExceptionType}(exceptions);");
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     The pipeline context of one notification type: its handlers that have injected parameters and the injected services of its
    ///     notification behaviors, resolved once per scope; the publish strategy, which calls the handlers directly; and one flat link struct
    ///     per notification behavior (<c>Link{k}</c>, whose last one runs the strategy).
    /// </summary>
    private static void WriteNotificationContext(CodeWriter writer, NotificationPipeline pipeline, int index)
    {
        var notification = pipeline.Notification;
        var context = NotificationContextName(index);
        writer.Line(
            $"/// <summary>The publish pipeline context of <c>{Display(notification.Type)}</c>: its handlers and the injected services of its behaviors, resolved once per scope, and its publish strategy.</summary>"
        );
        writer.Line($"private sealed class {context}");
        writer.Open();
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            if (!pipeline.Handlers[i].Handler.StaticInvoker)
            {
                writer.Line($"public readonly {pipeline.Handlers[i].Handler.HandlerClass} {HandlerField(i)};");
            }
        }

        WriteServiceFields(writer, pipeline.Behaviors);
        writer.Line();
        writer.Line($"public {context}({ServiceProvider} {ServiceProviderName})");
        writer.Open();
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            if (!pipeline.Handlers[i].Handler.StaticInvoker)
            {
                writer.Line($"{HandlerField(i)} = {ServiceProviderExtensions}.GetRequiredService<{pipeline.Handlers[i].Handler.HandlerClass}>({ServiceProviderName});");
            }
        }

        WriteServiceResolution(writer, pipeline.Behaviors);
        writer.Close();

        if (!pipeline.Handlers.IsEmpty)
        {
            writer.Line();
            if (notification.Parallel)
            {
                WriteParallelStrategy(writer, pipeline);
            }
            else
            {
                WriteSequentialStrategy(writer, pipeline);
            }
        }

        for (var k = 0; k < pipeline.Behaviors.Length; k++)
        {
            writer.Line();
            WritePublishLink(writer, pipeline, index, k);
        }

        writer.Close();
    }

    /// <summary>
    ///     The sequential strategy (ADR 0001, Publish strategy, Failures): each handler is called directly and awaited in handler order, every
    ///     one runs whatever the others do, and the failures are combined into one <c>AggregateException</c>. Handlers that complete synchronously and
    ///     successfully take a path that allocates nothing; the first one that does not hands over to <c>RunHandlersAsync</c>, which awaits it
    ///     and runs the rest.
    /// </summary>
    private static void WriteSequentialStrategy(CodeWriter writer, NotificationPipeline pipeline)
    {
        var notification = pipeline.Notification;
        var boxParameter = pipeline.Boxes ? $"{NotificationInterface} boxed, " : "";
        var boxArgument = pipeline.Boxes ? "boxed, " : "";
        writer.Line("/// <summary>Runs every handler in handler order (sequential), then reports every failure in one AggregateException.</summary>");
        writer.Line($"public {ValueTask} RunHandlers({notification.Type} notification, {CancellationToken} cancellationToken)");
        writer.Open();
        WriteBox(writer, pipeline);
        writer.Line($"{ExceptionList}? exceptions = null;");
        writer.Line($"{ValueTask} task;");
        for (var i = 0; i < pipeline.Handlers.Length; i++)
        {
            writer.Line("try");
            writer.Open();
            writer.Line($"task = {HandlerCall(pipeline, i)};");
            writer.Close();
            writer.Line($"catch ({ExceptionType} exception)");
            writer.Open();
            writer.Line("(exceptions ??= new()).Add(exception);");
            writer.Line("task = default;");
            writer.Close();
            writer.Line();
            writer.Line("if (!task.IsCompletedSuccessfully)");
            writer.Open();
            writer.Line($"return RunHandlersAsync(notification, {boxArgument}task, {Number(i)}, exceptions, cancellationToken);");
            writer.Close();
            writer.Line();
            writer.Line("task.GetAwaiter().GetResult();");
        }

        writer.Line();
        writer.Line($"return exceptions is null ? default : {ValueTask}.FromException(new {AggregateExceptionType}(exceptions));");
        writer.Close();

        writer.Line();
        writer.Line("/// <summary>Awaits the handler at <paramref name=\"position\" />, runs the ones after it in order, then reports every failure.</summary>");
        writer.Line("[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]");
        writer.Line(
            $"private async {ValueTask} RunHandlersAsync({notification.Type} notification, {boxParameter}{ValueTask} pending, int position, {ExceptionList}? exceptions, {CancellationToken} cancellationToken)"
        );
        writer.Open();
        writer.Line("try");
        writer.Open();
        writer.Line("await pending.ConfigureAwait(false);");
        writer.Close();
        writer.Line($"catch ({ExceptionType} exception)");
        writer.Open();
        writer.Line("(exceptions ??= new()).Add(exception);");
        writer.Close();
        for (var i = 1; i < pipeline.Handlers.Length; i++)
        {
            writer.Line();
            writer.Line($"if (position < {Number(i)})");
            writer.Open();
            writer.Line("try");
            writer.Open();
            writer.Line($"await {HandlerCall(pipeline, i)}.ConfigureAwait(false);");
            writer.Close();
            writer.Line($"catch ({ExceptionType} exception)");
            writer.Open();
            writer.Line("(exceptions ??= new()).Add(exception);");
            writer.Close();
            writer.Close();
        }

        writer.Line();
        writer.Line("if (exceptions is not null)");
        writer.Open();
        writer.Line($"throw new {AggregateExceptionType}(exceptions);");
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     The parallel strategy (ADR 0001, Publish strategy): every handler is started in handler order on the calling thread without
    ///     awaiting it (no <c>Task.Run</c>), a synchronous throw is recorded as a faulted task and the rest still start. When every handler
    ///     completed successfully, nothing is allocated; otherwise one array plus one <c>AsTask()</c> per pipeline that did not, awaited
    ///     together, with every failure in handler order.
    /// </summary>
    private static void WriteParallelStrategy(CodeWriter writer, NotificationPipeline pipeline)
    {
        var notification = pipeline.Notification;
        var count = pipeline.Handlers.Length;
        writer.Line("/// <summary>Starts every handler in handler order without awaiting it (parallel), then awaits the pending ones together.</summary>");
        writer.Line($"public {ValueTask} RunHandlers({notification.Type} notification, {CancellationToken} cancellationToken)");
        writer.Open();
        WriteBox(writer, pipeline);
        for (var i = 0; i < count; i++)
        {
            var task = "task" + Number(i);
            writer.Line($"{ValueTask} {task};");
            writer.Line("try");
            writer.Open();
            writer.Line($"{task} = {HandlerCall(pipeline, i)};");
            writer.Close();
            writer.Line($"catch ({ExceptionType} exception)");
            writer.Open();
            writer.Line($"{task} = {ValueTask}.FromException(exception);");
            writer.Close();
            writer.Line();
        }

        // Each status is read once: a pending handler may complete between two reads.
        for (var i = 0; i < count; i++)
        {
            writer.Line($"var done{Number(i)} = task{Number(i)}.IsCompletedSuccessfully;");
            writer.Line($"if (done{Number(i)})");
            writer.Open();
            writer.Line($"task{Number(i)}.GetAwaiter().GetResult();");
            writer.Close();
            writer.Line();
        }

        writer.Line($"var pending = {string.Join(" + ", Enumerable.Range(0, count).Select(static i => $"(done{Number(i)} ? 0 : 1)"))};");
        writer.Line("if (pending == 0)");
        writer.Open();
        writer.Line("return default;");
        writer.Close();
        writer.Line();
        writer.Line($"var tasks = new {TaskType}[pending];");
        writer.Line("var next = 0;");
        for (var i = 0; i < count; i++)
        {
            writer.Line($"if (!done{Number(i)})");
            writer.Open();
            writer.Line($"tasks[next++] = task{Number(i)}.AsTask();");
            writer.Close();
            writer.Line();
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
    ///     The direct call of one handler's invoker, from inside the context: handlers have no behaviors of their own (ADR 0001,
    ///     Notifications).
    /// </summary>
    private static string HandlerCall(NotificationPipeline pipeline, int handler)
    {
        var entry = pipeline.Handlers[handler];
        return $"{HandlerTarget(entry, handler, null)}.{HandlerAnalysis.InvokerName}({HandlerArgument(entry, "notification", "boxed")}, cancellationToken)";
    }

    /// <summary>
    ///     What a handler's invoker is called on: the class for a static invoker, else the handler instance the context holds (a field of the
    ///     context itself when <paramref name="context" /> is <see langword="null" />).
    /// </summary>
    private static string HandlerTarget(NotificationHandlerPipeline entry, int handler, string? context) =>
        entry.Handler.StaticInvoker ? entry.Handler.HandlerClass : context is null ? HandlerField(handler) : $"{context}.{HandlerField(handler)}";

    /// <summary>
    ///     What a handler's invoker receives: the notification itself (a class converts to the handler's base type or interface implicitly),
    ///     or, for a struct notification and an interface-typed handler, the publish's one box.
    /// </summary>
    private static string HandlerArgument(NotificationHandlerPipeline entry, string notification, string boxed) =>
        entry.Boxed ? $"{Unsafe}.As<{entry.Handler.MessageType}>({boxed})" : notification;

    /// <summary>
    ///     One link of the publish pipeline: the <c>next</c> of notification behavior <c>k</c>. It calls behavior <c>k + 1</c>'s method
    ///     directly, and the last one runs the publish strategy (or completes, when the type has no handlers).
    /// </summary>
    private static void WritePublishLink(CodeWriter writer, NotificationPipeline pipeline, int index, int position)
    {
        var notification = pipeline.Notification;
        var context = NotificationContextName(index);
        var link = LinkName(position);
        var isLast = position == pipeline.Behaviors.Length - 1;
        var holds = !isLast || !pipeline.Handlers.IsEmpty;
        writer.Line(
            isLast
                ? $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: {( pipeline.Handlers.IsEmpty ? "nothing (no handlers)" : "the handlers" )}.</summary>"
                : $"/// <summary>The <c>next</c> of <c>{Display(pipeline.Behaviors[position].Identity)}</c>: <c>{Display(pipeline.Behaviors[position + 1].Identity)}</c>.</summary>"
        );
        writer.Line($"public readonly struct {link} : global::Keryx.INext");
        writer.Open();
        if (holds)
        {
            writer.Line($"private readonly {context} _pipeline;");
            writer.Line($"private readonly {notification.Type} _notification;");
            writer.Line();
        }

        writer.Line($"public {link}({context} pipeline, {notification.Type} notification)");
        writer.Open();
        if (holds)
        {
            writer.Line("_pipeline = pipeline;");
            writer.Line("_notification = notification;");
        }

        writer.Close();
        writer.Line();
        writer.Line($"public {ValueTask} InvokeAsync({CancellationToken} cancellationToken) =>");
        writer.Line(
            !isLast
                ? $"    {BehaviorCall(pipeline.Behaviors[position + 1], position + 1, LinkName(position + 1), "_pipeline", "_notification")};"
                : pipeline.Handlers.IsEmpty
                ? "    default;"
                : "    _pipeline.RunHandlers(_notification, cancellationToken);"
        );
        writer.Close();
    }

    private static string HandlerField(int handler) => "Handler" + Number(handler);

    private static string NotificationContextName(int index) => "__Notification" + Number(index);

    private static string NotificationContextField(int index) => "__notification" + Number(index);

    private static string CreateNotificationContextName(int index) => "__CreateNotification" + Number(index);

    private static string PublishCoreName(int index) => "__Publish" + Number(index);
}
