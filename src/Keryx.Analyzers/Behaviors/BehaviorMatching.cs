using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers.Behaviors;

/// <summary>
///     A pipeline target (ADR 0001, Matching): the kind of behavior method it takes, its message type and its response (request) or item
///     (stream) type. For a publish pipeline, the concrete published notification type and no response.
/// </summary>
/// <param name="Kind">The kind of behavior method that fits it.</param>
/// <param name="Message">The request, stream request or concrete notification type.</param>
/// <param name="Response">The response or item type; <see langword="null" /> for a notification.</param>
internal readonly record struct BehaviorTarget(BehaviorKind Kind, ITypeSymbol Message, ITypeSymbol? Response);

/// <summary>
///     A behavior method matched to a target: what a generated call needs.
/// </summary>
/// <param name="Method">The behavior method.</param>
/// <param name="TypeArguments">
///     The method's type arguments, in declared order, with <see langword="null" /> in place of <c>TNext</c> (the generated link).
/// </param>
/// <param name="Services">The injected parameter types, in declared order, with the target's types substituted.</param>
internal sealed record BehaviorMatch(BehaviorMethod Method, ImmutableArray<ITypeSymbol?> TypeArguments, ImmutableArray<ITypeSymbol> Services);

/// <summary>
///     Decides whether a behavior class fits a target and matches its behavior method of the target's kind (ADR 0001, Matching). Keryx checks
///     the constraints on the method's type parameters itself, because <c>IMethodSymbol.Construct</c> does not validate them.
/// </summary>
internal static class BehaviorMatching
{
    /// <summary>
    ///     Matches the behavior method of <paramref name="shape" /> whose kind fits <paramref name="target" />: an open method has its message
    ///     and response (or item) type parameters substituted with the target's types and every constraint checked; a closed method's message
    ///     and response (or item) types must be identical to the target's.
    /// </summary>
    /// <param name="shape">The behavior class's shape (<see cref="BehaviorShapes.Analyze" />).</param>
    /// <param name="target">The target.</param>
    /// <param name="compilation">The compilation, for conversions.</param>
    /// <param name="match">The matched method when it fits.</param>
    /// <param name="reason">Why it does not fit, for KRX009.</param>
    public static bool TryMatch(BehaviorShape shape, BehaviorTarget target, Compilation compilation, out BehaviorMatch match, out string reason)
    {
        match = null!;
        if (shape.Method(target.Kind) is not { } method)
        {
            reason = $"it has no {BehaviorShapes.KindName(target.Kind)} behavior method (the class does not carry {BehaviorShapes.AttributeName(target.Kind)})";
            return false;
        }

        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        if (method.IsOpen)
        {
            var parameters = ImmutableArray.CreateBuilder<ITypeParameterSymbol>();
            var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>();
            parameters.Add((ITypeParameterSymbol)method.Message);
            arguments.Add(target.Message);
            if (method.Response is ITypeParameterSymbol response && target.Response is { } targetResponse)
            {
                parameters.Add(response);
                arguments.Add(targetResponse);
            }

            if (!ConstraintsHold(parameters.ToImmutable(), arguments.ToImmutable(), compilation, out reason))
            {
                return false;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                map[parameters[i]] = arguments[i];
            }
        }
        else if (!SymbolEqualityComparer.Default.Equals(method.Message, target.Message)
              || ( method.Response is { } closedResponse
                && target.Response is { } expectedResponse
                && ( !SymbolEqualityComparer.Default.Equals(closedResponse, expectedResponse) || HandlerAnalysis.DiffersInNullabilityOnly(closedResponse, expectedResponse) ) ))
        {
            // Closed: identical types; assignable is not enough, and nullable annotations count when both sides are annotated.
            reason = target.Response is null
                ? $"its {BehaviorShapes.KindName(target.Kind)} behavior method is closed over '{Display(method.Message)}', but the target needs exactly '{Display(target.Message)}'"
                : $"its {BehaviorShapes.KindName(target.Kind)} behavior method is closed over '{Display(method.Message)}' and '{Display(method.Response)}', but the target needs exactly '{Display(target.Message)}' and '{Display(target.Response)}'";
            return false;
        }

        var typeArguments = method.Method.TypeParameters
                                  .Select(p => SymbolEqualityComparer.Default.Equals(p, method.Next) ? null : map.TryGetValue(p, out var argument) ? argument : p)
                                  .ToImmutableArray();
        var services = ImmutableArray.CreateBuilder<ITypeSymbol>();
        for (var i = 0; i < method.Roles.Length; i++)
        {
            if (method.Roles[i] == BehaviorParameterRole.Service)
            {
                services.Add(Substitute(method.Method.Parameters[i].Type, map, compilation));
            }
        }

        match = new(method, typeArguments, services.ToImmutable());
        reason = "";
        return true;
    }

    /// <summary>
    ///     Whether every constraint of <paramref name="parameters" /> holds for <paramref name="arguments" /> (ADR 0001, Matching): <c>class</c>,
    ///     <c>struct</c>, <c>unmanaged</c>, <c>new()</c> and <c>notnull</c> as the compiler defines them, nullable annotations counting for
    ///     <c>notnull</c> and <c>class</c>; a type constraint, after substitution, only through an identity, implicit reference or boxing
    ///     conversion.
    /// </summary>
    public static bool ConstraintsHold(
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments,
        Compilation compilation,
        out string reason
    )
    {
        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < parameters.Length; i++)
        {
            map[parameters[i]] = arguments[i];
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var argument = arguments[i];
            var argumentName = argument.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
            var isNullableValueType = argument.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var isAnnotated = argument.NullableAnnotation == NullableAnnotation.Annotated;

            string? failed = null;
            if (parameter.HasReferenceTypeConstraint
             && ( !argument.IsReferenceType
               || ( isAnnotated && parameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated ) ))
            {
                failed = parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class";
            }
            else if (parameter.HasUnmanagedTypeConstraint && ( !argument.IsUnmanagedType || !argument.IsValueType || isNullableValueType ))
            {
                failed = "unmanaged";
            }
            else if (parameter.HasValueTypeConstraint && ( !argument.IsValueType || isNullableValueType ))
            {
                failed = "struct";
            }
            else if (parameter.HasNotNullConstraint && ( isNullableValueType || ( isAnnotated && argument.IsReferenceType ) ))
            {
                failed = "notnull";
            }
            else if (parameter.HasConstructorConstraint && !HasPublicParameterlessConstructor(argument))
            {
                failed = "new()";
            }
            else
            {
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    var substituted = Substitute(constraint, map, compilation);

                    // A nullable value type satisfies no interface constraint (CS0313), although Nullable<T> boxes to T's interfaces. An annotated
                    // reference type satisfies a non-annotated type constraint only with a nullability warning (CS8631), an error in generated
                    // code; ClassifyConversion ignores nullability. A constraint that is a type parameter takes its argument's annotation; an
                    // oblivious constraint accepts anything.
                    var constraintAnnotation = constraint is ITypeParameterSymbol ? substituted.NullableAnnotation : constraint.NullableAnnotation;
                    if (!ConvertsForConstraint(argument, substituted, compilation)
                     || ( isNullableValueType && substituted.TypeKind == TypeKind.Interface )
                     || ( isAnnotated && argument.IsReferenceType && constraintAnnotation == NullableAnnotation.NotAnnotated ))
                    {
                        failed = substituted.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                        break;
                    }
                }
            }

            if (failed is not null)
            {
                reason = $"'{argumentName}' does not satisfy the constraint '{parameter.Name} : {failed}'";
                return false;
            }
        }

        reason = "";
        return true;
    }

    /// <summary>
    ///     A type constraint holds only through an identity conversion, an implicit reference conversion or a boxing conversion. User-defined,
    ///     numeric and nullable conversions do not count, although <c>ClassifyConversion</c> reports them as implicit.
    /// </summary>
    private static bool ConvertsForConstraint(ITypeSymbol argument, ITypeSymbol constraint, Compilation compilation)
    {
        if (compilation is not CSharpCompilation csharp)
        {
            return false;
        }

        var conversion = csharp.ClassifyConversion(argument, constraint);
        return conversion.IsIdentity || ( conversion.IsImplicit && conversion.IsReference ) || conversion.IsBoxing;
    }

    /// <summary>
    ///     Whether <paramref name="type" /> satisfies a <c>new()</c> constraint: a non-abstract class with a public parameterless constructor, or
    ///     a struct, and in either case no <c>required</c> members unless that constructor carries <c>[SetsRequiredMembers]</c> (CS9040).
    /// </summary>
    private static bool HasPublicParameterlessConstructor(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return type.IsValueType;
        }

        // A value type always has a parameterless constructor (implicit when none is declared).
        var constructor = named.InstanceConstructors.FirstOrDefault(static c => c.Parameters.IsEmpty);
        var constructible = named.IsValueType
                         || named is { TypeKind: TypeKind.Class, IsAbstract: false } && constructor is { DeclaredAccessibility: Accessibility.Public };
        return constructible && ( !HasRequiredMembers(named) || constructor is not null && constructor.GetAttributes().Any(IsSetsRequiredMembers) );

        static bool IsSetsRequiredMembers(AttributeData attribute) =>
            attribute.AttributeClass is { Name: "SetsRequiredMembersAttribute" } attributeClass
         && attributeClass.ContainingNamespace.ToDisplayString() == "System.Diagnostics.CodeAnalysis";
    }

    /// <summary>Whether a type or any of its base types declares a <c>required</c> field or property.</summary>
    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(static m => m is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Replaces type parameters in a constraint type with the target's types, keeping nullable annotations. A type nested in a generic type
    ///     (<c>Outer&lt;T&gt;.Inner</c>) is rebuilt from its substituted containing type.
    /// </summary>
    internal static ITypeSymbol Substitute(ITypeSymbol type, IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation) =>
        type switch
        {
            ITypeParameterSymbol parameter when map.TryGetValue(parameter, out var argument) => argument,
            IArrayTypeSymbol array => ( array.Rank == 1
                    ? compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, map, compilation))
                    : compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, map, compilation), array.Rank) )
               .WithNullableAnnotation(array.NullableAnnotation),
            INamedTypeSymbol { IsGenericType: true } named => SubstituteNamed(named, map, compilation).WithNullableAnnotation(named.NullableAnnotation),
            _ => type,
        };

    private static INamedTypeSymbol SubstituteNamed(INamedTypeSymbol named, IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation)
    {
        var definition = named.OriginalDefinition;
        var rebuilt = definition;
        if (named.ContainingType is { IsGenericType: true } containing)
        {
            var substitutedContaining = (INamedTypeSymbol)Substitute(containing, map, compilation);
            rebuilt = substitutedContaining.GetTypeMembers(named.Name, named.Arity)
                                           .First(member => SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, definition));
        }

        if (named.Arity == 0)
        {
            return rebuilt;
        }

        var arguments = named.TypeArguments.Select(argument => Substitute(argument, map, compilation)).ToImmutableArray();
        return rebuilt.Construct(arguments, arguments.Select(static argument => argument.NullableAnnotation).ToImmutableArray());
    }

    private static string Display(ITypeSymbol? type) =>
        type?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)) ?? "";
}
