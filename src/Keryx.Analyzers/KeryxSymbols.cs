using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers;

/// <summary>
///     The Keryx and framework symbols one compilation resolves, looked up by metadata name (<see cref="KnownTypeNames" />,
///     <see cref="WellKnownTypeNames" />). Never cached in an incremental model: it holds symbols.
/// </summary>
internal sealed class KeryxSymbols
{
    /// <summary>
    ///     The language version Keryx needs (KRX022).
    /// </summary>
    public const LanguageVersion MinimumLanguageVersion = LanguageVersion.CSharp14;

    private KeryxSymbols(Compilation compilation, INamedTypeSymbol requestOfT, INamedTypeSymbol streamRequest, INamedTypeSymbol notification)
    {
        Compilation = compilation;
        RequestOfT = requestOfT;
        StreamRequest = streamRequest;
        Notification = notification;
        Unit = compilation.GetTypeByMetadataName(KnownTypeNames.Unit);
        Sender = compilation.GetTypeByMetadataName(KnownTypeNames.ISender);
        BehaviorAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.BehaviorAttribute);
        GlobalBehaviorAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.GlobalBehaviorAttribute);
        HandlerOrderAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.HandlerOrderAttribute);
        PublishStrategyAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.PublishStrategyAttribute);
        KeryxHostAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.KeryxHostAttribute);
        KeryxContractsAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.KeryxContractsAttribute);
        KeryxManifestAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.KeryxManifestAttribute);
        BehaviorOrderAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.BehaviorOrderAttribute);
        RunsAfterAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.RunsAfterAttribute);
        RunsAfterAttributeOfT = compilation.GetTypeByMetadataName(KnownTypeNames.RunsAfterAttributeOfT);
        RunsBeforeAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.RunsBeforeAttribute);
        RunsBeforeAttributeOfT = compilation.GetTypeByMetadataName(KnownTypeNames.RunsBeforeAttributeOfT);
        ExcludeBehaviorAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.ExcludeBehaviorAttribute);
        ExcludeHandlerAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.ExcludeHandlerAttribute);
        ExternallyHandledAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.ExternallyHandledAttribute);
        KeryxHandlerAttribute = compilation.GetTypeByMetadataName(KnownTypeNames.KeryxHandlerAttribute);
        RequestPipelineBehavior = compilation.GetTypeByMetadataName(KnownTypeNames.IRequestPipelineBehavior);
        StreamPipelineBehavior = compilation.GetTypeByMetadataName(KnownTypeNames.IStreamPipelineBehavior);
        StreamHookBehavior = compilation.GetTypeByMetadataName(KnownTypeNames.IStreamHookBehavior);
        NotificationPipelineBehavior = compilation.GetTypeByMetadataName(KnownTypeNames.INotificationPipelineBehavior);
        NotificationHandlerPipelineBehavior = compilation.GetTypeByMetadataName(KnownTypeNames.INotificationHandlerPipelineBehavior);
        CancellationToken = compilation.GetTypeByMetadataName(WellKnownTypeNames.CancellationToken);
        Task = compilation.GetTypeByMetadataName(WellKnownTypeNames.Task);
        TaskOfT = compilation.GetTypeByMetadataName(WellKnownTypeNames.TaskOfT);
        ValueTask = compilation.GetTypeByMetadataName(WellKnownTypeNames.ValueTask);
        ValueTaskOfT = compilation.GetTypeByMetadataName(WellKnownTypeNames.ValueTaskOfT);
        AsyncEnumerableOfT = compilation.GetTypeByMetadataName(WellKnownTypeNames.IAsyncEnumerableOfT);
    }

    public Compilation Compilation { get; }
    public INamedTypeSymbol RequestOfT { get; }
    public INamedTypeSymbol StreamRequest { get; }
    public INamedTypeSymbol Notification { get; }
    public INamedTypeSymbol? Unit { get; }
    public INamedTypeSymbol? Sender { get; }
    public INamedTypeSymbol? BehaviorAttribute { get; }
    public INamedTypeSymbol? GlobalBehaviorAttribute { get; }
    public INamedTypeSymbol? HandlerOrderAttribute { get; }
    public INamedTypeSymbol? PublishStrategyAttribute { get; }
    public INamedTypeSymbol? KeryxHostAttribute { get; }
    public INamedTypeSymbol? KeryxContractsAttribute { get; }
    public INamedTypeSymbol? KeryxManifestAttribute { get; }
    public INamedTypeSymbol? BehaviorOrderAttribute { get; }
    public INamedTypeSymbol? RunsAfterAttribute { get; }
    public INamedTypeSymbol? RunsAfterAttributeOfT { get; }
    public INamedTypeSymbol? RunsBeforeAttribute { get; }
    public INamedTypeSymbol? RunsBeforeAttributeOfT { get; }
    public INamedTypeSymbol? ExcludeBehaviorAttribute { get; }
    public INamedTypeSymbol? ExcludeHandlerAttribute { get; }
    public INamedTypeSymbol? ExternallyHandledAttribute { get; }
    public INamedTypeSymbol? KeryxHandlerAttribute { get; }
    public INamedTypeSymbol? RequestPipelineBehavior { get; }
    public INamedTypeSymbol? StreamPipelineBehavior { get; }
    public INamedTypeSymbol? StreamHookBehavior { get; }
    public INamedTypeSymbol? NotificationPipelineBehavior { get; }
    public INamedTypeSymbol? NotificationHandlerPipelineBehavior { get; }
    public INamedTypeSymbol? CancellationToken { get; }
    public INamedTypeSymbol? Task { get; }
    public INamedTypeSymbol? TaskOfT { get; }
    public INamedTypeSymbol? ValueTask { get; }
    public INamedTypeSymbol? ValueTaskOfT { get; }
    public INamedTypeSymbol? AsyncEnumerableOfT { get; }

    /// <summary>
    ///     Resolves the symbols, or returns <see langword="null" /> when the compilation does not reference Keryx.
    /// </summary>
    public static KeryxSymbols? Create(Compilation compilation) =>
        compilation.GetTypeByMetadataName(KnownTypeNames.IRequestOfT) is { } requestOfT
     && compilation.GetTypeByMetadataName(KnownTypeNames.IStreamRequest) is { } streamRequest
     && compilation.GetTypeByMetadataName(KnownTypeNames.INotification) is { } notification
            ? new(compilation, requestOfT, streamRequest, notification)
            : null;

    /// <summary>
    ///     Whether the compilation is a host (<c>[assembly: KeryxHost]</c>).
    /// </summary>
    public bool IsHost => HasAssemblyAttribute(KeryxHostAttribute);

    /// <summary>
    ///     Whether the compilation is a contracts assembly (<c>[assembly: KeryxContracts]</c>).
    /// </summary>
    public bool IsContracts => HasAssemblyAttribute(KeryxContractsAttribute);

    /// <summary>
    ///     Whether the compilation's language version is at least <see cref="MinimumLanguageVersion" />.
    /// </summary>
    public static bool SupportsLanguageVersion(Compilation compilation) =>
        compilation is not CSharpCompilation csharp || csharp.LanguageVersion >= MinimumLanguageVersion;

    public bool IsCancellationToken(ITypeSymbol type) => SymbolEqualityComparer.Default.Equals(type, CancellationToken);

    public bool IsUnit(ITypeSymbol? type) => type is not null && SymbolEqualityComparer.Default.Equals(type, Unit);

    public static bool Is(ITypeSymbol? type, INamedTypeSymbol? definition) =>
        type is INamedTypeSymbol named && definition is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition);

    /// <summary>
    ///     Whether the attribute is one of the given Keryx attribute.
    /// </summary>
    public static bool IsAttribute(AttributeData attribute, INamedTypeSymbol? attributeType) =>
        attributeType is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType);

    /// <summary>
    ///     Whether a member was emitted by a Keryx generator (<c>[GeneratedCode("Keryx", ...)]</c>), so the rules on user members skip it.
    /// </summary>
    public static bool IsKeryxGenerated(ISymbol member) =>
        member.GetAttributes()
              .Any(static a => a.AttributeClass?.ToDisplayString() == WellKnownTypeNames.GeneratedCodeAttribute
                            && a.ConstructorArguments is [{ Value: "Keryx" }, ..]);

    private bool HasAssemblyAttribute(INamedTypeSymbol? attributeType) =>
        attributeType is not null && Compilation.Assembly.GetAttributes().Any(a => IsAttribute(a, attributeType));
}
