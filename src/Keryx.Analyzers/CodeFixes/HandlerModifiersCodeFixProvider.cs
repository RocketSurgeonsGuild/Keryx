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
///     KRX002: makes a handler method <c>private static</c>. Offered for the modifier half of KRX002 only; a wrong return type needs a person.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandlerModifiersCodeFixProvider))]
[Shared]
public sealed class HandlerModifiersCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "Keryx.MakeHandlerPrivateStatic";

    private static readonly SyntaxKind[] RemovedKinds =
    [
        SyntaxKind.StaticKeyword,
        SyntaxKind.VirtualKeyword,
        SyntaxKind.SealedKeyword,
        SyntaxKind.AbstractKeyword,
        SyntaxKind.NewKeyword,
    ];

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.InvalidHandlerSignature.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(Descriptors.ReasonProperty, out var reason)
             || reason != Descriptors.ModifiersReason
             || root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Make the handler private static",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(method, MakePrivateStatic(method)))),
                    EquivalenceKey
                ),
                diagnostic
            );
        }
    }

    private static MethodDeclarationSyntax MakePrivateStatic(MethodDeclarationSyntax method)
    {
        var kept = method.Modifiers.Where(static m => !SyntaxEditing.IsAccessibility(m) && Array.IndexOf(RemovedKinds, m.Kind()) < 0);
        var modifiers = new[] { SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword) }.Concat(kept);
        return SyntaxEditing.WithNewModifiers(method, modifiers);
    }
}
