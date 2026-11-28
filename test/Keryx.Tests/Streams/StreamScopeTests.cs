using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using static Keryx.Tests.Streams.StreamSources;

namespace Keryx.Tests.Streams;

/// <summary>
///     The three scopes for stream behaviors (ADR 0001, Three ways to attach a behavior): per target, <c>[assembly: Behavior]</c> and
///     <c>[GlobalBehavior]</c> (on the class or only on the stream behavior method), de-duplicated by behavior class, minus
///     <c>ExcludeBehavior</c>; open behavior methods matched with <c>TRequest</c>/<c>TItem</c> and kept only where their constraints hold;
///     request behaviors never applied to a stream.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamScopeTests : HostTest
{
    private static readonly string Source =
        "using System.Runtime.CompilerServices;\n\n"
      + "[assembly: Behavior(typeof(Sample.AsmWrap))]\n"
      + "[assembly: Behavior(typeof(Sample.AsmAudit))]\n"
      + "[assembly: ExcludeBehavior(typeof(Sample.Excluded))]\n\n"
      + "namespace Sample;\n\n"
      + "public interface IAudited { }\n\n"
      + Support
      // Per assembly: one only for value-type items, one only for audited requests.
      + Wrap("AsmWrap", 5, constraints: " where TItem : struct")
      + Wrap("AsmAudit", 20, constraints: ", IAudited")
      // Global: one only for audited requests, one only for reference-type items, one for every stream request, and one the host excludes.
      + Wrap("AuditWrap", 4, "[GlobalBehavior]", ", IAudited")
      + Wrap("GlobalWrap", 6, "[GlobalBehavior]", " where TItem : class")
      + Wrap("Everywhere", 10, "[GlobalBehavior]")
      + Wrap("Excluded", 25, "[GlobalBehavior]")
      // Per target.
      + Wrap("Target", 30)
      + """
        /// <summary>A global request behavior: never applied to a stream request.</summary>
        [RequestBehavior]
        [GlobalBehavior]
        public static class RequestOnly
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Log.Write("request behavior");
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>A request behavior class whose stream behavior method alone is global.</summary>
        [RequestBehavior]
        [StreamBehavior]
        [BehaviorOrder(Phase.Default, Order = 40)]
        public static class StreamOnlyGlobal
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Log.Write("never");
                return next.InvokeAsync(cancellationToken);
            }

            [GlobalBehavior]
            public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem>
            {
                Log.Write("method global");
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record AuditedInts(int Count) : IStreamRequest<int>, IAudited;
        public static partial class AuditedIntsHandler
        {
            [Behavior(typeof(Everywhere))]
            [Behavior(typeof(Target))]
            private static IAsyncEnumerable<int> Handle(AuditedInts request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record PlainInts(int Count) : IStreamRequest<int>;
        public static partial class PlainIntsHandler
        {
            private static IAsyncEnumerable<int> Handle(PlainInts request, CancellationToken cancellationToken) => Numbers.Range(request.Count, cancellationToken);
        }

        public sealed record Words : IStreamRequest<string>, IAudited;
        public static partial class WordsHandler
        {
            private static async IAsyncEnumerable<string> Handle(Words request)
            {
                await Task.Yield();
                yield return "w";
            }
        }
        """;

    [Test]
    public async Task Every_Scope_Applies_Where_Its_Constraints_Hold(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Source, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        _ = TakeLog(host);

        // AuditWrap (audited), AsmWrap (int items), Everywhere (once, although also attached to the method), AsmAudit (audited), Target and
        // the method-only global; not GlobalWrap (int is not a class), not Excluded, not the request behaviors.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.AuditedInts", 1), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "AuditWrap>", "AsmWrap>", "Everywhere>", "AsmAudit>", "Target>", "method global", "handler>", "<handler", "<Target",
                             "<AsmAudit", "<Everywhere", "<AsmWrap", "<AuditWrap",
                         ],
                         CollectionOrdering.Matching
                     );

        // Not audited: no AuditWrap, no AsmAudit.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.PlainInts", 1), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["AsmWrap>", "Everywhere>", "method global", "handler>", "<handler", "<Everywhere", "<AsmWrap"], CollectionOrdering.Matching);

        // Audited, with string items: AuditWrap, GlobalWrap instead of AsmWrap.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<string>)Create(host, "Sample.Words"), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         ["AuditWrap>", "GlobalWrap>", "Everywhere>", "AsmAudit>", "method global", "<AsmAudit", "<Everywhere", "<GlobalWrap", "<AuditWrap"],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task The_Exclusion_Is_Used_And_AddKeryx_Registers_No_Stream_Behaviors(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();

        // The exclusion removed a behavior from a stream pipeline, so it is used: no KRX036.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();

        var services = new ServiceCollection();
        AddKeryx(result.Assembly!, services);
        await Assert.That(services.Select(static d => d.ServiceType.Name).Order(StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo(["IMediator", "IPublisher", "ISender", "KeryxMediator"], CollectionOrdering.Matching);
    }
}
