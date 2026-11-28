using System.Collections.Immutable;
using System.Composition;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Keryx.Analyzers.CodeFixes;

/// <summary>
///     KRX001 (ADR 0001, Code fixes): add a handler stub, next to a nested request, or in a new <c>{RequestName}Handler</c> class and file for
///     a top-level one.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingHandlerCodeFixProvider))]
[Shared]
public sealed class MissingHandlerCodeFixProvider : CodeFixProvider
{
    private const string AddStubKey = "Keryx.AddHandlerStub";

    private static readonly SymbolDisplayFormat QualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.MissingHandler.Id];

    /// <summary>
    ///     No fix-all: the action may add a document per request.
    /// </summary>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null || KeryxSymbols.Create(semanticModel.Compilation) is not { } symbols)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<BaseTypeDeclarationSyntax>() is not { } declaration
             || semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } request)
            {
                continue;
            }

            var stub = Stub(request, symbols);
            if (stub is not null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Add a handler for '{request.Name}'",
                        cancellationToken => request.ContainingType is null
                            ? AddHandlerClassAsync(context.Document, declaration, request, stub, cancellationToken)
                            : AddNestedStubAsync(context.Document, root, declaration, stub, cancellationToken),
                        AddStubKey
                    ),
                    diagnostic
                );
            }
        }
    }

    private static MethodDeclarationSyntax? Stub(INamedTypeSymbol request, KeryxSymbols symbols)
    {
        var messageType = MessageType.Classify(request, symbols);
        string returnType;
        if (messageType.IsStream && messageType.ItemType is { } item)
        {
            returnType = $"global::System.Collections.Generic.IAsyncEnumerable<{item.ToDisplayString(QualifiedFormat)}>";
        }
        else if (messageType.IsRequest && messageType.ResponseType is { } response)
        {
            returnType = symbols.IsUnit(response)
                ? "global::System.Threading.Tasks.ValueTask"
                : $"global::System.Threading.Tasks.ValueTask<{response.ToDisplayString(QualifiedFormat)}>";
        }
        else
        {
            return null;
        }

        var method = (MethodDeclarationSyntax)ParseMemberDeclaration(
            $"private static {returnType} Handle({request.ToDisplayString(QualifiedFormat)} request, global::System.Threading.CancellationToken ct) => throw new global::System.NotImplementedException();"
        )!;
        return method.WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation, Simplifier.AddImportsAnnotation);
    }

    private static async Task<Solution> AddNestedStubAsync(
        Document document,
        SyntaxNode root,
        BaseTypeDeclarationSyntax request,
        MethodDeclarationSyntax stub,
        CancellationToken cancellationToken
    )
    {
        var updated = document.WithSyntaxRoot(root.InsertNodesAfter(request, [stub.WithLeadingTrivia(ElasticCarriageReturnLineFeed)]));
        return ( await AddImportsAsync(updated, cancellationToken).ConfigureAwait(false) ).Project.Solution;
    }

    /// <summary>
    ///     Adds <c>using</c> directives for the fully qualified names in the stub, so they simplify to plain type names.
    /// </summary>
    private static async Task<Document> AddImportsAsync(Document document, CancellationToken cancellationToken)
    {
        document = await ImportAdder.AddImportsAsync(document, Simplifier.AddImportsAnnotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        document = await Simplifier.ReduceAsync(document, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Solution> AddHandlerClassAsync(
        Document document,
        BaseTypeDeclarationSyntax requestDeclaration,
        INamedTypeSymbol request,
        MethodDeclarationSyntax stub,
        CancellationToken cancellationToken
    )
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var ns = request.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() : null;
        var name = UniqueName(request.Name + "Handler", ns, compilation, document.Project);

        var handlerClass = ClassDeclaration(name)
                          .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword)))
                          .WithMembers(List<MemberDeclarationSyntax>([stub]));

        MemberDeclarationSyntax member = handlerClass;
        if (ns is not null)
        {
            member = requestDeclaration.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().Any()
                ? FileScopedNamespaceDeclaration(ParseName(ns)).AddMembers(handlerClass)
                : NamespaceDeclaration(ParseName(ns)).AddMembers(handlerClass);
        }

        var unit = CompilationUnit()
                  .AddMembers(member)
                  .NormalizeWhitespace(eol: "\n")
                  .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation, Simplifier.AddImportsAnnotation);
        var added = document.Project.AddDocument(name + ".cs", unit, document.Folders);
        return ( await AddImportsAsync(added, cancellationToken).ConfigureAwait(false) ).Project.Solution;
    }

    private static string UniqueName(string baseName, string? ns, Compilation? compilation, Project project)
    {
        var documentNames = new HashSet<string>(project.Documents.Select(static d => d.Name), StringComparer.Ordinal);
        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? baseName : baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var metadataName = ns is null ? name : ns + "." + name;
            if (compilation?.GetTypeByMetadataName(metadataName) is null && !documentNames.Contains(name + ".cs"))
            {
                return name;
            }
        }
    }
}
