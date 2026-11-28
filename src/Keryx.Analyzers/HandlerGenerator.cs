using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers;

/// <summary>
///     Generates the other half of every handler class (constructor, invoker, <c>[KeryxHandler]</c> manifest entry) and the assembly's
///     <c>[assembly: KeryxManifest]</c> (ADR 0001, Handlers and Manifest). Runs in every project that references Keryx. It never reports
///     diagnostics: it skips handlers the analyzers reject, and it emits nothing below C# 14 (KRX022).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HandlerGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var languageVersion = context
                             .ParseOptionsProvider.Select(static (options, _) => options is CSharpParseOptions { LanguageVersion: >= KeryxSymbols.MinimumLanguageVersion })
                             .WithTrackingName(TrackingNames.LanguageVersion);

        // Whether the compilation references a Keryx that has the manifest attributes.
        var compilationSettings = context
                                 .CompilationProvider.Select(static (compilation, _) => KeryxSymbols.Create(compilation) is { KeryxManifestAttribute: not null })
                                 .WithTrackingName(TrackingNames.CompilationSettings);

        var isHost = context
                    .AnalyzerConfigOptionsProvider.Select(static (options, _) => KeryxBuildProperties.IsHost(options.GlobalOptions))
                    .WithTrackingName(TrackingNames.HostProperty);

        var settings = compilationSettings
                      .Combine(isHost)
                      .WithTrackingName(TrackingNames.HandlerCompilationAndHost)
                      .Combine(languageVersion)
                      .WithTrackingName(TrackingNames.HandlerSettingsInputs)
                      .Select(static (inputs, _) => new HandlerGeneratorSettings(inputs.Left.Left && inputs.Right, inputs.Left.Right))
                      .WithTrackingName(TrackingNames.HandlerSettings);

        var handlers = context
                      .SyntaxProvider.CreateSyntaxProvider(
                           static (node, _) => HandlerModelFactory.IsCandidateSyntax(node),
                           static (syntaxContext, cancellationToken) => HandlerModelFactory.Create(syntaxContext, cancellationToken)
                       )
                      .WithTrackingName(TrackingNames.HandlerCandidates)
                      .Where(static model => model is not null)
                      .WithTrackingName(TrackingNames.HandlerModels)
                      .Select(static (model, _) => model!)
                      .WithTrackingName(TrackingNames.Handlers);

        var sources = handlers.Combine(settings).WithTrackingName(TrackingNames.HandlerSources);

        context.RegisterSourceOutput(
            sources,
            static (production, pair) =>
            {
                var (model, generatorSettings) = pair;
                if (generatorSettings.Enabled && HandlerEmitter.Emit(model, generatorSettings.IsHost) is { } source)
                {
                    production.AddSource(model.HintName, source);
                }
            }
        );

        context.RegisterSourceOutput(
            settings,
            static (production, generatorSettings) =>
            {
                if (generatorSettings.Enabled)
                {
                    production.AddSource(HandlerEmitter.ManifestHintName, HandlerEmitter.Manifest());
                }
            }
        );
    }
}
