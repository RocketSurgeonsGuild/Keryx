using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     An open behavior method's message constraint through an interface that derives from the kind's message interface (OQ-57; ADR 0001,
///     Behavior methods; KRX019): <c>where TRequest : ICommand&lt;TResponse&gt;</c> with <c>ICommand&lt;T&gt; : IRequest&lt;T&gt;</c>, its
///     stream analogue, and <c>where TNotification : IDomainEvent</c> with <c>IDomainEvent : INotification</c> count as the message
///     constraint. The analyzer accepts them, matching applies them to the targets that implement the derived interface only, and the
///     generated code compiles and runs. A constraint to an unrelated interface is KRX019 (see <see cref="BehaviorAnalyzerTests" />).
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class DerivedMessageConstraintTests : HostTest
{
    private const string Source = """
        namespace Sample;

        public static class Log
        {
            public static readonly List<string> Entries = [];

            public static void Write(string entry)
            {
                lock (Entries)
                {
                    Entries.Add(entry);
                }
            }

            public static string[] Take()
            {
                lock (Entries)
                {
                    var entries = Entries.ToArray();
                    Entries.Clear();
                    return entries;
                }
            }
        }

        public interface ICommand<TResponse> : IRequest<TResponse> { }
        public interface IListing<TItem> : IStreamRequest<TItem> { }
        public interface IDomainEvent : INotification { }

        /// <summary>One method per kind, each constrained through the derived interface only.</summary>
        [RequestBehavior]
        [StreamBehavior]
        [NotificationBehavior]
        [GlobalBehavior]
        public static class Derived
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : ICommand<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Log.Write("command " + typeof(TRequest).Name);
                return next.InvokeAsync(cancellationToken);
            }

            public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IListing<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Log.Write("listing " + typeof(TRequest).Name);
                return next.InvokeAsync(cancellationToken);
            }

            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                where TNotification : IDomainEvent
                where TNext : struct, INext
            {
                Log.Write("event " + typeof(TNotification).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record Create(int Value) : ICommand<int>;
        public static partial class CreateHandler { private static int Handle(Create request) => request.Value; }

        public sealed record Query(int Value) : IRequest<int>;
        public static partial class QueryHandler { private static int Handle(Query request) => request.Value; }

        public sealed record Listing(int Count) : IListing<string>;
        public static partial class ListingHandler
        {
            private static async IAsyncEnumerable<string> Handle(Listing request)
            {
                await Task.Yield();
                yield return "listed";
            }
        }

        public sealed record Plain(int Count) : IStreamRequest<string>;
        public static partial class PlainHandler
        {
            private static async IAsyncEnumerable<string> Handle(Plain request)
            {
                await Task.Yield();
                yield return "plain";
            }
        }

        public sealed record Placed(string Id) : IDomainEvent;
        public static partial class PlacedHandler
        {
            private static ValueTask Handle(Placed notification)
            {
                Log.Write("handled " + notification.Id);
                return default;
            }
        }

        public sealed record Other(string Id) : INotification;
        public static partial class OtherHandler
        {
            private static ValueTask Handle(Other notification)
            {
                Log.Write("handled " + notification.Id);
                return default;
            }
        }
        """;

    [Test]
    public async Task The_Analyzer_Accepts_A_Message_Constraint_Through_A_Derived_Interface(CancellationToken cancellationToken)
    {
        var result = await Builder.WithKeryxHost().WithAnalyzer<BehaviorAnalyzer>().AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);

        await Assert.That(result.AnalyzerResults[typeof(BehaviorAnalyzer)].Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Matching_Applies_It_Only_To_Targets_Of_The_Derived_Interface(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Sample.Derived.Handle<global::Sample.Create, int, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Derived.Handle<global::Sample.Query");
        await Assert.That(mediator).Contains("global::Sample.Derived.HandleAsync<global::Sample.Listing, string, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Derived.HandleAsync<global::Sample.Plain");
        await Assert.That(mediator).Contains("global::Sample.Derived.Handle<global::Sample.Placed, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Derived.Handle<global::Sample.Other");
    }

    [Test]
    public async Task The_Generated_Pipelines_Run(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        _ = Call<string[]>(host, "Sample.Log", "Take");
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        string[] Log() => Call<string[]>(host, "Sample.Log", "Take");

        await Assert.That(await mediator.Send((IRequest<int>)Create(host, "Sample.Create", 2), cancellationToken)).IsEqualTo(2);
        await Assert.That(Log()).IsEquivalentTo(["command Create"]);
        await Assert.That(await mediator.Send((IRequest<int>)Create(host, "Sample.Query", 3), cancellationToken)).IsEqualTo(3);
        await Assert.That(Log()).IsEmpty();

        await Assert.That(await CollectAsync(mediator.Stream((IStreamRequest<string>)Create(host, "Sample.Listing", 1), cancellationToken))).IsEquivalentTo(["listed"]);
        await Assert.That(Log()).IsEquivalentTo(["listing Listing"]);
        await Assert.That(await CollectAsync(mediator.Stream((IStreamRequest<string>)Create(host, "Sample.Plain", 1), cancellationToken))).IsEquivalentTo(["plain"]);
        await Assert.That(Log()).IsEmpty();

        await mediator.Publish((INotification)Create(host, "Sample.Placed", "p"), cancellationToken);
        await Assert.That(Log()).IsEquivalentTo(["event Placed", "handled p"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await mediator.Publish((INotification)Create(host, "Sample.Other", "o"), cancellationToken);
        await Assert.That(Log()).IsEquivalentTo(["handled o"]);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> items)
    {
        var list = new List<T>();
        await foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }
}
