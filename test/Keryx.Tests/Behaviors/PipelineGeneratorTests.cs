using System.Globalization;
using System.Text;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Snapshots of generated request pipelines (ADR 0001, Generated pipeline shape): depths 0 to 8, each with its pipeline context holding
///     the injected services of the behavior methods, one flat link struct per behavior, direct static calls with explicit type arguments,
///     and no behavior registrations in <c>AddKeryx()</c>; and pipelines that resolve nothing, which have no context (OQ-65).
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
        var addKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName);
        await Assert.That(addKeryx).DoesNotContain("Step");
        await VerifyHost(result).UseParameters(depth);
    }

    [Test]
    public async Task Should_Skip_Global_Behaviors_Whose_Constraints_The_Compiler_Would_Reject(CancellationToken cancellationToken)
    {
        // Each of these used to reach generated code (or crash the generator): a constraint type nested in a generic type, an annotated
        // response against a non-annotated interface constraint (CS8631), and new() against a response with required members (CS9040).
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
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

                                           [RequestBehavior]
                                           [GlobalBehavior]
                                           public static class Nested
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [RequestBehavior]
                                           [GlobalBehavior]
                                           public static class Eq
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TResponse : IEquatable<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [RequestBehavior]
                                           [GlobalBehavior]
                                           public static class Creates
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TResponse : new()
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Sample.Nested.Handle<global::Sample.Marked, string, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Nested.Handle<global::Sample.Unmarked, string, ");
        await Assert.That(mediator).Contains("global::Sample.Eq.Handle<global::Sample.Marked, string, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Eq.Handle<global::Sample.Maybe");
        await Assert.That(mediator).DoesNotContain("global::Sample.Creates.");
    }

    [Test]
    public async Task Should_Skip_Behaviors_Out_Of_Reach_Or_Annotated_Against_A_Non_Annotated_Response(CancellationToken cancellationToken)
    {
        // File-local behaviors (KRX016: generated code in another file cannot name them), attached, assembly and global; and open behaviors
        // that carry 'TResponse?' in their message constraint or 'next' (OQ-58), which do not fit a request that responds with a
        // non-annotated string (the analyzer reports KRX009 for the attached one; the assembly and global ones are skipped silently) and used
        // to reach generated code as CS8631. None of them may appear in the mediator or the handler's manifest, and the host still compiles.
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: Behavior(typeof(Sample.FileLocalAssembly))]
                                           [assembly: Behavior(typeof(Sample.AnnotatedConstraint))]

                                           namespace Sample;

                                           public sealed record Pong(string Text) : IRequest<string>;

                                           public static partial class PongHandler
                                           {
                                               [Behavior(typeof(FileLocal))]
                                               [Behavior(typeof(AnnotatedNext))]
                                               private static string Handle(Pong request) => request.Text;
                                           }

                                           [RequestBehavior]
                                           file static class FileLocal
                                           {
                                               public static ValueTask<string> Handle<TNext>(Pong request, TNext next) where TNext : struct, INext<string> => next.InvokeAsync(default);
                                           }

                                           [RequestBehavior]
                                           [GlobalBehavior]
                                           file static class FileLocalGlobal
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(default);
                                           }

                                           [RequestBehavior]
                                           file static class FileLocalAssembly
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(default);
                                           }

                                           [RequestBehavior]
                                           public static class AnnotatedConstraint
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse?>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [RequestBehavior]
                                           [GlobalBehavior]
                                           public static class AnnotatedNext
                                           {
                                               public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse?> => next.InvokeAsync(cancellationToken);
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();
        result.AssertCompilationWasSuccessful();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::Sample.Pong request");
        await Assert.That(mediator).DoesNotContain("FileLocal");
        await Assert.That(mediator).DoesNotContain("Annotated");
    }

    [Test]
    public async Task Should_Generate_Pipelines_For_Static_Handlers_Struct_Requests_And_IRequest(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           public interface IAudit { void Write(string text); }

                                           [RequestBehavior]
                                           public static class Audit
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(
                                                   TRequest request,
                                                   IAudit audit,
                                                   TNext next,
                                                   CancellationToken cancellationToken,
                                                   [FromKeyedServices("retries")] int retries = 3
                                               )
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse>
                                               {
                                                   audit.Write(typeof(TRequest).Name + retries);
                                                   return next.InvokeAsync(cancellationToken);
                                               }
                                           }

                                           [RequestBehavior]
                                           public static class TickOnly
                                           {
                                               public static ValueTask<int> HandleAsync<TNext>(TNext next, Tick request) where TNext : struct, INext<int> => next.InvokeAsync(default);
                                           }

                                           public readonly record struct Tick(int Value) : IRequest<int>;
                                           public static partial class TickHandler
                                           {
                                               [Behavior(typeof(TickOnly))]
                                               [Behavior(typeof(Audit))]
                                               private static int Handle(Tick request) => request.Value + 1;
                                           }

                                           public sealed record Touch : IRequest;
                                           public static partial class TouchHandler
                                           {
                                               [Behavior(typeof(Audit))]
                                               private static void Handle(Touch request) { }
                                           }

                                           public sealed record Find(string Key) : IRequest<string?>;
                                           public static partial class FindHandler
                                           {
                                               [Behavior(typeof(Audit))]
                                               private static string? Handle(Find request) => null;
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await VerifyHost(result);
    }

    /// <summary>
    ///     OQ-65 (ADR 0001, Generated pipeline shape): a pipeline that resolves nothing has no context, and every link takes only what it
    ///     reads or passes on, so no link has an unread primary constructor parameter (CS9113, which the mediator no longer suppresses).
    ///     Covers a request and a stream request with static handlers whose behaviors inject nothing (no context, links in a static class,
    ///     <c>new(request)</c>), a static-handler request whose middle behavior injects a service (a context: the links up to that behavior
    ///     take <c>(pipeline, request)</c>, the rest only the request), and notification types without handlers whose behaviors inject
    ///     nothing (no context, last link <c>new()</c>) or a service (a context, last link <c>new()</c>).
    /// </summary>
    [Test]
    public async Task Should_Give_No_Context_To_A_Pipeline_That_Resolves_Nothing(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           public interface IClock { }

                                           [RequestBehavior]
                                           [BehaviorOrder(Phase.Default, Order = 1)]
                                           public static class Outer
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [RequestBehavior]
                                           [BehaviorOrder(Phase.Default, Order = 2)]
                                           public static class Clocked
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, IClock clock, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [RequestBehavior]
                                           [BehaviorOrder(Phase.Default, Order = 3)]
                                           public static class Inner
                                           {
                                               public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IRequest<TResponse>
                                                   where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                                           }

                                           [StreamBehavior]
                                           public static class Relay
                                           {
                                               public static IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                                                   where TRequest : IStreamRequest<TItem>
                                                   where TNext : struct, IStreamNext<TItem> => next.InvokeAsync(cancellationToken);
                                           }

                                           [NotificationBehavior]
                                           public static class Counted
                                           {
                                               public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                                                   where TNotification : INotification
                                                   where TNext : struct, INext => next.InvokeAsync(cancellationToken);
                                           }

                                           [NotificationBehavior]
                                           public static class Timed
                                           {
                                               public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IClock clock, CancellationToken cancellationToken)
                                                   where TNotification : INotification
                                                   where TNext : struct, INext => next.InvokeAsync(cancellationToken);
                                           }

                                           public sealed record Bare(int Value) : IRequest<int>;
                                           public static partial class BareHandler
                                           {
                                               [Behavior(typeof(Outer))]
                                               [Behavior(typeof(Inner))]
                                               private static int Handle(Bare request) => request.Value;
                                           }

                                           public sealed record Middle(int Value) : IRequest<int>;
                                           public static partial class MiddleHandler
                                           {
                                               [Behavior(typeof(Outer))]
                                               [Behavior(typeof(Clocked))]
                                               [Behavior(typeof(Inner))]
                                               private static int Handle(Middle request) => request.Value;
                                           }

                                           public sealed record Numbers(int Count) : IStreamRequest<int>;
                                           public static partial class NumbersHandler
                                           {
                                               [Behavior(typeof(Relay))]
                                               private static async IAsyncEnumerable<int> Handle(Numbers request)
                                               {
                                                   await Task.Yield();
                                                   yield return request.Count;
                                               }
                                           }

                                           [Behavior(typeof(Counted))]
                                           public sealed record Unheard : INotification;

                                           [Behavior(typeof(Counted))]
                                           [Behavior(typeof(Timed))]
                                           public sealed record Ticked : INotification;
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).DoesNotContain("CS9113");
        await Assert.That(mediator).Contains("private static class __Pipeline0");
        await Assert.That(mediator).DoesNotContain("__pipeline0");
        await Assert.That(mediator).Contains("private sealed class __Pipeline1");
        await Assert.That(mediator).Contains("private static class __StreamPipeline0");
        await Assert.That(mediator).Contains("private sealed class __Notification0");
        await Assert.That(mediator).Contains("private static class __Notification1");
        await Assert.That(mediator).DoesNotContain("__notification1");
        await VerifyHost(result);
    }

    /// <summary>
    ///     A host whose <c>Ping</c> handler has <paramref name="depth" /> behaviors: static behaviors <c>Step1</c> to <c>StepN</c> with open
    ///     behavior methods, ordered by <c>[BehaviorOrder(Phase.Default, Order = k)]</c> and attached in reverse order, so the snapshot also
    ///     shows the sort. The odd steps inject an <c>IClock</c>, which the pipeline context holds.
    /// </summary>
    internal static string Depth(int depth)
    {
        var builder = new StringBuilder("namespace Sample;\n\npublic interface IClock { }\n\n");
        for (var k = 1; k <= depth; k++)
        {
            var clock = k % 2 == 1 ? "IClock clock, " : "";
            builder.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [RequestBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Step{{k}}
                  {
                      public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, {{clock}}CancellationToken cancellationToken)
                          where TRequest : IRequest<TResponse>
                          where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);
                  }


                  """
            );
        }

        builder.Append("public sealed record Ping(string Text) : IRequest<string>;\n\npublic partial class PingHandler\n{\n");
        for (var k = depth; k >= 1; k--)
        {
            builder.Append(CultureInfo.InvariantCulture, $"    [Behavior(typeof(Step{k}))]\n");
        }

        builder.Append("    private static string Handle(Ping request, IClock clock) => request.Text;\n}\n");
        return builder.ToString();
    }

    private static string Source(GeneratorTestResults result, string hintName) => TestSources.HostSource(result, hintName);
}
