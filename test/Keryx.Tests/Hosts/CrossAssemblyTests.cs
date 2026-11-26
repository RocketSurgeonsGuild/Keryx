using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
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
///     (KRX005 across assemblies, KRX030 for referenced requests and <c>ExternallyHandled</c>, KRX032, KRX033, KRX034, KRX035, KRX036 for
///     <c>ExcludeHandler</c> and <c>ExternallyHandled</c>).
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class CrossAssemblyTests : HostTest
{
    /// <summary>A contracts assembly: requests, a stream request and notifications, with handlers elsewhere.</summary>
    internal const string Contracts = """
        using Keryx;

        [assembly: KeryxContracts]

        namespace Contracts;

        public sealed record CreateOrder(string Sku) : IRequest<int>;
        public sealed record GetName(int Id) : IRequest<string>;
        public sealed record Countdown(int From) : IStreamRequest<int>;
        public interface IDomainEvent : INotification { string Id { get; } }
        public sealed record OrderPlaced(string Id) : IDomainEvent;
        """;

    /// <summary>A handler library: handlers for the contracts, a global behavior, an attached behavior and a polymorphic notification handler.</summary>
    internal const string Orders = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        namespace Orders;

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
            public static async ValueTask<int> Handle<TNext>(Contracts.CreateOrder request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<int> =>
                2 * await next.InvokeAsync(cancellationToken);
        }

        public partial class CreateOrderHandler
        {
            [Behavior(typeof(Doubling))]
            private static ValueTask<int> Handle(Contracts.CreateOrder request, IJournal journal, CancellationToken cancellationToken)
            {
                journal.Write("handle " + request.Sku);
                return new(request.Sku.Length);
            }
        }

        public static partial class GetNameHandler
        {
            private static string Handle(Contracts.GetName request) => "order-" + request.Id;
        }

        public static partial class CountdownHandler
        {
            private static async IAsyncEnumerable<int> Handle(Contracts.Countdown request, [EnumeratorCancellation] CancellationToken cancellationToken)
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
        [assembly: KeryxHost]

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
        var contracts = TestLibraries.Image("Contracts", Contracts);
        var orders = TestLibraries.Image("Orders", Orders, [MetadataReference.CreateFromImage(contracts)]);
        var host = await CompileHostAsync(
            Host,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Contracts"] = contracts, ["Orders"] = orders },
            cancellationToken
        );
        var context = AssemblyLoadContext.GetLoadContext(host)!;
        var contractsAssembly = context.LoadFromAssemblyName(new("Contracts"));
        var ordersAssembly = context.LoadFromAssemblyName(new("Orders"));
        var journal = Create(host, "Sample.Journal");
        await using var provider = BuildProvider(host, services => services.AddScoped(ordersAssembly.GetType("Orders.IJournal", throwOnError: true)!, _ => journal));
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // The referenced global behavior (Phase.Observability) wraps the attached one, which doubles the handler's response.
        var created = await mediator.Send((IRequest<int>)Create(contractsAssembly, "Contracts.CreateOrder", "abc"), cancellationToken);
        var name = await mediator.Send((IRequest<string>)Create(contractsAssembly, "Contracts.GetName", 7), cancellationToken);
        var items = new List<int>();
        await foreach (var item in mediator.Stream((IStreamRequest<int>)Create(contractsAssembly, "Contracts.Countdown", 3), cancellationToken))
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
        var contracts = TestLibraries.Compilation("Contracts", Contracts).ToMetadataReference();
        var orders = TestLibraries.Compilation("Orders", Orders, [contracts]).ToMetadataReference();

        var result = await HostBuilder.AddReferences(contracts, orders).AddSources(Usings + Host).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal))).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::Contracts.CreateOrder request");
        await Assert.That(mediator).Contains("Stream(global::Contracts.Countdown request");
        await Assert.That(mediator).Contains("global::Orders.Journaled.Handle<global::Contracts.CreateOrder, int, ");
        await Assert.That(mediator).Contains("global::Orders.IJournal Behavior1Service0;");
        await Assert.That(mediator).Contains("global::Orders.Doubling.Handle<Link2>(");
        await Assert.That(Source(result, HostEmitter.ServiceCollectionExtensionsHintName))
                    .Contains("new global::Orders.CreateOrderHandler(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Orders.IJournal>(serviceProvider))");
    }

    [Test]
    public async Task KRX035_Lists_The_Referenced_Global_Behaviors_And_Their_Assembly(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(Host, cancellationToken);

        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(["KRX035: Global request behavior 'Journaled' from assembly 'Orders' applies in this host"]);
        // The referenced global has no source location, so it is reported on [assembly: KeryxHost].
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
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
    public async Task KRX005_Names_Both_Handlers_And_Their_Assemblies(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            Host + """

                   public static partial class MyGetName
                   {
                       private static string Handle(Contracts.GetName request) => "mine";
                   }
                   """,
            cancellationToken
        );

        var duplicate = Diagnostics(result).Single(static d => d.StartsWith("KRX005", StringComparison.Ordinal));
        await Assert.That(duplicate).StartsWith("KRX005: Request type 'Contracts.GetName' has more than one handler in this host: 'GetNameHandler' (assembly 'Orders'), 'MyGetName' (assembly '");
        // Reported on the host's own handler (OQ-49), not on [assembly: KeryxHost].
        var location = HostDiagnostics(result).Single(static d => d.Id == "KRX005").Location;
        await Assert.That(location.IsInSource).IsTrue();
        await Assert.That(location.SourceTree!.GetText(cancellationToken).ToString(location.SourceSpan)).IsEqualTo("Handle");
        await Assert.That(location.SourceTree.GetRoot(cancellationToken).FindNode(location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First().Identifier.Text)
                    .IsEqualTo("MyGetName");
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Send(global::Contracts.GetName request");
    }

    [Test]
    public async Task ExcludeHandler_Replaces_A_Referenced_Handler(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            "[assembly: ExcludeHandler(typeof(Orders.GetNameHandler))]\n"
          + Host
          + """

            public static partial class MyGetName
            {
                private static string Handle(Contracts.GetName request) => "mine";
            }
            """,
            cancellationToken
        );

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal))).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Sample.MyGetName.InvokeAsync(");
        await Assert.That(mediator).DoesNotContain("GetNameHandler");
    }

    [Test]
    public async Task ExcludeHandler_Without_A_Replacement_Leaves_The_Request_Unhandled(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync("[assembly: ExcludeHandler(typeof(Orders.GetNameHandler))]\n" + Host, cancellationToken);

        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)))
                    .IsEquivalentTo(["KRX030: Request type 'GetName' has no handler in this host"]);
    }

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
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.All(static d => d.Location.IsInSource)).IsTrue();
    }

    [Test]
    public async Task KRX030_For_A_Referenced_Request_Without_A_Handler(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", Contracts);

        var result = await HostBuilder.AddReferences(contracts).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX030: Request type 'Countdown' has no handler in this host",
                             "KRX030: Request type 'CreateOrder' has no handler in this host",
                             "KRX030: Request type 'GetName' has no handler in this host",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        // Reported on [assembly: KeryxHost], so per-file .editorconfig severity applies (OQ-35).
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.All(static d => d.Location.IsInSource)).IsTrue();
        // The referenced notification type is known: it gets a typed overload, although nothing handles it.
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Publish(global::Contracts.OrderPlaced notification");
    }

    [Test]
    public async Task KRX034_For_A_Manifest_Type_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // The handler library references the contracts, the host does not (a PrivateAssets reference, for example).
        var contracts = TestLibraries.Reference("Contracts", Contracts);
        var orders = TestLibraries.Reference("Orders", Orders, [contracts]);

        var result = await HostBuilder.AddReferences(orders).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        // The handlers are left out, so the generated code compiles: KRX034 is the only error.
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Distinct()).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Contracts.Countdown', which the Keryx manifest of 'Orders' needs, cannot be resolved or is not accessible from this host; reference assembly 'Contracts'",
                             "KRX034: Type 'Contracts.CreateOrder', which the Keryx manifest of 'Orders' needs, cannot be resolved or is not accessible from this host; reference assembly 'Contracts'",
                             "KRX034: Type 'Contracts.GetName', which the Keryx manifest of 'Orders' needs, cannot be resolved or is not accessible from this host; reference assembly 'Contracts'",
                             "KRX034: Type 'Contracts.IDomainEvent', which the Keryx manifest of 'Orders' needs, cannot be resolved or is not accessible from this host; reference assembly 'Contracts'",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Contracts");
    }

    [Test]
    public async Task KRX034_For_A_Constructor_Parameter_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // The generated constructor's parameter is not named in the manifest, but AddKeryx() would name it (OQ-47). The handler is left out,
        // and its request still counts as handled, so there is no KRX030 on top.
        var contracts = TestLibraries.Reference("Contracts", Contracts);
        var services = TestLibraries.Reference("Services", "namespace Services; public interface IClock { }");
        var library = TestLibraries.Reference(
            "Lib",
            """
            namespace Lib;

            public partial class GetNameHandler
            {
                private static string Handle(Contracts.GetName request, Services.IClock clock) => "";
            }
            """,
            [contracts, services]
        );

        var result = await HostBuilder.AddReferences(contracts, library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        var diagnostics = Diagnostics(result).ToArray();
        await Assert.That(diagnostics.Where(static d => d.StartsWith("KRX030", StringComparison.Ordinal) && d.Contains("GetName", StringComparison.Ordinal))).IsEmpty();
        await Assert.That(diagnostics.Single(static d => d.StartsWith("KRX034", StringComparison.Ordinal)))
                    .Contains("which the Keryx manifest of 'Lib' needs, cannot be resolved or is not accessible from this host; reference assembly 'Services'");
        await AssertOnHostAttributeAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("GetNameHandler");
    }

    [Test]
    public async Task KRX032_For_A_Newer_Manifest_Format(CancellationToken cancellationToken)
    {
        var future = TestLibraries.Reference("Future", FutureLibrary, generate: false);

        var result = await HostBuilder.AddReferences(future).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        // Nothing of the assembly is read, so its request is not reported as unhandled either.
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         ["KRX032: Assembly 'Future' carries a Keryx manifest of format version 2, but the Keryx in this project reads format versions up to 1; upgrade Keryx in this project"]
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
    }

    [Test]
    public async Task KRX033_For_A_Keryx_Reference_Without_A_Manifest(CancellationToken cancellationToken)
    {
        var plain = TestLibraries.Reference("Plain", "namespace Plain; public sealed record Ping : Keryx.IRequest<int>;", generate: false);

        var result = await HostBuilder.AddReferences(plain).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX033: Assembly 'Plain' references Keryx but has no Keryx manifest, so the Keryx generator did not run there and its handlers are not part of this host; reference the Keryx package directly from that project",
                         ]
                     );
        await Assert.That(HostDiagnostics(result).Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
    }

    /// <summary>An app host: one request whose handler is internal (no manifest entry), one whose handler is public.</summary>
    private const string AppHost = """
        using Keryx;

        [assembly: KeryxHost]

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
        var app = TestLibraries.Reference("App", AppHost, host: true);

        var result = await HostBuilder.AddReferences(app).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result)).IsEquivalentTo(["KRX030: Request type 'Hidden' has no handler in this host"]);
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Send(global::App.Visible request");
        await Assert.That(mediator).DoesNotContain("Hidden");
    }

    [Test]
    public async Task ExternallyHandled_Names_A_Request_Of_A_Referenced_App_Host(CancellationToken cancellationToken)
    {
        var app = TestLibraries.Reference("App", AppHost, host: true);

        var result = await HostBuilder.AddReferences(app)
                                      .AddSources(Usings + "[assembly: KeryxHost]\n[assembly: ExternallyHandled(typeof(App.Hidden))]\nnamespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Hidden");
    }

    [Test]
    public async Task ExternallyHandled_Silences_KRX030_For_The_Named_Referenced_Types_Only(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", Contracts);

        var result = await HostBuilder.AddReferences(contracts)
                                      .AddSources(
                                           Usings
                                         + "[assembly: KeryxHost]\n[assembly: ExternallyHandled(typeof(Contracts.GetName))]\n[assembly: ExternallyHandled(typeof(Contracts.Countdown))]\nnamespace Sample;"
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(Diagnostics(result)).IsEquivalentTo(["KRX030: Request type 'CreateOrder' has no handler in this host"]);
    }

    [Test]
    public async Task KRX036_For_ExternallyHandled_On_A_Handled_Referenced_Request_Or_A_Notification(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            "[assembly: ExternallyHandled(typeof(Contracts.GetName))]\n[assembly: ExternallyHandled(typeof(Contracts.OrderPlaced))]\n" + Host,
            cancellationToken
        );

        await Assert.That(Diagnostics(result).Where(static d => !d.StartsWith("KRX035", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    .IsEquivalentTo(
                         [
                             "KRX036: [assembly: ExternallyHandled(typeof(GetName))] has no effect, because 'GetName' has a handler in this host",
                             "KRX036: [assembly: ExternallyHandled(typeof(OrderPlaced))] has no effect, because 'OrderPlaced' is not a request or stream request",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
        // Reported on the attribute.
        foreach (var diagnostic in HostDiagnostics(result).Where(static d => d.Id == "KRX036"))
        {
            await Assert.That(diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)).StartsWith("ExternallyHandled");
        }
    }

    [Test]
    public async Task KRX005_For_Two_Referenced_Assemblies_That_Handle_One_Request(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", Contracts);
        var libA = TestLibraries.Reference("LibA", "namespace LibA; public static partial class NameA { private static string Handle(Contracts.GetName request) => \"a\"; }", [contracts]);
        var libB = TestLibraries.Reference("LibB", "namespace LibB; public static partial class NameB { private static string Handle(Contracts.GetName request) => \"b\"; }", [contracts]);

        var result = await HostBuilder.AddReferences(contracts, libA, libB).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        var duplicates = HostDiagnostics(result).Where(static d => d.Id == "KRX005").ToArray();
        await Assert.That(duplicates.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(["Request type 'Contracts.GetName' has more than one handler in this host: 'NameA' (assembly 'LibA'), 'NameB' (assembly 'LibB')"]);
        // Neither handler is in source, so it is reported on [assembly: KeryxHost] (OQ-49), and neither is dispatched.
        await AssertOnHostAttributeAsync(duplicates);
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Send(global::Contracts.GetName request");
    }

    [Test]
    public async Task KRX005_Is_Not_Reported_Again_For_Duplicates_Inside_One_Referenced_Assembly(CancellationToken cancellationToken)
    {
        // The library compiles: same-assembly KRX005 is its analyzer's, which the test library does not run, and its generator reports nothing.
        var contracts = TestLibraries.Reference("Contracts", Contracts);
        var library = TestLibraries.Reference(
            "Lib",
            """
            namespace Lib;

            public static partial class NameA { private static string Handle(Contracts.GetName request) => "a"; }
            public static partial class NameB { private static string Handle(Contracts.GetName request) => "b"; }
            """,
            [contracts]
        );

        var result = await HostBuilder.AddReferences(contracts, library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(HostDiagnostics(result).Where(static d => d.Id == "KRX005")).IsEmpty();
    }

    [Test]
    public async Task ExcludeHandler_Removes_A_Referenced_Notification_Handler(CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Image("Contracts", Contracts);
        var orders = TestLibraries.Image("Orders", Orders, [MetadataReference.CreateFromImage(contracts)]);
        var source = "[assembly: ExcludeHandler(typeof(Orders.AuditEvents))]\n" + Host;

        var result = await HostBuilder
                          .AddReferences(MetadataReference.CreateFromImage(contracts), MetadataReference.CreateFromImage(orders))
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
        var contracts = TestLibraries.Image("Contracts", Contracts);
        var orders = TestLibraries.Image("Orders", Orders, [MetadataReference.CreateFromImage(contracts)]);
        var host = await CompileHostAsync(
            "[assembly: ExcludeHandler(typeof(Orders.AuditEvents))]\n" + Host,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Contracts"] = contracts, ["Orders"] = orders },
            cancellationToken
        );
        var context = AssemblyLoadContext.GetLoadContext(host)!;
        var contractsAssembly = context.LoadFromAssemblyName(new("Contracts"));
        var ordersAssembly = context.LoadFromAssemblyName(new("Orders"));
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
        // A test host replaces a handler whose infrastructure it does not reference: the excluded entry is never resolved, so neither KRX034
        // nor KRX036 is reported.
        var infra = TestLibraries.Reference("Infra", "namespace Infra; public interface IDb { }");
        var library = TestLibraries.Image("Lib", ExcludedLibrary, [infra]);

        var result = await HostBuilder.AddReferences(MetadataReference.CreateFromImage(library)).AddSources(Usings + "[assembly: KeryxHost]\n" + ExcludingHost).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("global::Sample.MyPing.InvokeAsync(");
    }

    [Test]
    public async Task An_Excluded_Referenced_Handler_Is_Replaced_By_The_Hosts_Own(CancellationToken cancellationToken)
    {
        var infra = TestLibraries.Reference("Infra", "namespace Infra; public interface IDb { }");
        var library = TestLibraries.Image("Lib", ExcludedLibrary, [infra]);

        var host = await CompileHostAsync("[assembly: KeryxHost]\n" + ExcludingHost, "Lib", library, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var ping = (IRequest<int>)Create(AssemblyLoadContext.GetLoadContext(host)!.LoadFromAssemblyName(new("Lib")), "Lib.Ping");
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(ping, cancellationToken)).IsEqualTo(2);
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Global_Behavior_Whose_Method_Injects_An_Unreferenced_Type(CancellationToken cancellationToken)
    {
        // The behavior itself resolves, but the pipeline context would name its behavior method's injected parameter type (OQ-47): it is
        // left out of the request pipelines, the kind of that method (OQ-57), so the generated code compiles, and KRX035 does not list it.
        var infra = TestLibraries.Reference("Infra", "namespace Infra; public interface ILog { }");
        var library = TestLibraries.Reference(
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
            [infra]
        );

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Infra.ILog', which the request behavior method 'Logged.Handle' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'",
                         ]
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Logged");
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Send(global::Lib.Ping request");
    }

    [Test]
    public async Task KRX034_Is_Per_Kind_And_The_Other_Kinds_Of_The_Class_Still_Apply(CancellationToken cancellationToken)
    {
        // OQ-57: a referenced global behavior with a request and a stream behavior method, where only the request method injects a type the
        // host cannot resolve. KRX034 names that method and its kind; the class stays in the stream pipelines, and KRX035 lists it for the
        // stream kind only.
        var infra = TestLibraries.Image("Infra", "namespace Infra; public interface ILog { }");
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
        var library = TestLibraries.Image("Lib", librarySource, [MetadataReference.CreateFromImage(infra)]);

        var result = await HostBuilder.AddReferences(MetadataReference.CreateFromImage(library))
                                      .AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;")
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
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.Handle<");
        await Assert.That(mediator).Contains("global::Lib.Split.HandleAsync<global::Lib.Numbers, int, ");

        // The generated code compiles (KRX034 is the generator's error, not the compiler's), and the stream kind runs.
        var host = LoadHost(result, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Lib"] = library, ["Infra"] = infra }, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = AssemblyLoadContext.GetLoadContext(host)!.LoadFromAssemblyName(new("Lib"));
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
        var infra = TestLibraries.Image("Infra", "namespace Infra; public interface ILog { }");
        var library = TestLibraries.Image("Lib", SplitLibrary("", "[Behavior(typeof(Split))]"), [MetadataReference.CreateFromImage(infra)]);

        var result = await HostBuilder.AddReferences(MetadataReference.CreateFromImage(library))
                                      .AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal))).IsEquivalentTo([SplitKrx034]);
        await AssertOnHostAttributeAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Lib.Split.Handle<global::Lib.Ping, int, ");
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.HandleAsync<");
        await Assert.That(mediator).DoesNotContain("global::Lib.Numbers");

        var host = LoadHost(result, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Lib"] = library, ["Infra"] = infra }, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = AssemblyLoadContext.GetLoadContext(host)!.LoadFromAssemblyName(new("Lib"));
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
        var infra = TestLibraries.Image("Infra", "namespace Infra; public interface ILog { }");
        var library = TestLibraries.Image("Lib", SplitLibrary("[assembly: Behavior(typeof(Lib.Split))]", ""), [MetadataReference.CreateFromImage(infra)]);

        var result = await HostBuilder.AddReferences(MetadataReference.CreateFromImage(library))
                                      .AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;")
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result).Where(static d => d.StartsWith("KRX034", StringComparison.Ordinal))).IsEquivalentTo([SplitKrx034]);
        await AssertOnHostAttributeAsync(HostDiagnostics(result).Where(static d => d.Id == "KRX034"));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Lib.Split.Handle<global::Lib.Ping, int, ");
        await Assert.That(mediator).DoesNotContain("global::Lib.Split.HandleAsync<");

        var host = LoadHost(result, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Lib"] = library, ["Infra"] = infra }, cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var lib = AssemblyLoadContext.GetLoadContext(host)!.LoadFromAssemblyName(new("Lib"));
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
        var infra = TestLibraries.Reference("Infra", "namespace Infra; public interface ILog { }");
        var library = TestLibraries.Reference(
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
            [infra]
        );

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Infra.ILog', which the notification behavior method 'Audited.Handle' from assembly 'Lib' injects, cannot be resolved or is not accessible from this host; reference assembly 'Infra'",
                         ]
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("Publish(global::Lib.Placed notification");
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Audited");
    }

    [Test]
    public async Task KRX034_For_A_Referenced_Handler_Built_Against_Another_Shape_Of_Its_Request(CancellationToken cancellationToken)
    {
        // The library was built against a contracts assembly where Count is a request; the host references one where it is a stream request.
        var contractsV1 = TestLibraries.Reference("Contracts", "using Keryx; [assembly: KeryxContracts] namespace Contracts; public sealed record Count(int To) : IRequest<int>;");
        var contractsV2 = TestLibraries.Reference("Contracts", "using Keryx; [assembly: KeryxContracts] namespace Contracts; public sealed record Count(int To) : IStreamRequest<int>;");
        var library = TestLibraries.Reference("Lib", "namespace Lib; public static partial class CountHandler { private static int Handle(Contracts.Count request) => request.To; }", [contractsV1]);

        var result = await HostBuilder.AddReferences(contractsV2, library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        // The handler is left out with an error of its own; its request still counts as handled, so there is no KRX030 on top (OQ-47).
        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Handler 'CountHandler' in the Keryx manifest of 'Lib' does not fit the types this host resolves: the manifest describes a request handler of 'Contracts.Count', but that type is not a request this host can dispatch; build 'Lib' against the assemblies this host references",
                         ]
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("CountHandler");
    }

    [Test]
    public async Task KRX009_For_A_Behavior_A_Referenced_Manifest_Attaches_That_Does_Not_Fit(CancellationToken cancellationToken)
    {
        // The library attached a constrained behavior that fit the request it was built against; the request the host resolves no longer
        // satisfies the constraint, so the behavior would drop out of the pipeline silently.
        var contractsV1 = TestLibraries.Reference(
            "Contracts",
            "using Keryx; [assembly: KeryxContracts] namespace Contracts; public interface ITagged { } public sealed record GetName(int Id) : IRequest<string>, ITagged;"
        );
        var contractsV2 = TestLibraries.Reference(
            "Contracts",
            "using Keryx; [assembly: KeryxContracts] namespace Contracts; public interface ITagged { } public sealed record GetName(int Id) : IRequest<string>;"
        );
        var library = TestLibraries.Reference(
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

            public static partial class NameHandler
            {
                [Behavior(typeof(Tagged))]
                private static string Handle(Contracts.GetName request) => "name";
            }
            """,
            [contractsV1]
        );

        var result = await HostBuilder.AddReferences(contractsV2, library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        var diagnostics = HostDiagnostics(result).ToArray();
        await Assert.That(diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX009"]);
        await Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture))
                    .IsEqualTo("Behavior 'Tagged' does not fit 'NameHandler.Handle': 'GetName' does not satisfy the constraint 'TRequest : ITagged'");
        await Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture)).Contains("does not fit 'NameHandler.Handle': ");
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await AssertOnHostAttributeAsync(diagnostics);
        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX009"]);
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).DoesNotContain("Tagged");
    }

    [Test]
    public async Task KRX009_For_A_Behavior_On_A_Referenced_Notification_Type_That_No_Longer_Fits(CancellationToken cancellationToken)
    {
        // OQ-55: Contracts was built against a Behaviors assembly whose Tagged fit every notification; the Behaviors assembly the host
        // references constrains it to IUrgent, which Placed is not. Tagged is skipped with KRX009 on [assembly: KeryxHost]; Kept still runs.
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
        var behaviors1 = TestLibraries.Reference("Behaviors", behaviorsV1.Replace("CONSTRAINT", "INotification", StringComparison.Ordinal));
        var behaviors2 = TestLibraries.Reference("Behaviors", behaviorsV1.Replace("CONSTRAINT", "IUrgent", StringComparison.Ordinal));
        var contracts = TestLibraries.Reference(
            "Contracts",
            """
            using Keryx;

            [assembly: KeryxContracts]

            namespace Contracts;

            [Behavior(typeof(Behaviors.Tagged))]
            [Behavior(typeof(Behaviors.Kept))]
            public sealed record Placed : INotification;
            """,
            [behaviors1]
        );

        var result = await HostBuilder.AddReferences(behaviors2, contracts).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id != "KRX009")).IsEmpty();
        var diagnostics = HostDiagnostics(result).ToArray();
        await Assert.That(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(["Behavior 'Tagged' does not fit 'Placed': 'Placed' does not satisfy the constraint 'T : IUrgent'"]);
        await AssertOnHostAttributeAsync(diagnostics);
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Behaviors.Kept.Handle<global::Contracts.Placed, ");
        await Assert.That(mediator).DoesNotContain("Tagged");
    }

    [Test]
    public async Task KRX034_For_A_Behavior_On_A_Referenced_Notification_Type_Whose_Assembly_The_Host_Does_Not_Reference(CancellationToken cancellationToken)
    {
        // Contracts attaches Behaviors.Tagged, from a third assembly the host does not reference, to Placed. The host cannot resolve the
        // class, so it is KRX034 naming the assembly to reference, not KRX009; Kept (in Contracts) still runs.
        var behaviors = TestLibraries.Reference(
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
            """
        );
        var contracts = TestLibraries.Reference(
            "Contracts",
            """
            using System.Threading.Tasks;
            using Keryx;

            [assembly: KeryxContracts]

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
            [behaviors]
        );

        var result = await HostBuilder.AddReferences(contracts).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        await Assert.That(ErrorIds(result)).IsEquivalentTo(["KRX034"]);
        await Assert.That(Diagnostics(result))
                    .IsEquivalentTo(
                         [
                             "KRX034: Type 'Behaviors.Tagged', which the Keryx manifest of 'Contracts' needs, cannot be resolved or is not accessible from this host; reference assembly 'Behaviors'",
                         ]
                     );
        await AssertOnHostAttributeAsync(HostDiagnostics(result));
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("global::Contracts.Kept.Handle<global::Contracts.Placed, ");
        await Assert.That(mediator).DoesNotContain("Tagged");
    }

    [Test]
    public async Task A_Fitting_Behavior_On_A_Referenced_Notification_Type_Reports_Nothing(CancellationToken cancellationToken)
    {
        var library = TestLibraries.Reference(
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
            """
        );

        var result = await HostBuilder.AddReferences(library).AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;").Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(Diagnostics(result)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.MediatorHintName)).Contains("global::Lib.Kept.Handle<");
    }

    [Test]
    public async Task A_Referenced_Global_Behavior_Marked_Only_On_Its_Method_Is_Found(CancellationToken cancellationToken)
    {
        var library = TestLibraries.Reference(
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
            """
        );

        var result = await HostBuilder.AddReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
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
        var image = TestLibraries.Image("Future", FutureLibrary, generate: false);
        var directory = Path.Combine(Path.GetTempPath(), "keryx-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new IntermediateOutputOptions(directory + Path.DirectorySeparatorChar);
            var first = Driver(options).RunGenerators(HostCompilation(MetadataReference.CreateFromImage(image)), cancellationToken).GetRunResult();
            await Assert.That(first.Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["KRX032"]);

            var file = Path.Combine(directory, "ctp", "Keryx.ctpjson");
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            await Assert.That(text).Contains("\"formatVersion\":2");
            await File.WriteAllTextAsync(file, text.Replace("\"formatVersion\":2", "\"formatVersion\":7", StringComparison.Ordinal), cancellationToken);

            // A new reference instance over the same image: same MVID, nothing remembered in memory.
            var second = Driver(options, new CacheFileText(file))
                        .RunGenerators(HostCompilation(MetadataReference.CreateFromImage(image)), cancellationToken)
                        .GetRunResult();
            await Assert.That(second.Diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
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
        var image = TestLibraries.Image("Future", FutureLibrary, generate: false);
        var directory = Path.Combine(Path.GetTempPath(), "keryx-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new IntermediateOutputOptions(directory + Path.DirectorySeparatorChar);
            _ = Driver(options).RunGenerators(HostCompilation(MetadataReference.CreateFromImage(image)), cancellationToken).GetRunResult();

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

            var second = Driver(options, new CacheFileText(file))
                        .RunGenerators(HostCompilation(MetadataReference.CreateFromImage(image)), cancellationToken)
                        .GetRunResult();
            await Assert.That(second.Diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
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
            true,
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

    /// <summary>A library whose handler needs a type from an assembly (Infra) that a test host does not reference.</summary>
    private const string ExcludedLibrary = """
        using Keryx;

        namespace Lib;

        public sealed record Ping : IRequest<int>;

        public partial class PingHandler
        {
            private static int Handle(Ping request, Infra.IDb db) => 1;
        }
        """;

    /// <summary>A host that excludes <c>Lib.PingHandler</c> and handles <c>Lib.Ping</c> itself.</summary>
    private const string ExcludingHost = """
        [assembly: ExcludeHandler(typeof(Lib.PingHandler))]

        namespace Sample;

        public static partial class MyPing
        {
            private static int Handle(Lib.Ping request) => 2;
        }
        """;

    private const string FutureLibrary = """
        [assembly: Keryx.Internal.KeryxManifest(FormatVersion = 2)]

        namespace Future;

        public sealed record Ping : Keryx.IRequest<int>;
        """;

    private async Task<GeneratorTestResults> GenerateAsync(string host, CancellationToken cancellationToken)
    {
        var contracts = TestLibraries.Reference("Contracts", Contracts);
        var orders = TestLibraries.Reference("Orders", Orders, [contracts]);
        return await HostBuilder.AddReferences(contracts, orders).AddSources(Usings + host).Build().GenerateAsync(cancellationToken);
    }

    private static IEnumerable<string> Diagnostics(GeneratorTestResults result) =>
        HostDiagnostics(result).Select(static d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}");

    private static IEnumerable<Diagnostic> HostDiagnostics(GeneratorTestResults result) => result.Results[typeof(HostGenerator)].Diagnostics;

    /// <summary>Asserts that each diagnostic is reported on the <c>[assembly: KeryxHost]</c> attribute (ADR 0001, Diagnostics; OQ-35).</summary>
    private static async Task AssertOnHostAttributeAsync(IEnumerable<Diagnostic> diagnostics)
    {
        var all = diagnostics.ToArray();
        await Assert.That(all).IsNotEmpty();
        foreach (var diagnostic in all)
        {
            await Assert.That(diagnostic.Location.IsInSource).IsTrue();
            await Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).StartsWith("KeryxHost");
        }
    }

    /// <summary>Errors of the final compilation and the generators, by ID.</summary>
    private static IEnumerable<string> ErrorIds(GeneratorTestResults result) =>
        result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Distinct().Order(StringComparer.Ordinal);

    private static string Source(GeneratorTestResults result, string hintName) => Behaviors.PipelineGeneratorTests.Source(result, hintName);

    private static IEnumerable<string> Entries(object journal) =>
        (IEnumerable<string>)journal.GetType().GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance)!.GetValue(journal)!;

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation HostCompilation(MetadataReference library) =>
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Host",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("[assembly: Keryx.KeryxHost]", TestLibraries.ParseOptions, "Host.cs")],
            [.. TestLibraries.References, library],
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );

    private static GeneratorDriver Driver(IntermediateOutputOptions options, params AdditionalText[] additionalTexts) =>
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            [new HostGenerator().AsSourceGenerator()],
            additionalTexts,
            TestLibraries.ParseOptions,
            options
        );

    /// <summary>The cache file as the build feeds it back (an AdditionalFile).</summary>
    private sealed class CacheFileText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default) =>
            Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(Path));
    }

    /// <summary>The build properties the scan cache is written with: <c>IntermediateOutputPath</c>, absolute.</summary>
    private sealed class IntermediateOutputOptions(string intermediateOutputPath) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; } = new Options(intermediateOutputPath);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(string? intermediateOutputPath) : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            public static Options Empty { get; } = new(null);

            // The base method's out parameter is [NotNullWhen(true)]; that attribute is ambiguous here (the analyzers' Polyfill copy is
            // visible through InternalsVisibleTo), so the override leaves it off.
#pragma warning disable CS8765
            public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
            {
                value = key == "build_property.IntermediateOutputPath" ? intermediateOutputPath : null;
                return value is not null;
            }
        }
    }
}
