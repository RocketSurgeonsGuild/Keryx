using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Open behavior methods that write their response or item type parameter annotated, <c>TResponse?</c> / <c>TItem?</c> (OQ-58; ADR 0001,
///     Behavior methods, Matching): they fit requests and stream requests whose response or item type is annotated (<c>string?</c>),
///     nullable-oblivious (declared under <c>#nullable disable</c>) or a value type (<c>int</c>; on a type parameter constrained to
///     <c>struct</c>, <c>T?</c> is <c>Nullable&lt;T&gt;</c> and fits only <c>int?</c>), and not a non-annotated reference type: a global behavior
///     is skipped for that target, an attached one is KRX009. The generated code compiles without warnings for every fit and runs; the same
///     holds for a referenced global behavior.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class AnnotatedResponseTests : HostTest
{
    /// <summary>The behaviors, shared by the host and the library of the cross-assembly test.</summary>
    internal const string Behaviors = """
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

        /// <summary>Writes the response and item type parameters annotated everywhere: 'next', the return type and the message constraint.</summary>
        [RequestBehavior]
        [StreamBehavior]
        [GlobalBehavior]
        public static class Maybe
        {
            public static async ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse?>
                where TNext : struct, INext<TResponse?>
            {
                Log.Write("maybe " + typeof(TRequest).Name);
                return await next.InvokeAsync(cancellationToken);
            }

            public static IAsyncEnumerable<TItem?> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem?>
                where TNext : struct, IStreamNext<TItem?>
            {
                Log.Write("maybe " + typeof(TRequest).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }

        /// <summary>Annotates only the message constraint: 'next' and the return type carry the bare type parameter.</summary>
        [RequestBehavior]
        [GlobalBehavior]
        public static class AnnotatedConstraint
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse?>
                where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        /// <summary>Annotates only 'next' and the return type: the message constraint carries the bare type parameter.</summary>
        [RequestBehavior]
        [GlobalBehavior]
        public static class AnnotatedNext
        {
            public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse?> =>
                next.InvokeAsync(cancellationToken);
        }

        /// <summary>The stream analogue of AnnotatedNext.</summary>
        [StreamBehavior]
        [GlobalBehavior]
        public static class AnnotatedStreamNext
        {
            public static IAsyncEnumerable<TItem?> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem?> =>
                next.InvokeAsync(cancellationToken);
        }

        /// <summary>'TResponse?' on a type parameter constrained to class: fits annotated and oblivious reference responses only.</summary>
        [RequestBehavior]
        [GlobalBehavior]
        public static class MaybeClass
        {
            public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse?>
                where TResponse : class
                where TNext : struct, INext<TResponse?> =>
                next.InvokeAsync(cancellationToken);
        }

        /// <summary>'TResponse?' on a type parameter constrained to struct: Nullable&lt;TResponse&gt;, which fits only a nullable value type.</summary>
        [RequestBehavior]
        [GlobalBehavior]
        public static class MaybeValue
        {
            public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse?>
                where TResponse : struct
                where TNext : struct, INext<TResponse?>
            {
                Log.Write("value " + typeof(TResponse).Name);
                return next.InvokeAsync(cancellationToken);
            }
        }
        """;

    /// <summary>The requests and stream requests, one per kind of response or item type, with their handlers.</summary>
    internal const string Requests = """
        public sealed record Find(string? Key) : IRequest<string?>;
        public static partial class FindHandler { private static string? Handle(Find request) => request.Key; }

        public sealed record Count(int Value) : IRequest<int>;
        public static partial class CountHandler { private static int Handle(Count request) => request.Value; }

        public sealed record MaybeCount(int? Value) : IRequest<int?>;
        public static partial class MaybeCountHandler { private static int? Handle(MaybeCount request) => request.Value; }

        /// <summary>A non-annotated reference response: Maybe does not fit, so the global behavior is skipped for it.</summary>
        public sealed record Ping(string Text) : IRequest<string>;
        public static partial class PingHandler { private static string Handle(Ping request) => request.Text; }

        public sealed record FindAll(int Count) : IStreamRequest<string?>;
        public static partial class FindAllHandler
        {
            private static async IAsyncEnumerable<string?> Handle(FindAll request)
            {
                for (var i = 0; i < request.Count; i++)
                {
                    await Task.Yield();
                    yield return i % 2 == 0 ? null : "item" + i;
                }
            }
        }

        public sealed record Numbers(int Count) : IStreamRequest<int>;
        public static partial class NumbersHandler
        {
            private static async IAsyncEnumerable<int> Handle(Numbers request)
            {
                for (var i = 0; i < request.Count; i++)
                {
                    await Task.Yield();
                    yield return i;
                }
            }
        }

        /// <summary>A non-annotated reference item: Maybe does not fit.</summary>
        public sealed record Names(int Count) : IStreamRequest<string>;
        public static partial class NamesHandler
        {
            private static async IAsyncEnumerable<string> Handle(Names request)
            {
                await Task.Yield();
                yield return "name";
            }
        }

        #nullable disable
        /// <summary>Declared nullable-oblivious: Maybe fits it.</summary>
        public sealed record Legacy(string Text) : IRequest<string>;
        public sealed record LegacyAll(int Count) : IStreamRequest<string>;
        #nullable enable
        public static partial class LegacyHandler { private static string Handle(Legacy request) => request.Text; }
        public static partial class LegacyAllHandler
        {
            private static async IAsyncEnumerable<string> Handle(LegacyAll request)
            {
                await Task.Yield();
                yield return "legacy";
            }
        }
        """;

    /// <summary>
    ///     Closed behaviors over the host's requests, whose 'next' and return type are checked with nullability like an open method's: an
    ///     annotated 'next' over an oblivious response fits (called with nullable warnings disabled); an annotated return type over a
    ///     non-annotated reference response does not (a global is skipped, an attached one is KRX009).
    /// </summary>
    internal const string ClosedBehaviors = """
        [RequestBehavior]
        [GlobalBehavior]
        public static class ClosedLegacy
        {
            public static ValueTask<string?> Handle<TNext>(Legacy request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<string?> =>
                next.InvokeAsync(cancellationToken);
        }

        [StreamBehavior]
        [GlobalBehavior]
        public static class ClosedLegacyAll
        {
            public static IAsyncEnumerable<string?> HandleAsync<TNext>(LegacyAll request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<string?> =>
                next.InvokeAsync(cancellationToken);
        }

        [RequestBehavior]
        [GlobalBehavior]
        public static class ClosedReturn
        {
            public static async ValueTask<string?> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, INext<string> =>
                await next.InvokeAsync(cancellationToken);
        }

        [StreamBehavior]
        [GlobalBehavior]
        public static class ClosedNames
        {
            public static IAsyncEnumerable<string?> HandleAsync<TNext>(Names request, TNext next, CancellationToken cancellationToken)
                where TNext : struct, IStreamNext<string> =>
                next.InvokeAsync(cancellationToken);
        }
        """;

    private const string Host = "namespace Sample;\n\n" + Behaviors + "\n" + ClosedBehaviors + "\n" + Requests;

    private const string LibrarySource =
        "#nullable enable\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Keryx;\n\nnamespace Lib;\n\n" + Behaviors;

    private const string ReferencingHost = "namespace Sample;\n\n" + Requests;

    [Test]
    public async Task Should_Generate_Pipelines_For_Annotated_Response_Type_Parameters(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + Host).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        // Warnings are errors: no nullability warning (CS8619, CS8620, CS8631) in the generated code for any fit.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);

        // Annotated: TResponse is the response without its annotation, as C# infers it.
        await Assert.That(mediator).Contains("global::Sample.Maybe.Handle<global::Sample.Find, string, ");
        await Assert.That(mediator).Contains("global::Sample.Maybe.HandleAsync<global::Sample.FindAll, string, ");
        // Value types: T? on an unconstrained T is T; on a struct-constrained T it is Nullable<T>, which only int? fits.
        await Assert.That(mediator).Contains("global::Sample.Maybe.Handle<global::Sample.Count, int, ");
        await Assert.That(mediator).Contains("global::Sample.Maybe.Handle<global::Sample.MaybeCount, int?, ");
        await Assert.That(mediator).Contains("global::Sample.Maybe.HandleAsync<global::Sample.Numbers, int, ");
        await Assert.That(mediator).Contains("global::Sample.MaybeValue.Handle<global::Sample.MaybeCount, int, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.MaybeValue.Handle<global::Sample.Count");
        // Oblivious: called with nullable warnings disabled, as the oblivious declaration has them.
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Sample.Maybe.Handle<global::Sample.Legacy, string, ")).IsTrue();
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Sample.Maybe.HandleAsync<global::Sample.LegacyAll, string, ")).IsTrue();
        // Non-annotated reference responses and items: the global behavior is skipped.
        await Assert.That(mediator).DoesNotContain("global::Sample.Maybe.Handle<global::Sample.Ping");
        await Assert.That(mediator).DoesNotContain("global::Sample.Maybe.HandleAsync<global::Sample.Names");
        await AssertPartiallyAnnotatedAsync(mediator, "Sample");

        // Closed: an annotated 'next' over an oblivious response fits with warnings disabled; an annotated return type over a non-annotated
        // reference response is skipped.
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Sample.ClosedLegacy.Handle<")).IsTrue();
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Sample.ClosedLegacyAll.HandleAsync<")).IsTrue();
        await Assert.That(mediator).DoesNotContain("global::Sample.ClosedReturn.");
        await Assert.That(mediator).DoesNotContain("global::Sample.ClosedNames.");
        await VerifyHost(result);
    }

    [Test]
    public async Task An_Attached_Annotated_Behavior_That_Does_Not_Fit_Is_KRX009_And_Skipped(CancellationToken cancellationToken)
    {
        // Attached to handlers whose response or item is a non-annotated string: the analyzer reports KRX009, the generator leaves it out.
        var result = await HostBuilder.WithAnalyzer<BehaviorAnalyzer>()
                                      .AddSources(
                                           Usings
                                         + Host.Replace("[GlobalBehavior]\n", "", StringComparison.Ordinal)
                                               .Replace(
                                                    "public static partial class PingHandler { private static string Handle",
                                                    "public static partial class PingHandler { [Behavior(typeof(Maybe))] private static string Handle",
                                                    StringComparison.Ordinal
                                                )
                                               .Replace(
                                                    "public static partial class NamesHandler\n{\n    private",
                                                    "public static partial class NamesHandler\n{\n    [Behavior(typeof(Maybe))]\n    private",
                                                    StringComparison.Ordinal
                                                )
                                               .Replace(
                                                    "public static partial class FindHandler { private",
                                                    "public static partial class FindHandler { [Behavior(typeof(Maybe))] private",
                                                    StringComparison.Ordinal
                                                )
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(
                         result.AnalyzerResults[typeof(BehaviorAnalyzer)]
                               .Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
                               .Order(StringComparer.Ordinal)
                     )
                    .IsEquivalentTo(
                         [
                             "KRX009: Behavior 'Maybe' does not fit 'NamesHandler.Handle(Names)': 'Names' does not satisfy the constraint 'TRequest : IStreamRequest<string?>'",
                             "KRX009: Behavior 'Maybe' does not fit 'PingHandler.Handle(Ping)': 'Ping' does not satisfy the constraint 'TRequest : IRequest<string?>'",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Sample.Maybe.Handle<global::Sample.Find, string, ");
        await Assert.That(mediator).DoesNotContain("global::Sample.Maybe.Handle<global::Sample.Ping");
        await Assert.That(mediator).DoesNotContain("global::Sample.Maybe.HandleAsync<global::Sample.Names");
        await Assert.That(result.FinalCompilation.GetDiagnostics(cancellationToken).Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString()))
                    .IsEmpty();
    }

    [Test]
    public async Task An_Attached_Closed_Behavior_Is_Matched_With_Nullability(CancellationToken cancellationToken)
    {
        // Closed behaviors attached to handlers: an annotated return type over a non-annotated string is KRX009 and left out; an annotated
        // 'next' over an oblivious response fits and is called with nullable warnings disabled.
        var result = await HostBuilder.WithAnalyzer<BehaviorAnalyzer>()
                                      .AddSources(
                                           Usings
                                         + Host.Replace("[GlobalBehavior]\n", "", StringComparison.Ordinal)
                                               .Replace(
                                                    "public static partial class PingHandler { private static string Handle",
                                                    "public static partial class PingHandler { [Behavior(typeof(ClosedReturn))] private static string Handle",
                                                    StringComparison.Ordinal
                                                )
                                               .Replace(
                                                    "public static partial class NamesHandler\n{\n    private",
                                                    "public static partial class NamesHandler\n{\n    [Behavior(typeof(ClosedNames))]\n    private",
                                                    StringComparison.Ordinal
                                                )
                                               .Replace(
                                                    "public static partial class LegacyHandler { private",
                                                    "public static partial class LegacyHandler { [Behavior(typeof(ClosedLegacy))] private",
                                                    StringComparison.Ordinal
                                                )
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(
                         result.AnalyzerResults[typeof(BehaviorAnalyzer)]
                               .Diagnostics.Select(static d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
                               .Order(StringComparer.Ordinal)
                     )
                    .IsEquivalentTo(
                         [
                             "KRX009: Behavior 'ClosedNames' does not fit 'NamesHandler.Handle(Names)': its stream behavior method's return type carries 'string?', but the target's item type is 'string'",
                             "KRX009: Behavior 'ClosedReturn' does not fit 'PingHandler.Handle(Ping)': its request behavior method's return type carries 'string?', but the target's response type is 'string'",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Sample.ClosedLegacy.Handle<")).IsTrue();
        await Assert.That(mediator).DoesNotContain("global::Sample.ClosedReturn.");
        await Assert.That(mediator).DoesNotContain("global::Sample.ClosedNames.");
        await Assert.That(result.FinalCompilation.GetDiagnostics(cancellationToken).Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString()))
                    .IsEmpty();
    }

    [Test]
    public async Task Annotated_Behaviors_Run_For_Every_Target_They_Fit(CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Host, cancellationToken);
        _ = Call<string[]>(host, "Sample.Log", "Take");
        await using var provider = BuildProvider(host);

        await AssertRunsAsync(host, host, "Sample.Log", provider, cancellationToken);
    }

    [Test]
    public async Task A_Referenced_Global_Behavior_With_An_Annotated_Response_Fits_The_Same_Targets(CancellationToken cancellationToken)
    {
        // The behaviors live in a library; the host's own requests get them as global behaviors, matched against the library's metadata.
        var library = await CompileLibraryAsync("Lib", LibrarySource, cancellationToken);
        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + ReferencingHost).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).Select(static d => d.ToString())).IsEmpty();
        var mediator = TestSources.HostSource(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Lib.Maybe.Handle<global::Sample.Find, string, ");
        await Assert.That(CalledWithWarningsDisabled(mediator, "global::Lib.Maybe.Handle<global::Sample.Legacy, string, ")).IsTrue();
        await Assert.That(mediator).Contains("global::Lib.MaybeValue.Handle<global::Sample.MaybeCount, int, ");
        await Assert.That(mediator).DoesNotContain("global::Lib.Maybe.Handle<global::Sample.Ping");
        await Assert.That(mediator).DoesNotContain("global::Lib.Maybe.HandleAsync<global::Sample.Names");
        await AssertPartiallyAnnotatedAsync(mediator, "Lib");
    }

    [Test]
    public async Task A_Referenced_Global_Behavior_With_An_Annotated_Response_Runs_For_Every_Target_It_Fits(CancellationToken cancellationToken)
    {
        var library = await CompileLibraryAsync("Lib", LibrarySource, cancellationToken);
        var host = await CompileHostAsync(ReferencingHost, cancellationToken, library);
        var lib = library.Assembly!;
        _ = Call<string[]>(lib, "Lib.Log", "Take");
        await using var provider = BuildProvider(host);

        await AssertRunsAsync(host, lib, "Lib.Log", provider, cancellationToken);
    }

    /// <summary>
    ///     The partially annotated behaviors (OQ-58): the type argument each one gets for every target it fits, which targets it skips, and
    ///     which calls are emitted with nullable warnings disabled (only those that write 'TResponse?' in 'next' or the return type over an
    ///     oblivious response).
    /// </summary>
    private static async Task AssertPartiallyAnnotatedAsync(string mediator, string ns)
    {
        // An annotated message constraint only: the annotated response is substituted as is; the oblivious one needs no pragma.
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedConstraint.Handle<global::Sample.Find, string?, ");
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedConstraint.Handle<global::Sample.Count, int, ");
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedConstraint.Handle<global::Sample.Legacy, string, ");
        await Assert.That(CalledWithWarningsDisabled(mediator, $"global::{ns}.AnnotatedConstraint.Handle<global::Sample.Legacy, string, ")).IsFalse();
        await Assert.That(mediator).DoesNotContain($"global::{ns}.AnnotatedConstraint.Handle<global::Sample.Ping");

        // An annotated 'next' and return type only.
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedNext.Handle<global::Sample.Find, string?, ");
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedNext.Handle<global::Sample.Count, int, ");
        await Assert.That(CalledWithWarningsDisabled(mediator, $"global::{ns}.AnnotatedNext.Handle<global::Sample.Legacy, string, ")).IsTrue();
        await Assert.That(CalledWithWarningsDisabled(mediator, $"global::{ns}.AnnotatedNext.Handle<global::Sample.Find, string?, ")).IsFalse();
        await Assert.That(mediator).DoesNotContain($"global::{ns}.AnnotatedNext.Handle<global::Sample.Ping");

        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedStreamNext.HandleAsync<global::Sample.FindAll, string?, ");
        await Assert.That(mediator).Contains($"global::{ns}.AnnotatedStreamNext.HandleAsync<global::Sample.Numbers, int, ");
        await Assert.That(CalledWithWarningsDisabled(mediator, $"global::{ns}.AnnotatedStreamNext.HandleAsync<global::Sample.LegacyAll, string, ")).IsTrue();
        await Assert.That(mediator).DoesNotContain($"global::{ns}.AnnotatedStreamNext.HandleAsync<global::Sample.Names");

        // Constrained to class: annotated and oblivious reference responses only.
        await Assert.That(mediator).Contains($"global::{ns}.MaybeClass.Handle<global::Sample.Find, string, ");
        await Assert.That(CalledWithWarningsDisabled(mediator, $"global::{ns}.MaybeClass.Handle<global::Sample.Legacy, string, ")).IsTrue();
        await Assert.That(mediator).DoesNotContain($"global::{ns}.MaybeClass.Handle<global::Sample.Count");
        await Assert.That(mediator).DoesNotContain($"global::{ns}.MaybeClass.Handle<global::Sample.MaybeCount");
        await Assert.That(mediator).DoesNotContain($"global::{ns}.MaybeClass.Handle<global::Sample.Ping");
    }

    /// <summary>Whether the generated line that calls <paramref name="call" /> sits right after <c>#nullable disable warnings</c>.</summary>
    private static bool CalledWithWarningsDisabled(string mediator, string call)
    {
        var lines = mediator.Split('\n');
        var index = Array.FindIndex(lines, line => line.Contains(call, StringComparison.Ordinal));
        return index < 0
            ? throw new InvalidOperationException($"No generated call of '{call}'.")
            : index > 0 && lines[index - 1].Trim() == "#nullable disable warnings";
    }

    /// <summary>Sends and streams every request through the host's pipelines and checks the responses, items and log.</summary>
    private static async Task AssertRunsAsync(
        System.Reflection.Assembly host,
        System.Reflection.Assembly behaviors,
        string logType,
        ServiceProvider provider,
        CancellationToken cancellationToken
    )
    {
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var mediator = scope.ServiceProvider.GetRequiredService(host.GetType("Sample.KeryxMediator", throwOnError: true)!);
        string[] Log() => Call<string[]>(behaviors, logType, "Take");

        await Assert.That(await sender.Send((IRequest<string?>)Create(host, "Sample.Find", "key"), cancellationToken)).IsEqualTo("key");
        await Assert.That(await PipelineRuntimeTests.SendTyped<string?>(mediator, Create(host, "Sample.Find", [null]), cancellationToken)).IsNull();
        await Assert.That(Log()).IsEquivalentTo(["maybe Find", "maybe Find"]);

        await Assert.That(await PipelineRuntimeTests.SendTyped<string>(mediator, Create(host, "Sample.Legacy", "old"), cancellationToken)).IsEqualTo("old");
        await Assert.That(Log()).IsEquivalentTo(["maybe Legacy"]);

        await Assert.That(await sender.Send((IRequest<int>)Create(host, "Sample.Count", 3), cancellationToken)).IsEqualTo(3);
        await Assert.That(Log()).IsEquivalentTo(["maybe Count"]);

        await Assert.That(await sender.Send((IRequest<int?>)Create(host, "Sample.MaybeCount", 4), cancellationToken)).IsEqualTo(4);
        await Assert.That(Log().Order(StringComparer.Ordinal)).IsEquivalentTo(["maybe MaybeCount", "value Int32"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

        await Assert.That(await sender.Send((IRequest<string>)Create(host, "Sample.Ping", "pong"), cancellationToken)).IsEqualTo("pong");
        await Assert.That(Log()).IsEmpty();

        await Assert.That(await CollectAsync(sender.Stream((IStreamRequest<string?>)Create(host, "Sample.FindAll", 3), cancellationToken))).IsEquivalentTo([null, "item1", null]);
        await Assert.That(Log()).IsEquivalentTo(["maybe FindAll"]);

        await Assert.That(await CollectAsync(sender.Stream((IStreamRequest<int>)Create(host, "Sample.Numbers", 2), cancellationToken))).IsEquivalentTo([0, 1]);
        await Assert.That(Log()).IsEquivalentTo(["maybe Numbers"]);

        await Assert.That(await CollectAsync(sender.Stream((IStreamRequest<string>)Create(host, "Sample.LegacyAll", 1), cancellationToken))).IsEquivalentTo(["legacy"]);
        await Assert.That(Log()).IsEquivalentTo(["maybe LegacyAll"]);

        await Assert.That(await CollectAsync(sender.Stream((IStreamRequest<string>)Create(host, "Sample.Names", 1), cancellationToken))).IsEquivalentTo(["name"]);
        await Assert.That(Log()).IsEmpty();
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
