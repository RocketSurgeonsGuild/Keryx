using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.Diagnostics;

/// <summary>
///     KRX002-KRX007, KRX012 (handler classes), KRX013-KRX018 and KRX040, each with a positive and a negative case.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerAnalyzerTests : AnalyzerTest
{
    private const string Ping = "public sealed record Ping : IRequest<string>;\n";

    [Test]
    public async Task A_Valid_Handler_Reports_Nothing(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + """
            public interface IService { }

            public partial class PingHandler
            {
                static PingHandler() { }

                private static ValueTask<string> Handle(CancellationToken ct, Ping request, IService service) => new("pong");
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public static ValueTask<string> Handle(Ping request) => new(\"\");")]
    [Arguments("private ValueTask<string> Handle(Ping request) => new(\"\");")]
    [Arguments("internal static ValueTask<string> HandleAsync(Ping request) => new(\"\");")]
    public async Task KRX002_Handler_Not_Private_Static(string handler, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + $"public partial class PingHandler {{ {handler} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX002"]);
        await Assert.That(diagnostics[0].Properties["Reason"]).IsEqualTo("Modifiers");
    }

    [Test]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static int Handle(Ping request) => 1;")]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static ValueTask Handle(Ping request) => default;")]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static void Handle(Ping request) { }")]
    [Arguments("public sealed record Ping : IRequest;", "private static async void Handle(Ping request) { await Task.Yield(); }")]
    [Arguments("public sealed record Ping : IStreamRequest<int>;", "private static IEnumerable<int> Handle(Ping request) => [];")]
    [Arguments("public sealed record Ping : INotification;", "private static int Handle(Ping request) => 1;")]
    [Arguments("public sealed record Ping : INotification;", "private static void Handle(Ping request) { }")]
    public async Task KRX002_Wrong_Return_Type(string request, string handler, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>($"{request}\npublic static partial class PingHandler {{ {handler} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX002"]);
        await Assert.That(diagnostics[0].Properties["Reason"]).IsEqualTo("ReturnType");
    }

    [Test]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static ValueTask<string> Handle(Ping request) => default;")]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static Task<string> Handle(Ping request) => Task.FromResult(\"\");")]
    [Arguments("public sealed record Ping : IRequest<string>;", "private static string Handle(Ping request) => \"\";")]
    [Arguments("public sealed record Ping : IRequest;", "private static ValueTask Handle(Ping request) => default;")]
    [Arguments("public sealed record Ping : IRequest;", "private static Task Handle(Ping request) => Task.CompletedTask;")]
    [Arguments("public sealed record Ping : IRequest;", "private static void Handle(Ping request) { }")]
    [Arguments("public sealed record Ping : IRequest;", "private static Task<Unit> Handle(Ping request) => Task.FromResult(Unit.Value);")]
    [Arguments("public sealed record Ping : IRequest<Unit>;", "private static ValueTask Handle(Ping request) => default;")]
    [Arguments("public sealed record Ping : IRequest<Unit>;", "private static Task Handle(Ping request) => Task.CompletedTask;")]
    [Arguments("public sealed record Ping : IRequest<Unit>;", "private static void Handle(Ping request) { }")]
    [Arguments("public sealed record Ping : IRequest<Unit>;", "private static Unit Handle(Ping request) => Unit.Value;")]
    [Arguments("public sealed record Ping : IStreamRequest<int>;", "private static IAsyncEnumerable<int> Handle(Ping request) => null!;")]
    [Arguments("public sealed record Ping : INotification;", "private static ValueTask Handle(Ping request) => default;")]
    [Arguments("public sealed record Ping : INotification;", "private static Task Handle(Ping request) => Task.CompletedTask;")]
    public async Task KRX002_Allowed_Return_Types(string request, string handler, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>($"{request}\npublic static partial class PingHandler {{ {handler} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public static partial class Ping { public sealed record Query : IRequest<int>; private static int Handle() => 1; }")]
    [Arguments("public sealed record A : IRequest<int>; public sealed record B : IRequest<int>; public static partial class H { private static int Handle(A a, B b) => 1; }")]
    [Arguments("public static partial class H { private static int Handle(IRequest<int> request) => 1; }")]
    [Arguments("public abstract record Base : IRequest<int>; public static partial class H { private static int Handle(Base request) => 1; }")]
    [Arguments("public interface IQuery : IRequest<int> { } public static partial class H { private static int Handle(IQuery request) => 1; }")]
    public async Task KRX003_No_Single_Concrete_Request_Parameter(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX003"]);
    }

    [Test]
    [Arguments("public interface IEvent : INotification { } public static partial class H { private static ValueTask Handle(IEvent e) => default; }")]
    [Arguments("public static partial class H { private static ValueTask Handle(INotification e) => default; }")]
    [Arguments("public abstract record Event : INotification; public static partial class H { private static ValueTask Handle(Event e) => default; }")]
    public async Task KRX003_Polymorphic_Notification_Parameters_Are_Allowed(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task KRX004_Handler_Class_Or_Containing_Type_Not_Partial(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + """
            public static class Outer
            {
                public class Inner
                {
                    private static string Handle(Ping request) => "";
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004", "KRX004"]);
        await Assert.That(diagnostics.All(static d => d.Properties.ContainsKey("AddPartial"))).IsTrue();
    }

    [Test]
    public async Task KRX004_Handler_Class_Not_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + """
            public static partial class Outer
            {
                internal static partial class Inner
                {
                    private static string Handle(Ping request) => "";
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004"]);
        await Assert.That(diagnostics[0].Properties.ContainsKey("MakePublic")).IsTrue();
    }

    [Test]
    public async Task KRX004_Internal_Handler_Class_Is_Fine_In_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            "[assembly: KeryxHost]\n" + Ping + """
            internal static partial class Outer
            {
                internal static partial class Inner
                {
                    private static string Handle(Ping request) => "";
                }

                protected internal static partial class ProtectedInternal
                {
                    private static string Handle(Ping request) => "";
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("internal static partial class Outer { private static partial class H { private static string Handle(Ping request) => \"\"; } }")]
    [Arguments("public partial class Outer { protected static partial class H { private static string Handle(Ping request) => \"\"; } }")]
    [Arguments("public partial class Outer { private protected static partial class H { private static string Handle(Ping request) => \"\"; } }")]
    [Arguments("public partial class Outer { private partial class Middle { internal static partial class H { private static string Handle(Ping request) => \"\"; } } }")]
    public async Task KRX004_Private_Or_Protected_Handler_Class_In_A_Host(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>("[assembly: KeryxHost]\n" + Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004"]);
        await Assert.That(diagnostics[0].Properties.ContainsKey("MakePublic")).IsTrue();
    }

    [Test]
    public async Task KRX004_Private_Handler_Class_Outside_A_Host_Is_Reported_Once(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "public partial class Outer { private static partial class H { private static string Handle(Ping request) => \"\"; } }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004"]);
    }

    [Test]
    [Arguments("public partial struct H { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial record struct H { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial interface H { private static string Handle(Ping request) => \"\"; }")]
    public async Task KRX004_Handler_Class_Is_Not_A_Class(string handler, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + handler, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004"]);
    }

    [Test]
    [Arguments("file partial class H { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("file static partial class Outer { internal static partial class H { private static string Handle(Ping request) => \"\"; } }")]
    public async Task KRX004_File_Local_Type_In_The_Containing_Chain(string handler, CancellationToken cancellationToken)
    {
        // In a host, so only the file-local rule applies (outside a host the type is also not public).
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>("[assembly: KeryxHost]\n" + Ping + handler, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX004"]);
        await Assert.That(Messages(diagnostics, "KRX004").Single()).Contains("cannot be file-local");
    }

    [Test]
    public async Task KRX006_Class_With_Two_Handlers(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + """
            public sealed record Pong : IRequest<string>;

            public static partial class Handlers
            {
                private static string Handle(Ping request) => "";
                private static string HandleAsync(Pong request) => "";

                public static partial class Nested
                {
                    private static string Handle(Pong request) => "";
                }
            }
            """,
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX006", "KRX006"]);
    }

    [Test]
    public async Task KRX006_Helpers_Named_Handle_Are_Not_Handlers(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + """
            public interface IHandle { void Handle(Ping request); }

            public partial class PingHandler : IHandle
            {
                private static string Handle(Ping request) => "";
                private static void Handle(string text) { }
                private static void Handle<T>(T value) { }
                void IHandle.Handle(Ping request) { }
            }

            public class Implicit : IHandle
            {
                public void Handle(Ping request) { }
            }
            """,
            cancellationToken
        );

        await Assert.That(diagnostics.Select(static d => d.ToString())).IsEmpty();
    }

    [Test]
    [Arguments("public abstract class A { protected abstract string Handle(Ping request); }")]
    [Arguments("public abstract class A { public abstract ValueTask<string> HandleAsync(Ping request); }")]
    [Arguments("public interface IA { string Handle(Ping request); }")]
    public async Task Abstract_Handle_Methods_Are_Not_Handlers(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(diagnostics.Select(static d => d.ToString())).IsEmpty();
    }

    [Test]
    [Arguments("public partial class H { public H() { } private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial record H(int Value) { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial class H(int value) { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public abstract class Base { protected Base(int value) { } } public partial class H : Base { private static string Handle(Ping request, IServiceProvider services) => \"\"; }")]
    public async Task KRX007_Constructor(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX007"]);
    }

    [Test]
    [Arguments("public abstract class Base { protected Base() { } } public partial class H : Base { private static string Handle(Ping request, IServiceProvider services) => \"\"; }")]
    [Arguments("public abstract class Base { protected Base(int value = 0) { } } public partial class H : Base { private static string Handle(Ping request, IServiceProvider services) => \"\"; }")]
    [Arguments("public abstract class Base { protected Base(int value) { } } public partial class H : Base { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial record H { private static string Handle(Ping request, IServiceProvider services) => \"\"; }")]
    public async Task KRX007_Allowed_Constructors_And_Base_Types(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments(
        "public abstract partial class H { private static string Handle(Ping request, IServiceProvider services) => \"\"; }",
        "'H' has injected parameters, so a host constructs it, but it is abstract"
    )]
    [Arguments(
        "public partial class H { public required string Name { get; init; } private static string Handle(Ping request, IServiceProvider services) => \"\"; }",
        "'H' has injected parameters, so a host constructs it, but it has required members ('Name') that the generated constructor does not set"
    )]
    [Arguments(
        "public abstract class Base { public required int Count; } public partial class H : Base { private static string Handle(Ping request, IServiceProvider services) => \"\"; }",
        "'H' has injected parameters, so a host constructs it, but it has required members ('Count') that the generated constructor does not set"
    )]
    public async Task KRX007_A_Constructed_Handler_Class_Is_Abstract_Or_Has_Required_Members(string source, string message, CancellationToken cancellationToken)
    {
        // OQ-34: the host's generated factory constructs the class with `new`, which CS0144/CS9035 would reject in generated code.
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX007"]);
        await Assert.That(Messages(diagnostics, "KRX007")).IsEquivalentTo([message]);
    }

    [Test]
    [Arguments("public abstract partial class H { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial class H { public required string Name { get; init; } private static string Handle(Ping request) => \"\"; }")]
    public async Task KRX007_A_Static_Invoker_Is_Never_Constructed(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public partial class H<T> { private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public partial class Outer<T> { public partial class H { private static string Handle(Ping request) => \"\"; } }")]
    public async Task KRX012_Generic_Handler_Class(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX012"]);
    }

    [Test]
    [Arguments("ref int value")]
    [Arguments("in int value")]
    [Arguments("out int value")]
    [Arguments("params int[] values")]
    [Arguments("CancellationToken first, CancellationToken second")]
    [Arguments("ReadOnlySpan<char> span")]
    [Arguments("TypedReference reference")]
    public async Task KRX013_Parameter_Kind(string parameters, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + $"public partial class H {{ {Handler(parameters)} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX013"]);
    }

    [Test]
    [Arguments("ref int value")]
    [Arguments("in int value")]
    [Arguments("out int value")]
    [Arguments("params int[] values")]
    [Arguments("ReadOnlySpan<char> span")]
    public async Task KRX013_Parameter_Kind_In_A_Static_Class_Is_Not_Also_KRX014(string parameters, CancellationToken cancellationToken)
    {
        // An invalid parameter is not an injected service, so the static class does not also get KRX014 (and its "remove static" fix).
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + $"public static partial class H {{ {Handler(parameters)} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX013"]);
    }

    [Test]
    [Arguments("int* pointer")]
    [Arguments("delegate*<int, int> function")]
    public async Task KRX013_Pointer_Parameter(string parameter, CancellationToken cancellationToken)
    {
        var compilation = DirectCompilation.Create(
            Usings + Ping + $"public partial class H {{ private static unsafe string Handle(Ping request, {parameter}) => \"\"; }}",
            allowUnsafe: true
        );

        var diagnostics = await DirectCompilation.AnalyzeAsync<HandlerAnalyzer>(compilation, cancellationToken);
        var (output, sources) = DirectCompilation.Generate(compilation, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX013"]);
        // The generator skips the handler, so the generated code cannot fail to compile (CS0214, CS8345).
        await Assert.That(sources.Select(static s => s.HintName)).IsEquivalentTo(["KeryxManifest.g.cs"]);
        await Assert.That(output.GetDiagnostics(cancellationToken).Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)).IsEmpty();
    }

    [Test]
    public async Task KRX013_Parameter_Messages(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "public partial class H { private static string Handle(Ping request, ReadOnlySpan<char> span, IServiceProvider services) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Messages(diagnostics, "KRX013"))
                    .IsEquivalentTo(["Handler parameter 'span' is a ref struct; an injected service is stored in a field of the handler class"]);
    }

    private static string Handler(string parameters) =>
        $"private static string Handle(Ping request, {parameters}) {{ {( parameters.StartsWith("out", StringComparison.Ordinal) ? "value = 0; " : "" )}return \"\"; }}";

    [Test]
    public async Task KRX014_Static_Handler_Class_With_Injected_Parameters(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "public static partial class H { private static string Handle(Ping request, IServiceProvider services) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX014"]);
    }

    [Test]
    public async Task KRX014_Static_Handler_Class_Without_Injected_Parameters(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "public static partial class H { private static string Handle(Ping request, CancellationToken ct) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public void InvokeAsync() { }")]
    [Arguments("private int __keryx_value;")]
    [Arguments("public int InvokeAsync { get; set; }")]
    public async Task KRX015_Reserved_Member(string member, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + $"public partial class H {{ {member} private static string Handle(Ping request, IServiceProvider services) => \"\"; }}",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX015"]);
    }

    [Test]
    public async Task KRX016_Injected_Type_Not_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "internal interface IHidden { } public partial class H { private static string Handle(Ping request, IHidden hidden) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
    }

    [Test]
    public async Task KRX016_Polymorphic_Notification_Type_Not_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            "internal interface IHiddenEvent : INotification { } public static partial class H { private static ValueTask Handle(IHiddenEvent e) => default; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
    }

    [Test]
    public async Task KRX016_Attached_Behavior_Not_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "internal sealed class Hidden { } public static partial class H { [Behavior(typeof(Hidden))] private static string Handle(Ping request) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
    }

    [Test]
    public async Task KRX016_Internal_Types_Are_Fine_In_A_Host(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            "[assembly: KeryxHost]\n" + Ping
                                      + "internal interface IHidden { } public partial class H { private static string Handle(Ping request, IHidden hidden) => \"\"; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments(
        "internal static partial class H { private sealed record Secret : IRequest<int>; private static int Handle(Secret request) => 1; }",
        "'H.Secret' must be at least internal, because the handler 'H.Handle(H.Secret)' takes it and the host's generated mediator and AddKeryx(), outside the handler class, use it"
    )]
    [Arguments(
        "public sealed record Q : IRequest<int>; internal partial class H { private interface IHidden { } private static int Handle(Q request, IHidden hidden) => 1; }",
        "'H.IHidden' must be at least internal, because the handler injects it through parameter 'hidden' and the host's generated mediator and AddKeryx(), outside the handler class, use it"
    )]
    public async Task KRX016_Signature_Type_Narrower_Than_Internal_In_A_Host(string source, string message, CancellationToken cancellationToken)
    {
        // OQ-24: the host skips such a handler, so the analyzer must say why.
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>("[assembly: KeryxHost]\n" + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX016"]);
        await Assert.That(Messages(diagnostics, "KRX016")).IsEquivalentTo([message]);
    }

    [Test]
    [Arguments("private static ValueTask<string?> Handle(Ping request) => default;")]
    [Arguments("private static string? Handle(Ping request) => null;")]
    public async Task KRX017_Nullability_Only_Difference(string handler, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + $"public static partial class H {{ {handler} }}", cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX017"]);
        await Assert.That(Messages(diagnostics, "KRX017"))
                    .IsEquivalentTo(["Handler 'H.Handle(Ping)' returns 'string?', but request 'Ping' responds with 'string'"]);
    }

    [Test]
    public async Task KRX017_Oblivious_Nullability_Matches(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + "#nullable disable\npublic static partial class H { private static ValueTask<string> Handle(Ping request) => default; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("[FromKeyedServices] IServiceProvider services")]
    [Arguments("[FromKeyedServices(new[] { 1, 2 })] IServiceProvider services")]
    public async Task KRX018_Key_Is_Not_A_Constant(string parameter, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + $"public partial class H {{ private static string Handle(Ping request, {parameter}) => \"\"; }}",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX018"]);
    }

    [Test]
    [Arguments("[FromKeyedServices(\"key\")] IServiceProvider services")]
    [Arguments("[FromKeyedServices(42)] IServiceProvider services")]
    [Arguments("[FromKeyedServices(typeof(Ping))] IServiceProvider services")]
    public async Task KRX018_Constant_Keys(string parameter, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            Ping + $"public partial class H {{ private static string Handle(Ping request, {parameter}) => \"\"; }}",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    [Arguments("public static partial class H { [HandlerOrder(1)] private static string Handle(Ping request) => \"\"; }")]
    [Arguments("public static class Helpers { [HandlerOrder(1)] public static void Run() { } }")]
    public async Task KRX040_HandlerOrder_Not_On_A_Notification_Handler(string source, CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(Ping + source, cancellationToken);

        await Assert.That(Ids(diagnostics)).IsEquivalentTo(["KRX040"]);
    }

    [Test]
    public async Task KRX040_HandlerOrder_On_A_Notification_Handler(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            "public sealed record Placed : INotification; public static partial class H { [HandlerOrder(1)] private static ValueTask Handle(Placed e) => default; }",
            cancellationToken
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Reports_Nothing_Below_CSharp_11(CancellationToken cancellationToken)
    {
        var diagnostics = await AnalyzeAsync<HandlerAnalyzer>(
            "public sealed class Ping : IRequest<string> { } public class H { public string Handle(Ping request) => \"\"; }",
            cancellationToken,
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp10
        );

        await Assert.That(Ids(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Reports_Every_Rule_With_Its_Message(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .WithAnalyzer<HandlerAnalyzer>()
                          .AddSources(
                               Usings + """
                               namespace Sample;

                               public sealed record Ping : IRequest<string>;
                               public sealed record Pong : IRequest<string>;
                               public interface IService { }
                               internal interface IHidden { }

                               public static class NotPartial
                               {
                                   internal partial class Handler
                                   {
                                       public Handler() { }
                                       public void InvokeAsync() { }
                                       public string Handle(Ping request, ref int value, IHidden hidden, [FromKeyedServices] IService service) => "";
                                   }
                               }

                               public static partial class Twice
                               {
                                   [HandlerOrder(1)]
                                   private static ValueTask<string?> Handle(Pong request, IService service) => default;
                                   private static int HandleAsync(Pong request, CancellationToken a, CancellationToken b) => 1;
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Verify(Sorted(result.AnalyzerResults[typeof(HandlerAnalyzer)].Diagnostics));
    }
}
