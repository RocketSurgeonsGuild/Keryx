using System.Globalization;
using Keryx.Analyzers;
using Keryx.Analyzers.Hosts;
using Keryx.Tests.Hosts;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests.Notifications;

/// <summary>
///     Snapshots of generated publish pipelines (ADR 0001, Notifications; the Phase 6 brief): typed overloads, generic <c>Publish</c> with its
///     value-type and reference-type branches, the sequential and parallel strategies, polymorphic handlers in handler order, the one box of
///     a struct for interface-typed handlers called directly (no per-handler pipelines, links or behaviors under either strategy), the
///     notification behaviors' link structs and injected services at depths 0, 1, 3 and 8, and a notification type with behaviors but no
///     handlers. Also the host diagnostics notifications add: KRX037, and KRX036 for notification behavior exclusions.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class NotificationGeneratorTests : HostTest
{
    internal const string HostSource = """
        [assembly: Behavior(typeof(Sample.Logged))]

        namespace Sample;

        public interface IJournal { void Write(string entry); }
        public interface IDomainEvent : INotification { }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Observability)]
        public static class Logged
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                where TNotification : IDomainEvent
                where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        [NotificationBehavior]
        public static class Timed
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, IJournal journal, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext
            {
                journal.Write("timed");
                return next.InvokeAsync(cancellationToken);
            }
        }

        [NotificationBehavior]
        [BehaviorOrder(Phase.Outermost)]
        public static class Guarded
        {
            public static ValueTask HandleAsync<TNext>(OrderPlaced notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        /// <summary>Injects nothing; attached only to <c>Unattended</c>, which has no handlers.</summary>
        [NotificationBehavior]
        public static class Counted
        {
            public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                where TNotification : INotification
                where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        [Behavior(typeof(Guarded))]
        public sealed record OrderPlaced(string Sku) : IDomainEvent;
        public record Shipped : IDomainEvent;
        public readonly record struct Tick(int Value) : IDomainEvent;
        public sealed record Unheard : INotification;
        [Behavior(typeof(Counted))]
        public sealed record Unattended : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        [Behavior(typeof(Timed))]
        public sealed record Broadcast : INotification;

        public static partial class OnOrderPlaced
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
        }

        public partial class AuditDomainEvents
        {
            [HandlerOrder(-1)]
            ATTACHED
            private static Task Handle(IDomainEvent notification, IJournal journal) => Task.CompletedTask;
        }

        public static partial class OnTick
        {
            private static ValueTask Handle(Tick notification) => default;
        }

        public static partial class FirstListener
        {
            private static ValueTask Handle(Broadcast notification) => default;
        }

        public partial class SecondListener
        {
            private static ValueTask Handle(Broadcast notification, IJournal journal, CancellationToken cancellationToken) => default;
        }
        """;

    [Test]
    public async Task Should_Generate_Publish_Pipelines(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + HostSource.Replace("ATTACHED", "", StringComparison.Ordinal)).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var addKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName);
        await Assert.That(addKeryx).DoesNotContain("Timed");
        await VerifyHost(result);
    }

    [Test]
    public async Task A_Behavior_On_A_Notification_Handler_Method_Changes_No_Generated_Code(CancellationToken cancellationToken)
    {
        // A notification handler has no pipeline of its own: [Behavior] there is KRX042 and changes nothing, in the host's mediator or in the
        // handler's manifest entry.
        var without = await HostBuilder.WithProjectName("Without").AddSources(Usings + HostSource.Replace("ATTACHED", "", StringComparison.Ordinal)).Build().GenerateAsync(cancellationToken);
        var with = await HostBuilder.WithProjectName("With")
                                    .AddSources(Usings + HostSource.Replace("ATTACHED", "[Behavior(typeof(Timed))]\n    [Behavior(typeof(Logged))]", StringComparison.Ordinal))
                                    .Build()
                                    .GenerateAsync(cancellationToken);

        with.AssertCompilationWasSuccessful();
        await Assert.That(Source(with, HostEmitter.MediatorHintName)).IsEqualTo(Source(without, HostEmitter.MediatorHintName));
        await Assert.That(Generated(with, "AuditDomainEvents")).IsEqualTo(Generated(without, "AuditDomainEvents"));
        await Assert.That(Generated(with, "AuditDomainEvents")).DoesNotContain("Behaviors");

        static string Generated(GeneratorTestResults result, string handler) =>
            result.Results[typeof(HandlerGenerator)].SyntaxTrees.Single(t => t.FilePath.Contains(handler, StringComparison.Ordinal)).ToString();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(8)]
    public async Task Should_Generate_A_Publish_Pipeline_Of_Depth(int depth, CancellationToken cancellationToken)
    {
        var source = new System.Text.StringBuilder("namespace Sample;\n\npublic interface IClock { }\n\n");
        for (var k = 1; k <= depth; k++)
        {
            var clock = k % 2 == 1 ? "IClock clock, " : "";
            source.Append(
                CultureInfo.InvariantCulture,
                $$"""
                  [NotificationBehavior]
                  [BehaviorOrder(Phase.Default, Order = {{k}})]
                  public static class Step{{k}}
                  {
                      public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, {{clock}}CancellationToken cancellationToken)
                          where TNotification : INotification
                          where TNext : struct, INext => next.InvokeAsync(cancellationToken);
                  }


                  """
            );
        }

        for (var k = depth; k >= 1; k--)
        {
            source.Append(CultureInfo.InvariantCulture, $"[Behavior(typeof(Step{k}))]\n");
        }

        source.Append(
            """
            public sealed record Placed : INotification;

            public static partial class First { private static ValueTask Handle(Placed notification) => default; }
            public partial class Second { private static ValueTask Handle(Placed notification, IClock clock) => default; }
            """
        );
        var result = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Assert.That(Source(result, HostEmitter.ServiceCollectionExtensionsHintName)).DoesNotContain("Step");
        await VerifyHost(result).UseParameters(depth);
    }

    /// <summary>
    ///     A publish pipeline that resolves nothing (OQ-70): every handler's invoker is static and the behaviors, if any, inject nothing. It has
    ///     no context: its publish strategy is a static member of <c>private static class __Notification0</c>, the mediator has no
    ///     <c>__notification0</c> field and no <c>__CreateNotification0</c>, and its links take only the notification. Sequential and parallel,
    ///     without behaviors and with two.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Should_Give_No_Context_To_A_Publish_Pipeline_That_Resolves_Nothing(bool behaviors, bool parallel, CancellationToken cancellationToken)
    {
        var source = new System.Text.StringBuilder("namespace Sample;\n\n");
        if (behaviors)
        {
            for (var k = 1; k <= 2; k++)
            {
                source.Append(
                    CultureInfo.InvariantCulture,
                    $$"""
                      [NotificationBehavior]
                      [BehaviorOrder(Phase.Default, Order = {{k}})]
                      public static class Step{{k}}
                      {
                          public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                              where TNotification : INotification
                              where TNext : struct, INext => next.InvokeAsync(cancellationToken);
                      }


                      """
                );
            }

            source.Append("[Behavior(typeof(Step1))]\n[Behavior(typeof(Step2))]\n");
        }

        if (parallel)
        {
            source.Append("[PublishStrategy(PublishStrategy.Parallel)]\n");
        }

        source.Append(
            """
            public sealed record Placed : INotification;

            public static partial class First { [HandlerOrder(1)] private static ValueTask Handle(Placed notification) => default; }
            public static partial class Second { [HandlerOrder(2)] private static Task Handle(Placed notification, CancellationToken cancellationToken) => Task.CompletedTask; }
            """
        );
        var result = await HostBuilder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("private static class __Notification0");
        await Assert.That(mediator).Contains("public static global::System.Threading.Tasks.ValueTask RunHandlers(");
        await Assert.That(mediator).DoesNotContain("__notification0");
        await Assert.That(mediator).DoesNotContain("__CreateNotification0");
        await Assert.That(mediator).DoesNotContain("var pipeline");
        await Assert.That(mediator).DoesNotContain("__Notification0 pipeline");
        await VerifyHost(result).UseParameters(behaviors, parallel);
    }

    [Test]
    public async Task Should_Dispatch_Notifications_Through_A_FrozenDictionary_Above_The_Threshold(CancellationToken cancellationToken)
    {
        var source = string.Concat(
            Enumerable.Range(0, HostEmitter.DispatchThreshold + 1)
                      .Select(static i => string.Create(
                                  CultureInfo.InvariantCulture,
                                  $"public sealed record N{i} : INotification;\npublic static partial class N{i}Handler {{ private static ValueTask Handle(N{i} n) => default; }}\n"
                              )
                       )
        );
        var result = await HostBuilder.AddSources(Usings + "namespace Sample;\n" + source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(GenericDispatch.Of(mediator, "Publish")).IsEqualTo(new GenericDispatch("__NotificationIndex", 0, "__notificationTypes"));
    }

    [Test]
    public async Task KRX037_Is_Reported_For_A_Struct_Notification_With_Interface_Typed_Handlers(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + HostSource).Build().GenerateAsync(cancellationToken);

        var diagnostics = result.Results[typeof(HostGenerator)].Diagnostics.Where(static d => d.Id == "KRX037").ToArray();
        await Assert.That(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)))
                    .IsEquivalentTo(["Struct notification 'Tick' is boxed once per Publish, because handlers typed as an interface or INotification receive it: 'Sample.AuditDomainEvents'"]);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostics[0].Location.SourceTree!.GetText(cancellationToken).ToString(diagnostics[0].Location.SourceSpan)).IsEqualTo("Tick");
    }

    [Test]
    public async Task KRX037_Is_Not_Reported_When_Every_Handler_Takes_The_Struct_Itself(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           namespace Sample;
                                           public interface IMarker { }
                                           public readonly record struct Tick(int Value) : INotification, IMarker;
                                           public static partial class OnTick { private static ValueTask Handle(Tick notification) => default; }
                                           public static partial class OnAnyTick { private static ValueTask Handle(Tick notification, CancellationToken ct) => default; }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.Results[typeof(HostGenerator)].Diagnostics.Where(static d => d.Id == "KRX037")).IsEmpty();
    }

    [Test]
    public async Task KRX036_Is_Reported_For_A_Notification_Behavior_Exclusion_Only_When_It_Removes_Nothing(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: ExcludeBehavior(typeof(Sample.Everywhere))]
                                           [assembly: ExcludeBehavior(typeof(Sample.OnType))]
                                           [assembly: ExcludeBehavior(typeof(Sample.Nowhere))]
                                           namespace Sample;

                                           [NotificationBehavior]
                                           [GlobalBehavior]
                                           public static class Everywhere
                                           {
                                               public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext =>
                                                   next.InvokeAsync(default);
                                           }

                                           [NotificationBehavior]
                                           public static class OnType
                                           {
                                               public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext =>
                                                   next.InvokeAsync(default);
                                           }

                                           [NotificationBehavior]
                                           public static class Nowhere
                                           {
                                               public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext =>
                                                   next.InvokeAsync(default);
                                           }

                                           [Behavior(typeof(OnType))]
                                           public sealed record Placed : INotification;
                                           public static partial class OnPlaced
                                           {
                                               private static ValueTask Handle(Placed notification) => default;
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        await Assert.That(
                         result.Results[typeof(HostGenerator)]
                               .Diagnostics.Where(static d => d.Id == "KRX036")
                               .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
                     )
                    .IsEquivalentTo(["[assembly: ExcludeBehavior(typeof(Nowhere))] has no effect, because 'Nowhere' is not part of any pipeline in this host"]);
    }

    [Test]
    public async Task File_Local_Notification_Types_Are_Unknown_To_The_Host(CancellationToken cancellationToken)
    {
        // Generated code in another file cannot name a file-local type, nor a type nested in one (OQ-44): no Publish overload, no dispatch.
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           namespace Sample;

                                           file sealed record Hidden : INotification;

                                           file static class Outer
                                           {
                                               public sealed record Nested : INotification;
                                           }

                                           public sealed record Visible : INotification;
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Publish(global::Sample.Visible notification");
        await Assert.That(mediator).DoesNotContain("Hidden");
        await Assert.That(mediator).DoesNotContain("Nested");
    }

    [Test]
    public async Task Behavior_Scopes_Of_A_Referenced_Notification_Type_Come_From_Its_Own_Assembly(CancellationToken cancellationToken)
    {
        // OQ-3: notification behaviors per assembly come from the notification type's assembly; the host's [assembly: Behavior] does not
        // reach a referenced notification type. OQ-42 (b): a host handler's referenced parameter type is a notification type the host knows.
        var library = await CompileLibraryAsync(
            "Lib",
            """
            using System.Threading.Tasks;
            using Keryx;

            [assembly: Behavior(typeof(Lib.LibPublish))]

            namespace Lib;

            public sealed record Shipped : INotification;

            [NotificationBehavior]
            public static class LibPublish
            {
                public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext =>
                    next.InvokeAsync(default);
            }
            """,
            cancellationToken
        );
        var result = await HostBuilder.AddCompilationReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           [assembly: Behavior(typeof(Sample.HostPublish))]
                                           namespace Sample;

                                           [NotificationBehavior]
                                           public static class HostPublish
                                           {
                                               public static ValueTask Handle<T, TNext>(T notification, TNext next) where T : INotification where TNext : struct, INext =>
                                                   next.InvokeAsync(default);
                                           }

                                           public static partial class OnShipped
                                           {
                                               private static ValueTask Handle(Lib.Shipped notification) => default;
                                           }
                                           """
                                       )
                                      .Build()
                                      .GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("Publish(global::Lib.Shipped notification");
        await Assert.That(mediator).Contains("global::Lib.LibPublish.Handle<global::Lib.Shipped, ");
        await Assert.That(mediator).DoesNotContain("HostPublish.Handle");
    }

    private static string Source(GeneratorTestResults result, string hintName) => TestSources.HostSource(result, hintName);
}
