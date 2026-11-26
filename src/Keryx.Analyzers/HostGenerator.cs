using System.Collections.Immutable;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Keryx.Analyzers.Hosts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Specular.Scanning;

namespace Keryx.Analyzers;

/// <summary>
///     Generates a host's mediator and <c>AddKeryx()</c> (ADR 0001, Generated host) from its own handlers and those of its references (ADR
///     0001, Cross-assembly discovery), and reports the host-only rules. Active only with
///     <c>[assembly: KeryxHost]</c>; it emits and reports nothing below C# 14, so KRX022 stays the only error there, and it emits nothing in
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

        var notifications = context
                           .SyntaxProvider.CreateSyntaxProvider(
                                static (node, _) => HostModelFactory.IsRequestCandidateSyntax(node),
                                static (syntaxContext, cancellationToken) => HostModelFactory.CreateNotification(syntaxContext, cancellationToken)
                            )
                           .WithTrackingName(TrackingNames.HostNotificationCandidates)
                           .Where(static notification => notification is not null)
                           .WithTrackingName(TrackingNames.HostNotificationModels)
                           .Select(static (notification, _) => notification!)
                           .WithTrackingName(TrackingNames.HostNotifications);

        // The references, read with one Scan call (ADR 0001, How the host reads it): the three requests are built from the compilation, and
        // the transform projects each referenced assembly's manifest, cached per assembly in Specular's ScanCache.
        var scanRequests = context
                          .CompilationProvider.Select(static (compilation, _) => ReferenceScan.CreateRequests(compilation))
                          .WithTrackingName(TrackingNames.HostScanRequests)
                          .SelectMany(static (requests, _) => requests.AsImmutableArray())
                          .WithTrackingName(TrackingNames.HostScanRequest);

        var references = context.Scan(ReferenceScan.ConsumerId, scanRequests, ReferenceScan.Transform);

        var compositionInputs = settings
                               .Combine(handlers.Collect().WithTrackingName(TrackingNames.HostHandlerList))
                               .WithTrackingName(TrackingNames.HostSettingsAndHandlers)
                               .Combine(notifications.Collect().WithTrackingName(TrackingNames.HostNotificationList))
                               .WithTrackingName(TrackingNames.HostSettingsHandlersAndNotifications)
                               .Combine(references)
                               .WithTrackingName(TrackingNames.HostCompositionInputs);

        // Composing resolves the references' manifests in the compilation, so it runs on every compilation; its output is equatable, so the
        // source output stays cached while nothing it describes changed.
        var composition = compositionInputs
                         .Combine(context.CompilationProvider)
                         .WithTrackingName(TrackingNames.HostCompositionAndCompilation)
                         .Select(static (inputs, cancellationToken) => HostComposer.Compose(
                                     inputs.Left.Left.Left.Left,
                                     inputs.Left.Left.Left.Right,
                                     inputs.Left.Left.Right,
                                     inputs.Left.Right,
                                     inputs.Right,
                                     cancellationToken
                                 )
                          )
                         .WithTrackingName(TrackingNames.HostSources);

        context.RegisterSourceOutput(composition, static (production, model) => Produce(production, model));

        // Diagnostics have their own output: they are computed from the cached models, and only reporting them reads the compilation, to
        // put each one on its syntax tree so per-file .editorconfig severity applies (OQ-35). The source output above stays cached.
        var diagnostics = composition
                         .Combine(requests.Collect().WithTrackingName(TrackingNames.HostRequestList))
                         .WithTrackingName(TrackingNames.HostDiagnosticsInputs)
                         .Select(static (inputs, _) => CreateDiagnostics(inputs.Left, inputs.Right))
                         .WithTrackingName(TrackingNames.HostDiagnostics);

        context.RegisterSourceOutput(
            diagnostics.Combine(context.CompilationProvider).WithTrackingName(TrackingNames.HostDiagnosticsAndCompilation),
            static (production, inputs) => ReportDiagnostics(production, inputs.Left, inputs.Right)
        );
    }

    private static void Produce(SourceProductionContext production, HostModel model)
    {
        var settings = model.Settings;
        if (!settings.Enabled || settings.UnsupportedTargetFramework is not null)
        {
            return;
        }

        var composition = HostComposition.Create(model.Handlers, model.Notifications);
        production.AddSource(HostEmitter.MediatorHintName, HostEmitter.Mediator(settings, composition));
        production.AddSource(HostEmitter.ServiceCollectionExtensionsHintName, HostEmitter.ServiceCollectionExtensions(settings, composition));
    }

    /// <summary>
    ///     The host diagnostics: KRX030 for the host's own request and stream request types (from its syntax pipeline) and for the referenced
    ///     ones (from the scan, those declared in another host included, OQ-16), except the types <c>ExternallyHandled</c> names, then KRX031,
    ///     then the composition's own (KRX005 across assemblies, KRX032, KRX033, KRX034, KRX036 for <c>ExcludeHandler</c>), then KRX008 for
    ///     behavior pairs that involve a referenced behavior (the analyzer checks in-source pairs), KRX035 for each global behavior, KRX036 for
    ///     each <c>ExcludeBehavior</c> that removed nothing and for each <c>ExternallyHandled</c> that names a handled type or a type that is
    ///     not a request (OQ-10), and KRX037 for each struct notification that handlers typed as an interface receive. A request whose handler
    ///     candidate breaks a rule counts as handled: the analyzers report the handler.
    /// </summary>
    private static EquatableArray<HostDiagnosticModel> CreateDiagnostics(HostModel model, ImmutableArray<HostRequestModel> requests)
    {
        var settings = model.Settings;
        if (!settings.Enabled)
        {
            return ImmutableArray<HostDiagnosticModel>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<HostDiagnosticModel>();
        var handled = new HashSet<string>(model.HandledRequests, StringComparer.Ordinal);
        var external = new HashSet<string>(model.ExternallyHandled.Where(static e => e.IsRequest).Select(static e => e.Type), StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requests.OrderBy(static r => r.Type, StringComparer.Ordinal).Concat(model.ReferencedRequests))
        {
            if (!handled.Contains(request.Type) && !external.Contains(request.Type) && reported.Add(request.Type))
            {
                diagnostics.Add(new(Descriptors.UnhandledRequestInHost, request.Location ?? settings.HostAttribute, ImmutableArray.Create(request.DisplayName)));
            }
        }

        if (settings.UnsupportedTargetFramework is { } framework)
        {
            diagnostics.Add(new(Descriptors.HostTargetFrameworkTooOld, settings.HostAttribute, ImmutableArray.Create(settings.AssemblyName, framework)));
        }

        diagnostics.AddRange(model.Diagnostics);

        var models = model.Handlers;
        var composition = HostComposition.Create(models, model.Notifications);
        diagnostics.AddRange(HostBehaviorDiagnostics.Ordering(settings, composition));

        foreach (var global in model.GlobalBehaviors)
        {
            diagnostics.Add(new(Descriptors.GlobalBehaviorApplied, global.Location ?? settings.HostAttribute, ImmutableArray.Create(global.Name, global.Assembly)));
        }

        var used = new HashSet<string>(
            models
               .SelectMany(static m => m.UsedExclusions.Concat(m.Deliveries.SelectMany(static d => d.UsedExclusions.Concat(d.Notification.UsedExclusions))))
               .Concat(model.Notifications.SelectMany(static n => n.UsedExclusions)),
            StringComparer.Ordinal
        );
        foreach (var exclusion in model.BehaviorExclusions)
        {
            if (!used.Contains(exclusion.Operand))
            {
                diagnostics.Add(
                    new(
                        Descriptors.UnusedExclusion,
                        exclusion.Location ?? settings.HostAttribute,
                        ImmutableArray.Create("ExcludeBehavior", exclusion.Display, Descriptors.NotPartOfAnyPipeline(exclusion.Display))
                    )
                );
            }
        }

        // ExternallyHandled has no effect on a type the host handles or a type that is not a request or stream request (OQ-10).
        foreach (var entry in model.ExternallyHandled)
        {
            var reason = !entry.IsRequest ? Descriptors.NotARequest(entry.Display)
                : handled.Contains(entry.Type) ? Descriptors.HandledByThisHost(entry.Display)
                : null;
            if (reason is not null)
            {
                diagnostics.Add(
                    new(Descriptors.UnusedExclusion, entry.Location ?? settings.HostAttribute, ImmutableArray.Create("ExternallyHandled", entry.Display, reason))
                );
            }
        }

        var boxed = new List<string>();
        foreach (var notification in composition.Notifications)
        {
            boxed.Clear();
            foreach (var handler in notification.Handlers)
            {
                if (handler.Boxed)
                {
                    boxed.Add($"'{handler.Handler.HandlerClass.Replace("global::", "")}'");
                }
            }

            if (boxed.Count > 0)
            {
                diagnostics.Add(
                    new(
                        Descriptors.BoxedStructNotification,
                        notification.Notification.Location ?? settings.HostAttribute,
                        ImmutableArray.Create(notification.Notification.DisplayName, string.Join(", ", boxed))
                    )
                );
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
