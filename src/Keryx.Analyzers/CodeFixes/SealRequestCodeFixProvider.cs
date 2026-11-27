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
///     KRX024: makes a request or stream request class or record <c>sealed</c>. The modifier goes on the declaration the diagnostic is
///     reported on (for a partial type, one part is enough), after its accessibility and <c>new</c> modifiers; every other modifier, such as
///     <c>partial</c> or <c>unsafe</c>, is kept in place. It is not offered when a part of the type declares a <c>virtual</c> or
///     <c>protected</c> member that does not override one (including a record's own <c>PrintMembers</c> or copy constructor): sealing would
///     not compile (CS0549, CS8879) or would warn (CS0628), and which way to change the member is the author's choice.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SealRequestCodeFixProvider))]
[Shared]
public sealed class SealRequestCodeFixProvider : CodeFixProvider
{
    private const string MakeSealedKey = "Keryx.MakeSealed";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.UnsealedRequest.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            var declaration = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (declaration is not ( ClassDeclarationSyntax or RecordDeclarationSyntax )
             || declaration.Modifiers.Any(SyntaxKind.SealedKeyword)
             || semanticModel?.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type
             || DeclaresMemberSealingBreaks(type))
            {
                continue;
            }

            var document = context.Document;
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Make '{declaration.Identifier.ValueText}' sealed",
                    async cancellationToken =>
                    {
                        var current = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                        return current is null
                            ? document
                            : document.WithSyntaxRoot(current.ReplaceNode(declaration, SyntaxEditing.WithNewModifiers(declaration, AddSealed(declaration.Modifiers))));
                    },
                    MakeSealedKey
                ),
                diagnostic
            );
        }
    }

    /// <summary>
    ///     Whether the type declares, in source, a member that a sealed type cannot have as written: a <c>virtual</c> member, or a
    ///     <c>protected</c>, <c>protected internal</c> or <c>private protected</c> one, that does not override a base member. Members the
    ///     compiler synthesizes (a record's <c>PrintMembers</c>, <c>EqualityContract</c> and copy constructor) follow the type and do not count.
    /// </summary>
    private static bool DeclaresMemberSealingBreaks(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member.IsOverride)
            {
                continue;
            }

            if (member.IsVirtual
             || member.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Inserts <c>sealed</c> after the accessibility and <c>new</c> modifiers, so <c>public partial record</c> becomes
    ///     <c>public sealed partial record</c> and a declaration without modifiers starts with <c>sealed</c>.
    /// </summary>
    private static IEnumerable<SyntaxToken> AddSealed(SyntaxTokenList modifiers)
    {
        // No break: the position is after the LAST matching modifier, since an accessibility can be two tokens (protected internal) and
        // new can come before or after it.
        var position = 0;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (SyntaxEditing.IsAccessibility(modifiers[i]) || modifiers[i].IsKind(SyntaxKind.NewKeyword))
            {
                position = i + 1;
            }
        }

        return modifiers.Take(position).Append(SyntaxFactory.Token(SyntaxKind.SealedKeyword)).Concat(modifiers.Skip(position));
    }
}
