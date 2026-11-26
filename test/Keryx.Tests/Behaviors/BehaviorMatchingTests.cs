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
///     of an open behavior method; and closed behavior methods, which must be closed over exactly the target's types.
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
    [Arguments("AnnotatedConstraint")]
    [Arguments("AnnotatedNext")]
    [Arguments("AnnotatedStreamNext")]
    public async Task An_Open_Method_That_Annotates_Its_Response_Type_Parameter_Is_Rejected(string behaviorName)
    {
        // IRequest<TResponse?> or INext<TResponse?> (IStreamNext<TItem?>) for a request that responds with string would be a nullability error
        // in generated code (IRequest<> and INext<> are invariant), so the shape is KRX019 and never matches.
        var compilation = DirectCompilation.Create(Source);
        var symbols = KeryxSymbols.Create(compilation)!;
        var issues = new IssueCollector();

        await Assert.That(BehaviorShapes.Analyze(compilation.GetTypeByMetadataName("Sample." + behaviorName)!, symbols, issues)).IsNull();
        await Assert.That(issues.Issues.Select(static i => i.Descriptor.Id)).IsEquivalentTo(["KRX019"]);
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
        var response = MessageType.Classify(request, symbols).ResponseType!;

        var fits = BehaviorMatching.TryMatch(shape, new(BehaviorKind.Request, request, response), compilation, out var match, out var reason);
        return (fits, reason, fits ? string.Concat(match.TypeArguments.Select(static t => t is null ? "" : CSharpText.Type(t) + ", ")) : "");
    }
}
