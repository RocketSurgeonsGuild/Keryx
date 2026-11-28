using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Cross-assembly discovery (ADR 0001, Cross-assembly discovery; phase 7): a host composes the handlers, global behaviors, request and
///     notification types of the assemblies it references, read from their manifests with one scan, and reports the host rules about them
///     (KRX030 for referenced requests, KRX032, KRX033, KRX034, KRX035, KRX036 and KRX039 for <c>ExcludeHandler</c>). A
///     request's handler lives in the request's own assembly (KRX023), so the request types live in the handler library and the contracts
///     assembly holds only notifications.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class CrossAssemblyTests : HostTest
{
    /// <summary>A contracts assembly: notifications, handled elsewhere.</summary>
    internal const string Contracts = """
        using Keryx;

        namespace Contracts;

        public interface IDomainEvent : INotification { string Id { get; } }
        public sealed record OrderPlaced(string Id) : IDomainEvent;
        """;

    /// <summary>
    ///     A handler library: requests and a stream request with their handlers, a global behavior, an attached behavior and a polymorphic
    ///     handler of the contracts' notifications.
    /// </summary>
    internal const string Orders = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        namespace Orders;

        public sealed record CreateOrder(string Sku) : IRequest<int>;
        public sealed record GetName(int Id) : IRequest<string>;
        public sealed record Countdown(int From) : IStreamRequest<int>;

        public interface IJournal { void Write(string entry); }

        [RequestBehavior]
        [GlobalBehavior]
        [BehaviorOrder(Phase.Observability)]
        public static class Journaled
        {
            public static async ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, IJournal journal, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                journal.Write("before " + typeof(TRequest).Name);
                var response = await next.InvokeAsync(cancellationToken);
                journal.Write("after " + typeof(TRequest).Name);
                return response;
            }
        }

        [RequestBehavior]
        public static class Doubling
        {
            public static async ValueTask<int> Handle<TNext>(CreateOrder request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<int> =>
                2 * await next.InvokeAsync(cancellationToken);
        }

        public partial class CreateOrderHandler
        {
            [Behavior(typeof(Doubling))]
            private static ValueTask<int> Handle(CreateOrder request, IJournal journal, CancellationToken cancellationToken)
            {
                journal.Write("handle " + request.Sku);
                return new(request.Sku.Length);
            }
        }

        public static partial class GetNameHandler
        {
            private static string Handle(GetName request) => "order-" + request.Id;
        }

        public static partial class CountdownHandler
        {
            private static async IAsyncEnumerable<int> Handle(Countdown request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                for (var i = request.From; i > 0; i--)
                {
                    await Task.Yield();
                    yield return i;
                }
            }
        }

        public partial class AuditEvents
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(Contracts.IDomainEvent notification, IJournal journal)
            {
                journal.Write("audit " + notification.Id);
                return default;
            }
        }
        """;

    /// <summary>A host over both libraries, with a notification type and a polymorphic handler of its own.</summary>
    private const string Host = """
        namespace Sample;

        public sealed class Journal : Orders.IJournal
        {
            public List<string> Entries { get; } = new();

            public void Write(string entry) => Entries.Add(entry);
        }

        public sealed record Shipped(string Id) : Contracts.IDomainEvent;

        public partial class OnAnyEvent
        {
            private static ValueTask Handle(Contracts.IDomainEvent notification, Orders.IJournal journal)
            {
                journal.Write("host " + notification.Id);
                return default;
            }
        }
        """;

    [Test]
    public async Task A_Host_Dispatches_The_Handlers_Of_Its_Metadata_References(CancellationToken cancellationToken)
    {
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);
        var host = await CompileHostAsync(Host, cancellationToken, contracts, orders);
        var contractsAssembly = contracts.Assembly!;
        var ordersAssembly = orders.Assembly!;
        var journal = Create(host, "Sample.Journal");
        await using var provider = BuildProvider(host, services => services.AddScoped(ordersAssembly.GetType("Orders.IJournal", throwOnError: true)!, _ => journal));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // The referenced global behavior (Phase.Observability) wraps the attached one, which doubles the handler's response.
        var created = await mediator.Send((IRequest<int>)Create(ordersAssembly, "Orders.CreateOrder", "abc"), cancellationToken);
        var name = await mediator.Send((IRequest<string>)Create(ordersAssembly, "Orders.GetName", 7), cancellationToken);
        var items = new List<int>();
        await foreach (var item in mediator.Stream((IStreamRequest<int>)Create(ordersAssembly, "Orders.Countdown", 3), cancellationToken))
        {
            items.Add(item);
        }

        // The library's polymorphic handler receives the host's notification type, and the host's the library's; [HandlerOrder] orders them.
        await mediator.Publish((INotification)Create(contractsAssembly, "Contracts.OrderPlaced", "o1"), cancellationToken);
        await mediator.Publish((INotification)Create(host, "Sample.Shipped", "s1"), cancellationToken);

        await Assert.That(created).IsEqualTo(6);
        await Assert.That(name).IsEqualTo("order-7");
        await Assert.That(items).IsEquivalentTo([3, 2, 1], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Entries(journal))
                    .IsEquivalentTo(
                         ["before CreateOrder", "handle abc", "after CreateOrder", "before GetName", "after GetName", "host o1", "audit o1", "host s1", "audit s1"],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task A_Host_Composes_The_Handlers_Of_Its_Project_References(CancellationToken cancellationToken)
    {
        // An IDE project reference is a compilation reference: no MVID, so nothing reaches the disk cache, but the manifest reads the same.
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);

        var result = await HostBuilder.AddReferences(contracts.FinalCompilation.ToMetadataReference(), orders.FinalCompilation.ToMetadataReference())
                                      .AddSources(Usings + Host)
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal))).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::Orders.CreateOrder request");
        await Assert.That(mediator).Contains("Stream(global::Orders.Countdown request");
        await Assert.That(mediator).Contains("global::Orders.Journaled.Handle<global::Orders.CreateOrder, int, ");
        await Assert.That(mediator).Contains("global::Orders.IJournal Service0;");
        await Assert.That(mediator).Contains("global::Orders.Doubling.Handle<Link2>(");
        // A referenced handler with injected parameters is constructed by its pipeline context, never registered (owner decision 42), with
        // the IJournal the context resolved once and shares with the Journaled behavior (owner decision 43).
        await Assert.That(mediator).Contains("Handler = new global::Orders.CreateOrderHandler(Service0);");
        await Assert.That(Source(result, HostEmitter.ServiceCollectionExtensionsHintName)).DoesNotContain("CreateOrderHandler");
    }

    [Test]
    public async Task KRX035_Lists_The_Referenced_Global_Behaviors_And_Their_Assembly(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(Host, cancellationToken);

        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(["KRX035: Global request behavior 'Journaled' from assembly 'Orders' applies in this host"]);
        // The referenced global has no source location, so it is reported without one.
        await AssertWithoutLocationAsync(HostDiagnostics(result));
    }

    [Test]
    public async Task ExcludeBehavior_Removes_A_Referenced_Global_Behavior(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync("[assembly: ExcludeBehavior(typeof(Orders.Journaled))]\n" + Host, cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Journaled");
    }

    [Test]
    public async Task KRX008_For_A_Host_Behavior_Ordered_Against_A_Referenced_Global_Behavior(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            Host
          + """

            [RequestBehavior]
            [GlobalBehavior]
            [BehaviorOrder(Phase.Outermost)]
            [RunsAfter(typeof(Orders.Journaled))]
            public static class Early
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken
        );

        // Checked once per pair of behavior classes, however many pipelines (CreateOrder, GetName) contain it.
        var ordering = Diagnostics(result).Where(static d => d.StartsWith("KRX008", StringComparison.Ordinal)).ToArray();
        await Assert.That(ordering).Count().IsEqualTo(1);
        await Assert.That(ordering[0]).Contains("declares RunsAfter 'Orders.Journaled'");
    }

    [Test]
    public async Task A_Hosts_Own_Handler_Of_A_Referenced_Request_Is_KRX023_And_Not_Composed(CancellationToken cancellationToken)
    {
        // A request's handler lives in the request's own assembly, hosts included (decision 2026-11-26): the host's handler of a referenced
        // request is KRX023, which the generators skip like any handler error. The referenced handler is dispatched, and there is no KRX005.
        var result = await GenerateAsync(Host + MyGetName, cancellationToken, withHandlerAnalyzer: true);

        await Assert.That(result.AnalyzerResults[typeof(Analyzers.Diagnostics.HandlerAnalyzer)].Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX023"]);
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal))).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Orders.GetNameHandler.InvokeAsync(");
        await Assert.That(mediator).DoesNotContain("MyGetName");
    }

    [Test]
    public async Task KRX039_ExcludeHandler_Cannot_Replace_A_Referenced_Request_Handler(CancellationToken cancellationToken)
    {
        // ExcludeHandler names only notification handlers (OQ-59): naming a referenced request handler is KRX039, on the attribute, and the
        // handler is not excluded, so the referenced handler is dispatched. The host's replacement is still KRX023, and there is no KRX030.
        var result = await GenerateAsync("[assembly: ExcludeHandler(typeof(Orders.GetNameHandler))]\n" + Host + MyGetName, cancellationToken, withHandlerAnalyzer: true);

        await Assert.That(result.AnalyzerResults[typeof(Analyzers.Diagnostics.HandlerAnalyzer)].Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX023"]);
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)))
                    .IsEquivalentTo(
                         [
                             "KRX039: [assembly: ExcludeHandler(typeof(GetNameHandler))] names a request handler, which a host cannot exclude; ExcludeHandler names only notification handlers",
                         ]
                     );
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Orders.GetNameHandler.InvokeAsync(");
        await Assert.That(mediator).Contains("Send(global::Orders.GetName request");
        await Assert.That(mediator).DoesNotContain("MyGetName");
    }

    /// <summary>The host's own handler of the referenced <c>Orders.GetName</c>: KRX023.</summary>
    private const string MyGetName = """

        public static partial class MyGetName
        {
            private static string Handle(Orders.GetName request) => "mine";
        }
        """;

    [Test]
    public async Task KRX039_For_Referenced_Request_And_Stream_Request_Handlers_Which_Stay_In_The_Host(CancellationToken cancellationToken)
    {
        // Neither handler is excluded (OQ-59): the host is composed as if the attributes were not there, so both requests are dispatched,
        // there is no KRX030, and the exclusions count as used (no KRX036). KRX039 is an error, reported on each attribute.
        var libraries = await CompileLibrariesAsync(cancellationToken);
        var without = await GenerateAsync(Host, libraries, cancellationToken, ownLoadContext: true);
        var result = await GenerateAsync(
            "[assembly: ExcludeHandler(typeof(Orders.GetNameHandler))]\n[assembly: ExcludeHandler(typeof(Orders.CountdownHandler))]\n" + Host,
            libraries,
            cancellationToken
        );

        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX039: [assembly: ExcludeHandler(typeof(CountdownHandler))] names a stream request handler, which a host cannot exclude; ExcludeHandler names only notification handlers",
                             "KRX039: [assembly: ExcludeHandler(typeof(GetNameHandler))] names a request handler, which a host cannot exclude; ExcludeHandler names only notification handlers",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        var errors = HostDiagnostics(result).Where(static d => d.Id == "KRX039").ToArray();
        await Assert.That(errors.All(static d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource)).IsTrue();
        await Assert.That(errors.All(static d => d.Descriptor.HelpLinkUri == Analyzers.Diagnostics.Descriptors.HelpLinkBase + "krx039")).IsTrue();
        await AssertComposedAsWithoutAsync(result, without, cancellationToken);
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Orders.GetNameHandler.InvokeAsync(");
        await Assert.That(mediator).Contains("Send(global::Orders.GetName request");
        await Assert.That(mediator).Contains("global::Orders.CountdownHandler.InvokeAsync(");
        await Assert.That(mediator).Contains("Stream(global::Orders.Countdown request");
    }

    [Test]
    public async Task KRX039_For_The_Hosts_Own_Request_And_Stream_Request_Handlers(CancellationToken cancellationToken)
    {
        // The host's own request handlers cannot be excluded either (OQ-59); they stay dispatched. Its own notification handler is KRX036
        // (KRX036_For_An_ExcludeHandler_That_Names_No_Referenced_Handler).
        var libraries = await CompileLibrariesAsync(cancellationToken);
        var without = await GenerateAsync(Host + OwnRequests, libraries, cancellationToken, ownLoadContext: true);
        var result = await GenerateAsync(
            "[assembly: ExcludeHandler(typeof(Sample.PingHandler))]\n[assembly: ExcludeHandler(typeof(Sample.TicksHandler))]\n" + Host + OwnRequests,
            libraries,
            cancellationToken
        );

        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX039: [assembly: ExcludeHandler(typeof(PingHandler))] names a request handler, which a host cannot exclude; ExcludeHandler names only notification handlers",
                             "KRX039: [assembly: ExcludeHandler(typeof(TicksHandler))] names a stream request handler, which a host cannot exclude; ExcludeHandler names only notification handlers",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await Assert.That(HostDiagnostics(result).Where(static d => d.Id == "KRX039").All(static d => d.Location.IsInSource)).IsTrue();
        await AssertComposedAsWithoutAsync(result, without, cancellationToken);
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::Sample.Ping request");
        await Assert.That(mediator).Contains("Stream(global::Sample.Ticks request");
    }

    /// <summary>The host's own request and stream request with their handlers.</summary>
    private const string OwnRequests = """

        public sealed record Ping : IRequest<int>;

        public static partial class PingHandler
        {
            private static int Handle(Ping request) => 1;
        }

        public sealed record Ticks : IStreamRequest<int>;

        public static partial class TicksHandler
        {
            private static async IAsyncEnumerable<int> Handle(Ticks request)
            {
                await Task.Yield();
                yield return 1;
            }
        }
        """;

    [Test]
    public async Task KRX036_For_An_ExcludeHandler_That_Names_No_Referenced_Handler(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            "[assembly: ExcludeHandler(typeof(Sample.Journal))]\n[assembly: ExcludeHandler(typeof(Sample.OnAnyEvent))]\n" + Host,
            cancellationToken
        );

        // The host's own handler is not a referenced handler: excluding it has no effect (OQ-45).
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX036: [assembly: ExcludeHandler(typeof(Journal))] has no effect, because 'Journal' is not part of any pipeline in this host",
                             "KRX036: [assembly: ExcludeHandler(typeof(OnAnyEvent))] has no effect, because 'OnAnyEvent' is not part of any pipeline in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        // Each on its attribute (the referenced global's KRX035 has no source location, so it has none).
        await Assert.That(HostDiagnostics(result).Where(static d => d.Id == "KRX036").All(static d => d.Location.IsInSource)).IsTrue();
    }

    [Test]
    public async Task KRX038_For_A_Referenced_Request_Type_That_Is_Not_Sealed(CancellationToken cancellationToken)
    {
        // The library suppressed KRX024, which must not reach the host's dispatch (OQ-68): each referenced request and stream request type
        // that is not sealed is KRX038, without a location, and is not composed. Its sealed request types, the sealed cases of its abstract
        // and closed bases and its notification type (notifications need not be sealed) are composed and not reported. The host's own type
        // that is not sealed is KRX024 in the host (suppressed here), not KRX038, and is not composed either.
        var library = await CompileUnsealedLibraryAsync(cancellationToken);

        var result = await HostBuilder.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview)
                                      .WithAnalyzer<Analyzers.Diagnostics.MessageTypeAnalyzer>()
                                      .AddGlobalOption("dotnet_diagnostic.KRX024.severity", "none")
                                      .AddCompilationReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           public record Mine(int Id) : IRequest<string>;
                                           public static partial class MineHandler { private static string Handle(Mine request) => "mine"; }

                                           public sealed record Ours(int Id) : IRequest<string>;
                                           public static partial class OursHandler { private static string Handle(Ours request) => "ours"; }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(result.AnalyzerResults[typeof(Analyzers.Diagnostics.MessageTypeAnalyzer)].Diagnostics).IsEmpty();
        await Assert.That(Diagnostics(result).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX038: Request type 'Get' from assembly 'Open' is not sealed, so this host does not dispatch it; make it sealed in 'Open' (KRX024)",
                             "KRX038: Stream request type 'Items' from assembly 'Open' is not sealed, so this host does not dispatch it; make it sealed in 'Open' (KRX024)",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await Assert.That(HostDiagnostics(result).All(static d => d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        foreach (var unsealed in (string[])["global::Open.Get ", "global::Open.Get)", "GetHandler", "global::Open.Items", "global::Sample.Mine"])
        {
            await Assert.That(mediator).DoesNotContain(unsealed);
        }

        foreach (var composed in (string[])["Send(global::Open.Ping request", "Send(global::Open.Ship request", "Send(global::Open.Circle request", "Send(global::Sample.Ours request", "global::Open.Noted"])
        {
            await Assert.That(mediator).Contains(composed);
        }
    }

    [Test]
    public async Task KRX030_For_A_Referenced_Request_Without_A_Handler(CancellationToken cancellationToken)
    {
        // A library with requests and no handlers: built without the analyzers here, as if KRX001 were suppressed there.
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var unhandled = await CompileLibraryAsync(
            "Unhandled",
            """
            using Keryx;

            namespace Unhandled;

            public sealed record CreateOrder(string Sku) : IRequest<int>;
            public sealed record GetName(int Id) : IRequest<string>;
            public sealed record Countdown(int From) : IStreamRequest<int>;
            """,
            cancellationToken
        );

        var result = await HostBuilder.AddCompilationReferences(contracts, unhandled).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX030: Request type 'Countdown' has no handler in this host",
                             "KRX030: Request type 'CreateOrder' has no handler in this host",
                             "KRX030: Request type 'GetName' has no handler in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        // The referenced requests have no source location, so they are reported without one: only the compilation-wide severity applies.
        await AssertWithoutLocationAsync(result.Results[typeof(HostGenerator)].Diagnostics);
        // The referenced notification type is known: it gets a typed overload, although nothing handles it.
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Publish(global::Contracts.OrderPlaced notification");
    }

    [Test]
    public async Task KRX034_For_A_Manifest_Type_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // The handler library references the contracts, the host does not (a PrivateAssets reference, for example). Only the polymorphic
        // notification handler names a contracts type; the request handlers live with their requests and are composed.
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);

        var result = await HostBuilder.AddCompilationReferences(orders).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        // The handlers are left out, so the generated code compiles: KRX034 is the only error.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Distinct()).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Contracts.IDomainEvent', which the Keryx manifest of 'Orders' needs, cannot be resolved or is not accessible from this host; reference assembly 'Contracts'",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).DoesNotContain("Contracts");
        await Assert.That(mediator).Contains("Send(global::Orders.CreateOrder request");
    }

    [Test]
    public async Task KRX034_For_A_Constructor_Parameter_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // The generated constructor's parameter is not named in the manifest, but AddKeryx() would name it (OQ-47). The handler is left out,
        // and its request still counts as handled, so there is no KRX030 on top.
        var services = await CompileLibraryAsync("Services", "namespace Services; public interface IClock { }", cancellationToken);
        var library = await CompileLibraryAsync(
            "Lib",
            """
            namespace Lib;

            public sealed record GetName(int Id) : Keryx.IRequest<string>;

            public partial class GetNameHandler
            {
                private static string Handle(GetName request, Services.IClock clock) => "";
            }
            """,
            cancellationToken,
            references: [services]
        );

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        var diagnostics = Diagnostics(result).ToArray();
        await Assert.That(diagnostics.Where(static d => d.StartsWith("KRX030", StringComparison.Ordinal) && d.Contains("GetName", StringComparison.Ordinal))).IsEmpty();
        await Assert.That(diagnostics.Single(static d => d.StartsWith("KRX034", StringComparison.Ordinal)))
                    .Contains("which the Keryx manifest of 'Lib' needs, cannot be resolved or is not accessible from this host; reference assembly 'Services'");
        await AssertWithoutLocationAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("GetNameHandler");
    }

    [Test]
    public async Task KRX032_For_A_Newer_Manifest_Format(CancellationToken cancellationToken)
    {
        var future = await CompileLibraryAsync("Future", FutureLibrary, cancellationToken, kind: LibraryKind.Plain);

        var result = await HostBuilder.AddCompilationReferences(future).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        // Nothing of the assembly is read, so its request is not reported as unhandled either.
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         ["KRX032: Assembly 'Future' carries a Keryx manifest of format version 2, but the Keryx in this project reads format versions up to 1; upgrade Keryx in this project"]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
    }

    [Test]
    public async Task KRX033_For_A_Keryx_Reference_Without_A_Manifest(CancellationToken cancellationToken)
    {
        var plain = await CompileLibraryAsync("Plain", "namespace Plain; public sealed record Ping : Keryx.IRequest<int>;", cancellationToken, kind: LibraryKind.Plain);

        var result = await HostBuilder.AddCompilationReferences(plain).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX033: Assembly 'Plain' references Keryx but has no Keryx manifest, so the Keryx generator did not run there and its handlers are not part of this host; reference the Keryx package directly from that project",
                         ]
                     );
        await Assert.That(HostDiagnostics(result).Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await AssertWithoutLocationAsync(HostDiagnostics(result));
    }

    /// <summary>An app host: one request whose handler is internal (no manifest entry), one whose handler is public.</summary>
    private const string AppHost = """
        using Keryx;

        namespace App;

        public sealed record Hidden : IRequest<int>;
        internal static partial class HiddenHandler { private static int Handle(Hidden request) => 1; }

        public sealed record Visible : IRequest<int>;
        public static partial class VisibleHandler { private static int Handle(Visible request) => 2; }
        """;

    [Test]
    public async Task A_Test_Host_That_References_An_App_Host(CancellationToken cancellationToken)
    {
        // OQ-16: the app host's request types are covered by KRX030 like any other reference's. Its public handler has a manifest entry, so
        // the test host dispatches that request; its internal handler is invisible, so the test host reports KRX030 for that request.
        var app = await CompileLibraryAsync("App", AppHost, cancellationToken, kind: LibraryKind.Host);

        var result = await HostBuilder.AddCompilationReferences(app).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result)).IsEquivalentTo(["KRX030: Request type 'Hidden' has no handler in this host"]);
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::App.Visible request");
        await Assert.That(mediator).DoesNotContain("Hidden");
    }

    [Test]
    public async Task Duplicates_Inside_One_Referenced_Assembly_Are_Its_Own_KRX005(CancellationToken cancellationToken)
    {
        // The library compiles: same-assembly KRX005 is its analyzer's, which the test library does not run, and its generator reports nothing.
        // The host has no KRX005 of its own (OQ-60): it reports nothing and dispatches neither handler.
        var library = await CompileLibraryAsync(
            "Lib",
            """
            namespace Lib;

            public sealed record GetName(int Id) : Keryx.IRequest<string>;
            public static partial class NameA { private static string Handle(GetName request) => "a"; }
            public static partial class NameB { private static string Handle(GetName request) => "b"; }
            """,
            cancellationToken
        );

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(HostDiagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Send(global::Lib.GetName request");
    }

    [Test]
    public async Task ExcludeHandler_Removes_A_Referenced_Notification_Handler(CancellationToken cancellationToken)
    {
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);
        var source = "[assembly: ExcludeHandler(typeof(Orders.AuditEvents))]\n" + Host;

        var result = await HostBuilder
                          .AddCompilationReferences(contracts, orders)
                          .AddSources(Usings + source)
                          .Build()
                          .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal))).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).DoesNotContain("AuditEvents");
        await Assert.That(mediator).Contains("Publish(global::Contracts.OrderPlaced notification");
        await Assert.That(mediator).Contains("global::Sample.OnAnyEvent");
    }

    [Test]
    public async Task An_Excluded_Referenced_Notification_Handler_Is_Not_Delivered(CancellationToken cancellationToken)
    {
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);
        var host = await CompileHostAsync("[assembly: ExcludeHandler(typeof(Orders.AuditEvents))]\n" + Host, cancellationToken, contracts, orders);
        var contractsAssembly = contracts.Assembly!;
        var ordersAssembly = orders.Assembly!;
        var journal = Create(host, "Sample.Journal");
        await using var provider = BuildProvider(host, services => services.AddScoped(ordersAssembly.GetType("Orders.IJournal", throwOnError: true)!, _ => journal));
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMediator>();

        await publisher.Publish((INotification)Create(contractsAssembly, "Contracts.OrderPlaced", "o1"), cancellationToken);
        await publisher.Publish((INotification)Create(host, "Sample.Shipped", "s1"), cancellationToken);

        await Assert.That(Entries(journal)).IsEquivalentTo(["host o1", "host s1"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExcludeHandler_Skips_A_Referenced_Handler_The_Host_Cannot_Resolve(CancellationToken cancellationToken)
    {
        // A test host excludes a notification handler whose infrastructure it does not reference: the excluded entry is never resolved, so
        // neither KRX034 nor KRX036 is reported.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface IDb { }", cancellationToken);
        var library = await CompileLibraryAsync("Lib", ExcludedLibrary, cancellationToken, references: [infra]);

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + ExcludingHost).Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("OnSaved");
        // The generated host still compiles: the compiler reports no error (KRX030 is the generator's, not the compiler's), so nothing
        // generated names the excluded handler or the unreferenced Infra assembly.
        await Assert.That(result.FinalCompilation.GetDiagnostics(cancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.ToString()))
                    .IsEmpty();
        await Assert.That(result.Results.Values.SelectMany(static r => r.SyntaxTrees).Select(static t => t.ToString()).Where(static text => text.Contains("Infra", StringComparison.Ordinal)))
                    .IsEmpty();
    }

    [Test]
    public async Task KRX039_Does_Not_Skip_A_Referenced_Request_Handler_The_Host_Cannot_Resolve(CancellationToken cancellationToken)
    {
        // A request handler is never excluded (OQ-59), so the host resolves it and reports what it cannot resolve (KRX034); its request
        // counts as handled (no KRX030). A test host references the handler's infrastructure and swaps its services instead.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface IDb { }", cancellationToken);
        var library = await CompileLibraryAsync(
            "Lib",
            """
            namespace Lib;

            public sealed record Ping : Keryx.IRequest<int>;

            public partial class PingHandler
            {
                private static int Handle(Ping request, Infra.IDb db) => 1;
            }
            """,
            cancellationToken,
            references: [infra]
        );

        var result = await HostBuilder
                          .AddCompilationReferences(library)
                          .AddSources(Usings + "[assembly: ExcludeHandler(typeof(Lib.PingHandler))]\n\nnamespace Sample;")
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result).Order(StringComparer.Ordinal)).IsEquivalentTo(["KRX034", "KRX039"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Send(global::Lib.Ping request");
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Global_Behavior_Whose_Method_Injects_An_Unreferenced_Type(CancellationToken cancellationToken)
    {
        // The behavior itself resolves, but the pipeline context would name its behavior method's injected parameter type (OQ-47): it is
        // left out of the request pipelines, the kind of that method (OQ-57), so the generated code compiles, and KRX035 does not list it.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface ILog { }", cancellationToken);
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            public sealed record Ping : IRequest<int>;

            public static partial class PingHandler
            {
                private static int Handle(Ping request) => 1;
            }

            [RequestBehavior]
            [GlobalBehavior]
            public static class Logged
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, Infra.ILog log, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken,
            references: [infra]
        );

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Infra.ILog', which the request behavior method 'Logged.Handle' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'",
                         ]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Logged");
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Send(global::Lib.Ping request");
    }

    [Test]
    public async Task KRX034_Is_Per_Kind_And_The_Other_Kinds_Of_The_Class_Still_Apply(CancellationToken cancellationToken)
    {
        // OQ-57: a referenced global behavior with a request and a stream behavior method, where only the request method injects a type the
        // host cannot resolve. KRX034 names that method and its kind; the class stays in the stream pipelines, and KRX035 lists it for the
        // stream kind only.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface ILog { }", cancellationToken);
        const string librarySource = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            public static class Log
            {
                public static readonly List<string> Entries = [];
            }

            public sealed record Ping : IRequest<int>;

            public static partial class PingHandler
            {
                private static int Handle(Ping request) => 1;
            }

            public sealed record Numbers : IStreamRequest<int>;

            public static partial class NumbersHandler
            {
                private static async IAsyncEnumerable<int> Handle(Numbers request)
                {
                    await Task.Yield();
                    yield return 7;
                }
            }

            [RequestBehavior]
            [StreamBehavior]
            [GlobalBehavior]
            public static class Split
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, Infra.ILog log, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);

                public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem>
                {
                    Log.Entries.Add("split " + typeof(TRequest).Name);
                    return next.InvokeAsync(cancellationToken);
                }
            }
            """;
        var library = await CompileLibraryAsync("Lib", librarySource, cancellationToken, references: [infra]);

        var result = await HostBuilder.AddCompilationReferences(library)
                                      .AddSources(Usings + "namespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Infra.ILog', which the request behavior method 'Split.Handle' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'",
                             "KRX035: Global stream behavior 'Split' from assembly 'Lib' applies in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.Handle<");
        await Assert.That(mediator).Contains("global::Lib.Split.HandleAsync<global::Lib.Numbers, int, ");

        // The generated code compiles (KRX034 is the generator's error, not the compiler's), and the stream kind runs.
        var host = result.Assembly!;
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = library.Assembly!;
        var mediatorService = scope.ServiceProvider.GetRequiredService<IMediator>();
        await Assert.That(await mediatorService.Send((IRequest<int>)Create(lib, "Lib.Ping"), cancellationToken)).IsEqualTo(1);
        var items = new List<int>();
        await foreach (var item in mediatorService.Stream((IStreamRequest<int>)Create(lib, "Lib.Numbers"), cancellationToken))
        {
            items.Add(item);
        }

        await Assert.That(items).IsEquivalentTo([7]);
        var entries = (List<string>)lib.GetType("Lib.Log", throwOnError: true)!.GetField("Entries")!.GetValue(null)!;
        await Assert.That(entries).IsEquivalentTo(["split Numbers"]);
    }

    /// <summary>
    ///     A library whose two-kind behavior class <c>Split</c> injects <c>Infra.ILog</c> only in its stream behavior method, with a request
    ///     handler and a stream handler. <paramref name="attach" /> goes before both handlers; <paramref name="assemblyAttributes" /> before
    ///     the namespace.
    /// </summary>
    private static string SplitLibrary(string assemblyAttributes, string attach) =>
        $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        {{assemblyAttributes}}

        namespace Lib;

        public static class Log
        {
            public static readonly List<string> Entries = [];
        }

        public sealed record Ping : IRequest<int>;

        public static partial class PingHandler
        {
            {{attach}}
            private static int Handle(Ping request) => 1;
        }

        public sealed record Numbers : IStreamRequest<int>;

        public static partial class NumbersHandler
        {
            {{attach}}
            private static async IAsyncEnumerable<int> Handle(Numbers request)
            {
                await Task.Yield();
                yield return 7;
            }
        }

        [RequestBehavior]
        [StreamBehavior]
        public static class Split
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                where TRequest : IRequest<TResponse>
                where TNext : struct, INext<TResponse>
            {
                Log.Entries.Add("split " + typeof(TRequest).Name);
                return next.InvokeAsync(cancellationToken);
            }

            public static IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next, Infra.ILog log, CancellationToken cancellationToken)
                where TRequest : IStreamRequest<TItem>
                where TNext : struct, IStreamNext<TItem> =>
                next.InvokeAsync(cancellationToken);
        }
        """;

    private const string SplitKrx034 =
        "KRX034: Type 'Infra.ILog', which the stream behavior method 'Split.HandleAsync' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'";

    [Test]
    public async Task KRX034_For_An_Attached_Two_Kind_Behavior_Leaves_Out_Only_The_Handler_Of_The_Unresolvable_Kind(CancellationToken cancellationToken)
    {
        // OQ-57: an attached behavior is checked for the kind of the handler it is attached to. The request handler keeps Split (its request
        // method resolves); the stream handler, whose Split method injects a type the host cannot resolve, is left out, with one KRX034.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface ILog { }", cancellationToken);
        var library = await CompileLibraryAsync("Lib", SplitLibrary("", "[Behavior(typeof(Split))]"), cancellationToken, references: [infra]);

        var result = await HostBuilder.AddCompilationReferences(library)
                                      .AddSources(Usings + "namespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal))).IsEquivalentTo([SplitKrx034]);
        await AssertWithoutLocationAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Lib.Split.Handle<global::Lib.Ping, int, ");
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.HandleAsync<");
        await Assert.That(mediator).DoesNotContain("global::Lib.Numbers");

        var host = result.Assembly!;
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = library.Assembly!;
        var mediatorService = scope.ServiceProvider.GetRequiredService<IMediator>();
        await Assert.That(await mediatorService.Send((IRequest<int>)Create(lib, "Lib.Ping"), cancellationToken)).IsEqualTo(1);
        var entries = (List<string>)lib.GetType("Lib.Log", throwOnError: true)!.GetField("Entries")!.GetValue(null)!;
        await Assert.That(entries).IsEquivalentTo(["split Ping"]);
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Assembly_Behavior_Is_Per_Kind_And_Reported_Once(CancellationToken cancellationToken)
    {
        // OQ-57: an [assembly: Behavior] class is checked for every kind it carries, once (the manifest loop and the catalog's skipped methods
        // both see it). Its request kind stays in the library's request pipelines; the stream handler runs without it.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface ILog { }", cancellationToken);
        var library = await CompileLibraryAsync("Lib", SplitLibrary("[assembly: Behavior(typeof(Lib.Split))]", ""), cancellationToken, references: [infra]);

        var result = await HostBuilder.AddCompilationReferences(library)
                                      .AddSources(Usings + "namespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal))).IsEquivalentTo([SplitKrx034]);
        await AssertWithoutLocationAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Lib.Split.Handle<global::Lib.Ping, int, ");
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.HandleAsync<");

        var host = result.Assembly!;
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = library.Assembly!;
        var mediatorService = scope.ServiceProvider.GetRequiredService<IMediator>();
        await Assert.That(await mediatorService.Send((IRequest<int>)Create(lib, "Lib.Ping"), cancellationToken)).IsEqualTo(1);
        var items = new List<int>();
        await foreach (var item in mediatorService.Stream((IStreamRequest<int>)Create(lib, "Lib.Numbers"), cancellationToken))
        {
            items.Add(item);
        }

        await Assert.That(items).IsEquivalentTo([7]);
        var entries = (List<string>)lib.GetType("Lib.Log", throwOnError: true)!.GetField("Entries")!.GetValue(null)!;
        await Assert.That(entries).IsEquivalentTo(["split Ping"]);
    }

    [Test]
    public async Task KRX034_For_A_Notification_Behavior_Whose_Method_Injects_An_Unreferenced_Type(CancellationToken cancellationToken)
    {
        // A [Behavior] on a referenced notification type is read from the type, not from the manifest; the host checks its injected
        // parameter types too.
        var infra = await CompileLibraryAsync("Infra", "namespace Infra; public interface ILog { }", cancellationToken);
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            [Behavior(typeof(Audited))]
            public sealed record Placed : INotification;

            [NotificationBehavior]
            public static class Audited
            {
                public static ValueTask Handle<TNotification, TNext>(TNotification notification, Infra.ILog log, TNext next, CancellationToken cancellationToken)
                    where TNotification : INotification
                    where TNext : struct, INext =>
                    next.InvokeAsync(cancellationToken);
            }
            """,
            cancellationToken,
            references: [infra]
        );

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Infra.ILog', which the notification behavior method 'Audited.Handle' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'",
                         ]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Publish(global::Lib.Placed notification");
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Audited");
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Handler_Built_Against_Another_Shape_Of_Its_Notification(CancellationToken cancellationToken)
    {
        // The library was built against a contracts assembly where Counted is a notification; the host references one where it is not. A
        // request handler cannot be in this position: it lives in its request's assembly (KRX023), so its manifest and its request agree.
        var contractsV1 = await CompileLibraryAsync("Contracts", "using Keryx; namespace Contracts; public sealed record Counted(int To) : INotification;", cancellationToken);
        var contractsV2 = await CompileLibraryAsync("Contracts", "namespace Contracts; public sealed record Counted(int To);", cancellationToken, ownLoadContext: true);
        var library = await CompileLibraryAsync(
            "Lib",
            "namespace Lib; public static partial class CountedHandler { private static System.Threading.Tasks.ValueTask Handle(Contracts.Counted notification) => default; }",
            cancellationToken,
            references: [contractsV1]
        );

        var result = await HostBuilder.AddCompilationReferences(contractsV2, library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        // The handler is left out with an error of its own (OQ-47).
        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Handler 'CountedHandler' in the Keryx manifest of 'Lib' does not fit the types this host resolves: the manifest describes a notification handler of 'Contracts.Counted', but that type is not a notification this host can dispatch; build 'Lib' against the assemblies this host references",
                         ]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("CountedHandler");
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Handler_Whose_Base_Class_Became_Disposable(CancellationToken cancellationToken)
    {
        // The library was built against a contracts assembly whose base class was not disposable (no KRX025); in the one the host resolves
        // it is. The host's pipeline context would construct the handler and never dispose it (owner decision 45), so it is left out.
        var contractsV1 = await CompileLibraryAsync("Contracts", "namespace Contracts; public abstract class Base { }", cancellationToken);
        var contractsV2 = await CompileLibraryAsync(
            "Contracts",
            "namespace Contracts; public abstract class Base : System.IDisposable { public void Dispose() { } }",
            cancellationToken,
            ownLoadContext: true
        );
        var library = await CompileLibraryAsync(
            "Lib",
            """
            namespace Lib;
            public sealed record Pinged : Keryx.INotification;
            public interface IClock { }
            public partial class PingedHandler : Contracts.Base { private static System.Threading.Tasks.ValueTask Handle(Pinged notification, IClock clock) => default; }
            """,
            cancellationToken,
            references: [contractsV1]
        );

        var result = await HostBuilder.AddCompilationReferences(contractsV2, library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Handler 'PingedHandler' in the Keryx manifest of 'Lib' does not fit the types this host resolves: the class implements 'System.IDisposable', which a handler class may not (KRX025); build 'Lib' against the assemblies this host references",
                         ]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("PingedHandler");
    }

    [Test]
    public async Task KRX009_For_A_Behavior_A_Referenced_Manifest_Attaches_That_Does_Not_Fit(CancellationToken cancellationToken)
    {
        // The library attached a constrained behavior that fit its request when it was built: the request's base class, from a contracts
        // assembly, implemented ITagged. In the contracts assembly the host resolves it no longer does, so the request no longer satisfies the
        // constraint, and the behavior would drop out of the pipeline silently.
        var contractsV1 = await CompileLibraryAsync("Contracts", "namespace Contracts; public interface ITagged { } public abstract class Base : ITagged { }", cancellationToken);
        var contractsV2 = await CompileLibraryAsync("Contracts", "namespace Contracts; public interface ITagged { } public abstract class Base { }", cancellationToken, ownLoadContext: true);
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            [RequestBehavior]
            public static class Tagged
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>, Contracts.ITagged
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(cancellationToken);
            }

            public sealed class GetName : Contracts.Base, IRequest<string>;

            public static partial class NameHandler
            {
                [Behavior(typeof(Tagged))]
                private static string Handle(GetName request) => "name";
            }
            """,
            cancellationToken,
            references: [contractsV1]
        );

        var result = await HostBuilder.AddCompilationReferences(contractsV2, library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        var diagnostics = HostDiagnostics(result).ToArray();
        await Assert.That(diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX009"]);
        await Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture))
                    .IsEqualTo("Behavior 'Tagged' does not fit 'NameHandler.Handle': 'GetName' does not satisfy the constraint 'TRequest : ITagged'");
        await Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture)).Contains("does not fit 'NameHandler.Handle': ");
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await AssertWithoutLocationAsync(diagnostics);
        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX009"]);
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Tagged");
    }

    [Test]
    public async Task KRX009_For_A_Behavior_On_A_Referenced_Notification_Type_That_No_Longer_Fits(CancellationToken cancellationToken)
    {
        // OQ-55: Contracts was built against a Behaviors assembly whose Tagged fit every notification; the Behaviors assembly the host
        // references constrains it to IUrgent, which Placed is not. Tagged is skipped with KRX009 (without a location); Kept still runs.
        const string behaviorsV1 = """
            using System.Threading.Tasks;
            using Keryx;

            namespace Behaviors;

            public interface IUrgent : INotification { }

            [NotificationBehavior]
            public static class Tagged
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : CONSTRAINT where TNext : struct, INext => next.InvokeAsync(default);
            }

            [NotificationBehavior]
            public static class Kept
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext => next.InvokeAsync(default);
            }
            """;
        var behaviors1 = await CompileLibraryAsync("Behaviors", behaviorsV1.Replace("CONSTRAINT", "INotification", StringComparison.Ordinal), cancellationToken);
        var behaviors2 = await CompileLibraryAsync("Behaviors", behaviorsV1.Replace("CONSTRAINT", "IUrgent", StringComparison.Ordinal), cancellationToken, ownLoadContext: true);
        var contracts = await CompileLibraryAsync(
            "Contracts",
            """
            using Keryx;

            namespace Contracts;

            [Behavior(typeof(Behaviors.Tagged))]
            [Behavior(typeof(Behaviors.Kept))]
            public sealed record Placed : INotification;
            """,
            cancellationToken,
            references: [behaviors1]
        );

        var result = await HostBuilder.AddCompilationReferences(behaviors2, contracts).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "KRX009")).IsEmpty();
        var diagnostics = HostDiagnostics(result).ToArray();
        await Assert.That(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(["Behavior 'Tagged' does not fit 'Placed': 'Placed' does not satisfy the constraint 'T : IUrgent'"]);
        await AssertWithoutLocationAsync(diagnostics);
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Behaviors.Kept.Handle<global::Contracts.Placed, ");
        await Assert.That(mediator).DoesNotContain("Tagged");
    }

    [Test]
    public async Task KRX034_For_A_Behavior_On_A_Referenced_Notification_Type_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // Contracts attaches Behaviors.Tagged, from a third assembly the host does not reference, to Placed. The host cannot resolve the
        // class, so it is KRX034 naming the assembly to reference, not KRX009; Kept (in Contracts) still runs.
        var behaviors = await CompileLibraryAsync(
            "Behaviors",
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Behaviors;

            [NotificationBehavior]
            public static class Tagged
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext => next.InvokeAsync(default);
            }
            """,
            cancellationToken
        );
        var contracts = await CompileLibraryAsync(
            "Contracts",
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Contracts;

            [Behavior(typeof(Behaviors.Tagged))]
            [Behavior(typeof(Kept))]
            public sealed record Placed : INotification;

            [NotificationBehavior]
            public static class Kept
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext => next.InvokeAsync(default);
            }
            """,
            cancellationToken,
            references: [behaviors]
        );

        var result = await HostBuilder.AddCompilationReferences(contracts).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Behaviors.Tagged', which the Keryx manifest of 'Contracts' needs, cannot be resolved or is not accessible from this host; reference assembly 'Behaviors'",
                         ]
                     );
        await AssertWithoutLocationAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Contracts.Kept.Handle<global::Contracts.Placed, ");
        await Assert.That(mediator).DoesNotContain("Tagged");
    }

    [Test]
    public async Task A_Fitting_Behavior_On_A_Referenced_Notification_Type_Reports_Nothing(CancellationToken cancellationToken)
    {
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            [Behavior(typeof(Kept))]
            public sealed record Placed : INotification;

            [NotificationBehavior]
            public static class Kept
            {
                public static ValueTask Handle<TNext>(Placed notification, TNext next) where TNext : struct, INext => next.InvokeAsync(default);
            }
            """,
            cancellationToken
        );

        var result = await HostBuilder.AddCompilationReferences(library).AddSources(Usings + "namespace Sample;").Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("global::Lib.Kept.Handle<");
    }

    [Test]
    public async Task A_Referenced_Global_Behavior_Marked_Only_On_Its_Method_Is_Found(CancellationToken cancellationToken)
    {
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Lib;

            public sealed record Ping : IRequest<int>;

            public static partial class PingHandler
            {
                private static int Handle(Ping request) => 1;
            }

            [RequestBehavior]
            [StreamBehavior]
            public static class MethodOnly
            {
                [GlobalBehavior]
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> =>
                    next.InvokeAsync(default);

                public static System.Collections.Generic.IAsyncEnumerable<TItem> HandleAsync<TRequest, TItem, TNext>(TRequest request, TNext next)
                    where TRequest : IStreamRequest<TItem>
                    where TNext : struct, IStreamNext<TItem> =>
                    next.InvokeAsync(default);
            }
            """,
            cancellationToken
        );

        var result = await HostBuilder.AddCompilationReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           public sealed record Names : IStreamRequest<string>;
                                           public static partial class NamesHandler { private static IAsyncEnumerable<string> Handle(Names request) => null!; }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEquivalentTo(["KRX035: Global request behavior 'MethodOnly' from assembly 'Lib' applies in this host"]);
        var mediator = Source(result, HostEmitter.MediatorHintName);
        // Only its request kind is global: the host's stream request does not get its stream behavior method.
        await Assert.That(mediator).Contains("global::Lib.MethodOnly.Handle<global::Lib.Ping, int, ");
        await Assert.That(mediator).DoesNotContain("MethodOnly.HandleAsync");
    }

    [Test]
    public async Task The_Projection_Is_Read_From_The_Scan_Cache_File(CancellationToken cancellationToken)
    {
        // The first build writes obj/ctp/Keryx.ctpjson; the next one reads the projection back while the reference's MVID is unchanged. The
        // cached projection is altered on disk, so a diagnostic that reflects the alteration proves the cache was read.
        var future = await CompileLibraryAsync("Future", FutureLibrary, cancellationToken, kind: LibraryKind.Plain);
        var directory = Path.Combine(Path.GetTempPath(), "keryx-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = await ScanCacheHostAsync("First", future, directory, cancellationToken);
            await Assert.That(first.Select(static d => d.Id)).IsEquivalentTo(["KRX032"]);

            var file = Path.Combine(directory, "ctp", "Keryx.ctpjson");
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            await Assert.That(text).Contains("\"formatVersion\":2");
            await File.WriteAllTextAsync(file, text.Replace("\"formatVersion\":2", "\"formatVersion\":7", StringComparison.Ordinal), cancellationToken);

            var second = await ScanCacheHostAsync("Second", future, directory, cancellationToken, file);
            await Assert.That(second.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                        .IsEquivalentTo(
                             ["Assembly 'Future' carries a Keryx manifest of format version 7, but the Keryx in this project reads format versions up to 1; upgrade Keryx in this project"]
                         );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task A_Projection_Cached_With_An_Older_Data_Version_Is_Recomputed(CancellationToken cancellationToken)
    {
        // ProjectionDataVersion changes whenever the projection's shape or derivation does (static behaviors: 2). An entry written with another
        // data version is ignored and the projection recomputed, so the altered format version on disk is never seen.
        var future = await CompileLibraryAsync("Future", FutureLibrary, cancellationToken, kind: LibraryKind.Plain);
        var directory = Path.Combine(Path.GetTempPath(), "keryx-tests", Guid.NewGuid().ToString("N"));
        try
        {
            _ = await ScanCacheHostAsync("First", future, directory, cancellationToken);

            var file = Path.Combine(directory, "ctp", "Keryx.ctpjson");
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var stale = System.Text.RegularExpressions.Regex.Replace(
                text.Replace("\"formatVersion\":2", "\"formatVersion\":7", StringComparison.Ordinal),
                "\"[Vv]ersion\":\\s*" + ReferenceScan.ProjectionDataVersion.ToString(CultureInfo.InvariantCulture) + "\\b",
                "\"version\":1"
            );
            await Assert.That(stale).Contains("\"formatVersion\":7");
            await Assert.That(stale).Contains("\"version\":1");
            await File.WriteAllTextAsync(file, stale, cancellationToken);

            var second = await ScanCacheHostAsync("Second", future, directory, cancellationToken, file);
            await Assert.That(second.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                        .IsEquivalentTo(
                             ["Assembly 'Future' carries a Keryx manifest of format version 2, but the Keryx in this project reads format versions up to 1; upgrade Keryx in this project"]
                         );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task The_Projection_Round_Trips_Through_Json(CancellationToken cancellationToken)
    {
        var reference = new KeryxTypeReference("Lib", "Lib.Outer`1+Inner", false, Array(new KeryxTypeReference("System.Runtime", "System.Int32", false, Array<KeryxTypeReference>(), Array(1, 2))), Array<int>());
        var ping = new KeryxTypeReference("Lib", "Lib.Ping", false, Array<KeryxTypeReference>(), Array<int>());
        var audit = new KeryxTypeReference("Lib", "Lib.Audit`2", true, Array<KeryxTypeReference>(), Array<int>());
        var model = new ReferencedAssemblyModel(
            "Lib",
            1,
            Array(new ReferencedHandlerModel(reference, ping, Analyzers.Handlers.HandlerKind.Notification, Array(audit), 3, true)),
            Array(audit),
            Array(ping),
            Array(audit)
        );

        var json = JsonSerializer.Serialize(AssemblyProjection.From(model), KeryxProjectionJsonContext.Default.AssemblyProjection);
        var read = JsonSerializer.Deserialize(json, KeryxProjectionJsonContext.Default.AssemblyProjection)!.ToModel();

        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(read).IsEqualTo(model);
        await Assert.That(reference.Display).IsEqualTo("Lib.Outer`1+Inner[System.Int32[][,]]");
    }

    private static Specular.Scanning.EquatableArray<T> Array<T>(params T[] items) => System.Collections.Immutable.ImmutableArray.Create(items);

    /// <summary>A library whose notification handler needs a type from an assembly (Infra) that a test host does not reference.</summary>
    private const string ExcludedLibrary = """
        using System.Threading.Tasks;
        using Keryx;

        namespace Lib;

        public sealed record Saved : INotification;

        public partial class OnSaved
        {
            private static ValueTask Handle(Saved notification, Infra.IDb db) => default;
        }
        """;

    /// <summary>A host that excludes <c>Lib.OnSaved</c>.</summary>
    private const string ExcludingHost = """
        [assembly: ExcludeHandler(typeof(Lib.OnSaved))]

        namespace Sample;
        """;

    private const string FutureLibrary = """
        [assembly: Keryx.Internal.KeryxManifest(FormatVersion = 2)]

        namespace Future;

        public sealed record Ping : Keryx.IRequest<int>;
        """;

    private async Task<GeneratorTestResults> GenerateAsync(string host, CancellationToken cancellationToken, bool withHandlerAnalyzer = false) =>
        await GenerateAsync(host, await CompileLibrariesAsync(cancellationToken), cancellationToken, withHandlerAnalyzer);

    /// <summary>
    ///     Runs a host over <paramref name="libraries" />, so two hosts in one test can share one compile (one load) of the libraries. A
    ///     second host of the same project name needs <paramref name="ownLoadContext" />: it cannot share a load context with the first.
    /// </summary>
    private async Task<GeneratorTestResults> GenerateAsync(
        string host,
        GeneratorTestResults[] libraries,
        CancellationToken cancellationToken,
        bool withHandlerAnalyzer = false,
        bool ownLoadContext = false
    )
    {
        var builder = ( ownLoadContext ? HostBuilder.WithAssemblyLoadContext(NewLoadContext()) : HostBuilder ).AddCompilationReferences(libraries);
        builder = withHandlerAnalyzer ? builder.WithAnalyzer<Analyzers.Diagnostics.HandlerAnalyzer>() : builder;
        return await builder.AddSources(Usings + host).Build().GenerateAsync(cancellationToken);
    }

    /// <summary>The <see cref="Contracts" /> and <see cref="Orders" /> libraries, compiled.</summary>
    private async Task<GeneratorTestResults[]> CompileLibrariesAsync(CancellationToken cancellationToken)
    {
        var contracts = await CompileLibraryAsync("Contracts", Contracts, cancellationToken);
        var orders = await CompileLibraryAsync("Orders", Orders, cancellationToken, references: [contracts]);
        return [contracts, orders];
    }

    private static IEnumerable<string> Diagnostics(GeneratorTestResults result) =>
        HostDiagnostics(result).Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}");

    private static IEnumerable<Diagnostic> HostDiagnostics(GeneratorTestResults result) => result.Results[typeof(HostGenerator)].Diagnostics;

    /// <summary>
    ///     Asserts that each diagnostic is reported without a location (<see cref="Location.None" />): a host diagnostic about something with
    ///     no source location of its own has nowhere else to go, since a host is an MSBuild property (ADR 0001, Diagnostics).
    /// </summary>
    private static async Task AssertWithoutLocationAsync(IEnumerable<Diagnostic> diagnostics)
    {
        var all = diagnostics.ToArray();
        await Assert.That(all).IsNotEmpty();
        foreach (var diagnostic in all)
        {
            await Assert.That(diagnostic.Location).IsEqualTo(Location.None);
        }
    }

    /// <summary>Errors of the final compilation and the generators, by ID.</summary>
    private static IEnumerable<string> ErrorIds(GeneratorTestResults result) =>
        result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Distinct().Order(StringComparer.Ordinal);

    private static string Source(GeneratorTestResults result, string hintName) => TestSources.HostSource(result, hintName);

    /// <summary>
    ///     Asserts that <paramref name="result" /> generated exactly the sources <paramref name="without" /> generated (same hint names, same
    ///     text) and that its generated host compiles: an <c>ExcludeHandler</c> that is KRX039 leaves the host as if it were not there.
    /// </summary>
    private static async Task AssertComposedAsWithoutAsync(GeneratorTestResults result, GeneratorTestResults without, CancellationToken cancellationToken)
    {
        await Assert.That(GeneratedSources(result)).IsEquivalentTo(GeneratedSources(without), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(GeneratedSources(result).Any(static s => s.Hint.EndsWith(HostEmitter.MediatorHintName, StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.FinalCompilation.GetDiagnostics(cancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.ToString()))
                    .IsEmpty();
    }

    private static IEnumerable<(string Hint, string Text)> GeneratedSources(GeneratorTestResults result) =>
        result
           .Results.Values.SelectMany(static r => r.SyntaxTrees)
           .Select(static t => (Hint: Path.GetFileName(t.FilePath), Text: t.ToString()))
           .OrderBy(static s => s.Hint, StringComparer.Ordinal);

    private static IEnumerable<string> Entries(object journal) =>
        (IEnumerable<string>)journal.GetType().GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance)!.GetValue(journal)!;

    /// <summary>
    ///     Runs a host that references <paramref name="library" /> and writes its scan cache under <paramref name="directory" />
    ///     (<c>IntermediateOutputPath</c>), with the cache file fed back as an AdditionalFile when given, as the build does; returns the host
    ///     generator's diagnostics. Each run references a new reference instance over the same image: same MVID, nothing remembered in memory.
    /// </summary>
    private async Task<ImmutableArray<Diagnostic>> ScanCacheHostAsync(
        string projectName,
        GeneratorTestResults library,
        string directory,
        CancellationToken cancellationToken,
        string? cacheFile = null
    )
    {
        var builder = HostBuilder.WithProjectName(projectName)
                                 .WithIntermediateOutputPath(directory + Path.DirectorySeparatorChar)
                                 .AddReferences(MetadataReference.CreateFromImage(library.Image))
                                 .AddSource("Host.cs", "namespace Host;");
        builder = cacheFile is null ? builder : builder.AddAdditionalText(cacheFile, await File.ReadAllTextAsync(cacheFile, cancellationToken));
        var result = await builder.Build().GenerateAsync(cancellationToken);
        return result.Results[typeof(HostGenerator)].Diagnostics;
    }
}
