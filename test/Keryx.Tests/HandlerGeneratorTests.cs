using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keryx.Tests;

/// <summary>
///     Snapshots of the handler generator's output for every handler shape (ADR 0001, Handlers; the Phase 2 brief).
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerGeneratorTests : GeneratorTest
{
    private const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;
        using Microsoft.Extensions.DependencyInjection;

        """;

    [Test]
    public async Task Should_Run_Without_Diagnostics_On_A_Compilation_Without_Handlers(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .AddSources(
                               """
                               namespace Sample;

                               public sealed class NotAHandler
                               {
                                   public int Value { get; set; }
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Instance_Handler_With_Injected_Parameters(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public interface IOrderRepository { }

            public partial class CreateOrder
            {
                public sealed record Command(string Sku, int Quantity) : IRequest<Guid>;

                private static ValueTask<Guid> Handle(Command request, IOrderRepository orders, CancellationToken ct) => new(Guid.Empty);
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Static_Invoker_For_Static_Handler_Class(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public static partial class Ping
            {
                public sealed record Query : IRequest<string>;

                private static string Handle(Query request) => "pong";
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Static_Invoker_For_Top_Level_Request_Without_Dependencies(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed class GetTotal : IRequest<decimal>
            {
                public int OrderId { get; init; }
            }

            public partial class GetTotalHandler
            {
                private static Task<decimal> HandleAsync(CancellationToken cancellationToken, GetTotal request) => Task.FromResult(1m);
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Adapt_Every_Request_Return_Type(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Number : IRequest<int>;
            public sealed record Delete : IRequest;
            public readonly record struct Touch(int Id) : IRequest<Unit>;

            public static partial class ReturnsValueTaskOfT { private static ValueTask<int> Handle(Number request) => new(1); }
            public static partial class ReturnsTaskOfT { private static Task<int> Handle(Number request) => Task.FromResult(1); }
            public static partial class ReturnsT { private static int Handle(Number request) => 1; }
            public static partial class ReturnsValueTask { private static ValueTask Handle(Delete request) => default; }
            public static partial class ReturnsTask { private static Task Handle(Delete request) => Task.CompletedTask; }
            public static partial class ReturnsVoid { private static void Handle(Delete request) { } }
            public static partial class ReturnsValueTaskOfUnit { private static ValueTask<Unit> Handle(Touch request) => new(Unit.Value); }
            public static partial class ReturnsUnit { private static async Task<Unit> Handle(Delete request, CancellationToken ct) { await Task.Yield(); return Unit.Value; } }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Stream_Handlers(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Countdown(int From) : IStreamRequest<int>;
            public sealed record Names : IStreamRequest<string>;

            public static partial class CountdownHandler
            {
                private static async IAsyncEnumerable<int> Handle(Countdown request, [EnumeratorCancellation] CancellationToken ct)
                {
                    for (var i = request.From; i > 0; i--)
                    {
                        await Task.Yield();
                        yield return i;
                    }
                }
            }

            public interface INameSource
            {
                IAsyncEnumerable<string> All();
            }

            public partial class NamesHandler
            {
                private static IAsyncEnumerable<string> Handle(Names request, INameSource source) => source.All();
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Generate_Notification_Handlers_Including_Polymorphic_Ones(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public interface IDomainEvent : INotification { }
            public sealed record OrderPlaced(Guid Id) : IDomainEvent;
            public readonly record struct Tick : INotification;

            public interface IAuditLog { }

            public static partial class OnOrderPlaced
            {
                [HandlerOrder(2)]
                private static ValueTask Handle(OrderPlaced notification, CancellationToken ct) => default;
            }

            public partial class AuditDomainEvents
            {
                [HandlerOrder(-1)]
                private static Task Handle(IDomainEvent notification, IAuditLog log) => Task.CompletedTask;
            }

            public static partial class CatchAll
            {
                private static ValueTask Handle(INotification notification) => default;
            }

            public static partial class OnTick
            {
                private static Task HandleAsync(Tick notification) => Task.CompletedTask;
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Copy_Keyed_And_Default_Parameters(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public enum Region { North = 1, South = -2 }
            public interface IClock { }
            public interface ICache { }
            public interface IStore { }

            public sealed record Lookup : IRequest<string>;

            public partial class LookupHandler
            {
                private static string Handle(
                    Lookup request,
                    [FromKeyedServices("primary")] IStore store,
                    [FromKeyedServices(Region.South)] IStore regional,
                    [FromKeyedServices(42)] IClock clock,
                    IServiceProvider services,
                    CancellationToken ct,
                    [FromKeyedServices("cache")] ICache? cache = null,
                    IClock? fallbackClock = null,
                    int retries = 3,
                    long limit = -1,
                    double ratio = 0.5,
                    float scale = 1.25f,
                    decimal price = 9.99m,
                    bool enabled = true,
                    char separator = ',',
                    string name = "a \"quoted\" name",
                    Region region = Region.South,
                    Region? maybeRegion = Region.North,
                    int? maybeCount = null,
                    Guid id = default,
                    TimeSpan delay = default
                ) => "";
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Repeat_Containing_Types_Namespaces_And_Records(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .AddSources(
                               $$"""
                               {{Usings}}
                               namespace Sample.Orders
                               {
                                   public static partial class Orders
                                   {
                                       public partial class Create
                                       {
                                           public sealed record Command(string Sku) : IRequest<Guid>;

                                           private static ValueTask<Guid> Handle(Command request) => new(Guid.Empty);
                                       }

                                       public partial record Cancel
                                       {
                                           public sealed record Command(Guid Id) : IRequest;

                                           private static void Handle(Command request, IServiceProvider services) { }
                                       }
                                   }
                               }

                               public sealed record GlobalRequest : IRequest<int>;

                               public partial class GlobalHandler
                               {
                                   private static int Handle(GlobalRequest request, IMediator mediator) => 0;
                               }

                               public partial class @event
                               {
                                   public sealed record Fire : IRequest<int>;

                                   private static int Handle(Fire request, IServiceProvider @class) => 0;
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Verify(result);
    }

    [Test]
    public async Task Should_List_Attached_Behaviors_In_The_Manifest(CancellationToken cancellationToken)
    {
        // A request handler's attached behavior classes go into KeryxHandler.Behaviors; a notification handler has no behaviors of its own,
        // so its [Behavior] (KRX042) is not listed.
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Ping : IRequest<string>;
            public sealed record Placed : INotification;

            [RequestBehavior]
            [NotificationBehavior]
            public static class Audit
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
                    where TRequest : IRequest<TResponse>
                    where TNext : struct, INext<TResponse> => next.InvokeAsync(cancellationToken);

                public static ValueTask Handle<TNotification, TNext>(TNotification notification, TNext next, CancellationToken cancellationToken)
                    where TNotification : INotification
                    where TNext : struct, INext => next.InvokeAsync(cancellationToken);
            }

            [RequestBehavior]
            public static class PingBehavior
            {
                public static ValueTask<string> Handle<TNext>(Ping request, TNext next, CancellationToken cancellationToken)
                    where TNext : struct, INext<string> => next.InvokeAsync(cancellationToken);
            }

            public static partial class PingHandler
            {
                [Behavior(typeof(Audit))]
                [Behavior(typeof(PingBehavior))]
                private static string Handle(Ping request) => "pong";
            }

            public static partial class PlacedHandler
            {
                [Behavior(typeof(Audit))]
                private static ValueTask Handle(Placed notification) => default;
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Suppress_A_Nullability_Only_Response_Difference(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Find : IRequest<string>;

            public static partial class FindHandler
            {
                private static ValueTask<string?> Handle(Find request) => new((string?)null);
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Leave_Internal_Handlers_Out_Of_A_Host_Manifest(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record PublicRequest : IRequest<int>;
            internal sealed record InternalRequest : IRequest<int>;

            internal static partial class InternalHandler
            {
                private static int Handle(PublicRequest request) => 1;
            }

            internal interface IInternalService { }

            public partial class PublicHandlerWithInternalTypes
            {
                private static int Handle(InternalRequest request, IInternalService service) => 1;
            }

            internal sealed record OtherRequest : IRequest<int>;
            internal sealed record ProtectedRequest : IRequest<int>;

            public partial class Outer
            {
                // Its generated members are internal: public members could not take InternalRequest (CS0051).
                protected internal partial class ProtectedInternalHandler
                {
                    private static int Handle(OtherRequest request, IInternalService service) => 1;
                }

                // Narrower than internal: skipped (KRX004 in a host, OQ-29).
                protected partial class ProtectedHandler
                {
                    private static int Handle(ProtectedRequest request) => 1;
                }
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Skip_A_Host_Handler_Whose_Signature_Uses_A_Private_Type(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            [assembly: KeryxHost]

            namespace Sample;

            public sealed record Ping : IRequest<int>;

            public partial class Outer
            {
                private interface IPrivateService { }

                internal partial class PingHandler
                {
                    private static int Handle(Ping request, IPrivateService service) => 1;
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(result.Results.Values.Single().SyntaxTrees.Select(static t => Path.GetFileName(t.FilePath))).IsEquivalentTo(["KeryxManifest.g.cs"]);
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
    }

    [Test]
    public async Task Should_Give_Distinct_Hint_Names_To_Names_The_Sanitised_Form_Or_Case_Would_Merge(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public static partial class Orders
            {
                public static partial class Cancel
                {
                    public sealed record Command : IRequest<int>;

                    private static int Handle(Command request) => 1;
                }
            }

            public static partial class Orders_Cancel
            {
                public sealed record Command : IRequest<int>;

                private static int Handle(Command request) => 2;
            }

            public static partial class Ping
            {
                public sealed record Query : IRequest<int>;

                private static int Handle(Query request) => 3;
            }

            public static partial class ping
            {
                public sealed record Query : IRequest<int>;

                private static int Handle(Query request) => 4;
            }
            """,
            cancellationToken
        );

        var hintNames = result.Results.Values.Single().SyntaxTrees.Select(static t => Path.GetFileName(t.FilePath)).ToArray();
        await Assert.That(hintNames)
                    .IsEquivalentTo(
                         [
                             "KeryxManifest.g.cs",
                             Analyzers.Handlers.CSharpText.HintName("Sample.Orders+Cancel"),
                             Analyzers.Handlers.CSharpText.HintName("Sample.Orders_Cancel"),
                             Analyzers.Handlers.CSharpText.HintName("Sample.Ping"),
                             Analyzers.Handlers.CSharpText.HintName("Sample.ping"),
                         ]
                     );
        await Assert.That(hintNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(5);
        await Assert.That(
                         hintNames.Where(static n => n != "KeryxManifest.g.cs")
                                  .All(static n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^Sample\.(Orders_Cancel|Ping|ping)\.[0-9a-f]{8}\.Keryx\.g\.cs$"))
                     )
                    .IsTrue();
        await Assert.That(result.Results.Values.Single().Diagnostics).IsEmpty();
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
    }

    [Test]
    public async Task Should_Compile_Null_Defaults_From_A_Nullable_Oblivious_Handler(CancellationToken cancellationToken)
    {
        var compilation = DirectCompilation.Create(
            """
            using Keryx;

            namespace Sample;

            public interface IFoo { }

            public sealed record Q1 : IRequest<string>;

            public partial class Ping
            {
                private static string Handle(Q1 request, IFoo foo = null, string s = null, int? count = null) => s;
            }
            """,
            NullableContextOptions.Disable
        );

        var (output, sources) = DirectCompilation.Generate(compilation, cancellationToken);

        await Assert.That(output.GetDiagnostics(cancellationToken).Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
        await Verify(sources.Single(static s => s.HintName.EndsWith(".Keryx.g.cs", StringComparison.Ordinal)).SourceText.ToString());
    }

    [Test]
    public async Task Should_Skip_Handlers_That_Are_Not_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Ping : IRequest<int>;
            internal sealed record Hidden : IRequest<int>;

            internal static partial class InternalHandler
            {
                private static int Handle(Ping request) => 1;
            }

            public static partial class HandlerOfInternalRequest
            {
                private static int Handle(Hidden request) => 1;
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Skip_Malformed_Handlers_Without_Reporting_Diagnostics(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public sealed record Ping : IRequest<int>;
            public sealed record Pong : IRequest<int>;
            public interface IService { }

            public class NotPartial { private static int Handle(Ping request) => 1; }
            public static partial class NotPrivateStatic { public static int Handle(Ping request) => 1; }
            public static partial class WrongReturn { private static string Handle(Ping request) => ""; }
            public static partial class TwoHandlers
            {
                private static int Handle(Ping request) => 1;
                private static int HandleAsync(Pong request) => 1;
            }
            public partial class WithConstructor { public WithConstructor() { } private static int Handle(Ping request) => 1; }
            public static partial class StaticWithService { private static int Handle(Ping request, IService service) => 1; }
            public static partial class RefParameter { private static int Handle(Ping request, ref int value) => 1; }
            public static partial class ReservedMember { public static void InvokeAsync() { } private static int Handle(Ping request) => 1; }
            public partial struct StructHandler { private static int Handle(Ping request) => 1; }
            public partial class Generic<T> { private static int Handle(Ping request) => 1; }
            public static partial class KeyWithoutValue { private static int Handle(Ping request, [FromKeyedServices] IService service) => 1; }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Emit_Nothing_Below_CSharp_14(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .WithLanguageVersion(LanguageVersion.CSharp13)
                          .AddSources(
                               """
                               namespace Sample;

                               public sealed record Ping : Keryx.IRequest<int>;

                               public static partial class PingHandler
                               {
                                   private static int Handle(Ping request) => 1;
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Assert.That(result.Results.Values.Single().SyntaxTrees).IsEmpty();
        await Assert.That(result.Results.Values.Single().Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments(LanguageVersion.CSharp14)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Should_Emit_From_CSharp_14(LanguageVersion languageVersion, CancellationToken cancellationToken)
    {
        var result = await Builder
                          .WithLanguageVersion(languageVersion)
                          .AddSources(
                               """
                               namespace Sample;

                               public sealed record Ping : Keryx.IRequest<int>;

                               public static partial class PingHandler
                               {
                                   private static int Handle(Ping request) => 1;
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Assert.That(result.Results.Values.Single().SyntaxTrees.Select(static t => Path.GetFileName(t.FilePath)))
                    .IsEquivalentTo(["KeryxManifest.g.cs", Analyzers.Handlers.CSharpText.HintName("Sample.PingHandler")]);
        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
    }

    [Test]
    public async Task Should_Never_Put_GeneratedCode_On_A_Partial_Declaration(CancellationToken cancellationToken)
    {
        var result = await Generate(
            """
            namespace Sample;

            public interface IService { }

            public static partial class Outer
            {
                public partial class Create
                {
                    public sealed record Command : IRequest<int>;

                    private static int Handle(Command request, IService service) => 1;
                }
            }
            """,
            cancellationToken
        );

        var trees = result.Results.Values.Single().SyntaxTrees.Where(static t => t.FilePath.EndsWith(".Keryx.g.cs", StringComparison.Ordinal)).ToArray();
        await Assert.That(trees).IsNotEmpty();
        foreach (var tree in trees)
        {
            var root = await tree.GetRootAsync(cancellationToken);
            var typeAttributes = root.DescendantNodes()
                                     .OfType<TypeDeclarationSyntax>()
                                     .SelectMany(static t => t.AttributeLists.SelectMany(static l => l.Attributes))
                                     .Select(static a => a.Name.ToString());
            await Assert.That(typeAttributes).DoesNotContain(static name => name.Contains("GeneratedCode", StringComparison.Ordinal));

            var members = root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(static m => m is FieldDeclarationSyntax or ConstructorDeclarationSyntax or MethodDeclarationSyntax);
            foreach (var member in members)
            {
                await Assert.That(member.AttributeLists.SelectMany(static l => l.Attributes).Select(static a => a.Name.ToString()))
                            .Contains(static name => name.Contains("GeneratedCode", StringComparison.Ordinal));
            }
        }

        await Assert.That(result.FinalDiagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning)).IsEmpty();
    }

    private Task<Rocket.Surgery.Extensions.Testing.SourceGenerators.GeneratorTestResults> Generate(string source, CancellationToken cancellationToken) =>
        Builder.AddSources(Usings + source).Build().GenerateAsync(cancellationToken);
}
