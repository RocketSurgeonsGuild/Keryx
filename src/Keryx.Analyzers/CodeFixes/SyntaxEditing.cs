using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.CodeFixes;

/// <summary>
///     Modifier edits the Keryx code fixes share. Code-fix code only: it is never reached from a generator or analyzer (see the RS1038 note in
///     <c>Keryx.Analyzers.csproj</c>).
/// </summary>
internal static class SyntaxEditing
{
    private static readonly SyntaxKind[] AccessibilityKinds =
    [
        SyntaxKind.PublicKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.ProtectedKeyword,
        SyntaxKind.InternalKeyword,
        SyntaxKind.FileKeyword,
    ];

    public static bool IsAccessibility(SyntaxToken token) => Array.IndexOf(AccessibilityKinds, token.Kind()) >= 0;

    /// <summary>
    ///     Replaces a member's modifiers, keeping the leading trivia where it was (on the first modifier, or on the token that follows the
    ///     modifiers when there were none).
    /// </summary>
    public static TMember WithNewModifiers<TMember>(TMember member, IEnumerable<SyntaxToken> modifiers)
        where TMember : MemberDeclarationSyntax
    {
        var list = TokenList(modifiers.Select(static m => m.WithoutTrivia().WithTrailingTrivia(Space)));
        var old = member.Modifiers;
        if (old.Count > 0)
        {
            if (list.Count > 0)
            {
                list = list.Replace(list[0], list[0].WithLeadingTrivia(old[0].LeadingTrivia));
            }

            return (TMember)member.WithModifiers(list).WithAdditionalAnnotations(Formatter.Annotation);
        }

        if (list.Count == 0)
        {
            return member;
        }

        // No modifiers before: the indentation and comments sit on the token after them (the type keyword or the return type).
        var next = member.AttributeLists.Count > 0 ? member.AttributeLists.Last().GetLastToken().GetNextToken() : member.GetFirstToken();
        var updated = member.ReplaceToken(next, next.WithLeadingTrivia(TriviaList()));
        list = list.Replace(list[0], list[0].WithLeadingTrivia(next.LeadingTrivia));
        return (TMember)updated.WithModifiers(list).WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    ///     Puts <c>partial</c> last, right before the type keyword, as C# requires.
    /// </summary>
    public static IEnumerable<SyntaxToken> AddPartial(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.PartialKeyword) ? modifiers : modifiers.Append(Token(SyntaxKind.PartialKeyword));

    /// <summary>
    ///     Replaces the accessibility modifiers with <paramref name="accessibility" />, placed first.
    /// </summary>
    public static IEnumerable<SyntaxToken> WithAccessibility(SyntaxTokenList modifiers, params SyntaxKind[] accessibility) =>
        accessibility.Select(Token).Concat(modifiers.Where(static m => !IsAccessibility(m)));
}
