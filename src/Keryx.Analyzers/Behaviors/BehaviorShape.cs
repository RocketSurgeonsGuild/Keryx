using System.Collections.Immutable;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Behaviors;

/// <summary>The Keryx behavior interface a behavior implements (ADR 0001, Behavior kinds).</summary>
internal enum BehaviorKind
{
    /// <summary><c>IRequestPipelineBehavior&lt;TRequest, TResponse&gt;</c>.</summary>
    Request,

    /// <summary><c>IStreamPipelineBehavior&lt;TRequest, TItem&gt;</c>, a wrap behavior.</summary>
    Stream,

    /// <summary><c>IStreamHookBehavior&lt;TRequest, TItem&gt;</c>, a hook behavior.</summary>
    StreamHook,

    /// <summary><c>INotificationPipelineBehavior&lt;TNotification&gt;</c>, around one whole publish.</summary>
    Notification,

    /// <summary><c>INotificationHandlerPipelineBehavior&lt;TNotification&gt;</c>, around each notification handler.</summary>
    NotificationHandler,
}

/// <summary>
///     A behavior class whose shape Keryx supports (ADR 0001, Behavior shapes): one Keryx behavior interface closing, either open with the
///     interface's type parameters mapped positionally or closed and non-generic, not abstract, implemented with non-virtual public methods,
///     with exactly one public constructor. Holds symbols, so it never reaches an incremental model.
/// </summary>
/// <param name="Type">The behavior class (its definition when it is generic).</param>
/// <param name="Kind">Which behavior interface it implements.</param>
/// <param name="Interface">The behavior interface closing, as the class declares it.</param>
/// <param name="Constructor">The one public constructor.</param>
internal sealed record BehaviorShape(INamedTypeSymbol Type, BehaviorKind Kind, INamedTypeSymbol Interface, IMethodSymbol Constructor)
{
    /// <summary>Whether the class is open (generic, its type parameters mapped to the interface's).</summary>
    public bool IsOpen => Type.IsGenericType;
}

/// <summary>
///     The behavior class rules (ADR 0001, Behavior shapes): KRX019 and KRX020, shared by the analyzer, which reports them, and the host
///     generator, which skips a behavior they reject.
/// </summary>
internal static class BehaviorShapes
{
    /// <summary>
    ///     The Keryx behavior interface closings a type implements, distinct, each with its kind, in the order of <c>AllInterfaces</c>.
    /// </summary>
    public static ImmutableArray<(BehaviorKind Kind, INamedTypeSymbol Interface)> Interfaces(ITypeSymbol type, KeryxSymbols symbols)
    {
        var builder = ImmutableArray.CreateBuilder<(BehaviorKind, INamedTypeSymbol)>();
        foreach (var @interface in type.AllInterfaces)
        {
            if (KindOf(@interface, symbols) is { } kind && !Contains(builder, @interface))
            {
                builder.Add((kind, @interface));
            }
        }

        return builder.ToImmutable();

        static bool Contains(ImmutableArray<(BehaviorKind, INamedTypeSymbol)>.Builder builder, INamedTypeSymbol @interface)
        {
            foreach (var (_, existing) in builder)
            {
                if (SymbolEqualityComparer.Default.Equals(existing, @interface))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The behavior kind of a closing of one of the Keryx behavior interfaces, or <see langword="null" />.</summary>
    public static BehaviorKind? KindOf(INamedTypeSymbol @interface, KeryxSymbols symbols) =>
        @interface.OriginalDefinition switch
        {
            var d when SymbolEqualityComparer.Default.Equals(d, symbols.RequestPipelineBehavior) => BehaviorKind.Request,
            var d when SymbolEqualityComparer.Default.Equals(d, symbols.StreamPipelineBehavior) => BehaviorKind.Stream,
            var d when SymbolEqualityComparer.Default.Equals(d, symbols.StreamHookBehavior) => BehaviorKind.StreamHook,
            var d when SymbolEqualityComparer.Default.Equals(d, symbols.NotificationPipelineBehavior) => BehaviorKind.Notification,
            var d when SymbolEqualityComparer.Default.Equals(d, symbols.NotificationHandlerPipelineBehavior) => BehaviorKind.NotificationHandler,
            _ => null,
        };

    /// <summary>Whether the type implements any Keryx behavior interface (and so is held to the behavior rules).</summary>
    public static bool IsBehaviorCandidate(INamedTypeSymbol type, KeryxSymbols symbols) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct && type.AllInterfaces.Any(i => KindOf(i, symbols) is not null);

    /// <summary>
    ///     The behavior's shape, or <see langword="null" /> when it is not a behavior Keryx can use. Every rule it breaks is added to
    ///     <paramref name="issues" /> (KRX019, KRX020, KRX018 for a keyed constructor parameter).
    /// </summary>
    /// <param name="type">The behavior class: a definition, never a constructed type.</param>
    /// <param name="symbols">The compilation's Keryx symbols.</param>
    /// <param name="issues">Collects the rule violations; the host generator discards them.</param>
    public static BehaviorShape? Analyze(INamedTypeSymbol type, KeryxSymbols symbols, IssueCollector issues)
    {
        var interfaces = Interfaces(type, symbols);
        if (interfaces.IsEmpty || type.TypeKind is not ( TypeKind.Class or TypeKind.Struct ))
        {
            return null;
        }

        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var location = type.Locations.FirstOrDefault();
        var valid = true;

        void Shape(string reason)
        {
            issues.Add(Descriptors.InvalidBehaviorShape, location, name, reason);
            valid = false;
        }

        if (type.TypeKind == TypeKind.Struct)
        {
            // Open questions, behavior edge cases: the host registers a behavior as a scoped service and holds it in a pipeline context.
            Shape("is a struct; a behavior is a class, resolved once per scope and held by the pipeline context");
        }

        if (interfaces.Length > 1)
        {
            Shape(
                $"implements more than one Keryx behavior interface closing ({string.Join(", ", interfaces.Select(static i => i.Interface.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)))}); a behavior implements exactly one"
            );
        }

        if (type.IsAbstract && type.TypeKind == TypeKind.Class)
        {
            Shape("is abstract; generated code constructs it and calls it on its concrete type");
        }

        var (kind, @interface) = interfaces[0];
        if (interfaces.Length == 1 && ShapeProblem(type, @interface) is { } problem)
        {
            Shape(problem);
        }

        if (interfaces.Length == 1)
        {
            foreach (var member in @interface.GetMembers().OfType<IMethodSymbol>())
            {
                if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
                {
                    continue;
                }

                if (!implementation.ExplicitInterfaceImplementations.IsEmpty)
                {
                    Shape($"implements '{member.Name}' explicitly; generated code calls it on the concrete type, so implement it as a public method");
                }
                else if (implementation.IsVirtual || implementation.IsAbstract || implementation.IsOverride)
                {
                    var modifier = implementation.IsAbstract ? "abstract" : implementation.IsOverride ? "override" : "virtual";
                    Shape(
                        $"implements '{member.Name}' with a method that is '{modifier}'; generated code calls it on the concrete type, which must be a direct (non-virtual) call"
                    );
                }
            }
        }

        // An abstract class is already rejected (KRX019); its constructors are protected, so KRX020 would only repeat that.
        var constructors = type.InstanceConstructors.Where(static c => c.DeclaredAccessibility == Accessibility.Public).ToArray();
        if (type is { TypeKind: TypeKind.Class, IsAbstract: false } && constructors.Length != 1)
        {
            issues.Add(
                Descriptors.InvalidBehaviorConstructor,
                location,
                name,
                constructors.Length == 0
                    ? "has no public constructor; the host constructs each behavior through its one public constructor"
                    : $"has {constructors.Length} public constructors; the host constructs each behavior through exactly one"
            );
            valid = false;
        }
        else if (constructors.Length == 1)
        {
            foreach (var parameter in constructors[0].Parameters)
            {
                if (ConstructorParameterProblem(parameter) is { } parameterProblem)
                {
                    issues.Add(
                        Descriptors.InvalidBehaviorConstructor,
                        parameter.Locations.FirstOrDefault() ?? location,
                        name,
                        $"has a constructor parameter '{parameter.Name}' that {parameterProblem}"
                    );
                    valid = false;
                }

                foreach (var attribute in parameter.GetAttributes())
                {
                    if (!HandlerAnalysis.IsFromKeyedServices(attribute))
                    {
                        continue;
                    }

                    var attributeLocation = HandlerAnalysis.AttributeLocation(attribute) ?? parameter.Locations.FirstOrDefault() ?? location;
                    if (attribute.ConstructorArguments.IsEmpty)
                    {
                        issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses [FromKeyedServices] without a key; give the key as a compile-time constant");
                        valid = false;
                    }
                    else if (attribute.ConstructorArguments[0].Kind is TypedConstantKind.Array or TypedConstantKind.Error)
                    {
                        issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses a [FromKeyedServices] key that is not a compile-time constant");
                        valid = false;
                    }
                }
            }
        }

        return valid && constructors.Length == 1 ? new(type, kind, @interface, constructors[0]) : null;
    }

    /// <summary>
    ///     Why the class's type parameters do not match the interface closing (ADR 0001, Behavior shapes), or <see langword="null" />.
    /// </summary>
    private static string? ShapeProblem(INamedTypeSymbol type, INamedTypeSymbol @interface)
    {
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (containing.TypeParameters.Length > 0)
            {
                return "is nested in a generic type; a behavior is open with the interface's type parameters, or closed and non-generic";
            }
        }

        if (!type.IsGenericType)
        {
            return null;
        }

        var arguments = @interface.TypeArguments;
        if (type.TypeParameters.Length != arguments.Length
         || !arguments.Select((argument, i) => SymbolEqualityComparer.Default.Equals(argument, type.TypeParameters[i])).All(static same => same))
        {
            var interfaceName = @interface.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            return $"implements '{interfaceName}' with type parameters that are not the interface's, in order; a generic behavior declares exactly the interface's type parameters (as in 'X<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>'), or is closed and non-generic";
        }

        return null;
    }

    /// <summary>Why a behavior constructor parameter cannot be injected by a generated factory, or <see langword="null" />.</summary>
    private static string? ConstructorParameterProblem(IParameterSymbol parameter) =>
        parameter switch
        {
            { RefKind: not RefKind.None } => "is passed by reference; constructor parameters are injected by value",
            { IsParams: true } => "is 'params'; constructor parameters are injected one by one",
            { Type.TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer } => "is a pointer, which a service cannot be",
            { Type.IsRefLikeType: true } => "is a ref struct, which a service cannot be",
            _ => null,
        };
}
