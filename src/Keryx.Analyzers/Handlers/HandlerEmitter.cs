using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Keryx.Analyzers.GeneratedSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     Writes the generated half of a handler class (ADR 0001, Handlers, Example) and the assembly manifest, built with
///     <see cref="SyntaxFactory" /> (<see cref="GeneratedSyntax" />).
/// </summary>
internal static class HandlerEmitter
{
    /// <summary>The manifest format this Keryx writes and reads (ADR 0001, Versioning).</summary>
    public const int ManifestFormatVersion = 1;

    /// <summary>The hint name of the assembly manifest. It does not end in <c>.Keryx.g.cs</c>, so no handler class can take it.</summary>
    public const string ManifestHintName = "KeryxManifest.g.cs";

    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";
    private const string Unit = "global::Keryx.Unit";
    private const string CancellationToken = "global::System.Threading.CancellationToken";

    /// <summary>The version in <c>[GeneratedCode("Keryx", version)]</c>.</summary>
    public static string Version => GeneratedSyntax.Version;

    /// <summary><c>[assembly: KeryxManifest(FormatVersion = n)]</c>, which marks an assembly whose handlers the host reads.</summary>
    public static SourceText Manifest()
    {
        var attribute = AttributeOf(
                            "global::Keryx.Internal.KeryxManifest",
                            AttributeArgument(Number(ManifestFormatVersion)).WithNameEquals(NameEquals(IdentifierName("FormatVersion")))
                        )
                       .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)));
        return Render(CompilationUnit().AddAttributeLists(attribute), pragma: null);
    }

    /// <summary>
    ///     The source of the handler class's generated half, or <see langword="null" /> when nothing may be generated for it in this
    ///     compilation.
    /// </summary>
    public static SourceText? Emit(HandlerModel model, bool isHost)
    {
        // In a host, generated code outside the class must reach the class and every type its members mention (OQ-24, OQ-29).
        if (isHost ? model.SignatureAccess == AccessLevel.Restricted || model.ClassAccess == AccessLevel.Restricted : !model.ValidOutsideHost)
        {
            return null;
        }

        // Member accessibility follows the signature alone: a signature that mentions internal types gets internal members, which compile
        // in a class of any accessibility (a public or protected internal class's public members could not take an internal type). Such a
        // handler stays out of the manifest with the host's other internal handlers.
        var memberAccess = model.SignatureAccess == AccessLevel.Public ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword;
        var inManifest = model is { ClassAccess: AccessLevel.Public, SignatureAccess: AccessLevel.Public };

        MemberDeclarationSyntax declaration = Declaration(model.Types[^1]).WithMembers(List(Members(model, memberAccess)));
        if (inManifest)
        {
            declaration = WithHandlerAttribute((TypeDeclarationSyntax)declaration, model);
        }

        for (var i = model.Types.Count - 2; i >= 0; i--)
        {
            declaration = Declaration(model.Types[i]).AddMembers(declaration);
        }

        var unit = CompilationUnit()
           .AddMembers(model.Namespace is { } ns ? NamespaceDeclaration(Qualified(ns)).AddMembers(declaration) : declaration);
        return Render(unit);
    }

    /// <summary>The partial declaration of one type of the chain, with the modifiers and keyword the user declared it with.</summary>
    private static TypeDeclarationSyntax Declaration(TypeDeclarationModel type)
    {
        var modifiers = type.Modifiers.Length > 0 ? type.Modifiers + " " : "";
        return (TypeDeclarationSyntax)ParseMemberDeclaration($"{modifiers}partial {type.Keyword} {type.Name} {{ }}")!;
    }

    /// <summary>
    ///     The handler class with its <c>[KeryxHandler]</c> manifest entry. A <c>typeof</c> operand cannot carry a top-level nullable
    ///     annotation, and dropping a nested one (<c>Tag&lt;Ping, string&gt;</c> for a request that responds with <c>string?</c>) breaks a
    ///     nullable constraint (CS8631), so with behaviors the attribute is written with the nullable context disabled. Attribute metadata
    ///     keeps no annotations anyway.
    /// </summary>
    private static TypeDeclarationSyntax WithHandlerAttribute(TypeDeclarationSyntax declaration, HandlerModel model)
    {
        var arguments = new List<AttributeArgumentSyntax>
        {
            AttributeArgument(TypeOf(TypeOfOperand(model.MessageType))),
            AttributeArgument(Qualified("global::Keryx.Internal.HandlerKind." + model.Kind)),
        };
        if (model.Behaviors.Count > 0)
        {
            arguments.Add(
                AttributeArgument(
                        ArrayCreationExpression(
                            ArrayType(TypeName("global::System.Type"), SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
                            InitializerExpression(SyntaxKind.ArrayInitializerExpression, SeparatedList<ExpressionSyntax>(model.Behaviors.Select(static b => TypeOf(b))))
                        )
                    )
                   .WithNameEquals(NameEquals(IdentifierName("Behaviors")))
            );
        }

        if (model.Order != 0)
        {
            arguments.Add(AttributeArgument(Integer(model.Order)).WithNameEquals(NameEquals(IdentifierName("Order"))));
        }

        if (model.StaticInvoker)
        {
            arguments.Add(AttributeArgument(BooleanLiteral(true)).WithNameEquals(NameEquals(IdentifierName("StaticInvoker"))));
        }

        var attributed = declaration.AddAttributeLists(AttributeOf("global::Keryx.Internal.KeryxHandler", [.. arguments]));
        if (model.Behaviors.Count == 0)
        {
            return attributed;
        }

        // Not "restore" after the attribute: that returns to the project default, which is disabled for generated code.
        var afterAttributes = attributed.AttributeLists[^1].GetLastToken().GetNextToken();
        attributed = attributed.ReplaceToken(afterAttributes, afterAttributes.WithLeadingTrivia(afterAttributes.LeadingTrivia.Insert(0, NullableContext(enable: true))));
        return attributed.WithLeadingTrivia(attributed.GetLeadingTrivia().Insert(0, NullableContext(enable: false)));
    }

    private static IEnumerable<MemberDeclarationSyntax> Members(HandlerModel model, SyntaxKind memberAccess)
    {
        var className = model.Types[^1].Name;
        var injected = model.Parameters.Where(static p => p.Role == ParameterRole.Injected).ToArray();

        foreach (var parameter in injected)
        {
            yield return Field(Modifiers(SyntaxKind.PrivateKeyword, SyntaxKind.ReadOnlyKeyword), TypeName(parameter.Type), FieldName(parameter))
               .AddAttributeLists(GeneratedCodeAttribute());
        }

        if (injected.Length > 0)
        {
            yield return ConstructorDeclaration(Identifier(className))
                        .WithModifiers(Modifiers(memberAccess))
                        .WithParameterList(Params([.. injected.Select(ConstructorParameter)]))
                        .WithBody(Block(injected.Select(static p => Assign(Id(FieldName(p)), Id(CSharpText.Identifier(p.Name))))))
                        .AddAttributeLists(GeneratedCodeAttribute())
                        .Documented("<summary>Creates the handler with the services its handler method injects.</summary>");
        }

        var messageName = model.Kind == HandlerKind.Notification ? "notification" : "request";
        var call = Invoke(
            Id(model.MethodName),
            [
                .. model.Parameters.Select(p => p.Role switch
                    {
                        ParameterRole.Message => Id(messageName),
                        ParameterRole.CancellationToken => Id("cancellationToken"),
                        ParameterRole.Injected or ParameterRole.Invalid => Id(FieldName(p)),
                        _ => Id(FieldName(p)),
                    }
                ),
            ]
        );
        ExpressionSyntax result = model.NullabilityDiffers ? NotNull(call) : call;

        var invoker = Method(
                          model.StaticInvoker ? Modifiers(memberAccess, SyntaxKind.StaticKeyword) : Modifiers(memberAccess),
                          InvokerReturnType(model),
                          HandlerAnalysis.InvokerName,
                          Params(Param(TypeName(model.MessageType), messageName), Param(TypeName(CancellationToken), "cancellationToken"))
                      )
                     .AddAttributeLists(GeneratedCodeAttribute());

        invoker = model.ReturnShape switch
        {
            ReturnShape.Void => invoker.Statements(
                [
                    ExpressionStatement(call),
                    Return(New(TypeName($"{ValueTask}<{Unit}>"), Member(Qualified(Unit), "Value"))),
                ]
            ),
            ReturnShape.ValueTaskOfT or ReturnShape.AsyncEnumerable => invoker.Expression(result),
            ReturnShape.TaskOfT or ReturnShape.Synchronous => invoker.Expression(New(TypeName($"{ValueTask}<{model.ResponseType}>"), result)),
            ReturnShape.ValueTask or ReturnShape.Task when model.Kind == HandlerKind.Request => invoker.Expression(
                Invoke(Member(Qualified("global::Keryx.Internal.UnitAdapter"), "ToUnit"), call)
            ),
            ReturnShape.ValueTask => invoker.Expression(call),
            _ => invoker.Expression(New(TypeName(ValueTask), call)),
        };

        yield return invoker.Documented("<summary>Runs the handler only. No behaviors run.</summary>");
    }

    private static TypeSyntax InvokerReturnType(HandlerModel model) =>
        TypeName(
            model.Kind switch
            {
                HandlerKind.Notification => ValueTask,
                HandlerKind.Stream => $"global::System.Collections.Generic.IAsyncEnumerable<{model.ResponseType}>",
                HandlerKind.Request => $"{ValueTask}<{model.ResponseType}>",
                _ => $"{ValueTask}<{model.ResponseType}>",
            }
        );

    private static ParameterSyntax ConstructorParameter(ParameterModel parameter)
    {
        var syntax = Param(
            TypeName(parameter.Type),
            CSharpText.Identifier(parameter.Name),
            parameter.DefaultValue is { } defaultValue ? Code(defaultValue) : null
        );
        return parameter.ServiceKey is { } key
            ? syntax.AddAttributeLists(AttributeOf("global::Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute", AttributeArgument(Code(key))))
            : syntax;
    }

    private static string FieldName(ParameterModel parameter) => HandlerAnalysis.ReservedFieldPrefix + parameter.Name;

    /// <summary>A <c>typeof</c> operand cannot carry a nullable reference annotation.</summary>
    private static string TypeOfOperand(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type[..^1] : type;
}
