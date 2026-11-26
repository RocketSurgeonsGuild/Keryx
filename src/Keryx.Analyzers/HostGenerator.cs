using System.Collections.Immutable;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Specular.Scanning;

namespace Keryx.Analyzers;

/// <summary>
///     Generates a host's mediator and <c>AddKeryx()</c> (ADR 0001, Generated host) and reports the host-only rules. Active only with
///     <c>[assembly: KeryxHost]</c>; it emits and reports nothing below C# 11, so KRX022 stays the only error there, and it emits nothing in
///     a host that targets a framework older than <c>net10.0</c>, where it reports KRX031 (OQ-32). Diagnostics are reported on the syntax
///     tree of their source location, so per-file analyzer config (<c>.editorconfig</c>) applies to them (OQ-35).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HostGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var languageVersion = context
                             .ParseOptionsProvider.Select(static (options, _) => options is CSharpParseOptions { LanguageVersion: >= KeryxSymbols.MinimumLanguageVersion })
                             .WithTrackingName(TrackingNames.HostLanguageVersion);

        var compilation = context
                         .CompilationProvider.Select(static (compilation, _) => HostModelFactory.CreateCompilationInfo(compilation))
                         .WithTrackingName(TrackingNames.HostCompilation);

        var buildProperties = context
                             .AnalyzerConfigOptionsProvider.Select(static (options, _) => HostModelFactory.ReadBuildProperties(options))
                             .WithTrackingName(TrackingNames.HostBuildProperties);

        var settings = compilation
                      .Combine(buildProperties)
                      .Combine(languageVersion)
                      .WithTrackingName(TrackingNames.HostSettingsInputs)
                      .Select(static (inputs, _) => HostModelFactory.CreateSettings(inputs.Left.Left, inputs.Left.Right, inputs.Right))
                      .WithTrackingName(TrackingNames.HostSettings);

        var handlers = context
                      .SyntaxProvider.CreateSyntaxProvider(
                           static (node, _) => HandlerModelFactory.IsCandidateSyntax(node),
                           static (syntaxContext, cancellationToken) => HostModelFactory.CreateHandler(syntaxContext, cancellationToken)
                       )
                      .WithTrackingName(TrackingNames.HostHandlerCandidates)
                      .Where(static entry => entry is not null)
                      .WithTrackingName(TrackingNames.HostHandlerEntries)
                      .Select(static (entry, _) => entry!)
                      .WithTrackingName(TrackingNames.HostHandlers);

        var requests = context
                      .SyntaxProvider.CreateSyntaxProvider(
                           static (node, _) => HostModelFactory.IsRequestCandidateSyntax(node),
                           static (syntaxContext, cancellationToken) => HostModelFactory.CreateRequest(syntaxContext, cancellationToken)
                       )
                      .WithTrackingName(TrackingNames.HostRequestCandidates)
                      .Where(static request => request is not null)
                      .WithTrackingName(TrackingNames.HostRequestModels)
                      .Select(static (request, _) => request!)
                      .WithTrackingName(TrackingNames.HostRequests);

        var sources = settings
                     .Combine(handlers.Collect().WithTrackingName(TrackingNames.HostHandlerList))
                     .WithTrackingName(TrackingNames.HostSettingsAndHandlers)
                     .Combine(requests.Collect().WithTrackingName(TrackingNames.HostRequestList))
                     .WithTrackingName(TrackingNames.HostSources);

        context.RegisterSourceOutput(sources, static (production, inputs) => Produce(production, inputs.Left.Left, inputs.Left.Right));

        // Diagnostics have their own output: they are computed from the cached models, and only reporting them reads the compilation, to
        // put each one on its syntax tree so per-file .editorconfig severity applies (OQ-35). The source output above stays cached.
        var diagnostics = sources
                         .Select(static (inputs, _) => CreateDiagnostics(inputs.Left.Left, inputs.Left.Right, inputs.Right))
                         .WithTrackingName(TrackingNames.HostDiagnostics);

        context.RegisterSourceOutput(
            diagnostics.Combine(context.CompilationProvider).WithTrackingName(TrackingNames.HostDiagnosticsAndCompilation),
            static (production, inputs) => ReportDiagnostics(production, inputs.Left, inputs.Right)
        );
    }

    private static void Produce(SourceProductionContext production, HostSettings settings, ImmutableArray<HostHandlerEntry> handlers)
    {
        if (!settings.Enabled || settings.UnsupportedTargetFramework is not null)
        {
            return;
        }

        var composition = HostComposition.Create(handlers.Select(static entry => entry.Handler).OfType<HostHandlerModel>());
        production.AddSource(HostEmitter.MediatorHintName, HostEmitter.Mediator(settings, composition));
        production.AddSource(HostEmitter.ServiceCollectionExtensionsHintName, HostEmitter.ServiceCollectionExtensions(settings, composition));
    }

    /// <summary>
    ///     The host diagnostics: KRX030 for the host's own request and stream request types (from its syntax pipeline), then KRX031, then
    ///     KRX008 for behavior pairs that involve a referenced behavior (the analyzer checks in-source pairs), KRX035 for each global
    ///     behavior and KRX036 for each <c>ExcludeBehavior</c> that removed nothing. A request whose handler candidate breaks a rule counts as
    ///     handled: the analyzers report the handler.
    /// </summary>
    private static EquatableArray<HostDiagnosticModel> CreateDiagnostics(
        HostSettings settings,
        ImmutableArray<HostHandlerEntry> handlers,
        ImmutableArray<HostRequestModel> requests
    )
    {
        if (!settings.Enabled)
        {
            return ImmutableArray<HostDiagnosticModel>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<HostDiagnosticModel>();
        var handled = new HashSet<string>(handlers.Select(static entry => entry.HandledType).OfType<string>(), StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requests.OrderBy(static r => r.Type, StringComparer.Ordinal))
        {
            if (!handled.Contains(request.Type) && reported.Add(request.Type))
            {
                diagnostics.Add(new(Descriptors.UnhandledRequestInHost, request.Location, ImmutableArray.Create(request.DisplayName)));
            }
        }

        if (settings.UnsupportedTargetFramework is { } framework)
        {
            diagnostics.Add(new(Descriptors.HostTargetFrameworkTooOld, settings.HostAttribute, ImmutableArray.Create(settings.AssemblyName, framework)));
        }

        var models = handlers.Select(static entry => entry.Handler).OfType<HostHandlerModel>().ToArray();
        diagnostics.AddRange(HostBehaviorDiagnostics.Ordering(settings, HostComposition.Create(models)));

        foreach (var global in settings.GlobalBehaviors)
        {
            diagnostics.Add(new(Descriptors.GlobalBehaviorApplied, global.Location ?? settings.HostAttribute, ImmutableArray.Create(global.Name, global.Assembly)));
        }

        var used = new HashSet<string>(models.SelectMany(static m => m.UsedExclusions), StringComparer.Ordinal);
        foreach (var exclusion in settings.Exclusions)
        {
            if (exclusion.Checked && !used.Contains(exclusion.Operand))
            {
                diagnostics.Add(new(Descriptors.UnusedExclusion, exclusion.Location ?? settings.HostAttribute, ImmutableArray.Create("ExcludeBehavior", exclusion.Display)));
            }
        }

        return diagnostics.ToImmutable();
    }

    private static void ReportDiagnostics(SourceProductionContext production, EquatableArray<HostDiagnosticModel> diagnostics, Compilation compilation)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        var trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            trees[tree.FilePath] = tree;
        }

        foreach (var diagnostic in diagnostics)
        {
            var location = diagnostic.Location?.ToLocation(trees) ?? Location.None;
            var arguments = new object?[diagnostic.Arguments.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = diagnostic.Arguments[i];
            }

            production.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, location, arguments));
        }
    }
}
