using System.Collections.Immutable;
using System.Composition;
using Keryx.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keryx.Analyzers.CodeFixes;

/// <summary>
///     KRX004: adds the missing <c>partial</c> and, outside a host, <c>public</c> modifiers to a handler class or a type that contains one.
///     KRX014: removes <c>static</c> from a handler class whose handler injects services.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandlerClassCodeFixProvider))]
[Shared]
public sealed class HandlerClassCodeFixProvider : CodeFixProvider
{
    private const string AddPartialKey = "Keryx.AddPartial";
    private const string MakePublicKey = "Keryx.MakePublic";
    private const string RemoveStaticKey = "Keryx.RemoveStatic";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [Descriptors.InvalidHandlerClass.Id, Descriptors.StaticHandlerClassWithDependencies.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration
             || semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type)
            {
                continue;
            }

            var solution = context.Document.Project.Solution;
            if (diagnostic.Id == Descriptors.StaticHandlerClassWithDependencies.Id)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Remove 'static' from '{type.Name}'",
                        cancellationToken => EditDeclarationsAsync(
                            solution,
                            type,
                            static (d, _) => SyntaxEditing.WithNewModifiers(d, d.Modifiers.Where(static m => !m.IsKind(SyntaxKind.StaticKeyword))),
                            cancellationToken
                        ),
                        RemoveStaticKey
                    ),
                    diagnostic
                );
            }
            else if (diagnostic.Properties.ContainsKey(Descriptors.AddPartialProperty))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Make '{type.Name}' partial",
                        cancellationToken => EditDeclarationsAsync(
                            solution,
                            type,
                            static (d, _) => SyntaxEditing.WithNewModifiers(d, SyntaxEditing.AddPartial(d.Modifiers)),
                            cancellationToken
                        ),
                        AddPartialKey
                    ),
                    diagnostic
                );
            }
            else if (diagnostic.Properties.ContainsKey(Descriptors.MakePublicProperty))
            {
                // A partial type states its accessibility once: public on the first declaration, none on the others.
                var first = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken);
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Make '{type.Name}' public",
                        cancellationToken => EditDeclarationsAsync(
                            solution,
                            type,
                            (d, _) => SyntaxEditing.WithNewModifiers(
                                d,
                                first is not null && d.SyntaxTree.FilePath == first.SyntaxTree.FilePath && d.SpanStart == first.SpanStart
                                    ? SyntaxEditing.WithAccessibility(d.Modifiers, SyntaxKind.PublicKeyword)
                                    : SyntaxEditing.WithAccessibility(d.Modifiers)
                            ),
                            cancellationToken
                        ),
                        MakePublicKey
                    ),
                    diagnostic
                );
            }
        }
    }

    private static async Task<Solution> EditDeclarationsAsync(
        Solution solution,
        INamedTypeSymbol type,
        Func<TypeDeclarationSyntax, Document, TypeDeclarationSyntax> edit,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in type.DeclaringSyntaxReferences.GroupBy(static r => r.SyntaxTree))
        {
            if (solution.GetDocument(group.Key) is not { } document
             || await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
            {
                continue;
            }

            var declarations = new List<TypeDeclarationSyntax>();
            foreach (var reference in group)
            {
                if (await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) is TypeDeclarationSyntax declaration)
                {
                    declarations.Add(declaration);
                }
            }

            solution = solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNodes(declarations, (original, _) => edit(original, document)));
        }

        return solution;
    }
}
