using System.Reflection;

namespace Keryx.Tests.Abstractions;

public class InterfaceShapeTests
{
    [Test]
    [Arguments(typeof(IRequest<>))]
    [Arguments(typeof(IStreamRequest<>))]
    [Arguments(typeof(INext<>))]
    [Arguments(typeof(IStreamNext<>))]
    [Arguments(typeof(IRequestPipelineBehavior<,>))]
    [Arguments(typeof(IStreamPipelineBehavior<,>))]
    [Arguments(typeof(IStreamHookBehavior<,>))]
    [Arguments(typeof(INotificationPipelineBehavior<>))]
    [Arguments(typeof(INotificationHandlerPipelineBehavior<>))]
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
    public async Task Request_Behaviors_Constrain_The_Request_To_Its_Response()
    {
        var pipeline = typeof(IRequestPipelineBehavior<,>).GetGenericArguments();
        var stream = typeof(IStreamPipelineBehavior<,>).GetGenericArguments();
        var hook = typeof(IStreamHookBehavior<,>).GetGenericArguments();

        await Assert.That(pipeline[0].GetGenericParameterConstraints()).IsEquivalentTo([typeof(IRequest<>).MakeGenericType(pipeline[1])]);
        await Assert.That(stream[0].GetGenericParameterConstraints()).IsEquivalentTo([typeof(IStreamRequest<>).MakeGenericType(stream[1])]);
        await Assert.That(hook[0].GetGenericParameterConstraints()).IsEquivalentTo([typeof(IStreamRequest<>).MakeGenericType(hook[1])]);
        await Assert.That(typeof(INotificationPipelineBehavior<>).GetGenericArguments()[0].GetGenericParameterConstraints())
                    .IsEquivalentTo([typeof(INotification)]);
        await Assert.That(typeof(INotificationHandlerPipelineBehavior<>).GetGenericArguments()[0].GetGenericParameterConstraints())
                    .IsEquivalentTo([typeof(INotification)]);
    }

    [Test]
    [Arguments(typeof(IRequestPipelineBehavior<,>), typeof(INext<>))]
    [Arguments(typeof(IStreamPipelineBehavior<,>), typeof(IStreamNext<>))]
    [Arguments(typeof(INotificationPipelineBehavior<>), typeof(INext))]
    [Arguments(typeof(INotificationHandlerPipelineBehavior<>), typeof(INext))]
    public async Task Behavior_Next_Is_A_Struct_Link(Type behaviorType, Type nextType)
    {
        var handle = behaviorType.GetMethod("Handle")!;
        var next = handle.GetGenericArguments().Single();
        var expected = nextType.IsGenericTypeDefinition ? nextType.MakeGenericType(behaviorType.GetGenericArguments()[1]) : nextType;

        await Assert.That(next.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask)
                    .IsEqualTo(GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);
        await Assert.That(next.GetGenericParameterConstraints().Where(static t => t != typeof(ValueType)).ToArray()).IsEquivalentTo([expected]);
    }

    [Test]
    public async Task Stream_Behavior_Wraps_The_Inner_Stream()
    {
        var handle = typeof(IStreamPipelineBehavior<,>).GetMethod("Handle")!;
        var item = typeof(IStreamPipelineBehavior<,>).GetGenericArguments()[1];
        var invoke = typeof(IStreamNext<>).GetMethod(nameof(IStreamNext<string>.InvokeAsync))!;

        await Assert.That(typeof(IStreamPipelineBehavior<,>).GetMethods()).HasSingleItem();
        await Assert.That(handle.ReturnType).IsEqualTo(typeof(IAsyncEnumerable<>).MakeGenericType(item));
        await Assert.That(handle.GetParameters().Select(static p => p.ParameterType.Name).ToArray())
                    .IsEquivalentTo(["TRequest", "TNext", nameof(CancellationToken)]);
        await Assert.That(invoke.ReturnType).IsEqualTo(typeof(IAsyncEnumerable<>).MakeGenericType(typeof(IStreamNext<>).GetGenericArguments()[0]));
        await Assert.That(invoke.GetParameters().Single().ParameterType).IsEqualTo(typeof(CancellationToken));
    }

    [Test]
    public async Task Stream_Hook_OnItem_Takes_The_Item_By_Reference()
    {
        var onItem = typeof(IStreamHookBehavior<,>).GetMethod(nameof(IStreamHookBehavior<StringStream, string>.OnItem))!;

        await Assert.That(onItem.ReturnType).IsEqualTo(typeof(bool));
        await Assert.That(onItem.GetParameters()[1].ParameterType.IsByRef).IsTrue();
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
        await Assert.That(exception.Message).Contains("KeryxHost");
        await Assert.That(typeof(KeryxHandlerNotFoundException).IsSealed).IsTrue();
    }

    [Test]
    public async Task KeryxHandlerNotFoundException_Rejects_A_Null_Type() =>
        await Assert.That(() => new KeryxHandlerNotFoundException(null!)).Throws<ArgumentNullException>();

    private sealed class StringRequest : IRequest<string>;

    private sealed class UnitRequest : IRequest;

    private sealed class StringStream : IStreamRequest<string>;
}
