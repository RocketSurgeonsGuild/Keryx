using System.Collections.Immutable;
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
/// <param name="ObliviousFit">
///     Whether the method fits only because the target's response (or item) type is nullable-oblivious where the method writes it annotated
///     (<c>TResponse?</c> against a <c>string</c> declared under <c>#nullable disable</c>, OQ-58). Generated code, which enables nullable,
///     names that type without the annotation, so the call is emitted with nullable warnings disabled, as the oblivious declaration has them.
/// </param>
internal sealed record BehaviorMatch(BehaviorMethod Method, ImmutableArray<ITypeSymbol?> TypeArguments, ImmutableArray<ITypeSymbol> Services, bool ObliviousFit);

/// <summary>
///     Decides whether a behavior class fits a target and matches its behavior method of the target's kind (ADR 0001, Matching). Keryx checks
///     the constraints on the method's type parameters itself, because <c>IMethodSymbol.Construct</c> does not validate them.
/// </summary>
internal static class BehaviorMatching
{
    /// <summary>
    ///     Matches the behavior method of <paramref name="shape" /> whose kind fits <paramref name="target" />: an open method has its message
    ///     and response (or item) type parameters substituted with the target's types and every constraint checked; a closed method's message
    ///     and response (or item) types must be identical to the target's, and its <c>next</c> and return type must carry the target's response
    ///     (or item) type with its nullability, as an open method's do after substitution.
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
        var oblivious = false;
        if (method.IsOpen)
        {
            if (!TryMatchOpen(method, target, compilation, map, out oblivious, out reason))
            {
                return false;
            }
        }
        else if (!SymbolEqualityComparer.Default.Equals(method.Message, target.Message)
              || ( method.Response is { } closedResponse
                && target.Response is { } expectedResponse
                && !SymbolEqualityComparer.Default.Equals(closedResponse, expectedResponse) ))
        {
            // Closed: identical types; assignable is not enough.
            reason = target.Response is null
                ? $"its {BehaviorShapes.KindName(target.Kind)} behavior method is closed over '{Display(method.Message)}', but the target needs exactly '{Display(target.Message)}'"
                : $"its {BehaviorShapes.KindName(target.Kind)} behavior method is closed over '{Display(method.Message)}' and '{Display(method.Response)}', but the target needs exactly '{Display(target.Message)}' and '{Display(target.Response)}'";
            return false;
        }
        else if (method.Response is not null
              && target.Response is { } closedTarget
              && !PositionsFit(method, closedTarget, map, compilation, target.Kind == BehaviorKind.Stream ? "item" : "response", out oblivious, out reason))
        {
            // Closed: 'next' and the return type must carry the target's response with its nullability, as for an open method (OQ-58).
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

        match = new(method, typeArguments, services.ToImmutable(), oblivious);
        reason = "";
        return true;
    }

    /// <summary>
    ///     Matches an open method (ADR 0001, Matching): substitutes its message type parameter with the target's message and its response (or
    ///     item) type parameter with the type that makes its <c>next</c> carry exactly the target's response (OQ-58): the response itself for
    ///     a bare <c>TResponse</c>; for <c>TResponse?</c>, the response without its annotation (as C# infers it), else the response itself;
    ///     for <c>Nullable&lt;TResponse&gt;</c>, the underlying type of a nullable value type response. Then every constraint must hold, and
    ///     <c>next</c> and the return type, substituted with their annotations, must be the target's response with the same nullability.
    /// </summary>
    private static bool TryMatchOpen(
        BehaviorMethod method,
        BehaviorTarget target,
        Compilation compilation,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> map,
        out bool oblivious,
        out string reason
    )
    {
        var messageParameter = (ITypeParameterSymbol)method.Message;
        oblivious = false;
        if (method.ResponseParameter is not { } responseParameter || target.Response is not { } targetResponse || method.Response is not { } carried)
        {
            map[messageParameter] = target.Message;
            return ConstraintsHold([messageParameter], [target.Message], compilation, out reason);
        }

        var candidates = new List<ITypeSymbol>(2);
        if (carried is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            if (targetResponse is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableResponse)
            {
                candidates.Add(nullableResponse.TypeArguments[0]);
            }
        }
        else
        {
            if (carried.NullableAnnotation == NullableAnnotation.Annotated && !targetResponse.IsValueType && targetResponse.NullableAnnotation == NullableAnnotation.Annotated)
            {
                candidates.Add(targetResponse.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            }

            candidates.Add(targetResponse);
        }

        var what = target.Kind == BehaviorKind.Stream ? "item" : "response";
        if (candidates.Count == 0)
        {
            reason = $"its {BehaviorShapes.KindName(target.Kind)} behavior method's 'next' carries '{Display(carried)}', which is never the {what} type '{Display(targetResponse)}'";
            return false;
        }

        string? firstReason = null;
        foreach (var candidate in candidates)
        {
            map.Clear();
            map[messageParameter] = target.Message;
            map[responseParameter] = candidate;
            if (ConstraintsHold([messageParameter, responseParameter], [target.Message, candidate], compilation, out reason)
             && PositionsFit(method, targetResponse, map, compilation, what, out oblivious, out reason))
            {
                return true;
            }

            firstReason ??= reason;
        }

        map.Clear();
        reason = firstReason!;
        return false;
    }

    /// <summary>
    ///     Whether the method's <c>next</c> and return type, substituted (unchanged for a closed method, whose map is empty), carry exactly
    ///     <paramref name="response" />, nullability included (the generated link implements <c>INext&lt;TResponse&gt;</c> over it, and the
    ///     pipeline returns it). An annotated position over an oblivious response fits with <paramref name="oblivious" /> set.
    /// </summary>
    private static bool PositionsFit(
        BehaviorMethod method,
        ITypeSymbol response,
        IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol> map,
        Compilation compilation,
        string what,
        out bool oblivious,
        out string reason
    )
    {
        oblivious = false;
        var positions = new List<(string Position, ITypeSymbol Type)>(2) { ("'next' carries", method.Response!) };
        if (method.Method.ReturnType is INamedTypeSymbol { TypeArguments: [var returned] })
        {
            positions.Add(("return type carries", returned));
        }

        foreach (var (position, type) in positions)
        {
            var substituted = Substitute(type, map, compilation);
            switch (CompareNullability(substituted, response))
            {
                case NullabilityFit.Exact:
                    continue;
                case NullabilityFit.Oblivious:
                    oblivious = true;
                    continue;
                case NullabilityFit.Mismatch:
                default:
                    reason = map.Count == 0
                        ? $"its {BehaviorShapes.KindName(method.Kind)} behavior method's {position} '{Display(type)}', but the target's {what} type is '{Display(response)}'"
                        : $"its {BehaviorShapes.KindName(method.Kind)} behavior method's {position} '{Display(type)}', which is '{Display(substituted)}' for this target, but the target's {what} type is '{Display(response)}'";
                    return false;
            }
        }

        reason = "";
        return true;
    }

    /// <summary>How a type the method writes, substituted, compares with the target's type, nullability included.</summary>
    private enum NullabilityFit
    {
        /// <summary>Different types, or the same type with a nullability the compiler warns about.</summary>
        Mismatch,

        /// <summary>The same type and nullability (or the method's side is oblivious).</summary>
        Exact,

        /// <summary>
        ///     The same type, but only because the target's side is oblivious where the method's is annotated: generated code, which names the
        ///     target's type without an annotation, needs nullable warnings disabled at the call.
        /// </summary>
        Oblivious,
    }

    /// <summary>
    ///     Compares <paramref name="written" /> (from the method, substituted) with <paramref name="target" /> as an identity conversion with
    ///     nullability: every reference type position must have the same annotation, unless one side is oblivious.
    /// </summary>
    private static NullabilityFit CompareNullability(ITypeSymbol written, ITypeSymbol target)
    {
        return !SymbolEqualityComparer.Default.Equals(written, target) ? NullabilityFit.Mismatch : Compare(written, target);

        static NullabilityFit Compare(ITypeSymbol written, ITypeSymbol target)
        {
            var result = NullabilityFit.Exact;
            if (!written.IsValueType && written.NullableAnnotation != NullableAnnotation.None && written.NullableAnnotation != target.NullableAnnotation)
            {
                if (target.NullableAnnotation != NullableAnnotation.None)
                {
                    return NullabilityFit.Mismatch;
                }

                if (written.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    result = NullabilityFit.Oblivious;
                }
            }

            var (writtenArguments, targetArguments) = (written, target) switch
            {
                (IArrayTypeSymbol w, IArrayTypeSymbol t) => (ImmutableArray.Create(w.ElementType), ImmutableArray.Create(t.ElementType)),
                (INamedTypeSymbol w, INamedTypeSymbol t) => (w.TypeArguments, t.TypeArguments),
                _ => (ImmutableArray<ITypeSymbol>.Empty, ImmutableArray<ITypeSymbol>.Empty),
            };
            for (var i = 0; i < writtenArguments.Length && i < targetArguments.Length; i++)
            {
                var nested = Compare(writtenArguments[i], targetArguments[i]);
                if (nested == NullabilityFit.Mismatch)
                {
                    return NullabilityFit.Mismatch;
                }

                if (nested == NullabilityFit.Oblivious)
                {
                    result = NullabilityFit.Oblivious;
                }
            }

            return result;
        }
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
                    // The type arguments of the constraint, substituted with their annotations ('IRequest<TResponse?>' is 'IRequest<string?>'),
                    // must match the argument's own base type or interface with nullability too (CS8631; OQ-58).
                    var constraintAnnotation = constraint is ITypeParameterSymbol ? substituted.NullableAnnotation : constraint.NullableAnnotation;
                    if (!ConvertsForConstraint(argument, substituted, compilation)
                     || ( isNullableValueType && substituted.TypeKind == TypeKind.Interface )
                     || ( isAnnotated && argument.IsReferenceType && constraintAnnotation == NullableAnnotation.NotAnnotated )
                     || !NestedNullabilityHolds(argument, substituted))
                    {
                        failed = Display(substituted);
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
    ///     Whether the type arguments of <paramref name="constraint" /> match, with nullability, those of the base type or interface of
    ///     <paramref name="argument" /> it converts through (the argument itself on an identity conversion), honoring <c>out</c> and <c>in</c>
    ///     variance. An oblivious side matches anything, as the compiler treats it.
    /// </summary>
    private static bool NestedNullabilityHolds(ITypeSymbol argument, ITypeSymbol constraint)
    {
        if (constraint is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return true;
        }

        var through = argument is INamedTypeSymbol self && SymbolEqualityComparer.Default.Equals(self.OriginalDefinition, named.OriginalDefinition)
            ? self
            : BaseTypesAndInterfaces(argument).FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, named.OriginalDefinition));
        return through is null || ArgumentsHold(through, named);

        static IEnumerable<INamedTypeSymbol> BaseTypesAndInterfaces(ITypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                yield return current;
            }

            foreach (var @interface in type.AllInterfaces)
            {
                yield return @interface;
            }

            if (type is ITypeParameterSymbol parameter)
            {
                foreach (var constraintType in parameter.ConstraintTypes.OfType<INamedTypeSymbol>())
                {
                    yield return constraintType;
                }
            }
        }

        static bool ArgumentsHold(INamedTypeSymbol have, INamedTypeSymbol want)
        {
            var parameters = want.OriginalDefinition.TypeParameters;
            for (var i = 0; i < want.TypeArguments.Length && i < have.TypeArguments.Length; i++)
            {
                // The compiler treats IEquatable<T> as contravariant for nullability ('string' implements IEquatable<string?>).
                var variance = IsEquatable(want) ? VarianceKind.In : i < parameters.Length ? parameters[i].Variance : VarianceKind.None;
                if (!Holds(have.TypeArguments[i], want.TypeArguments[i], variance))
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsEquatable(INamedTypeSymbol type) =>
            type is { Name: "IEquatable", Arity: 1, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

        static bool Holds(ITypeSymbol have, ITypeSymbol want, VarianceKind variance)
        {
            if (!have.IsValueType
             && have.NullableAnnotation != NullableAnnotation.None
             && want.NullableAnnotation != NullableAnnotation.None
             && have.NullableAnnotation != want.NullableAnnotation)
            {
                // Covariant: a non-annotated argument converts to an annotated one; contravariant: the reverse.
                var widening = have.NullableAnnotation == NullableAnnotation.NotAnnotated;
                if (!( variance == VarianceKind.Out && widening ) && !( variance == VarianceKind.In && !widening ))
                {
                    return false;
                }
            }

            return have is not INamedTypeSymbol { IsGenericType: true } haveNamed
                || want is not INamedTypeSymbol { IsGenericType: true } wantNamed
                || !SymbolEqualityComparer.Default.Equals(haveNamed.OriginalDefinition, wantNamed.OriginalDefinition)
                || ArgumentsHold(haveNamed, wantNamed);
        }
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
    ///     Replaces type parameters in a constraint type with the target's types, keeping nullable annotations: where the method writes
    ///     <c>T?</c> on an unconstrained or reference type parameter, the substituted reference type is annotated (<c>string?</c>), and a
    ///     value type stays as it is (<c>int</c>; OQ-58). A type nested in a generic type (<c>Outer&lt;T&gt;.Inner</c>) is rebuilt from its
    ///     substituted containing type.
    /// </summary>
    internal static ITypeSymbol Substitute(ITypeSymbol type, IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation) =>
        type switch
        {
            ITypeParameterSymbol parameter when map.TryGetValue(parameter, out var argument) =>
                parameter.NullableAnnotation == NullableAnnotation.Annotated && !argument.IsValueType
                    ? argument.WithNullableAnnotation(NullableAnnotation.Annotated)
                    : argument,
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
