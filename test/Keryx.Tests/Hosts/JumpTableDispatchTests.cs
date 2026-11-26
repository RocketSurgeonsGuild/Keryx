using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Keryx.Analyzers.Hosts;
using Microsoft.Extensions.DependencyInjection;

namespace Keryx.Tests.Hosts;

/// <summary>
///     Generic dispatch above <see cref="HostEmitter.DispatchThreshold" /> (ADR 0001, Generic entry points): the generic <c>Send</c>,
///     <c>Stream</c> and <c>Publish&lt;T&gt;</c> look the runtime type up in a <c>FrozenDictionary</c> and jump on its index; at or below the
///     threshold they compare types one after another. The jump table matches the exact runtime type only: a type derived from a handled
///     message, with no handler of its own, is not dispatched to the base type's handler. Each kind is checked at the boundary in the
///     generated syntax and in the compiled IL (a <c>switch</c> opcode above the threshold, none at or below it), and at run time above it.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class JumpTableDispatchTests : HostTest
{
    /// <summary>The message kinds a host dispatches generically.</summary>
    public enum Kind
    {
        Request,
        Stream,
        Notification,
    }

    [Test]
    public async Task Streams_Above_The_Threshold_Stream_From_Their_Own_Handler(CancellationToken cancellationToken)
    {
        // Every arm reinterprets the request with Unsafe.As, and all S{n} share one layout, so a mismatched index or arm would quietly stream
        // another handler's items.
        var count = HostEmitter.DispatchThreshold + 1;
        var host = await CompileHostAsync(Messages(Kind.Stream, count), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.That(mediator.GetType().GetField("__streamRequestTypes", BindingFlags.NonPublic | BindingFlags.Static)).IsNotNull();
        for (var i = 0; i < count; i++)
        {
            object value = 100 + i;
            var request = (IStreamRequest<int>)Create(host, TypeName(Kind.Stream, i), value);
            await Assert.That(await ToListAsync(mediator.Stream(request, cancellationToken), cancellationToken))
                        .IsEquivalentTo([i, 100 + i], TUnit.Assertions.Enums.CollectionOrdering.Matching)
                        .Because($"{TypeName(Kind.Stream, i)} through IMediator.Stream");
        }

        // An unknown type takes the default arm (index -1) and throws when Stream is called, before anything is enumerated.
        var foreign = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => _ = mediator.Stream(new ForeignStream(), cancellationToken));
        await Assert.That(foreign.RequestType).IsEqualTo(typeof(ForeignStream));
    }

    [Test]
    [Arguments(Kind.Request)]
    [Arguments(Kind.Stream)]
    [Arguments(Kind.Notification)]
    public async Task A_Derived_Type_Is_Not_Dispatched_To_Its_Base_Types_Handler_Above_The_Threshold(Kind kind, CancellationToken cancellationToken)
    {
        // A type the host does not know, derived from one it dispatches: the jump table holds exact types only, so it takes the default arm
        // (not found for a request or a stream request, nothing to run for a notification) instead of the base type's handler.
        var count = HostEmitter.DispatchThreshold + 1;
        var hostResult = await HostBuilder.AddSources(Usings + Messages(kind, count)).Build().GenerateAsync(cancellationToken);
        hostResult.AssertCompilationWasSuccessful();
        var host = hostResult.Assembly!;
        var late = await CompileLibraryAsync(
            "Late",
            string.Create(CultureInfo.InvariantCulture, $"namespace Late;\n\npublic sealed record Derived(int Value) : {TypeName(kind, 0)}(Value);\n"),
            cancellationToken,
            references: [hostResult],
            kind: LibraryKind.Plain
        );
        var derived = Create(late.Assembly!, "Late.Derived", 7);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // The base type itself is dispatched to its handler.
        var handled = Create(host, TypeName(kind, 0), 7);
        switch (kind)
        {
            case Kind.Request:
                await Assert.That(await mediator.Send((IRequest<int>)handled, cancellationToken)).IsEqualTo(7);
                var request = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send((IRequest<int>)derived, cancellationToken)));
                await Assert.That(request.RequestType).IsEqualTo(derived.GetType());
                break;
            case Kind.Stream:
                await Assert.That(await ToListAsync(mediator.Stream((IStreamRequest<int>)handled, cancellationToken), cancellationToken))
                            .IsEquivalentTo([0, 7], TUnit.Assertions.Enums.CollectionOrdering.Matching);
                var stream = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => _ = mediator.Stream((IStreamRequest<int>)derived, cancellationToken));
                await Assert.That(stream.RequestType).IsEqualTo(derived.GetType());
                break;
            case Kind.Notification:
                _ = Call<string[]>(host, "Sample.Log", "Take");
                await mediator.Publish((INotification)handled, cancellationToken);
                await Assert.That(Call<string[]>(host, "Sample.Log", "Take")).IsEquivalentTo(["00 7"]);
                await mediator.Publish((INotification)derived, cancellationToken);
                await Assert.That(Call<string[]>(host, "Sample.Log", "Take")).IsEmpty();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    [Test]
    [Arguments(Kind.Request)]
    [Arguments(Kind.Stream)]
    [Arguments(Kind.Notification)]
    public async Task An_Unknown_Type_Takes_The_Default_Arm_Above_The_Threshold(Kind kind, CancellationToken cancellationToken)
    {
        var host = await CompileHostAsync(Messages(kind, HostEmitter.DispatchThreshold + 1), cancellationToken);
        await using var provider = BuildProvider(host);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        switch (kind)
        {
            case Kind.Request:
                var request = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => Ignore(mediator.Send(new ForeignRequest(), cancellationToken)));
                await Assert.That(request.RequestType).IsEqualTo(typeof(ForeignRequest));
                break;
            case Kind.Stream:
                var stream = ThrowsSynchronously<KeryxHandlerNotFoundException>(() => _ = mediator.Stream(new ForeignStream(), cancellationToken));
                await Assert.That(stream.RequestType).IsEqualTo(typeof(ForeignStream));
                break;
            case Kind.Notification:
                _ = Call<string[]>(host, "Sample.Log", "Take");
                var publish = mediator.Publish(new ForeignNotification(), cancellationToken);
                await Assert.That(publish.IsCompletedSuccessfully).IsTrue();
                await Assert.That(Call<string[]>(host, "Sample.Log", "Take")).IsEmpty();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    [Test]
    [Arguments(Kind.Stream, HostEmitter.DispatchThreshold)]
    [Arguments(Kind.Stream, HostEmitter.DispatchThreshold + 1)]
    [Arguments(Kind.Notification, HostEmitter.DispatchThreshold)]
    [Arguments(Kind.Notification, HostEmitter.DispatchThreshold + 1)]
    public async Task Generic_Dispatch_Is_Linear_Up_To_The_Threshold_And_A_Jump_Table_Above(Kind kind, int count, CancellationToken cancellationToken)
    {
        // The request kind's boundary is HostGeneratorTests.Should_Compare_Types_Linearly_Up_To_The_Threshold and
        // Should_Dispatch_Through_A_FrozenDictionary_Above_The_Threshold.
        var result = await HostBuilder.AddSources(Usings + Messages(kind, count)).Build().GenerateAsync(cancellationToken);

        result.AssertCompilationWasSuccessful();
        var shape = GenericDispatch.Of(TestSources.HostSource(result, HostEmitter.MediatorHintName), GenericMethodName(kind));
        var (table, lookup) = kind == Kind.Stream ? ("__streamRequestTypes", "__StreamRequestIndex") : ("__notificationTypes", "__NotificationIndex");
        await Assert.That(shape).IsEqualTo(count > HostEmitter.DispatchThreshold ? new GenericDispatch(lookup, 0, table) : new GenericDispatch(null, count, ""));
    }

    [Test]
    [Arguments(Kind.Request, HostEmitter.DispatchThreshold)]
    [Arguments(Kind.Request, HostEmitter.DispatchThreshold + 1)]
    [Arguments(Kind.Stream, HostEmitter.DispatchThreshold)]
    [Arguments(Kind.Stream, HostEmitter.DispatchThreshold + 1)]
    [Arguments(Kind.Notification, HostEmitter.DispatchThreshold)]
    [Arguments(Kind.Notification, HostEmitter.DispatchThreshold + 1)]
    public async Task The_Compiled_Generic_Entry_Point_Jumps_Above_The_Threshold(Kind kind, int count, CancellationToken cancellationToken)
    {
        // What the JIT gets: the IL of the generic entry point has a 'switch' opcode (a jump table on the looked-up index) above the
        // threshold and none at or below it, where it compares types one after another.
        var result = await HostBuilder.AddSources(Usings + Messages(kind, count)).Build().GenerateAsync(cancellationToken);
        result.AssertCompilationWasSuccessful();

        var switches = SwitchOpCodes(result.Image, $"{HostNamespace}.KeryxMediator", GenericMethodName(kind));

        await Assert.That(switches).IsEqualTo(count > HostEmitter.DispatchThreshold ? 1 : 0);
    }

    /// <summary>
    ///     <paramref name="count" /> messages of one kind, each with its own handler, as non-sealed records (so another assembly can derive from
    ///     them): <c>R{n}</c> answers <c>Value + n</c>, <c>S{n}</c> streams <c>n</c> then <c>Value</c>, and <c>N{n}</c> logs <c>"{n} {Value}"</c>
    ///     to <c>Sample.Log</c>.
    /// </summary>
    private static string Messages(Kind kind, int count)
    {
        var builder = new StringBuilder("namespace Sample;\n\n");
        builder.Append(
            """
            public static class Log
            {
                private static readonly List<string> Entries = [];

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


            """
        );
        for (var i = 0; i < count; i++)
        {
            var name = TypeName(kind, i)[( HostNamespace.Length + 1 )..];
            var index = i.ToString(CultureInfo.InvariantCulture);
            var label = i.ToString("00", CultureInfo.InvariantCulture);
            builder.Append(
                kind switch
                {
                    Kind.Request => $$"""
                                      public record {{name}}(int Value) : IRequest<int>;
                                      public static partial class {{name}}Handler { private static int Handle({{name}} request) => request.Value + {{index}}; }

                                      """,
                    Kind.Stream => $$"""
                                     public record {{name}}(int Value) : IStreamRequest<int>;
                                     public static partial class {{name}}Handler
                                     {
                                         private static async IAsyncEnumerable<int> Handle({{name}} request)
                                         {
                                             await Task.Yield();
                                             yield return {{index}};
                                             yield return request.Value;
                                         }
                                     }

                                     """,
                    Kind.Notification => $$"""
                                           public record {{name}}(int Value) : INotification;
                                           public static partial class {{name}}Handler
                                           {
                                               private static ValueTask Handle({{name}} notification)
                                               {
                                                   Log.Write("{{label}} " + notification.Value);
                                                   return default;
                                               }
                                           }

                                           """,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
                }
            );
        }

        return builder.ToString();
    }

    private static string TypeName(Kind kind, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{HostNamespace}.{kind switch { Kind.Request => "R", Kind.Stream => "S", _ => "N" }}{index:00}");

    private static string GenericMethodName(Kind kind) => kind switch
    {
        Kind.Request => "Send",
        Kind.Stream => "Stream",
        _ => "Publish",
    };

    /// <summary>
    ///     The number of <c>switch</c> opcodes in the IL of the generic method <paramref name="methodName" /> (one type parameter) of
    ///     <paramref name="typeName" />, read from the compiled image with System.Reflection.Metadata.
    /// </summary>
    private static int SwitchOpCodes(System.Collections.Immutable.ImmutableArray<byte> image, string typeName, string methodName)
    {
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
                           .Single(t => $"{metadata.GetString(t.Namespace)}.{metadata.GetString(t.Name)}" == typeName);
        var method = type.GetMethods()
                         .Select(metadata.GetMethodDefinition)
                         .Single(m => metadata.GetString(m.Name) == methodName && m.GetGenericParameters().Count == 1);
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;

        var switches = 0;
        var offset = 0;
        while (offset < il.Length)
        {
            var value = il[offset] == 0xFE ? unchecked((short)( 0xFE00 | il[offset + 1] )) : il[offset];
            offset += il[offset] == 0xFE ? 2 : 1;
            var opCode = OpCodesByValue[value];
            if (opCode == OpCodes.Switch)
            {
                switches++;
                offset += 4 + 4 * BitConverter.ToInt32(il, offset);
                continue;
            }

            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                _ => 4,
            };
        }

        return switches;
    }

    private static Dictionary<short, OpCode> OpCodesByValue { get; } =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(static f => (OpCode)f.GetValue(null)!).ToDictionary(static o => o.Value);

    private static async Task<List<int>> ToListAsync(IAsyncEnumerable<int> items, CancellationToken cancellationToken)
    {
        var list = new List<int>();
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list;
    }

    private static TException ThrowsSynchronously<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown when the method was called.");
    }

    private static void Ignore<T>(ValueTask<T> task) => _ = task.AsTask();

    private sealed record ForeignRequest : IRequest<int>;

    private sealed record ForeignStream : IStreamRequest<int>;

    private sealed record ForeignNotification : INotification;
}
