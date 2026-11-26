using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using static Keryx.Tests.Streams.StreamSources;

namespace Keryx.Tests.Streams;

/// <summary>
///     The three scopes for both stream behavior kinds (ADR 0001, Three ways to attach a behavior): per target, <c>[assembly: Behavior]</c>
///     and <c>[GlobalBehavior]</c>, de-duplicated by closed type, minus <c>ExcludeBehavior</c>; open generic wrap and hook behaviors
///     substituted with <c>TRequest</c>/<c>TItem</c> and kept only where their constraints hold; request behaviors never applied to a stream.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class StreamScopeTests : HostTest
{
    private static readonly string Source =
        "using System.Runtime.CompilerServices;\n\n"
      + "[assembly: KeryxHost]\n"
      + "[assembly: Behavior(typeof(Sample.AsmWrap<,>))]\n"
      + "[assembly: Behavior(typeof(Sample.AsmHook<,>))]\n"
      + "[assembly: ExcludeBehavior(typeof(Sample.Excluded<,>))]\n\n"
      + "namespace Sample;\n\n"
      + "public interface IAudited { }\n\n"
      + Support
      // Per assembly: a wrap behavior only for value-type items, a hook only for audited requests.
      + Wrap("AsmWrap", 5, constraints: " where TItem : struct")
      + Hook("AsmHook", 20, constraints: ", IAudited")
      // Global: a wrap behavior only for reference-type items, a wrap behavior only for audited requests, a hook for every stream
      // request, a hook only for reference-type items, and one the host excludes.
      + Wrap("AuditWrap", 4, "[GlobalBehavior]", ", IAudited")
      + Wrap("GlobalWrap", 6, "[GlobalBehavior]", " where TItem : class")
      + Hook("GlobalHook", 10, "[GlobalBehavior]")
      + Hook("RefHook", 15, "[GlobalBehavior]", " where TItem : class")
      + Hook("Excluded", 25, "[GlobalBehavior]")
      // Per target.
      + Hook("TargetHook", 30)
      + """
        /// <summary>A global request behavior: never applied to a stream request.</summary>
        [GlobalBehavior]
        public sealed class RequestOnly<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse>
            {
                Log.Write("request behavior");
                return next.InvokeAsync(cancellationToken);
            }
        }

        public sealed record AuditedInts(int Count) : IStreamRequest<int>, IAudited;
        public static partial class AuditedIntsHandler
        {
            [Behavior(typeof(GlobalHook<,>))]
            [Behavior(typeof(TargetHook<,>))]
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

        // AuditWrap (audited), AsmWrap (int items), GlobalHook (once, although also attached to the method), AsmHook (audited), TargetHook;
        // not GlobalWrap or RefHook (int is not a class), not Excluded, not the request behavior.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.AuditedInts", 1), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "AuditWrap>", "AsmWrap>", "GlobalHook.start", "AsmHook.start", "TargetHook.start", "handler>", "TargetHook.item 1",
                             "AsmHook.item 1", "GlobalHook.item 1", "<handler", "TargetHook.complete", "AsmHook.complete", "GlobalHook.complete",
                             "<AsmWrap", "<AuditWrap",
                         ],
                         CollectionOrdering.Matching
                     );

        // Not audited: no AuditWrap, no AsmHook.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.PlainInts", 1), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(["AsmWrap>", "GlobalHook.start", "handler>", "GlobalHook.item 1", "<handler", "GlobalHook.complete", "<AsmWrap"], CollectionOrdering.Matching);

        // Audited, with string items: AuditWrap, GlobalWrap instead of AsmWrap, and RefHook.
        _ = await ConsumeAsync(sender.Stream((IStreamRequest<string>)Create(host, "Sample.Words"), cancellationToken), cancellationToken);
        await Assert.That(TakeLog(host))
                    .IsEquivalentTo(
                         [
                             "AuditWrap>", "GlobalWrap>", "GlobalHook.start", "RefHook.start", "AsmHook.start", "AsmHook.item w", "RefHook.item w",
                             "GlobalHook.item w", "AsmHook.complete", "RefHook.complete", "GlobalHook.complete", "<GlobalWrap", "<AuditWrap",
                         ],
                         CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task AddKeryx_Registers_The_Closed_Stream_Behaviors_The_Pipelines_Use(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();

        // The exclusion removed a behavior from a stream pipeline, so it is used: no KRX036.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();

        var services = new ServiceCollection();
        AddKeryx(result.Assembly!, services);
        var registered = services.Select(static d => d.ServiceType)
                                 .Where(static t => t.IsGenericType)
                                 .Select(static t => t.Name + "[" + string.Join(",", t.GenericTypeArguments.Select(static a => a.Name)) + "]")
                                 .Order(StringComparer.Ordinal)
                                 .ToArray();
        await Assert.That(registered)
                    .IsEquivalentTo(
                         [
                             "AsmHook`2[AuditedInts,Int32]",
                             "AsmHook`2[Words,String]",
                             "AsmWrap`2[AuditedInts,Int32]",
                             "AsmWrap`2[PlainInts,Int32]",
                             "AuditWrap`2[AuditedInts,Int32]",
                             "AuditWrap`2[Words,String]",
                             "GlobalHook`2[AuditedInts,Int32]",
                             "GlobalHook`2[PlainInts,Int32]",
                             "GlobalHook`2[Words,String]",
                             "GlobalWrap`2[Words,String]",
                             "RefHook`2[Words,String]",
                             "TargetHook`2[AuditedInts,Int32]",
                         ],
                         CollectionOrdering.Matching
                     );
    }
}
