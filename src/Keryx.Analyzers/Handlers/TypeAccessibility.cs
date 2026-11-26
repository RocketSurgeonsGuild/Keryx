using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     How widely a type can be used, taking containing types and type arguments into account.
/// </summary>
internal enum AccessLevel
{
    /// <summary>
    ///     Narrower than <c>internal</c> (private, protected or private protected somewhere in the chain), or file-local somewhere in the
    ///     chain: generated code in another file cannot name it either (OQ-44).
    /// </summary>
    Restricted = 0,

    /// <summary>Usable throughout its own assembly, but not effectively public.</summary>
    Internal = 1,

    /// <summary>Effectively public.</summary>
    Public = 2,
}

internal static class TypeAccessibility
{
    public static AccessLevel Of(ITypeSymbol? type) =>
        type switch
        {
            null => AccessLevel.Public,
            IArrayTypeSymbol array => Of(array.ElementType),
            IPointerTypeSymbol pointer => Of(pointer.PointedAtType),
            INamedTypeSymbol named => OfNamed(named),
            _ => AccessLevel.Public,
        };

    public static AccessLevel OfDeclaration(INamedTypeSymbol type)
    {
        var level = AccessLevel.Public;
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return AccessLevel.Restricted;
            }

            level = Min(level, OfDeclaredAccessibility(current.DeclaredAccessibility));
        }

        return level;
    }

    public static AccessLevel Min(AccessLevel left, AccessLevel right) => left < right ? left : right;

    private static AccessLevel OfNamed(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error)
        {
            return AccessLevel.Public;
        }

        var level = OfDeclaration(type);
        foreach (var argument in type.TypeArguments)
        {
            level = Min(level, Of(argument));
        }

        return level;
    }

    /// <summary>The access level of one declared accessibility, ignoring containing types.</summary>
    public static AccessLevel OfDeclaredAccessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => AccessLevel.Public,
            Accessibility.Internal or Accessibility.ProtectedOrInternal => AccessLevel.Internal,
            Accessibility.NotApplicable or Accessibility.Private or Accessibility.ProtectedAndInternal or Accessibility.Protected => AccessLevel.Restricted,
            _ => AccessLevel.Restricted,
        };
}
