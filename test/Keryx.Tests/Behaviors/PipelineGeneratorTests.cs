using System.Globalization;
using System.Text;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Snapshots of generated request pipelines (ADR 0001, Generated pipeline shape; the Phase 4 brief): depths 0 to 8, each with its pipeline
///     context, one flat link struct per behavior and the closed behavior registrations in <c>AddKeryx()</c>.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class PipelineGeneratorTests : HostTest
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task Should_Generate_A_Pipeline_Of_Depth(int depth, CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Depth(depth)).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(new { Mediator = Source(result, HostEmitter.MediatorHintName), AddKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName) })
             .UseParameters(depth);
    }

    [Test]
    public async Task Should_Skip_Global_Behaviors_Whose_Constraints_The_Compiler_Would_Reject(CancellationToken cancellationToken)
    {
        // Each of these used to reach generated code (or crash the generator): a constraint type nested in a generic type, an annotated
        // response against a non-annotated interface constraint (CS8631), and new() against a response with required members (CS9040).
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]

                                           namespace Sample;

                                           public sealed class Outer<T> { public interface IMarker { } }
                                           public sealed class Reply { public required string Text { get; init; } }

                                           public sealed record Marked : IRequest<string>, Outer<string>.IMarker;
                                           public sealed record Unmarked : IRequest<string>;
                                           public sealed record Maybe : IRequest<string?>;
                                           public sealed record Make : IRequest<Reply>;

                                           public static partial class MarkedHandler { private static string Handle(Marked request) => ""; }
                                           public static partial class UnmarkedHandler { private static string Handle(Unmarked request) => ""; }
                                           public static partial class MaybeHandler { private static string? Handle(Maybe request) => null; }
                                           public static partial class MakeHandler { private static Reply Handle(Make request) => new() { Text = "" }; }

                                           [GlobalBehavior]
                                           public sealed class Nested<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                                               where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker
                                           {
                                               public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [GlobalBehavior]
                                           public sealed class Eq<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                                               where TRequest : IRequest<TResponse> where TResponse : IEquatable<TResponse>
                                           {
                                               public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [GlobalBehavior]
                                           public sealed class Creates<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                                               where TRequest : IRequest<TResponse> where TResponse : new()
                                           {
                                               public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var addKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName);
        await Assert.That(addKeryx).Contains("global::Sample.Nested<global::Sample.Marked, string>");
        await Assert.That(addKeryx).DoesNotContain("global::Sample.Nested<global::Sample.Unmarked, string>");
        await Assert.That(addKeryx).Contains("global::Sample.Eq<global::Sample.Marked, string>");
        await Assert.That(addKeryx).DoesNotContain("global::Sample.Eq<global::Sample.Maybe");
        await Assert.That(addKeryx).DoesNotContain("global::Sample.Creates<");
    }

    [Test]
    public async Task Should_Generate_Pipelines_For_Static_Handlers_Struct_Requests_And_IRequest(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]

                                           namespace Sample;

                                           public interface IAudit { void Write(string text); }

                                           public sealed class Audit<TRequest, TResponse>(IAudit audit, [FromKeyedServices("retries")] int retries = 3)
                                               : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
                                           {
                                               public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TNext : struct, INext<TResponse>
                                               {
                                                   audit.Write(typeof(TRequest).Name + retries);
                                                   return next.InvokeAsync(cancellationToken);
                                               }
                                           }

                                           public sealed class TickOnly : IRequestPipelineBehavior<Tick, int>
                                           {
                                               public ValueTask<int> Handle<TNext>(Tick request, TNext next, CancellationToken cancellationToken)
                                                   where TNext : struct, INext<int> => next.InvokeAsync(cancellationToken);
                                           }

                                           public readonly record struct Tick(int Value) : IRequest<int>;
                                           public static partial class TickHandler
                                           {
                                               [Behavior(typeof(TickOnly))]
                                               [Behavior(typeof(Audit<,>))]
                                               private static int Handle(Tick request) => request.Value + 1;
                                           }

                                           public sealed record Touch : IRequest;
                                           public static partial class TouchHandler
                                           {
                                               [Behavior(typeof(Audit<,>))]
                                               private static void Handle(Touch request) { }
                                           }

                                           public sealed record Find(string Key) : IRequest<string?>;
                                           public static partial class FindHandler
                                           {
                                               [Behavior(typeof(Audit<,>))]
                                               private static string? Handle(Find request) => null;
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(new { Mediator = Source(result, HostEmitter.MediatorHintName), AddKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName) });
    }

    /// <summary>
    ///     A host whose <c>Ping</c> handler has <paramref name="depth" /> behaviors: open behaviors <c>Step1</c> to <c>StepN</c>, ordered by
    ///     <c>[BehaviorOrder(Phase.Default, Order = k)]</c> and attached in reverse order, so the snapshot also shows the sort.
    /// </summary>
    internal static string Depth(int depth)
    {
        var builder = new StringBuilder("[assembly: KeryxHost]\n\nnamespace Sample;\n\npublic interface IClock { }\n\n");
        for (var k = 1; k <= depth; k++)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public sealed class Step{{k}}<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
                  {
                      public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                          where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                  }


                  """
            );
        }

        builder.Append("public sealed record Ping(string Text) : IRequest<string>;\n\npublic partial class PingHandler\n{\n");
        for (var k = depth; k >= 1; k--)
        {
            builder.Append(CultureInfo.InvariantCulture, $"    [Behavior(typeof(Step{k}<,>))]\n");
        }

        builder.Append("    private static string Handle(Ping request, IClock clock) => request.Text;\n}\n");
        return builder.ToString();
    }

    internal static string Source(GeneratorTestResults result, string hintName) =>
        result.Results[typeof(HostGenerator)].SyntaxTrees.Single(t => t.FilePath.EndsWith(hintName, StringComparison.Ordinal)).ToString();
}
