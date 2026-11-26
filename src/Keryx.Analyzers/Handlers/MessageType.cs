using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     What a type is to Keryx, from the Keryx interfaces it implements (ADR 0001, Requests and notifications).
/// </summary>
internal readonly struct MessageType
{
    private MessageType(ITypeSymbol type, int requestClosings, int streamClosings, bool isNotification, ITypeSymbol? responseType, ITypeSymbol? itemType)
    {
        Type = type;
        RequestClosings = requestClosings;
        StreamClosings = streamClosings;
        IsNotification = isNotification;
        ResponseType = responseType;
        ItemType = itemType;
    }

    public ITypeSymbol Type { get; }

    /// <summary>The number of distinct <c>IRequest&lt;&gt;</c> closings.</summary>
    public int RequestClosings { get; }

    /// <summary>The number of distinct <c>IStreamRequest&lt;&gt;</c> closings.</summary>
    public int StreamClosings { get; }

    /// <summary>Whether the type is or implements <c>INotification</c>.</summary>
    public bool IsNotification { get; }

    /// <summary>The <c>TResponse</c> of the only <c>IRequest&lt;&gt;</c> closing.</summary>
    public ITypeSymbol? ResponseType { get; }

    /// <summary>The <c>TItem</c> of the only <c>IStreamRequest&lt;&gt;</c> closing.</summary>
    public ITypeSymbol? ItemType { get; }

    /// <summary>
    ///     Whether the type takes part in Keryx at all: it is or implements a request, stream request or notification interface.
    /// </summary>
    public bool IsMessage => RequestClosings + StreamClosings > 0 || IsNotification;

    /// <summary>
    ///     Whether the type implements more than one closing of <c>IRequest&lt;&gt;</c>/<c>IStreamRequest&lt;&gt;</c>, both kinds, or a request
    ///     interface and <c>INotification</c> (KRX011).
    /// </summary>
    public bool IsConflicting => RequestClosings + StreamClosings > 1 || ( RequestClosings + StreamClosings > 0 && IsNotification );

    public bool IsRequest => RequestClosings == 1 && StreamClosings == 0 && !IsNotification;

    public bool IsStream => StreamClosings == 1 && RequestClosings == 0 && !IsNotification;

    public bool IsNotificationOnly => IsNotification && RequestClosings + StreamClosings == 0;

    /// <summary>
    ///     A concrete request or stream request type of a supported shape: the types KRX030 covers and <c>ExternallyHandled</c> can name
    ///     (ADR 0001, Host control). An interface, an abstract request base or an open generic request type is not one.
    /// </summary>
    public bool IsCoveredRequest => ( IsRequest || IsStream ) && IsConcrete && !IsUnsupportedShape;

    /// <summary>
    ///     A concrete type: a non-abstract class or struct (a record is either).
    /// </summary>
    public bool IsConcrete => Type is INamedTypeSymbol { IsAbstract: false, IsStatic: false, TypeKind: TypeKind.Class or TypeKind.Struct };

    /// <summary>
    ///     A type that cannot be a request, stream request or notification: it has type parameters, is nested in a generic type, or is a
    ///     <c>ref struct</c> (KRX012).
    /// </summary>
    public bool IsUnsupportedShape => Type.IsRefLikeType || HasTypeParameters(Type);

    public static MessageType Classify(ITypeSymbol type, KeryxSymbols symbols)
    {
        var requestClosings = new List<ITypeSymbol>();
        var streamClosings = new List<ITypeSymbol>();
        var isNotification = SymbolEqualityComparer.Default.Equals(type, symbols.Notification);

        foreach (var @interface in type is INamedTypeSymbol { TypeKind: TypeKind.Interface } self ? type.AllInterfaces.Insert(0, self) : type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, symbols.RequestOfT))
            {
                AddDistinct(requestClosings, @interface.TypeArguments[0]);
            }
            else if (SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, symbols.StreamRequest))
            {
                AddDistinct(streamClosings, @interface.TypeArguments[0]);
            }
            else if (SymbolEqualityComparer.Default.Equals(@interface, symbols.Notification))
            {
                isNotification = true;
            }
        }

        return new(
            type,
            requestClosings.Count,
            streamClosings.Count,
            isNotification,
            requestClosings.Count == 1 ? requestClosings[0] : null,
            streamClosings.Count == 1 ? streamClosings[0] : null
        );

        static void AddDistinct(List<ITypeSymbol> list, ITypeSymbol type)
        {
            if (!list.Contains(type, SymbolEqualityComparer.Default))
            {
                list.Add(type);
            }
        }
    }

    /// <summary>
    ///     Whether the type has type parameters of its own or through a containing type.
    /// </summary>
    public static bool HasTypeParameters(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
