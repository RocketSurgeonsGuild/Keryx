using System.Collections.Immutable;
using System.Text;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     Projects the host's compilation, build properties and syntax into the host generator's equatable models.
/// </summary>
internal static class HostModelFactory
{
    /// <summary>The runtime assembly KRX031 falls back to when the target framework properties are absent (ADR 0001, Which projects may target what).</summary>
    private const string SystemRuntime = "System.Runtime";

    /// <summary>The oldest framework a host may target (KRX031).</summary>
    private static readonly Version MinimumFramework = new(10, 0);

    public static HostCompilationInfo CreateCompilationInfo(Compilation compilation)
    {
        if (KeryxSymbols.Create(compilation) is not { IsHost: true } symbols)
        {
            return new(false, compilation.AssemblyName ?? "", null, null, null);
        }

        var hostAttribute = compilation.Assembly.GetAttributes().FirstOrDefault(a => KeryxSymbols.IsAttribute(a, symbols.KeryxHostAttribute));
        var runtime = compilation.ReferencedAssemblyNames.FirstOrDefault(static identity => identity.Name == SystemRuntime)
         ?? compilation.ObjectType.ContainingAssembly?.Identity;
        return new(
            true,
            compilation.AssemblyName ?? "",
            hostAttribute is null ? null : LocationInfo.From(HandlerAnalysis.AttributeLocation(hostAttribute)),
            runtime?.Name,
            runtime?.Version
        );
    }

    public static HostBuildProperties ReadBuildProperties(AnalyzerConfigOptionsProvider options) =>
        new(
            Property(options, "RootNamespace"),
            Property(options, "TargetFrameworkIdentifier"),
            Property(options, "TargetFrameworkVersion")
        );

    public static HostSettings CreateSettings(HostCompilationInfo compilation, HostBuildProperties properties, bool languageVersionSupported) =>
        compilation.IsHost && languageVersionSupported
            ? new(
                true,
                Namespace(properties.RootNamespace, compilation.AssemblyName),
                compilation.AssemblyName,
                compilation.HostAttribute,
                UnsupportedTargetFramework(properties, compilation)
            )
            : HostSettings.Disabled;

    /// <summary>
    ///     The namespace of the generated host types: <c>RootNamespace</c>, falling back to the assembly name, sanitised into a valid namespace
    ///     (ADR 0001, Opting in): characters that are not valid in an identifier become <c>_</c>, a segment that starts with a digit gets a
    ///     <c>_</c> prefix, an empty segment becomes <c>_</c> and a keyword is escaped with <c>@</c>.
    /// </summary>
    public static string Namespace(string? rootNamespace, string assemblyName)
    {
        var name = string.IsNullOrWhiteSpace(rootNamespace) ? assemblyName : rootNamespace!.Trim();
        var segments = name.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Segment(segments[i]);
        }

        return string.Join(".", segments);

        static string Segment(string segment)
        {
            var builder = new StringBuilder(segment.Length + 1);
            foreach (var character in segment)
            {
                builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
            }

            if (builder.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(builder[0]))
            {
                builder.Insert(0, '_');
            }

            var identifier = builder.ToString();
            return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
        }
    }

    /// <summary>
    ///     The framework KRX031 reports, or <see langword="null" /> when the host targets <c>net10.0</c> or newer. Reads
    ///     <c>TargetFrameworkIdentifier</c>/<c>TargetFrameworkVersion</c>; when they are absent (for example in a test harness), the version
    ///     of the referenced <c>System.Runtime</c> (or, without one, of the core library: <c>System.Private.CoreLib</c> passes like
    ///     <c>System.Runtime</c>) decides.
    /// </summary>
    public static string? UnsupportedTargetFramework(HostBuildProperties properties, HostCompilationInfo compilation)
    {
        return properties is { TargetFrameworkIdentifier: { } identifier, TargetFrameworkVersion: { } versionText }
            ? string.Equals(identifier, ".NETCoreApp", StringComparison.OrdinalIgnoreCase)
             && Version.TryParse(versionText.TrimStart('v', 'V'), out var version)
             && version >= MinimumFramework
                ? null
                : $"{identifier},Version={versionText}"
            : compilation switch
            {
                { RuntimeAssemblyName: SystemRuntime or "System.Private.CoreLib", RuntimeAssemblyVersion: { } runtimeVersion } when runtimeVersion >= MinimumFramework => null,
                { RuntimeAssemblyName: { } name, RuntimeAssemblyVersion: { } runtimeVersion } =>
                    $"a framework whose {name} is version {runtimeVersion.ToString(2)}",
                _ => "a framework without System.Runtime",
            };
    }

    /// <summary>
    ///     The syntax predicate of the host's request pipeline: a class, struct or record declaration with a base list (a type without one
    ///     implements no interface).
    /// </summary>
    public static bool IsRequestCandidateSyntax(SyntaxNode node) =>
        node is TypeDeclarationSyntax { BaseList: not null } and ( ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax );

    /// <summary>
    ///     The request or stream request type a declaration declares, for KRX030, or nothing. Uses the same rules as KRX001.
    /// </summary>
    public static HostRequestModel? CreateRequest(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol type
         || KeryxSymbols.Create(context.SemanticModel.Compilation) is not { } symbols)
        {
            return null;
        }

        var messageType = MessageType.Classify(type, symbols);
        return ( messageType.IsRequest || messageType.IsStream ) && messageType.IsConcrete && !messageType.IsUnsupportedShape
            ? new(
                CSharpText.TypeOf(type),
                type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                LocationInfo.From(type.Locations.FirstOrDefault())
            )
            : null;
    }

    /// <summary>
    ///     What the host needs from one handler candidate (see <see cref="HostHandlerEntry" />), or nothing when the method is not a candidate.
    /// </summary>
    public static HostHandlerEntry? CreateHandler(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not IMethodSymbol method
         || KeryxSymbols.Create(context.SemanticModel.Compilation) is not { } symbols
         || !HandlerAnalysis.IsCandidate(method, symbols))
        {
            return null;
        }

        var handledType = HandlerAnalysis.HandledRequestType(method, symbols) is { } requestType ? CSharpText.TypeOf(requestType) : null;
        return new(handledType, CreateHandler(method, symbols));
    }

    /// <summary>
    ///     The host's model of a handler, or <see langword="null" /> when the handler generator generates nothing for it in a host (a rule
    ///     is broken, or the class or its signature is narrower than <c>internal</c>; OQ-24, OQ-29).
    /// </summary>
    public static HostHandlerModel? CreateHandler(IMethodSymbol method, KeryxSymbols symbols)
    {
        if (HandlerModelFactory.Create(method, symbols) is not { } model
         || model.ClassAccess == AccessLevel.Restricted
         || model.SignatureAccess == AccessLevel.Restricted)
        {
            return null;
        }

        ITypeSymbol? messageType = null;
        var services = ImmutableArray.CreateBuilder<ServiceParameterModel>();
        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var parameter = method.Parameters[i];
            switch (model.Parameters[i].Role)
            {
                case ParameterRole.Message:
                    messageType = parameter.Type;
                    break;
                case ParameterRole.Injected:
                    services.Add(Service(parameter.Type, model.Parameters[i]));
                    break;
                case ParameterRole.CancellationToken or ParameterRole.Invalid:
                    break;
                default:
                    break;
            }
        }

        return messageType is null
            ? null
            : new(
                model.HandlerClass,
                model.Kind,
                CSharpText.TypeOf(messageType),
                messageType.IsValueType,
                messageType.IsValueType || messageType.IsSealed,
                model.ResponseType,
                model.StaticInvoker,
                services.ToImmutable(),
                model.Behaviors,
                model.Order
            );
    }

    private static ServiceParameterModel Service(ITypeSymbol type, ParameterModel parameter)
    {
        var serviceType = type.IsReferenceType ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type;
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : null;
        return new(
            parameter.Type,
            CSharpText.Type(serviceType),
            parameter.ServiceKey,
            parameter.DefaultValue,
            type.IsValueType,
            underlying is null ? null : CSharpText.Type(underlying),
            type is INamedTypeSymbol { Name: "IServiceProvider", Arity: 0, ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        );
    }

    private static string? Property(AnalyzerConfigOptionsProvider options, string name) =>
        options.GlobalOptions.TryGetValue("build_property." + name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
