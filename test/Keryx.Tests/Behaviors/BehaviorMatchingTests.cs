using Keryx.Analyzers;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Constraint evaluation (ADR 0001, Matching), one case per constraint kind: <c>class</c> and <c>class?</c>, <c>struct</c>,
///     <c>unmanaged</c>, <c>new()</c> (including <c>required</c> members), <c>notnull</c>, interface and base-class type constraints (identity, implicit reference and boxing
///     conversions count; user-defined conversions, nullable value types and annotated reference types against a non-annotated constraint do
///     not), constraint types nested in generic types, a constraint that mentions another type parameter, all checked on the type parameters
///     of an open behavior method; and closed behavior methods, which must be closed over exactly the target's types. Open methods that write
///     the response or item type parameter annotated (<c>TResponse?</c>, OQ-58) fit annotated, oblivious and value type responses, with the
///     substituted nullability checked against the target, and not a non-annotated reference response.
/// </summary>
public class BehaviorMatchingTests
{
    private const string Source = """
        #nullable enable
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using Keryx;

        namespace Sample;

        public interface IThing { }
        public interface IHasKey<T> { }
        public sealed class Thing : IThing { }
        public struct StructThing : IThing { }
        public struct Managed { public string Text; }
        public sealed class Newable { }
        public sealed class NoNew { private NoNew() { } }
        public abstract class AbstractThing { }
        public class BaseThing { }
        public sealed class Derived : BaseThing { }
        public sealed class Convertible { public static implicit operator BaseThing(Convertible value) => new(); }

        public sealed record RString : IRequest<string>;
        public sealed record RNullableString : IRequest<string?>;
        public sealed record RInt : IRequest<int>;
        public sealed record RNullableInt : IRequest<int?>;
        public sealed record RManaged : IRequest<Managed>;
        public sealed record RNewable : IRequest<Newable>;
        public sealed record RNoNew : IRequest<NoNew>;
        public sealed record RAbstract : IRequest<AbstractThing>;
        public sealed record RThing : IRequest<Thing>;
        public sealed record RStructThing : IRequest<StructThing>;
        public sealed record RDerived : IRequest<Derived>;
        public sealed record RConvertible : IRequest<Convertible>;
        public sealed class RequiredReply { public required string Text { get; init; } }
        public class RequiredBase { public required int Id { get; init; } }
        public sealed class DerivedRequired : RequiredBase { }
        public sealed class SetsReply { [SetsRequiredMembers] public SetsReply() { Text = ""; } public required string Text { get; init; } }
        public struct RequiredStruct { public required int Value; }
        public sealed class Outer<T> { public interface IMarker { } public interface IInner<U> { } }
        public sealed record RRequired : IRequest<RequiredReply>;
        public sealed record RDerivedRequired : IRequest<DerivedRequired>;
        public sealed record RSetsRequired : IRequest<SetsReply>;
        public sealed record RRequiredStruct : IRequest<RequiredStruct>;
        public sealed record RMarked : IRequest<string>, Outer<string>.IMarker;
        public sealed record RWrongMarked : IRequest<string>, Outer<int>.IMarker;
        public sealed record RInner : IRequest<string>, Outer<string>.IInner<RInner>;
        public sealed record RWrongInner : IRequest<string>, Outer<int>.IInner<RWrongInner>;
        public sealed record KeyedRequest : IRequest<string>, IHasKey<string>;
        public sealed record WrongKey : IRequest<string>, IHasKey<int>;

        [RequestBehavior] public static class ClassB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : class where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class NullableClassB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : class? where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class StructB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : struct where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class UnmanagedB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : unmanaged where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class NewB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : new() where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class NotNullB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : notnull where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class InterfaceB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : IThing where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class BaseB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : BaseThing where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class KeyedB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse>, IHasKey<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class ComparableB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : IComparable where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class EquatableB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TResponse : IEquatable<TResponse> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class NestedB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class NestedGenericB { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse>, Outer<TResponse>.IInner<TRequest> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class ClosedB { public static ValueTask<string> Handle<TNext>(RString r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }
        [RequestBehavior] public static class ClosedNonNullable { public static ValueTask<string> Handle<TNext>(RNullableString r, TNext n, CancellationToken c) where TNext : struct, INext<string> => n.InvokeAsync(c); }
        [RequestBehavior] public static class AnnotatedConstraint { public static ValueTask<TResponse> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse?> where TNext : struct, INext<TResponse> => n.InvokeAsync(c); }
        [RequestBehavior] public static class AnnotatedNext { public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse> where TNext : struct, INext<TResponse?> => n.InvokeAsync(c); }
        [StreamBehavior] public static class AnnotatedStreamNext { public static System.Collections.Generic.IAsyncEnumerable<TItem?> Handle<TRequest, TItem, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IStreamRequest<TItem> where TNext : struct, IStreamNext<TItem?> => n.InvokeAsync(c); }
        [RequestBehavior] public static class MaybeB { public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse?> where TNext : struct, INext<TResponse?> => n.InvokeAsync(c); }
        [RequestBehavior] public static class MaybeClassB { public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse?> where TResponse : class where TNext : struct, INext<TResponse?> => n.InvokeAsync(c); }
        [RequestBehavior] public static class MaybeStructB { public static ValueTask<TResponse?> Handle<TRequest, TResponse, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IRequest<TResponse?> where TResponse : struct where TNext : struct, INext<TResponse?> => n.InvokeAsync(c); }
        [StreamBehavior] public static class MaybeStreamB { public static System.Collections.Generic.IAsyncEnumerable<TItem?> Handle<TRequest, TItem, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IStreamRequest<TItem?> where TNext : struct, IStreamNext<TItem?> => n.InvokeAsync(c); }
        public sealed record SString : IStreamRequest<string>;
        public sealed record SNullableString : IStreamRequest<string?>;
        public sealed record SInt : IStreamRequest<int>;
        #nullable disable
        public sealed record ROblivious : IRequest<string>;
        public sealed record SOblivious : IStreamRequest<string>;
        #nullable enable
        [RequestBehavior] public static class ClosedObliviousB { public static ValueTask<string?> Handle<TNext>(ROblivious r, TNext n, CancellationToken c) where TNext : struct, INext<string?> => n.InvokeAsync(c); }
        [RequestBehavior] public static class ClosedReturnB { public static async ValueTask<string?> Handle<TNext>(RString r, TNext n, CancellationToken c) where TNext : struct, INext<string> => await n.InvokeAsync(c); }
        [StreamBehavior] public static class ClosedStreamObliviousB { public static System.Collections.Generic.IAsyncEnumerable<string?> Handle<TNext>(SOblivious r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string?> => n.InvokeAsync(c); }
        [StreamBehavior] public static class ClosedStreamReturnB { public static System.Collections.Generic.IAsyncEnumerable<string?> Handle<TNext>(SString r, TNext n, CancellationToken c) where TNext : struct, IStreamNext<string> => n.InvokeAsync(c); }
        [StreamBehavior] public static class StreamOnly { public static System.Collections.Generic.IAsyncEnumerable<TItem> Handle<TRequest, TItem, TNext>(TRequest r, TNext n, CancellationToken c) where TRequest : IStreamRequest<TItem> where TNext : struct, IStreamNext<TItem> => n.InvokeAsync(c); }
        """;

    [Test]
    [Arguments("ClassB", "RString", true)]
    [Arguments("ClassB", "RNullableString", false)]
    [Arguments("ClassB", "RInt", false)]
    [Arguments("NullableClassB", "RNullableString", true)]
    [Arguments("NullableClassB", "RString", true)]
    [Arguments("NullableClassB", "RInt", false)]
    [Arguments("StructB", "RInt", true)]
    [Arguments("StructB", "RNullableInt", false)]
    [Arguments("StructB", "RString", false)]
    [Arguments("UnmanagedB", "RInt", true)]
    [Arguments("UnmanagedB", "RManaged", false)]
    [Arguments("UnmanagedB", "RNullableInt", false)]
    [Arguments("NewB", "RNewable", true)]
    [Arguments("NewB", "RInt", true)]
    [Arguments("NewB", "RNoNew", false)]
    [Arguments("NewB", "RAbstract", false)]
    [Arguments("NewB", "RRequired", false)]
    [Arguments("NewB", "RDerivedRequired", false)]
    [Arguments("NewB", "RSetsRequired", true)]
    [Arguments("NewB", "RRequiredStruct", false)]
    [Arguments("NotNullB", "RString", true)]
    [Arguments("NotNullB", "RInt", true)]
    [Arguments("NotNullB", "RNullableString", false)]
    [Arguments("NotNullB", "RNullableInt", false)]
    [Arguments("InterfaceB", "RThing", true)]
    [Arguments("InterfaceB", "RStructThing", true)]
    [Arguments("InterfaceB", "RString", false)]
    [Arguments("BaseB", "RDerived", true)]
    [Arguments("BaseB", "RConvertible", false)]
    [Arguments("KeyedB", "KeyedRequest", true)]
    [Arguments("KeyedB", "WrongKey", false)]
    [Arguments("EquatableB", "RString", true)]
    [Arguments("EquatableB", "RNullableString", false)]
    [Arguments("EquatableB", "RInt", true)]
    [Arguments("NestedB", "RMarked", true)]
    [Arguments("NestedB", "RWrongMarked", false)]
    [Arguments("NestedGenericB", "RInner", true)]
    [Arguments("NestedGenericB", "RWrongInner", false)]
    [Arguments("NestedGenericB", "RMarked", false)]
    [Arguments("ComparableB", "RInt", true)]
    [Arguments("ComparableB", "RNullableInt", false)]
    public async Task Open_Behavior_Constraints(string behavior, string request, bool fits)
    {
        var (fitted, reason, typeArguments) = Match(behavior, request);

        await Assert.That(fitted).IsEqualTo(fits).Because(reason);
        if (fits)
        {
            // TRequest, TResponse and, in place of TNext, nothing: the generated link.
            await Assert.That(typeArguments).StartsWith($"global::Sample.{request}, ");
            await Assert.That(typeArguments).EndsWith(", ");
        }
        else
        {
            await Assert.That(reason).Contains("does not satisfy the constraint");
        }
    }

    [Test]
    [Arguments("ClosedB", "RString", true, "")]
    [Arguments("ClosedB", "RInt", false, "its request behavior method is closed over 'RString' and 'string', but the target needs exactly 'RInt' and 'int'")]
    public async Task Closed_Behaviors_Need_The_Exact_Types(string behavior, string request, bool fits, string expected)
    {
        var (fitted, reason, typeArguments) = Match(behavior, request);

        await Assert.That(fitted).IsEqualTo(fits);
        await Assert.That(reason).IsEqualTo(expected);
        if (fits)
        {
            await Assert.That(typeArguments).IsEqualTo("");
        }
    }

    [Test]
    public async Task A_Closed_Method_Whose_Next_Drops_The_Nullable_Annotation_Is_Rejected()
    {
        // INext<string> for a request that responds with string? would be a nullability error in generated code (KRX019).
        var compilation = DirectCompilation.Create(Source);
        var symbols = KeryxSymbols.Create(compilation)!;
        var issues = new IssueCollector();

        await Assert.That(BehaviorShapes.Analyze(compilation.GetTypeByMetadataName("Sample.ClosedNonNullable")!, symbols, issues)).IsNull();
        await Assert.That(issues.Issues.Select(static i => i.Descriptor.Id)).IsEquivalentTo(["KRX019"]);
    }

    [Test]
    [Arguments("MaybeB", "RString", false, "", false)]
    [Arguments("MaybeB", "RNullableString", true, "string", false)]
    [Arguments("MaybeB", "ROblivious", true, "string", true)]
    [Arguments("MaybeB", "RInt", true, "int", false)]
    [Arguments("MaybeB", "RNullableInt", true, "int?", false)]
    [Arguments("MaybeClassB", "RString", false, "", false)]
    [Arguments("MaybeClassB", "RNullableString", true, "string", false)]
    [Arguments("MaybeClassB", "ROblivious", true, "string", true)]
    [Arguments("MaybeClassB", "RInt", false, "", false)]
    [Arguments("MaybeStructB", "RNullableInt", true, "int", false)]
    [Arguments("MaybeStructB", "RInt", false, "", false)]
    [Arguments("MaybeStructB", "RNullableString", false, "", false)]
    [Arguments("AnnotatedConstraint", "RString", false, "", false)]
    [Arguments("AnnotatedConstraint", "RNullableString", true, "string?", false)]
    [Arguments("AnnotatedConstraint", "ROblivious", true, "string", false)]
    [Arguments("AnnotatedConstraint", "RInt", true, "int", false)]
    [Arguments("AnnotatedNext", "RString", false, "", false)]
    [Arguments("AnnotatedNext", "RNullableString", true, "string?", false)]
    [Arguments("AnnotatedNext", "ROblivious", true, "string", true)]
    [Arguments("AnnotatedNext", "RInt", true, "int", false)]
    [Arguments("MaybeStreamB", "SString", false, "", false)]
    [Arguments("MaybeStreamB", "SNullableString", true, "string", false)]
    [Arguments("MaybeStreamB", "SOblivious", true, "string", true)]
    [Arguments("MaybeStreamB", "SInt", true, "int", false)]
    [Arguments("AnnotatedStreamNext", "SString", false, "", false)]
    [Arguments("AnnotatedStreamNext", "SNullableString", true, "string?", false)]
    public async Task An_Open_Method_That_Annotates_Its_Response_Type_Parameter_Matches_With_Nullability(
        string behavior,
        string request,
        bool fits,
        string responseArgument,
        bool oblivious
    )
    {
        // OQ-58: 'TResponse?' is substituted where the method writes it and checked, with its nullability, against the target: an annotated,
        // oblivious or value type response fits; a non-annotated reference response does not (IRequest<> and INext<> are invariant).
        var (fitted, reason, typeArguments, obliviousFit) = MatchWithFlags(behavior, request);

        await Assert.That(fitted).IsEqualTo(fits).Because(reason);
        if (fits)
        {
            await Assert.That(typeArguments).IsEqualTo($"global::Sample.{request}, {responseArgument}, ");
            await Assert.That(obliviousFit).IsEqualTo(oblivious);
        }
        else
        {
            await Assert.That(reason).IsNotEmpty();
        }
    }

    [Test]
    [Arguments("ClosedB", "RString", true, false)]
    [Arguments("ClosedObliviousB", "ROblivious", true, true)]
    [Arguments("ClosedReturnB", "RString", false, false)]
    [Arguments("ClosedStreamObliviousB", "SOblivious", true, true)]
    [Arguments("ClosedStreamReturnB", "SString", false, false)]
    public async Task A_Closed_Method_Matches_Its_Next_And_Return_Type_With_Nullability(string behavior, string request, bool fits, bool oblivious)
    {
        // A closed method's 'next' and return type are checked with nullability against the target, as an open method's are after
        // substitution (OQ-58): an annotated position over an oblivious response fits (called with nullable warnings disabled), and an
        // annotated return type over a non-annotated reference response does not (CS8619 in the generated entry point).
        var (fitted, reason, _, obliviousFit) = MatchWithFlags(behavior, request);

        await Assert.That(fitted).IsEqualTo(fits).Because(reason);
        await Assert.That(obliviousFit).IsEqualTo(oblivious);
        if (!fits)
        {
            await Assert.That(reason)
                        .IsEqualTo(
                             request.StartsWith('S')
                                 ? "its stream behavior method's return type carries 'string?', but the target's item type is 'string'"
                                 : "its request behavior method's return type carries 'string?', but the target's response type is 'string'"
                         );
        }
    }

    [Test]
    public async Task A_Non_Annotated_Reference_Response_Names_The_Annotated_Position()
    {
        // The message constraint is checked first, with its substituted nullability; then 'next' and the return type.
        var (fitted, reason, _, _) = MatchWithFlags("MaybeClassB", "RString");
        await Assert.That(fitted).IsFalse();
        await Assert.That(reason).IsEqualTo("'RString' does not satisfy the constraint 'TRequest : IRequest<string?>'");

        (fitted, reason, _, _) = MatchWithFlags("AnnotatedNext", "RString");
        await Assert.That(fitted).IsFalse();
        await Assert.That(reason)
                    .IsEqualTo("its request behavior method's 'next' carries 'TResponse?', which is 'string?' for this target, but the target's response type is 'string'");

        (fitted, reason, _, _) = MatchWithFlags("MaybeStructB", "RInt");
        await Assert.That(fitted).IsFalse();
        await Assert.That(reason).IsEqualTo("its request behavior method's 'next' carries 'TResponse?', which is never the response type 'int'");
    }

    [Test]
    public async Task A_Behavior_Of_Another_Kind_Never_Fits()
    {
        var compilation = DirectCompilation.Create(Source);
        var symbols = KeryxSymbols.Create(compilation)!;
        var request = compilation.GetTypeByMetadataName("Sample.RString")!;
        var stream = new BehaviorTarget(BehaviorKind.Stream, request, compilation.GetSpecialType(SpecialType.System_String));
        var notification = new BehaviorTarget(BehaviorKind.Notification, request, null);

        var requestShape = BehaviorShapes.Analyze(compilation.GetTypeByMetadataName("Sample.ClassB")!, symbols, new IssueCollector())!;
        var streamShape = BehaviorShapes.Analyze(compilation.GetTypeByMetadataName("Sample.StreamOnly")!, symbols, new IssueCollector())!;

        await Assert.That(BehaviorMatching.TryMatch(requestShape, stream, compilation, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo("it has no stream behavior method (the class does not carry [StreamBehavior])");
        await Assert.That(BehaviorMatching.TryMatch(streamShape, notification, compilation, out _, out reason)).IsFalse();
        await Assert.That(reason).IsEqualTo("it has no notification behavior method (the class does not carry [NotificationBehavior])");
    }

    private static (bool Fits, string Reason, string TypeArguments) Match(string behaviorName, string requestName)
    {
        var (fits, reason, typeArguments, _) = MatchWithFlags(behaviorName, requestName);
        return (fits, reason, typeArguments);
    }

    private static (bool Fits, string Reason, string TypeArguments, bool Oblivious) MatchWithFlags(string behaviorName, string requestName)
    {
        var compilation = DirectCompilation.Create(Source);
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(static d => d.ToString())));
        }

        var symbols = KeryxSymbols.Create(compilation)!;
        var behavior = compilation.GetTypeByMetadataName("Sample." + behaviorName)!;
        var shape = BehaviorShapes.Analyze(behavior, symbols, new IssueCollector()) ?? throw new InvalidOperationException(behaviorName + " is not a valid behavior");
        var request = compilation.GetTypeByMetadataName("Sample." + requestName)!;
        var classified = MessageType.Classify(request, symbols);
        var target = classified.IsStream ? new BehaviorTarget(BehaviorKind.Stream, request, classified.ItemType) : new BehaviorTarget(BehaviorKind.Request, request, classified.ResponseType);

        var fits = BehaviorMatching.TryMatch(shape, target, compilation, out var match, out var reason);
        return (fits, reason, fits ? string.Concat(match.TypeArguments.Select(static t => t is null ? "" : CSharpText.Type(t) + ", ")) : "", fits && match.ObliviousFit);
    }
}
