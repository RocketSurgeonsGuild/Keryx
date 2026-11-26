using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Keryx.Analyzers;

/// <summary>
///     Generates the other half of every handler class (constructor, invoker, <c>[KeryxHandler]</c> manifest entry) and the assembly's
///     <c>[assembly: KeryxManifest]</c> (ADR 0001, Handlers and Manifest). Runs in every project that references Keryx. It never reports
///     diagnostics: it skips handlers the analyzers reject, and it emits nothing below C# 11 (KRX022).
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

        var compilationSettings = context
                                 .CompilationProvider.Select(
                                      static (compilation, _) => KeryxSymbols.Create(compilation) is { } symbols
                                          ? new HandlerGeneratorSettings(symbols.KeryxManifestAttribute is not null, symbols.IsHost)
                                          : new HandlerGeneratorSettings(false, false)
                                  )
                                 .WithTrackingName(TrackingNames.CompilationSettings);

        var settings = compilationSettings
                      .Combine(languageVersion)
                      .WithTrackingName(TrackingNames.HandlerSettingsInputs)
                      .Select(static (pair, _) => pair.Left with { Enabled = pair.Left.Enabled && pair.Right })
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
