using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     Turns a handler method into a <see cref="HandlerModel" />, or nothing when the method is not a handler candidate or breaks a rule that
///     stops Keryx from generating its partial. The analyzers report those rules; this never does.
/// </summary>
internal static class HandlerModelFactory
{
    /// <summary>
    ///     The syntax predicate of the handler pipeline: a non-generic, non-explicit method named <c>Handle</c> or <c>HandleAsync</c> in a type.
    /// </summary>
    public static bool IsCandidateSyntax(SyntaxNode node) =>
        node is MethodDeclarationSyntax
        {
            Identifier.ValueText: HandlerAnalysis.HandleName or HandlerAnalysis.HandleAsyncName,
            TypeParameterList: null,
            ExplicitInterfaceSpecifier: null,
            Parent: TypeDeclarationSyntax,
        };

    public static HandlerModel? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken) =>
        context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is IMethodSymbol method
     && KeryxSymbols.Create(context.SemanticModel.Compilation) is { } symbols
            ? Create(method, symbols)
            : null;

    public static HandlerModel? Create(IMethodSymbol method, KeryxSymbols symbols)
    {
        if (!HandlerAnalysis.IsCandidate(method, symbols))
        {
            return null;
        }

        var issues = new IssueCollector();
        var signature = HandlerAnalysis.AnalyzeForGeneration(method, symbols, issues);
        if (issues.HasErrors || signature.MessageParameter is not { } messageParameter)
        {
            return null;
        }

        var handlerClass = method.ContainingType;
        var signatureAccess = TypeAccessibility.Min(TypeAccessibility.Of(messageParameter.Type), TypeAccessibility.Of(signature.ResponseType));
        var parameters = ImmutableArray.CreateBuilder<ParameterModel>(signature.Parameters.Count);
        foreach (var (parameter, role) in signature.Parameters)
        {
            if (role == ParameterRole.Injected)
            {
                signatureAccess = TypeAccessibility.Min(signatureAccess, TypeAccessibility.Of(parameter.Type));
            }

            parameters.Add(
                new(
                    parameter.Name,
                    CSharpText.Type(parameter.Type),
                    role,
                    role == ParameterRole.Injected ? ServiceKey(parameter) : null,
                    role == ParameterRole.Injected ? CSharpText.DefaultValue(parameter) : null
                )
            );
        }

        var types = ImmutableArray.CreateBuilder<TypeDeclarationModel>();
        for (var current = handlerClass; current is not null; current = current.ContainingType)
        {
            types.Insert(0, Declaration(current));
        }

        var classAccess = TypeAccessibility.OfDeclaration(handlerClass);
        return new(
            CSharpText.HintName(handlerClass),
            handlerClass.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            types.ToImmutable(),
            CSharpText.Type(handlerClass),
            signature.Kind,
            CSharpText.Type(messageParameter.Type),
            signature.ResponseType is { } response ? CSharpText.Type(response) : null,
            signature.ReturnShape,
            signature.NullabilityDiffers,
            method.Name,
            parameters.MoveToImmutable(),
            Behaviors(method, symbols),
            signature.Kind == HandlerKind.Notification ? Order(method, symbols) : 0,
            !issues.HasOutsideHostErrors && classAccess == AccessLevel.Public && signatureAccess == AccessLevel.Public,
            classAccess,
            signatureAccess
        );
    }

    private static TypeDeclarationModel Declaration(INamedTypeSymbol type)
    {
        var modifiers = new List<string>(3);
        var accessibility = CSharpText.Accessibility(type.DeclaredAccessibility);
        if (accessibility.Length > 0)
        {
            modifiers.Add(accessibility);
        }

        if (type.IsStatic)
        {
            modifiers.Add("static");
        }

        if (type is { TypeKind: TypeKind.Struct, IsReadOnly: true })
        {
            modifiers.Add("readonly");
        }

        if (type.IsRefLikeType)
        {
            modifiers.Add("ref");
        }

        var keyword = type switch
        {
            { TypeKind: TypeKind.Struct, IsRecord: true } => "record struct",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            { IsRecord: true } => "record",
            _ => "class",
        };

        return new(string.Join(" ", modifiers), keyword, CSharpText.Identifier(type.Name));
    }

    /// <summary>The <c>[FromKeyedServices(key)]</c> key of a parameter as a C# expression, or <see langword="null" />.</summary>
    internal static string? ServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (HandlerAnalysis.IsFromKeyedServices(attribute) && attribute.ConstructorArguments is [var key])
            {
                return CSharpText.Constant(key);
            }
        }

        return null;
    }

    private static ImmutableArray<string> Behaviors(IMethodSymbol method, KeryxSymbols symbols)
    {
        var behaviors = ImmutableArray.CreateBuilder<string>();
        foreach (var attribute in method.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.BehaviorAttribute) && attribute.ConstructorArguments is [{ Value: ITypeSymbol behavior }])
            {
                behaviors.Add(CSharpText.TypeOf(behavior));
            }
        }

        return behaviors.ToImmutable();
    }

    private static int Order(IMethodSymbol method, KeryxSymbols symbols)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (KeryxSymbols.IsAttribute(attribute, symbols.HandlerOrderAttribute) && attribute.ConstructorArguments is [{ Value: int order }])
            {
                return order;
            }
        }

        return 0;
    }
}
