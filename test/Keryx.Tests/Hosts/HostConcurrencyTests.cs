using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Concurrent first use of one scope (ADR 0001, Mediator): mediators are transient, and every mediator of a scope reads the pipeline
///     contexts from the scope's one state (owner decision 46), one field per pipeline, read with <c>Volatile.Read</c> and set on first use
///     with <c>Interlocked.CompareExchange</c>, so every caller in the scope, through any of its mediators, ends up with the same context and
///     the same handler, also when several pipelines are first used at once.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HostConcurrencyTests : HostTest
{
    private const int Callers = 16;
    private const int Scopes = 50;

    /// <summary>The pipelines with a context that race in one scope.</summary>
    private const int Pipelines = 6;

    /// <summary>The mediators each scope resolves; each caller sends through one of them.</summary>
    private const int Mediators = 4;

    /// <summary>
    ///     <see cref="Pipelines" /> requests, <c>Sample.Who0</c> to <c>Who5</c>, each with a handler that answers the <c>Identity</c> it was
    ///     constructed with.
    /// </summary>
    private static string ManyPipelines()
    {
        var source = new System.Text.StringBuilder(
            """
            namespace Sample;

            /// <summary>Counts the instances made, so tests can tell how often the container constructed it.</summary>
            public sealed class Identity
            {
                public static int Created;
                public int Id { get; } = Interlocked.Increment(ref Created);
            }

            """
        );
        for (var i = 0; i < Pipelines; i++)
        {
            source.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $$"""
                  public sealed record Who{{i}} : IRequest<int>;
                  public partial class Who{{i}}Handler { private static int Handle(Who{{i}} request, Identity identity) => identity.Id; }

                  """
            );
        }

        return source.ToString();
    }

    [Test]
    public async Task Concurrent_First_Use_In_One_Scope_Shares_One_Pipeline_Context(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(HostRuntimeTests.Source, cancellationToken);
        var handlerType = host.GetType("Sample.WhoAmIHandler", throwOnError: true)!;

        // A transient handler (AddKeryx keeps an existing registration) makes every context construction observable: each context built
        // gets a new handler with a new Identity, so only a race that keeps a single context gives every caller the same identity.
        await using var provider = BuildProvider(host, services => HostRuntimeTests.RegistrationsWithTransientIdentityHandlers(host)(services));
        var state = new ScopeState(host);
        var whoAmIType = host.GetType("Sample.WhoAmI", throwOnError: true)!;
        var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
        var created = identityType.GetField("Created", BindingFlags.Public | BindingFlags.Static)!;
        var identityId = identityType.GetProperty("Id")!;
        var handlerIdentity = handlerType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == identityType);
        var typedSend = state.MediatorType.GetMethod("Send", [whoAmIType, typeof(CancellationToken)])!;

        for (var scopeIndex = 0; scopeIndex < Scopes; scopeIndex++)
        {
            await using var scope = provider.CreateAsyncScope();
            var mediators = Enumerable.Range(0, Mediators).Select(_ => scope.ServiceProvider.GetRequiredService(state.MediatorType)).ToArray();
            var whoAmI = (IRequest<int>)Activator.CreateInstance(whoAmIType)!;
            await Assert.That(state.Contexts(scope.ServiceProvider)).IsEmpty();
            var createdBefore = (int)created.GetValue(null)!;

            using var start = new Barrier(Callers);
            var callers = new Task<int>[Callers];
            for (var i = 0; i < Callers; i++)
            {
                // The callers spread over the scope's mediators; half use the generic entry point, half the typed overload.
                var mediator = mediators[i % Mediators];
                var generic = i / Mediators % 2 == 0;
                callers[i] = Task.Factory.StartNew(
                    () =>
                    {
                        start.SignalAndWait(cancellationToken);
                        return generic
                            ? ( (ISender)mediator ).Send(whoAmI, cancellationToken).AsTask().GetAwaiter().GetResult()
                            : ( (ValueTask<int>)typedSend.Invoke(mediator, [whoAmI, cancellationToken])! ).AsTask().GetAwaiter().GetResult();
                    },
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            var identities = await Task.WhenAll(callers);

            // Every caller used the context the state kept: one identity, the one held by that context's handler. Losers may have built a
            // context each (at most one per caller), but none of them was used.
            await Assert.That(identities.Distinct()).HasSingleItem();
            var context = await Assert.That(state.Contexts(scope.ServiceProvider)).HasSingleItem();
            var storedIdentity = handlerIdentity.GetValue(context!.GetType().GetField("Handler")!.GetValue(context));
            await Assert.That((int)identityId.GetValue(storedIdentity)!).IsEqualTo(identities[0]);
            await Assert.That((int)created.GetValue(null)! - createdBefore).IsBetween(1, Callers);

            // Later calls, through those mediators or a new one, reuse that context: no new handler, the same identity.
            foreach (var mediator in mediators.Append(scope.ServiceProvider.GetRequiredService<ISender>()))
            {
                await Assert.That(await ( (ISender)mediator ).Send(whoAmI, cancellationToken)).IsEqualTo(identities[0]);
            }

            await Assert.That(state.Contexts(scope.ServiceProvider)).HasSingleItem().And.Contains(context);
        }
    }

    [Test]
    public async Task Pipelines_Racing_In_One_Scope_Each_Keep_One_Context(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(ManyPipelines(), cancellationToken);

        // Transient handlers, so every context construction gets a new Identity (see above).
        await using var provider = BuildProvider(
            host,
            services =>
            {
                var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
                services.AddTransient(identityType);
                for (var i = 0; i < Pipelines; i++)
                {
                    var handlerType = host.GetType($"Sample.Who{i}Handler", throwOnError: true)!;
                    services.AddTransient(handlerType, sp => Activator.CreateInstance(handlerType, sp.GetRequiredService(identityType))!);
                }
            }
        );
        var state = new ScopeState(host);
        await Assert.That(state.Fields).IsEqualTo(Pipelines);
        var requests = Enumerable.Range(0, Pipelines).Select(i => (IRequest<int>)Create(host, $"Sample.Who{i}")).ToArray();

        for (var scopeIndex = 0; scopeIndex < Scopes; scopeIndex++)
        {
            await using var scope = provider.CreateAsyncScope();
            var mediators = Enumerable.Range(0, Mediators).Select(_ => scope.ServiceProvider.GetRequiredService<ISender>()).ToArray();

            // Every pipeline is first used by several callers at once, each through one of the scope's mediators: the pipelines race each
            // other for the scope state, and the callers of one pipeline race each other for its context.
            var callers = Pipelines * Mediators;
            using var start = new Barrier(callers);
            var sends = new Task<(int Pipeline, int Identity)>[callers];
            for (var i = 0; i < callers; i++)
            {
                var pipeline = i % Pipelines;
                var sender = mediators[i / Pipelines];
                sends[i] = Task.Factory.StartNew(
                    () =>
                    {
                        start.SignalAndWait(cancellationToken);
                        return (pipeline, sender.Send(requests[pipeline], cancellationToken).AsTask().GetAwaiter().GetResult());
                    },
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            var results = await Task.WhenAll(sends);

            // One identity per pipeline, a different one for each pipeline.
            var identities = results.GroupBy(static r => r.Pipeline).ToDictionary(static g => g.Key, static g => g.Select(static r => r.Identity).Distinct().ToArray());
            await Assert.That(identities.Count).IsEqualTo(Pipelines);
            await Assert.That(identities.Values.All(static ids => ids.Length == 1)).IsTrue();
            await Assert.That(identities.Values.Select(static ids => ids[0]).Distinct().Count()).IsEqualTo(Pipelines);

            // The state kept one context per pipeline, each in that pipeline's field.
            var kept = state.FieldValues(scope.ServiceProvider);
            await Assert.That(kept.Length).IsEqualTo(Pipelines);
            for (var position = 0; position < Pipelines; position++)
            {
                await Assert.That(kept[position]?.GetType().Name).IsEqualTo($"__Pipeline{position}");
            }

            // Later calls through a new mediator reuse the kept contexts.
            var later = scope.ServiceProvider.GetRequiredService<IMediator>();
            for (var pipeline = 0; pipeline < Pipelines; pipeline++)
            {
                await Assert.That(await later.Send(requests[pipeline], cancellationToken)).IsEqualTo(identities[pipeline][0]).Because($"Who{pipeline}");
            }

            await Assert.That(state.FieldValues(scope.ServiceProvider)).IsEquivalentTo(kept, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    /// <summary>
    ///     Every kind of pipeline with a context, two of each, all of whose handlers answer the <c>Identity</c> they were constructed with:
    ///     requests <c>Ask0</c> and <c>Ask2</c> (with <c>Ask1</c>, a static handler without a context, between them), streams <c>Flow0</c> and
    ///     <c>Flow1</c>, and notifications <c>Told0</c> and <c>Told1</c> (whose handlers record it in the notification).
    /// </summary>
    private const string MixedPipelines = """
        namespace Sample;

        /// <summary>Counts the instances made, so tests can tell how often the container constructed it.</summary>
        public sealed class Identity
        {
            public static int Created;
            public int Id { get; } = Interlocked.Increment(ref Created);
        }

        public sealed record Ask0 : IRequest<int>;
        public partial class Ask0Handler { private static int Handle(Ask0 request, Identity identity) => identity.Id; }
        public sealed record Ask1 : IRequest<int>;
        public static partial class Ask1Handler { private static int Handle(Ask1 request) => -1; }
        public sealed record Ask2 : IRequest<int>;
        public partial class Ask2Handler { private static int Handle(Ask2 request, Identity identity) => identity.Id; }

        public static class Items
        {
            public static async IAsyncEnumerable<int> Once(int value)
            {
                await Task.Yield();
                yield return value;
            }
        }

        public sealed record Flow0 : IStreamRequest<int>;
        public partial class Flow0Handler { private static IAsyncEnumerable<int> Handle(Flow0 request, Identity identity) => Items.Once(identity.Id); }
        public sealed record Flow1 : IStreamRequest<int>;
        public partial class Flow1Handler { private static IAsyncEnumerable<int> Handle(Flow1 request, Identity identity) => Items.Once(identity.Id); }

        public sealed record Told0(System.Collections.Concurrent.ConcurrentQueue<int> Ids) : INotification;
        public partial class Told0Handler
        {
            private static ValueTask Handle(Told0 notification, Identity identity)
            {
                notification.Ids.Enqueue(identity.Id);
                return default;
            }
        }

        public sealed record Told1(System.Collections.Concurrent.ConcurrentQueue<int> Ids) : INotification;
        public partial class Told1Handler
        {
            private static ValueTask Handle(Told1 notification, Identity identity)
            {
                notification.Ids.Enqueue(identity.Id);
                return default;
            }
        }
        """;

    [Test]
    public async Task Request_Stream_And_Publish_Pipelines_Racing_In_One_Scope_Each_Keep_Their_Own_Context(CancellationToken cancellationToken)
    {
        // The state's fields are object and each accessor reinterprets its own with Unsafe.As, so a field holding another pipeline's context
        // would misbehave silently instead of throwing: check, by name, that every field holds the context of its own pipeline after
        // requests, streams and publishes, each first used at once through several mediators, raced for the one state.
        var host = await CompileHostAsync(MixedPipelines, cancellationToken);
        string[] handlers = ["Ask0", "Ask2", "Flow0", "Flow1", "Told0", "Told1"];
        await using var provider = BuildProvider(
            host,
            services =>
            {
                var identityType = host.GetType("Sample.Identity", throwOnError: true)!;
                services.AddTransient(identityType);
                foreach (var name in handlers)
                {
                    var handlerType = host.GetType($"Sample.{name}Handler", throwOnError: true)!;
                    services.AddTransient(handlerType, sp => Activator.CreateInstance(handlerType, sp.GetRequiredService(identityType))!);
                }
            }
        );
        var state = new ScopeState(host);
        string[] expectedFields = ["__pipeline0", "__pipeline2", "__streamPipeline0", "__streamPipeline1", "__notification0", "__notification1"];
        await Assert.That(state.FieldNames).IsEquivalentTo(expectedFields, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        // Each operation answers the identity its pipeline's handler holds.
        Func<object, Task<int>>[] operations =
        [
            mediator => ( (ISender)mediator ).Send((IRequest<int>)Create(host, "Sample.Ask0"), cancellationToken).AsTask(),
            mediator => ( (ISender)mediator ).Send((IRequest<int>)Create(host, "Sample.Ask2"), cancellationToken).AsTask(),
            mediator => FirstAsync(( (ISender)mediator ).Stream((IStreamRequest<int>)Create(host, "Sample.Flow0"), cancellationToken)),
            mediator => FirstAsync(( (ISender)mediator ).Stream((IStreamRequest<int>)Create(host, "Sample.Flow1"), cancellationToken)),
            mediator => PublishAsync(mediator, "Sample.Told0"),
            mediator => PublishAsync(mediator, "Sample.Told1"),
        ];

        for (var scopeIndex = 0; scopeIndex < Scopes; scopeIndex++)
        {
            await using var scope = provider.CreateAsyncScope();
            var mediators = Enumerable.Range(0, Mediators).Select(_ => scope.ServiceProvider.GetRequiredService(state.MediatorType)).ToArray();

            var callers = operations.Length * Mediators;
            using var start = new Barrier(callers);
            var runs = new Task<(int Pipeline, int Identity)>[callers];
            for (var i = 0; i < callers; i++)
            {
                var pipeline = i % operations.Length;
                var mediator = mediators[i / operations.Length];
                runs[i] = Task.Factory.StartNew(
                    () =>
                    {
                        start.SignalAndWait(cancellationToken);
                        return (pipeline, operations[pipeline](mediator).GetAwaiter().GetResult());
                    },
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            }

            var results = await Task.WhenAll(runs);

            // One identity per pipeline, a different one for each pipeline.
            var identities = results.GroupBy(static r => r.Pipeline).ToDictionary(static g => g.Key, static g => g.Select(static r => r.Identity).Distinct().ToArray());
            await Assert.That(identities.Count).IsEqualTo(operations.Length);
            await Assert.That(identities.Values.All(static ids => ids.Length == 1)).IsTrue();
            await Assert.That(identities.Values.Select(static ids => ids[0]).Distinct().Count()).IsEqualTo(operations.Length);

            // Every field holds its own pipeline's context: __pipelineN a __PipelineN, __streamPipelineN a __StreamPipelineN and
            // __notificationN a __NotificationN.
            var kept = state.FieldValues(scope.ServiceProvider);
            for (var position = 0; position < expectedFields.Length; position++)
            {
                var field = expectedFields[position];
                var expectedType = "__" + char.ToUpperInvariant(field[2]) + field[3..];
                await Assert.That(kept[position]?.GetType().Name).IsEqualTo(expectedType).Because(field);
            }

            // A later mediator reuses every kept context.
            var later = scope.ServiceProvider.GetRequiredService(state.MediatorType);
            for (var pipeline = 0; pipeline < operations.Length; pipeline++)
            {
                await Assert.That(await operations[pipeline](later)).IsEqualTo(identities[pipeline][0]).Because(handlers[pipeline]);
            }

            await Assert.That(state.FieldValues(scope.ServiceProvider)).IsEquivalentTo(kept, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }

        async Task<int> PublishAsync(object mediator, string notificationType)
        {
            var ids = new System.Collections.Concurrent.ConcurrentQueue<int>();
            await ( (IPublisher)mediator ).Publish((INotification)Create(host, notificationType, ids), cancellationToken);
            return ids.Single();
        }
    }

    private static async Task<int> FirstAsync(IAsyncEnumerable<int> items)
    {
        await foreach (var item in items)
        {
            return item;
        }

        throw new InvalidOperationException("The stream yielded nothing.");
    }

    /// <summary>Reads a host's scope state (<c>KeryxMediator.__KeryxScopeState</c>) through reflection, outside any raced code.</summary>
    private sealed class ScopeState(Assembly host)
    {
        public Type MediatorType { get; } = host.GetType("Sample.KeryxMediator", throwOnError: true)!;

        private Type StateType => MediatorType.GetNestedType("__KeryxScopeState", BindingFlags.NonPublic)!;

        /// <summary>The state's fields, one per pipeline with a context, in the host's pipeline order.</summary>
        private FieldInfo[] ContextFields => StateType.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(static f => f.MetadataToken).ToArray();

        public int Fields => ContextFields.Length;

        public string[] FieldNames => ContextFields.Select(static f => f.Name).ToArray();

        public object?[] FieldValues(IServiceProvider scope)
        {
            var state = scope.GetRequiredService(StateType);
            return ContextFields.Select(f => f.GetValue(state)).ToArray();
        }

        /// <summary>Every context the scope's state holds.</summary>
        public object[] Contexts(IServiceProvider scope) => FieldValues(scope).Where(static c => c is not null).Select(static c => c!).ToArray();
    }
}
