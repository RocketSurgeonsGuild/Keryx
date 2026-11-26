namespace Keryx.Analyzers;

/// <summary>
///     Metadata names of the Keryx types the analyzers and generators look for. The analyzers never reference <c>Keryx.dll</c>; they find
///     these types with <c>Compilation.GetTypeByMetadataName</c>. A test checks that every entry resolves against the real assembly.
/// </summary>
internal static class KnownTypeNames
{
    public const string Unit = "Keryx.Unit";
    public const string IRequestOfT = "Keryx.IRequest`1";
    public const string IRequest = "Keryx.IRequest";
    public const string IStreamRequest = "Keryx.IStreamRequest`1";
    public const string INotification = "Keryx.INotification";
    public const string ISender = "Keryx.ISender";
    public const string IPublisher = "Keryx.IPublisher";
    public const string IMediator = "Keryx.IMediator";
    public const string KeryxHandlerNotFoundException = "Keryx.KeryxHandlerNotFoundException";

    public const string INextOfT = "Keryx.INext`1";
    public const string INext = "Keryx.INext";
    public const string IRequestPipelineBehavior = "Keryx.IRequestPipelineBehavior`2";
    public const string IStreamNext = "Keryx.IStreamNext`1";
    public const string IStreamPipelineBehavior = "Keryx.IStreamPipelineBehavior`2";
    public const string IStreamHookBehavior = "Keryx.IStreamHookBehavior`2";
    public const string INotificationPipelineBehavior = "Keryx.INotificationPipelineBehavior`1";
    public const string INotificationHandlerPipelineBehavior = "Keryx.INotificationHandlerPipelineBehavior`1";

    public const string Phase = "Keryx.Phase";
    public const string PublishStrategy = "Keryx.PublishStrategy";

    public const string BehaviorAttribute = "Keryx.BehaviorAttribute";
    public const string GlobalBehaviorAttribute = "Keryx.GlobalBehaviorAttribute";
    public const string BehaviorOrderAttribute = "Keryx.BehaviorOrderAttribute";
    public const string RunsAfterAttribute = "Keryx.RunsAfterAttribute";
    public const string RunsAfterAttributeOfT = "Keryx.RunsAfterAttribute`1";
    public const string RunsBeforeAttribute = "Keryx.RunsBeforeAttribute";
    public const string RunsBeforeAttributeOfT = "Keryx.RunsBeforeAttribute`1";
    public const string ExcludeBehaviorAttribute = "Keryx.ExcludeBehaviorAttribute";
    public const string ExcludeHandlerAttribute = "Keryx.ExcludeHandlerAttribute";
    public const string PublishStrategyAttribute = "Keryx.PublishStrategyAttribute";
    public const string HandlerOrderAttribute = "Keryx.HandlerOrderAttribute";
    public const string KeryxHostAttribute = "Keryx.KeryxHostAttribute";
    public const string KeryxContractsAttribute = "Keryx.KeryxContractsAttribute";

    public const string KeryxHandlerAttribute = "Keryx.Internal.KeryxHandlerAttribute";
    public const string KeryxManifestAttribute = "Keryx.Internal.KeryxManifestAttribute";
    public const string HandlerKind = "Keryx.Internal.HandlerKind";
    public const string UnitAdapter = "Keryx.Internal.UnitAdapter";
}
