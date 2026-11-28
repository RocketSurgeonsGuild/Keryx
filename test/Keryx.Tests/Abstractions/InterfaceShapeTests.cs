using System.Reflection;

namespace Keryx.Tests.Abstractions;

public class InterfaceShapeTests
{
    [Test]
    [Arguments(typeof(IRequest<>))]
    [Arguments(typeof(IStreamRequest<>))]
    [Arguments(typeof(INext<>))]
    [Arguments(typeof(IStreamNext<>))]
    public async Task Generic_Interface_Is_Invariant(Type type)
    {
        foreach (var parameter in type.GetGenericArguments())
        {
            await Assert.That(parameter.GenericParameterAttributes & GenericParameterAttributes.VarianceMask)
                        .IsEqualTo(GenericParameterAttributes.None);
        }
    }

    [Test]
    public async Task A_Request_Does_Not_Convert_To_Another_Response_Type()
    {
        // Generated dispatch reinterprets ValueTask<TExact> as ValueTask<TResponse>; that is only safe while IRequest<> is invariant.
        object request = new StringRequest();

        await Assert.That(request is IRequest<string>).IsTrue();
        await Assert.That(request is IRequest<object>).IsFalse();
        object stream = new StringStream();

        await Assert.That(stream is IStreamRequest<string>).IsTrue();
        await Assert.That(stream is IStreamRequest<object>).IsFalse();
    }

    [Test]
    public async Task IRequest_Is_A_Request_Of_Unit()
    {
        await Assert.That(typeof(IRequest).GetInterfaces()).IsEquivalentTo([typeof(IRequest<Unit>)]);
        await Assert.That(new UnitRequest() is IRequest<Unit>).IsTrue();
    }

    [Test]
    public async Task IMediator_Combines_ISender_And_IPublisher()
    {
        await Assert.That(typeof(IMediator).GetInterfaces().OrderBy(static t => t.Name, StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo([typeof(IPublisher), typeof(ISender)]);
        await Assert.That(typeof(IMediator).GetMethods()).IsEmpty();
    }

    [Test]
    public async Task Marker_Interfaces_Declare_No_Members()
    {
        await Assert.That(typeof(IRequest<>).GetMembers()).IsEmpty();
        await Assert.That(typeof(IRequest).GetMembers()).IsEmpty();
        await Assert.That(typeof(IStreamRequest<>).GetMembers()).IsEmpty();
        await Assert.That(typeof(INotification).GetMembers()).IsEmpty();
    }

    [Test]
    [Arguments(typeof(INext<>))]
    [Arguments(typeof(INext))]
    [Arguments(typeof(IStreamNext<>))]
    public async Task Next_Has_One_InvokeAsync_That_Takes_The_Token(Type nextType)
    {
        var invoke = nextType.GetMethods().Single();
        var expected = nextType == typeof(INext) ? typeof(ValueTask)
            : nextType == typeof(INext<>) ? typeof(ValueTask<>).MakeGenericType(nextType.GetGenericArguments()[0])
            : typeof(IAsyncEnumerable<>).MakeGenericType(nextType.GetGenericArguments()[0]);

        await Assert.That(invoke.Name).IsEqualTo(nameof(INext.InvokeAsync));
        await Assert.That(invoke.ReturnType).IsEqualTo(expected);
        await Assert.That(invoke.GetParameters().Single().ParameterType).IsEqualTo(typeof(CancellationToken));
    }

    [Test]
    public async Task Behaviors_Are_Static_Classes_With_Kind_Attributes()
    {
        // No behavior interface exists: a behavior is a static class marked with one or more kind attributes (ADR 0001, Behavior kinds).
        var exported = typeof(Unit).Assembly.GetExportedTypes().Select(static t => t.Name).ToArray();

        await Assert.That(exported.Where(static n => n.StartsWith('I') && ( n.EndsWith("Behavior`1", StringComparison.Ordinal) || n.EndsWith("Behavior`2", StringComparison.Ordinal) ))).IsEmpty();
        await Assert.That(exported).Contains(nameof(RequestBehaviorAttribute));
        await Assert.That(exported).Contains(nameof(StreamBehaviorAttribute));
        await Assert.That(exported).Contains(nameof(NotificationBehaviorAttribute));
        // The only behavior attributes: the three kind attributes and the attaching, global and exclusion attributes (no per-handler kind).
        await Assert.That(exported.Where(static n => n.EndsWith("BehaviorAttribute", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo(
                         [
                             "BehaviorAttribute",
                             "ExcludeBehaviorAttribute",
                             "GlobalBehaviorAttribute",
                             "NotificationBehaviorAttribute",
                             "RequestBehaviorAttribute",
                             "StreamBehaviorAttribute",
                         ],
                         TUnit.Assertions.Enums.CollectionOrdering.Matching
                     );
    }

    [Test]
    public async Task Publish_Constrains_The_Notification()
    {
        var publish = typeof(IPublisher).GetMethod(nameof(IPublisher.Publish))!;

        await Assert.That(publish.GetGenericArguments().Single().GetGenericParameterConstraints()).IsEquivalentTo([typeof(INotification)]);
        await Assert.That(publish.ReturnType).IsEqualTo(typeof(ValueTask));
    }

    [Test]
    public async Task KeryxHandlerNotFoundException_Names_The_Request_Type()
    {
        var exception = new KeryxHandlerNotFoundException(typeof(StringRequest));

        await Assert.That(exception).IsAssignableTo<InvalidOperationException>();
        await Assert.That(exception.RequestType).IsEqualTo(typeof(StringRequest));
        await Assert.That(exception.Message).Contains(typeof(StringRequest).FullName!);
        await Assert.That(exception.Message).Contains("KeryxHost MSBuild property");
        await Assert.That(typeof(KeryxHandlerNotFoundException).IsSealed).IsTrue();
    }

    [Test]
    public async Task KeryxHandlerNotFoundException_Rejects_A_Null_Type() =>
        await Assert.That(() => new KeryxHandlerNotFoundException(null!)).Throws<ArgumentNullException>();

    private sealed class StringRequest : IRequest<string>;

    private sealed class UnitRequest : IRequest;

    private sealed class StringStream : IStreamRequest<string>;
}
