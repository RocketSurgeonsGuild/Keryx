using Keryx.Analyzers;
using Keryx.Analyzers.Behaviors;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Tests.Behaviors;

/// <summary>
///     Constraint evaluation (ADR 0001, Matching), one case per constraint kind: <c>class</c> and <c>class?</c>, <c>struct</c>,
///     <c>unmanaged</c>, <c>new()</c> (including <c>required</c> members), <c>notnull</c>, interface and base-class type constraints (identity, implicit reference and boxing
///     conversions count; user-defined conversions, nullable value types and annotated reference types against a non-annotated constraint do
///     not), constraint types nested in generic types, a constraint that mentions another type parameter, and
///     closed behaviors, which must be closed over exactly the target's types.
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

        public abstract class Base<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            public ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken) where TNext : struct, INext<TResponse> =>
                next.InvokeAsync(cancellationToken);
        }

        public sealed class ClassB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : class;
        public sealed class NullableClassB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : class?;
        public sealed class StructB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : struct;
        public sealed class UnmanagedB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : unmanaged;
        public sealed class NewB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : new();
        public sealed class NotNullB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : notnull;
        public sealed class InterfaceB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : IThing;
        public sealed class BaseB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : BaseThing;
        public sealed class KeyedB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, IHasKey<TResponse>;
        public sealed class ComparableB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : IComparable;
        public sealed class EquatableB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse> where TResponse : IEquatable<TResponse>;
        public sealed class NestedB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, Outer<TResponse>.IMarker;
        public sealed class NestedGenericB<TRequest, TResponse> : Base<TRequest, TResponse>, IRequestPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>, Outer<TResponse>.IInner<TRequest>;
        public sealed class ClosedB : Base<RString, string>, IRequestPipelineBehavior<RString, string>;
        public sealed class ClosedNonNullable : Base<RNullableString, string>, IRequestPipelineBehavior<RNullableString, string>;
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
        var (fitted, reason, closed) = Match(behavior + "`2", request, open: true);

        await Assert.That(fitted).IsEqualTo(fits).Because(reason);
        if (fits)
        {
            await Assert.That(closed).StartsWith($"global::Sample.{behavior}<global::Sample.{request}, ");
        }
        else
        {
            await Assert.That(reason).Contains("does not satisfy the constraint");
        }
    }

    [Test]
    [Arguments("ClosedB", "RString", true, "")]
    [Arguments("ClosedB", "RInt", false, "it is closed over 'IRequestPipelineBehavior<RString, string>', but the target needs exactly 'IRequestPipelineBehavior<RInt, int>'")]
    [Arguments("ClosedNonNullable", "RNullableString", false, "it is closed over 'IRequestPipelineBehavior<RNullableString, string>', but the target needs exactly 'IRequestPipelineBehavior<RNullableString, string?>'")]
    public async Task Closed_Behaviors_Need_The_Exact_Types(string behavior, string request, bool fits, string expected)
    {
        var (fitted, reason, _) = Match(behavior, request, open: false);

        await Assert.That(fitted).IsEqualTo(fits);
        await Assert.That(reason).IsEqualTo(expected);
    }

    [Test]
    public async Task A_Behavior_Of_Another_Kind_Never_Fits()
    {
        var compilation = DirectCompilation.Create(Source);
        var symbols = KeryxSymbols.Create(compilation)!;
        var behavior = compilation.GetTypeByMetadataName("Sample.ClassB`2")!;
        var shape = BehaviorShapes.Analyze(behavior, symbols, new IssueCollector())!;
        var request = compilation.GetTypeByMetadataName("Sample.RString")!;
        var target = new BehaviorTarget(BehaviorTargetKind.Stream, [request, compilation.GetSpecialType(SpecialType.System_String)]);

        var fitted = BehaviorMatching.TryClose(behavior.ConstructUnboundGenericType(), shape, target, compilation, out _, out var reason);

        await Assert.That(fitted).IsFalse();
        await Assert.That(reason)
                    .IsEqualTo("it is a request behavior (IRequestPipelineBehavior), but the target needs IStreamPipelineBehavior<TRequest, TItem> or IStreamHookBehavior<TRequest, TItem>");
    }

    private static (bool Fits, string Reason, string Closed) Match(string behaviorName, string requestName, bool open)
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
        var target = new BehaviorTarget(BehaviorTargetKind.Request, [request, response]);

        var fits = BehaviorMatching.TryClose(open ? behavior.ConstructUnboundGenericType() : behavior, shape, target, compilation, out var closed, out var reason);
        return (fits, reason, CSharpText.Type(closed));
    }
}
