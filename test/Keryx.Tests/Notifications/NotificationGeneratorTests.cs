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
///     a struct for interface-typed handlers, per-publish and per-handler link structs, and a notification type with behaviors but no
///     handlers. Also the host diagnostics notifications add: KRX037, and KRX036 for notification behavior exclusions.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class NotificationGeneratorTests : HostTest
{
    internal const string HostSource = """
        [assembly: KeryxHost]
        [assembly: Behavior(typeof(Sample.Logged<>))]

        namespace Sample;

        public interface IJournal { void Write(string entry); }
        public interface IDomainEvent : INotification { }

        [BehaviorOrder(Phase.Observability)]
        public sealed class Logged<TNotification> : INotificationPipelineBehavior<TNotification> where TNotification : IDomainEvent
        {
            public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        public sealed class Timed<TNotification>(IJournal journal) : INotificationHandlerPipelineBehavior<TNotification> where TNotification : INotification
        {
            public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext
            {
                journal.Write("timed");
                return next.InvokeAsync(cancellationToken);
            }
        }

        [BehaviorOrder(Phase.Outermost)]
        public sealed class Guarded<TNotification> : INotificationHandlerPipelineBehavior<TNotification> where TNotification : INotification
        {
            public ValueTask Handle<TNext>(TNotification notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                next.InvokeAsync(cancellationToken);
        }

        public sealed record OrderPlaced(string Sku) : IDomainEvent;
        public record Shipped : IDomainEvent;
        public readonly record struct Tick(int Value) : IDomainEvent;
        public sealed record Unheard : INotification;

        [PublishStrategy(PublishStrategy.Parallel)]
        public sealed record Broadcast : INotification;

        public static partial class OnOrderPlaced
        {
            [HandlerOrder(1)]
            private static ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
        }

        public partial class AuditDomainEvents
        {
            [HandlerOrder(-1)]
            [Behavior(typeof(Timed<>))]
            [Behavior(typeof(Guarded<>))]
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
            [Behavior(typeof(Timed<>))]
            private static ValueTask Handle(Broadcast notification, IJournal journal, CancellationToken cancellationToken) => default;
        }
        """;

    [Test]
    public async Task Should_Generate_Publish_Pipelines(CancellationToken cancellationToken)
    {
        var result = await HostBuilder.AddSources(Usings + HostSource).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(new { Mediator = Source(result, HostEmitter.MediatorHintName), AddKeryx = Source(result, HostEmitter.ServiceCollectionExtensionsHintName) });
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
        var result = await HostBuilder.AddSources(Usings + "[assembly: KeryxHost]\nnamespace Sample;\n" + source).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var mediator = Source(result, HostEmitter.MediatorHintName);
        await Assert.That(mediator).Contains("switch (__NotificationIndex(notificationType))");
        await Assert.That(mediator).Contains("__notificationTypes");
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
                                           [assembly: KeryxHost]
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
                                           [assembly: KeryxHost]
                                           [assembly: ExcludeBehavior(typeof(Sample.Everywhere<>))]
                                           [assembly: ExcludeBehavior(typeof(Sample.PerHandler<>))]
                                           [assembly: ExcludeBehavior(typeof(Sample.Nowhere<>))]
                                           namespace Sample;

                                           [GlobalBehavior]
                                           public sealed class Everywhere<T> : INotificationPipelineBehavior<T> where T : INotification
                                           {
                                               public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                                                   next.InvokeAsync(cancellationToken);
                                           }

                                           public sealed class PerHandler<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
                                           {
                                               public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                                                   next.InvokeAsync(cancellationToken);
                                           }

                                           public sealed class Nowhere<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
                                           {
                                               public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                                                   next.InvokeAsync(cancellationToken);
                                           }

                                           public sealed record Placed : INotification;
                                           public static partial class OnPlaced
                                           {
                                               [Behavior(typeof(PerHandler<>))]
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
                    .IsEquivalentTo(["[assembly: ExcludeBehavior(typeof(Nowhere<>))] has no effect, because 'Nowhere<>' is not part of any pipeline in this host"]);
    }

    [Test]
    public async Task File_Local_Notification_Types_Are_Unknown_To_The_Host(CancellationToken cancellationToken)
    {
        // Generated code in another file cannot name a file-local type, nor a type nested in one (OQ-44): no Publish overload, no dispatch.
        var result = await HostBuilder.AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
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
    public async Task Behavior_Scopes_Of_A_Referenced_Notification_Type_Come_From_Their_Own_Assemblies(CancellationToken cancellationToken)
    {
        // OQ-3: per-publish behaviors from the notification type's assembly, per-handler behaviors from the handler's assembly; OQ-42 (b): a
        // host handler's referenced parameter type is a notification type the host knows.
        var library = MetadataReference.CreateFromImage(
            CompileLibrary(
                "Lib",
                """
                using System.Threading;
                using System.Threading.Tasks;
                using Keryx;

                [assembly: Behavior(typeof(Lib.LibPublish<>))]
                [assembly: Behavior(typeof(Lib.LibHandler<>))]

                namespace Lib;

                public sealed record Shipped : INotification;

                public sealed class LibPublish<T> : INotificationPipelineBehavior<T> where T : INotification
                {
                    public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                        next.InvokeAsync(cancellationToken);
                }

                public sealed class LibHandler<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
                {
                    public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                        next.InvokeAsync(cancellationToken);
                }
                """
            )
        );
        var result = await HostBuilder.AddReferences(library)
                                      .AddSources(
                                           Usings
                                         + """
                                           [assembly: KeryxHost]
                                           [assembly: Behavior(typeof(Sample.HostPublish<>))]
                                           [assembly: Behavior(typeof(Sample.HostHandler<>))]
                                           namespace Sample;

                                           public sealed class HostPublish<T> : INotificationPipelineBehavior<T> where T : INotification
                                           {
                                               public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                                                   next.InvokeAsync(cancellationToken);
                                           }

                                           public sealed class HostHandler<T> : INotificationHandlerPipelineBehavior<T> where T : INotification
                                           {
                                               public ValueTask Handle<TNext>(T notification, TNext next, CancellationToken cancellationToken) where TNext : struct, INext =>
                                                   next.InvokeAsync(cancellationToken);
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
        await Assert.That(mediator).Contains("global::Lib.LibPublish<global::Lib.Shipped>");
        await Assert.That(mediator).Contains("global::Sample.HostHandler<global::Lib.Shipped>");
        await Assert.That(mediator).DoesNotContain("HostPublish<");
        await Assert.That(mediator).DoesNotContain("LibHandler<");
    }

    private static string Source(GeneratorTestResults result, string hintName) => Behaviors.PipelineGeneratorTests.Source(result, hintName);
}
