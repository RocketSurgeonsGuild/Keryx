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
        return Render(CompilationUnitOf("global::Keryx.Internal.KeryxManifest", Named("FormatVersion", Number(ManifestFormatVersion))), pragma: null);
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

        // The depth of each declaration of the chain: the outermost one is in the namespace, if any.
        var outer = model.Namespace is null ? 0 : 1;
        var innermost = outer + model.Types.Count - 1;
        MemberDeclarationSyntax declaration = Declaration(model.Types[^1]).WithBody(innermost, Members(model, memberAccess, innermost + 1));
        if (inManifest)
        {
            declaration = WithHandlerAttribute((TypeDeclarationSyntax)declaration, model);
        }

        for (var i = model.Types.Count - 2; i >= 0; i--)
        {
            declaration = Declaration(model.Types[i]).WithBody(outer + i, [declaration]);
        }

        return Render(CompilationUnitOf(model.Namespace is { } ns ? Namespace(ns, declaration) : declaration));
    }

    /// <summary>The partial declaration of one type of the chain, with the modifiers and keyword the user declared it with.</summary>
    private static TypeDeclarationSyntax Declaration(TypeDeclarationModel type)
    {
        // Each keyword of the text keeps the one space after it; the name has none, so the body's braces go on their own lines.
        var modifiers = type.Modifiers.Length > 0 ? type.Modifiers + " " : "";
        return (TypeDeclarationSyntax)ParseMemberDeclaration($"{modifiers}partial {type.Keyword} {type.Name}{{}}")!;
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
            arguments.Add(Named("Behaviors", NewArray(TypeName("global::System.Type"), model.Behaviors.Select(static b => (ExpressionSyntax)TypeOf(b)))));
        }

        if (model.Order != 0)
        {
            arguments.Add(Named("Order", Integer(model.Order)));
        }

        if (model.StaticInvoker)
        {
            arguments.Add(Named("StaticInvoker", BooleanLiteral(true)));
        }

        var attributed = declaration.AddAttributeLists(AttributeOf("global::Keryx.Internal.KeryxHandler", [.. arguments]));
        return model.Behaviors.Count == 0 ? attributed : attributed.WithNullableObliviousAttributes();
    }

    /// <summary>The members of the handler class's generated half, at <paramref name="depth" />.</summary>
    private static IEnumerable<MemberDeclarationSyntax> Members(HandlerModel model, SyntaxKind memberAccess, int depth)
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
            yield return Constructor(
                             depth,
                             Modifiers(memberAccess),
                             className,
                             [.. injected.Select(ConstructorParameter)],
                             injected.Select(static p => Assign(Id(FieldName(p)), Id(CSharpText.Identifier(p.Name))))
                         )
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
                depth,
                [
                    Do(call),
                    Return(New(TypeName($"{ValueTask}<{Unit}>"), Member(Qualified(Unit), "Value"))),
                ]
            ),
            ReturnShape.ValueTaskOfT or ReturnShape.AsyncEnumerable => invoker.Expression(depth, result),
            ReturnShape.TaskOfT or ReturnShape.Synchronous => invoker.Expression(depth, New(TypeName($"{ValueTask}<{model.ResponseType}>"), result)),
            ReturnShape.ValueTask or ReturnShape.Task when model.Kind == HandlerKind.Request => invoker.Expression(
                depth,
                Invoke(Member(Qualified("global::Keryx.Internal.UnitAdapter"), "ToUnit"), call)
            ),
            ReturnShape.ValueTask => invoker.Expression(depth, call),
            _ => invoker.Expression(depth, New(TypeName(ValueTask), call)),
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
            ? syntax.WithAttribute(AttributeOf("global::Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute", AttributeArgument(Code(key))))
            : syntax;
    }

    private static string FieldName(ParameterModel parameter) => HandlerAnalysis.ReservedFieldPrefix + parameter.Name;

    /// <summary>A <c>typeof</c> operand cannot carry a nullable reference annotation.</summary>
    private static string TypeOfOperand(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type[..^1] : type;
}
