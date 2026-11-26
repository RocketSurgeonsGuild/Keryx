using System.Collections.Immutable;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers.Behaviors;

/// <summary>What a behavior is matched against (ADR 0001, Three ways to attach a behavior, Matching).</summary>
internal enum BehaviorTargetKind
{
    /// <summary>A request handler: type arguments <c>TRequest</c>, <c>TResponse</c>.</summary>
    Request,

    /// <summary>A stream request handler: type arguments <c>TRequest</c>, <c>TItem</c>.</summary>
    Stream,

    /// <summary>One notification handler (per-handler behaviors): type argument <c>TNotification</c>, the concrete published type.</summary>
    NotificationHandler,

    /// <summary>A notification type (per-publish behaviors): type argument <c>TNotification</c>.</summary>
    Notification,
}

/// <summary>
///     A pipeline target: its kind and the type arguments a behavior interface closing must have to fit it.
/// </summary>
/// <param name="Kind">What the target is.</param>
/// <param name="TypeArguments">The type arguments in the behavior interface's order.</param>
internal readonly record struct BehaviorTarget(BehaviorTargetKind Kind, ImmutableArray<ITypeSymbol> TypeArguments)
{
    /// <summary>Whether a behavior of this kind can apply to the target at all.</summary>
    public bool Accepts(BehaviorKind kind) =>
        (Kind, kind) switch
        {
            (BehaviorTargetKind.Request, BehaviorKind.Request) => true,
            (BehaviorTargetKind.Stream, BehaviorKind.Stream or BehaviorKind.StreamHook) => true,
            (BehaviorTargetKind.NotificationHandler, BehaviorKind.NotificationHandler) => true,
            (BehaviorTargetKind.Notification, BehaviorKind.Notification) => true,
            _ => false,
        };

    /// <summary>The behavior interfaces that fit the target, for messages.</summary>
    public string ExpectedInterfaces =>
        Kind switch
        {
            BehaviorTargetKind.Request => "IRequestPipelineBehavior<TRequest, TResponse>",
            BehaviorTargetKind.Stream => "IStreamPipelineBehavior<TRequest, TItem> or IStreamHookBehavior<TRequest, TItem>",
            BehaviorTargetKind.NotificationHandler => "INotificationHandlerPipelineBehavior<TNotification>",
            BehaviorTargetKind.Notification => "INotificationPipelineBehavior<TNotification>",
            _ => "a Keryx behavior interface",
        };
}

/// <summary>
///     Decides whether a behavior fits a target and closes it over the target's types (ADR 0001, Matching). Keryx checks the constraints
///     itself, because <c>INamedTypeSymbol.Construct</c> does not validate them.
/// </summary>
internal static class BehaviorMatching
{
    /// <summary>
    ///     Closes the behavior named by <paramref name="behaviorType" /> (an unbound generic for an open behavior, a constructed or non-generic
    ///     type for a closed one) over <paramref name="target" />.
    /// </summary>
    /// <param name="behaviorType">The behavior as an attribute or scope names it.</param>
    /// <param name="shape">The behavior class's shape (<see cref="BehaviorShapes.Analyze" />).</param>
    /// <param name="target">The target.</param>
    /// <param name="compilation">The compilation, for conversions.</param>
    /// <param name="closed">The closed behavior type when it fits.</param>
    /// <param name="reason">Why it does not fit, for KRX009.</param>
    public static bool TryClose(
        INamedTypeSymbol behaviorType,
        BehaviorShape shape,
        BehaviorTarget target,
        Compilation compilation,
        out INamedTypeSymbol closed,
        out string reason
    )
    {
        closed = behaviorType;
        if (!target.Accepts(shape.Kind))
        {
            reason = $"it is {Article(shape.Kind)}, but the target needs {target.ExpectedInterfaces}";
            return false;
        }

        if (behaviorType.IsUnboundGenericType)
        {
            // Open: every type parameter is substituted with the target's types, positionally, then every constraint must hold.
            var definition = shape.Type;
            if (definition.TypeParameters.Length != target.TypeArguments.Length)
            {
                reason = "its type parameters do not match the target's";
                return false;
            }

            if (!ConstraintsHold(definition.TypeParameters, target.TypeArguments, compilation, out reason))
            {
                return false;
            }

            closed = definition.Construct(target.TypeArguments, target.TypeArguments.Select(static t => t.NullableAnnotation).ToImmutableArray());
            return true;
        }

        // Closed (non-generic, or a constructed generic named with all its type arguments): the interface's type arguments are the
        // target's, identically. Assignable is not enough.
        var closing = behaviorType.AllInterfaces.FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, shape.Interface.OriginalDefinition));
        if (closing is null || closing.TypeArguments.Length != target.TypeArguments.Length)
        {
            reason = "it does not implement the target's behavior interface";
            return false;
        }

        for (var i = 0; i < closing.TypeArguments.Length; i++)
        {
            var actual = closing.TypeArguments[i];
            var expected = target.TypeArguments[i];
            if (!SymbolEqualityComparer.Default.Equals(actual, expected) || HandlerAnalysis.DiffersInNullabilityOnly(actual, expected))
            {
                reason =
                    $"it is closed over '{Display(closing)}', but the target needs exactly '{Display(shape.Interface.OriginalDefinition.Construct([.. target.TypeArguments]))}'";
                return false;
            }
        }

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

    private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private static string Article(BehaviorKind kind) =>
        kind switch
        {
            BehaviorKind.Request => "a request behavior (IRequestPipelineBehavior)",
            BehaviorKind.Stream => "a stream wrap behavior (IStreamPipelineBehavior)",
            BehaviorKind.StreamHook => "a stream hook behavior (IStreamHookBehavior)",
            BehaviorKind.Notification => "a per-publish notification behavior (INotificationPipelineBehavior)",
            BehaviorKind.NotificationHandler => "a per-handler notification behavior (INotificationHandlerPipelineBehavior)",
            _ => "a behavior",
        };
}
