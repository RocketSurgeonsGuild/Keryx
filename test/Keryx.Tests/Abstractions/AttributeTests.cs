using System.ComponentModel;
using System.Reflection;
using Keryx.Internal;

namespace Keryx.Tests.Abstractions;

public class AttributeTests
{
    internal const AttributeTargets BehaviorTargets =
        AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly;

    [Test]
    [Arguments(typeof(BehaviorAttribute), BehaviorTargets, true)]
    [Arguments(typeof(RequestBehaviorAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(StreamBehaviorAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(NotificationBehaviorAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(GlobalBehaviorAttribute), AttributeTargets.Class | AttributeTargets.Method, false)]
    [Arguments(typeof(BehaviorOrderAttribute), AttributeTargets.Class | AttributeTargets.Method, false)]
    [Arguments(typeof(RunsAfterAttribute), AttributeTargets.Class | AttributeTargets.Method, true)]
    [Arguments(typeof(RunsBeforeAttribute), AttributeTargets.Class | AttributeTargets.Method, true)]
    [Arguments(typeof(ExcludeBehaviorAttribute), AttributeTargets.Assembly, true)]
    [Arguments(typeof(ExcludeHandlerAttribute), AttributeTargets.Assembly, true)]
    [Arguments(typeof(PublishStrategyAttribute), AttributeTargets.Class | AttributeTargets.Struct, false)]
    [Arguments(typeof(HandlerOrderAttribute), AttributeTargets.Method, false)]
    [Arguments(typeof(KeryxHandlerAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(KeryxManifestAttribute), AttributeTargets.Assembly, false)]
    public async Task Attribute_Has_The_Declared_Usage(Type attributeType, AttributeTargets targets, bool allowMultiple)
    {
        var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>(false);

        await Assert.That(usage).IsNotNull();
        await Assert.That(usage!.ValidOn).IsEqualTo(targets);
        await Assert.That(usage.AllowMultiple).IsEqualTo(allowMultiple);
        await Assert.That(usage.Inherited).IsTrue();
        await Assert.That(attributeType.IsSealed).IsTrue();
        await Assert.That(attributeType.BaseType).IsEqualTo(typeof(Attribute));
    }

    [Test]
    public async Task Every_Public_Attribute_Is_Covered()
    {
        var attributes = typeof(Unit).Assembly.GetExportedTypes()
                                     .Where(static t => typeof(Attribute).IsAssignableFrom(t))
                                     .Select(static t => t.Name)
                                     .Order(StringComparer.Ordinal)
                                     .ToArray();

        await Assert.That(attributes)
                    .IsEquivalentTo(
                         [
                             "BehaviorAttribute",
                             "BehaviorOrderAttribute",
                             "ExcludeBehaviorAttribute",
                             "ExcludeHandlerAttribute",
                             "GlobalBehaviorAttribute",
                             "HandlerOrderAttribute",
                             "KeryxHandlerAttribute",
                             "KeryxManifestAttribute",
                             "NotificationBehaviorAttribute",
                             "PublishStrategyAttribute",
                             "RequestBehaviorAttribute",
                             "RunsAfterAttribute",
                             "RunsBeforeAttribute",
                             "StreamBehaviorAttribute",
                         ]
                     );
    }

    [Test]
    public async Task BehaviorOrder_Stores_Phase_And_Defaults_Order_To_Zero()
    {
        var attribute = new BehaviorOrderAttribute(Phase.Caching);
        var ordered = new BehaviorOrderAttribute((Phase)250) { Order = -3 };

        await Assert.That(attribute.Phase).IsEqualTo(Phase.Caching);
        await Assert.That(attribute.Order).IsEqualTo(0);
        await Assert.That(ordered.Phase).IsEqualTo((Phase)250);
        await Assert.That(ordered.Order).IsEqualTo(-3);
    }

    [Test]
    public async Task Type_Arguments_Are_Stored()
    {
        await Assert.That(new RunsAfterAttribute(typeof(List<>)).BehaviorType).IsEqualTo(typeof(List<>));
        await Assert.That(new RunsBeforeAttribute(typeof(string)).BehaviorType).IsEqualTo(typeof(string));
        await Assert.That(new BehaviorAttribute(typeof(Dictionary<,>)).BehaviorType).IsEqualTo(typeof(Dictionary<,>));
        await Assert.That(new ExcludeBehaviorAttribute(typeof(int)).BehaviorType).IsEqualTo(typeof(int));
        await Assert.That(new ExcludeHandlerAttribute(typeof(object)).HandlerType).IsEqualTo(typeof(object));
        await Assert.That(new PublishStrategyAttribute(PublishStrategy.Parallel).Strategy).IsEqualTo(PublishStrategy.Parallel);
        await Assert.That(new HandlerOrderAttribute(7).Order).IsEqualTo(7);
    }

    [Test]
    public async Task KeryxHandler_Stores_Arguments_And_Defaults()
    {
        var attribute = new KeryxHandlerAttribute(typeof(string), HandlerKind.Notification);
        var full = new KeryxHandlerAttribute(typeof(int), HandlerKind.Request)
        {
            Behaviors = [typeof(List<>), typeof(string)],
            Order = 4,
            StaticInvoker = true,
        };

        await Assert.That(attribute.TargetType).IsEqualTo(typeof(string));
        await Assert.That(attribute.Kind).IsEqualTo(HandlerKind.Notification);
        await Assert.That(attribute.Behaviors).IsNotNull();
        await Assert.That(attribute.Behaviors).IsEmpty();
        await Assert.That(attribute.Order).IsEqualTo(0);
        await Assert.That(attribute.StaticInvoker).IsFalse();
        await Assert.That(full.Behaviors).IsEquivalentTo([typeof(List<>), typeof(string)]);
        await Assert.That(full.Order).IsEqualTo(4);
        await Assert.That(full.StaticInvoker).IsTrue();
    }

    [Test]
    public async Task KeryxManifest_FormatVersion_Defaults_To_Zero()
    {
        await Assert.That(new KeryxManifestAttribute().FormatVersion).IsEqualTo(0);
        await Assert.That(new KeryxManifestAttribute { FormatVersion = 1 }.FormatVersion).IsEqualTo(1);
    }

    [Test]
    public async Task Internal_Types_Are_Hidden_From_IntelliSense()
    {
        var internalTypes = typeof(Unit).Assembly.GetExportedTypes()
                                        .Where(static t => t.Namespace == "Keryx.Internal")
                                        .Select(static t => t.Name)
                                        .Order(StringComparer.Ordinal)
                                        .ToArray();

        await Assert.That(internalTypes).IsEquivalentTo(["HandlerKind", "KeryxHandlerAttribute", "KeryxManifestAttribute", "UnitAdapter"]);
        foreach (var type in typeof(Unit).Assembly.GetExportedTypes().Where(static t => t.Namespace == "Keryx.Internal"))
        {
            var browsable = type.GetCustomAttribute<EditorBrowsableAttribute>(false);
            await Assert.That(browsable?.State).IsEqualTo(EditorBrowsableState.Never);
        }
    }

    [Test]
    public async Task Public_Types_Are_In_The_Keryx_Or_Keryx_Internal_Namespace()
    {
        var namespaces = typeof(Unit).Assembly.GetExportedTypes().Select(static t => t.Namespace ?? "").Distinct().Order(StringComparer.Ordinal).ToArray();

        await Assert.That(namespaces).IsEquivalentTo(["Keryx", "Keryx.Internal"]);
    }
}
