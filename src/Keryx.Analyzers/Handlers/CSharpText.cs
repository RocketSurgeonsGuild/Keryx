using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     Turns symbols and constants into C# source text for generated code: <c>global::</c>-qualified type names, escaped identifiers and
///     literals.
/// </summary>
internal static class CSharpText
{
    /// <summary>Fully qualified, <c>global::</c>-prefixed, with nullable reference annotations.</summary>
    public static SymbolDisplayFormat TypeFormat { get; } =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Fully qualified, <c>global::</c>-prefixed, without nullable annotations (for <c>typeof</c>).</summary>
    public static SymbolDisplayFormat TypeOfFormat { get; } = SymbolDisplayFormat.FullyQualifiedFormat;

    public static string Type(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    /// <summary>
    ///     The operand of a <c>typeof</c> expression; an unbound generic type keeps its empty type argument list (<c>X&lt;,&gt;</c>).
    /// </summary>
    public static string TypeOf(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsUnboundGenericType: true } unbound)
        {
            var definition = unbound.OriginalDefinition.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None)
            );
            return $"{definition}<{new string(',', unbound.Arity - 1)}>";
        }

        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeOfFormat);
    }

    public static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    ///     The C# expression for an attribute argument, or <see langword="null" /> when it cannot be written (an array or an error).
    /// </summary>
    public static string? Constant(TypedConstant constant) =>
        constant.Kind switch
        {
            TypedConstantKind.Type when constant.Value is ITypeSymbol type => $"typeof({TypeOf(type)})",
            TypedConstantKind.Enum when constant.Type is { } enumType => Value(constant.Value, enumType),
            TypedConstantKind.Primitive when constant.Value is null => "null",
            TypedConstantKind.Primitive when constant.Type is { } type => TypedPrimitive(constant.Value, type),
            TypedConstantKind.Error or TypedConstantKind.Array => null,
            _ => null,
        };

    /// <summary>
    ///     A primitive constant whose text keeps its type when it is boxed (a service key is compared with <c>object.Equals</c>): a
    ///     <c>byte</c>, <c>sbyte</c>, <c>short</c> or <c>ushort</c> literal would otherwise be an <c>int</c>, so it is cast.
    /// </summary>
    private static string TypedPrimitive(object value, ITypeSymbol type) =>
        type.SpecialType switch
        {
            SpecialType.System_Byte => $"(byte){Primitive(value)}",
            SpecialType.System_SByte => $"(sbyte){Primitive(value)}",
            SpecialType.System_Int16 => $"(short){Primitive(value)}",
            SpecialType.System_UInt16 => $"(ushort){Primitive(value)}",
            _ => Value(value, type),
        };

    /// <summary>
    ///     The C# expression for a parameter's explicit default value, or <see langword="null" /> when it has none.
    /// </summary>
    public static string? DefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        var type = parameter.Type;
        if (parameter.ExplicitDefaultValue is null)
        {
            if (type.IsValueType)
            {
                return type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ? "default" : "null";
            }

            // Generated code enables nullable, so a null default of a type that is not annotated (declared in a nullable-oblivious context,
            // or a non-nullable type the user already gets a warning for) would warn (CS8625) in the generated file.
            return type.NullableAnnotation == NullableAnnotation.Annotated ? "null" : "null!";
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return Value(parameter.ExplicitDefaultValue, type);
    }

    /// <summary>
    ///     The literal for a constant value of the given type; enums are cast from their underlying value.
    /// </summary>
    public static string Value(object? value, ITypeSymbol type)
    {
        return value is null
            ? type.IsValueType ? "default" : "null"
            : type is INamedTypeSymbol { TypeKind: TypeKind.Enum } ? $"({TypeOf(type)})({Primitive(value)})" : Primitive(value);
    }

    public static string Primitive(object value) =>
        value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool flag => flag ? "true" : "false",
            float single => Single(single),
            double number => Double(number),
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "default",
        };

    /// <summary>
    ///     A hint name: the fully qualified metadata name with every character outside <c>[A-Za-z0-9_.]</c> (including <c>+</c> and the arity
    ///     backtick) replaced by <c>_</c>, then 8 hex digits of the FNV-1a hash of the exact metadata name, then <c>.Keryx.g.cs</c>. The hash
    ///     keeps names apart that the sanitised form merges (<c>Orders+Cancel</c> and <c>Orders_Cancel</c>) or that Roslyn's case-insensitive
    ///     hint names merge (<c>Ping</c> and <c>ping</c>); see OQ-28.
    /// </summary>
    public static string HintName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var metadataName = string.Join("+", names);
        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            metadataName = ns.ToDisplayString() + "." + metadataName;
        }

        return HintName(metadataName);
    }

    /// <summary>The hint name for a fully qualified metadata name (<c>Namespace.Outer+Inner</c>).</summary>
    public static string HintName(string metadataName)
    {
        var builder = new StringBuilder(metadataName.Length + 20);
        foreach (var character in metadataName)
        {
            builder.Append(character is ( >= 'A' and <= 'Z' ) or ( >= 'a' and <= 'z' ) or ( >= '0' and <= '9' ) or '_' or '.' ? character : '_');
        }

        return builder
              .Append('.')
              .Append(Fnv1a(metadataName).ToString("x8", CultureInfo.InvariantCulture))
              .Append(".Keryx.g.cs")
              .ToString();
    }

    /// <summary>The 32-bit FNV-1a hash of the UTF-8 bytes of <paramref name="text" />: deterministic across processes and platforms.</summary>
    private static uint Fnv1a(string text)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            hash = unchecked(( hash ^ value ) * prime);
        }

        return hash;
    }

    public static string Accessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            Microsoft.CodeAnalysis.Accessibility.Private => "private",
            Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
            Microsoft.CodeAnalysis.Accessibility.NotApplicable => "",
            _ => "",
        };

    private static string Single(float value) =>
        float.IsNaN(value) ? "float.NaN"
        : float.IsPositiveInfinity(value) ? "float.PositiveInfinity"
        : float.IsNegativeInfinity(value) ? "float.NegativeInfinity"
        : value.ToString("R", CultureInfo.InvariantCulture) + "F";

    private static string Double(double value) =>
        double.IsNaN(value) ? "double.NaN"
        : double.IsPositiveInfinity(value) ? "double.PositiveInfinity"
        : double.IsNegativeInfinity(value) ? "double.NegativeInfinity"
        : value.ToString("R", CultureInfo.InvariantCulture) + "D";
}
