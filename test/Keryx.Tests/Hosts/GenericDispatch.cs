using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keryx.Tests.Hosts;

/// <summary>
///     The shape of a generated generic entry point, read from its syntax: what it looks the runtime type up with (the index method a
///     <c>switch</c> statement or expression is governed by), how many type comparisons it makes on the runtime type (<c>requestType ==
///     typeof(X)</c> or <c>notificationType == typeof(X)</c>, in an <c>if</c> or a <c>when</c> guard alike), and the type tables
///     (<c>FrozenDictionary</c> fields) of the mediator.
/// </summary>
internal sealed record GenericDispatch(string? IndexLookup, int TypeComparisons, string Tables)
{
    /// <summary>Reads the generic method <paramref name="methodName" /> (one type parameter) of the mediator source.</summary>
    public static GenericDispatch Of(string mediatorSource, string methodName)
    {
        var root = CSharpSyntaxTree.ParseText(mediatorSource).GetRoot();
        var method = root.DescendantNodes()
                         .OfType<MethodDeclarationSyntax>()
                         .Single(m => m.Identifier.Text == methodName && m.TypeParameterList is { Parameters.Count: 1 });
        var lookup = method.DescendantNodes()
                           .OfType<InvocationExpressionSyntax>()
                           .Where(static i => i.Parent is SwitchStatementSyntax or SwitchExpressionSyntax)
                           .Select(static i => i.Expression.ToString())
                           .SingleOrDefault();
        var comparisons = method.DescendantNodes()
                                .OfType<BinaryExpressionSyntax>()
                                .Count(static b => b.IsKind(SyntaxKind.EqualsExpression)
                                                && b.Right is TypeOfExpressionSyntax
                                                && b.Left is IdentifierNameSyntax { Identifier.Text: "requestType" or "notificationType" });
        var tables = root.DescendantNodes()
                         .OfType<FieldDeclarationSyntax>()
                         .Where(static f => f.Declaration.Type.ToString().Contains("FrozenDictionary", StringComparison.Ordinal))
                         .SelectMany(static f => f.Declaration.Variables.Select(static v => v.Identifier.Text));
        return new(lookup, comparisons, string.Join(", ", tables));
    }
}
