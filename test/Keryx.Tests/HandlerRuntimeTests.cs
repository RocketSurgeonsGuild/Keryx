using System.Reflection;
using Keryx.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests;

/// <summary>
///     Compiles handlers with the generator and runs the generated invokers: return adaptation, injected dependencies, and the metadata a host
///     reads (manifest attributes, keyed and default constructor parameters).
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerRuntimeTests : GeneratorTest
{
    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;
        using Microsoft.Extensions.DependencyInjection;

        namespace Sample;

        public interface IGreeter { string Greet(string name); }
        public sealed class Greeter : IGreeter { public string Greet(string name) => "hello " + name; }

        public sealed record Greet(string Name) : IRequest<string>;
        public sealed record Count(int Value) : IRequest<int>;
        public sealed record Touch : IRequest;
        public sealed record Wait : IRequest;
        public sealed record Numbers(int Count) : IStreamRequest<int>;
        public sealed record Happened : INotification;

        public partial class GreetHandler
        {
            private static ValueTask<string> Handle(
                Greet request,
                [FromKeyedServices("polite")] IGreeter greeter,
                CancellationToken ct,
                int repeat = 2,
                string suffix = "!") => new(greeter.Greet(request.Name) + suffix);
        }

        public static partial class CountHandler
        {
            private static Task<int> Handle(Count request) => Task.FromResult(request.Value + 1);
        }

        public static partial class TouchHandler
        {
            public static int Touched;

            private static void Handle(Touch request) => Touched++;
        }

        public static partial class WaitHandler
        {
            private static async Task Handle(Wait request, CancellationToken ct) => await Task.Delay(1, ct);
        }

        public static partial class NumbersHandler
        {
            private static async IAsyncEnumerable<int> Handle(Numbers request, [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 0; i < request.Count; i++)
                {
                    await Task.Yield();
                    yield return i;
                }
            }
        }

        public static partial class HappenedHandler
        {
            public static int Calls;

            [HandlerOrder(3)]
            private static Task Handle(Happened notification) { Calls++; return Task.CompletedTask; }
        }
        """;

    [Test]
    public async Task Invokers_Adapt_Every_Return_Shape(CancellationToken cancellationToken)
    {
        var assembly = await CompileAsync(cancellationToken);

        var greet = assembly.GetType("Sample.GreetHandler")!;
        var handler = Activator.CreateInstance(greet, Activator.CreateInstance(assembly.GetType("Sample.Greeter")!), 2, "!")!;
        var greeting = await (ValueTask<string>)greet.GetMethod("InvokeAsync")!.Invoke(handler, [Create(assembly, "Sample.Greet", "keryx"), cancellationToken])!;
        await Assert.That(greeting).IsEqualTo("hello keryx!");

        var count = await (ValueTask<int>)Invoke(assembly, "Sample.CountHandler", Create(assembly, "Sample.Count", 41), cancellationToken);
        await Assert.That(count).IsEqualTo(42);

        var touched = Invoke(assembly, "Sample.TouchHandler", Create(assembly, "Sample.Touch"), cancellationToken);
        await Assert.That(( (ValueTask<Unit>)touched ).IsCompletedSuccessfully).IsTrue();
        await Assert.That(await (ValueTask<Unit>)touched).IsEqualTo(Unit.Value);
        await Assert.That((int)assembly.GetType("Sample.TouchHandler")!.GetField("Touched")!.GetValue(null)!).IsEqualTo(1);

        var waited = await (ValueTask<Unit>)Invoke(assembly, "Sample.WaitHandler", Create(assembly, "Sample.Wait"), cancellationToken);
        await Assert.That(waited).IsEqualTo(Unit.Value);

        var numbers = new List<int>();
        await foreach (var number in (IAsyncEnumerable<int>)Invoke(assembly, "Sample.NumbersHandler", Create(assembly, "Sample.Numbers", 3), cancellationToken))
        {
            numbers.Add(number);
        }

        await Assert.That(numbers).IsEquivalentTo([0, 1, 2]);

        await (ValueTask)Invoke(assembly, "Sample.HappenedHandler", Create(assembly, "Sample.Happened"), cancellationToken);
        await Assert.That((int)assembly.GetType("Sample.HappenedHandler")!.GetField("Calls")!.GetValue(null)!).IsEqualTo(1);
    }

    [Test]
    public async Task Generated_Metadata_Carries_The_Manifest_And_Constructor_Details(CancellationToken cancellationToken)
    {
        var assembly = await CompileAsync(cancellationToken);

        await Assert.That(assembly.GetCustomAttribute<KeryxManifestAttribute>()?.FormatVersion).IsEqualTo(1);

        var greet = assembly.GetType("Sample.GreetHandler")!;
        var manifest = greet.GetCustomAttribute<KeryxHandlerAttribute>()!;
        await Assert.That(manifest.TargetType).IsEqualTo(assembly.GetType("Sample.Greet"));
        await Assert.That(manifest.Kind).IsEqualTo(HandlerKind.Request);
        await Assert.That(manifest.StaticInvoker).IsFalse();

        var parameters = greet.GetConstructors().Single().GetParameters();
        await Assert.That(parameters.Select(static p => p.Name!)).IsEquivalentTo(["greeter", "repeat", "suffix"]);
        await Assert.That(parameters[0].GetCustomAttribute<FromKeyedServicesAttribute>()?.Key).IsEqualTo("polite");
        await Assert.That(parameters[1].DefaultValue).IsEqualTo(2);
        await Assert.That(parameters[2].DefaultValue).IsEqualTo("!");

        var happened = assembly.GetType("Sample.HappenedHandler")!.GetCustomAttribute<KeryxHandlerAttribute>()!;
        await Assert.That(happened.Kind).IsEqualTo(HandlerKind.Notification);
        await Assert.That(happened.Order).IsEqualTo(3);
        await Assert.That(happened.StaticInvoker).IsTrue();

        var numbers = assembly.GetType("Sample.NumbersHandler")!.GetCustomAttribute<KeryxHandlerAttribute>()!;
        await Assert.That(numbers.Kind).IsEqualTo(HandlerKind.Stream);
    }

    private async Task<Assembly> CompileAsync(CancellationToken cancellationToken)
    {
        var result = await Builder.AddSources(Source).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();
        return result.Assembly!;
    }

    private static object Create(Assembly assembly, string typeName, params object[] arguments) =>
        Activator.CreateInstance(assembly.GetType(typeName)!, arguments)!;

    private static object Invoke(Assembly assembly, string handlerType, object message, CancellationToken cancellationToken) =>
        assembly.GetType(handlerType)!.GetMethod("InvokeAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [message, cancellationToken])!;
}
