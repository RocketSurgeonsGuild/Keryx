namespace Keryx.Analyzers;

/// <summary>
///     The tracking names of the generators' incremental steps (ADR 0001, Incremental generation rules: every step has one). The incremental
///     tests read the steps by these names.
/// </summary>
internal static class TrackingNames
{
    /// <summary>Whether the parse options are C# 11 or later (KRX022).</summary>
    public const string LanguageVersion = "Keryx.LanguageVersion";

    /// <summary>What the handler generator reads from the compilation itself. Fed directly by the compilation, so it always runs.</summary>
    public const string CompilationSettings = "Keryx.CompilationSettings";

    /// <summary><see cref="CompilationSettings" /> paired with <see cref="LanguageVersion" />, before they are merged.</summary>
    public const string HandlerSettingsInputs = "Keryx.HandlerSettingsInputs";

    /// <summary>The handler generator's settings: <see cref="LanguageVersion" /> combined with <see cref="CompilationSettings" />.</summary>
    public const string HandlerSettings = "Keryx.HandlerSettings";

    /// <summary>Every <c>Handle</c>/<c>HandleAsync</c> method projected to a handler model, or nothing.</summary>
    public const string HandlerCandidates = "Keryx.HandlerCandidates";

    /// <summary>The <see cref="HandlerCandidates" /> that produced a model (still typed as nullable).</summary>
    public const string HandlerModels = "Keryx.HandlerModels";

    /// <summary>The handler models.</summary>
    public const string Handlers = "Keryx.Handlers";

    /// <summary>The handler models combined with the settings, one per generated handler source.</summary>
    public const string HandlerSources = "Keryx.HandlerSources";

    /// <summary>The host generator: whether the parse options are C# 11 or later (KRX022).</summary>
    public const string HostLanguageVersion = "Keryx.Host.LanguageVersion";

    /// <summary>What the host generator reads from the compilation itself. Fed directly by the compilation, so it always runs.</summary>
    public const string HostCompilation = "Keryx.Host.Compilation";

    /// <summary>The MSBuild properties the host generator reads (RootNamespace, TargetFrameworkIdentifier, TargetFrameworkVersion).</summary>
    public const string HostBuildProperties = "Keryx.Host.BuildProperties";

    /// <summary><see cref="HostCompilation" />, <see cref="HostBuildProperties" /> and <see cref="HostLanguageVersion" />, before they are merged.</summary>
    public const string HostSettingsInputs = "Keryx.Host.SettingsInputs";

    /// <summary>The host generator's settings.</summary>
    public const string HostSettings = "Keryx.Host.Settings";

    /// <summary>Every <c>Handle</c>/<c>HandleAsync</c> method projected to what the host needs from it, or nothing.</summary>
    public const string HostHandlerCandidates = "Keryx.Host.HandlerCandidates";

    /// <summary>The <see cref="HostHandlerCandidates" /> that are handler candidates (still typed as nullable).</summary>
    public const string HostHandlerEntries = "Keryx.Host.HandlerEntries";

    /// <summary>What the host needs from each handler candidate.</summary>
    public const string HostHandlers = "Keryx.Host.Handlers";

    /// <summary><see cref="HostHandlers" />, collected.</summary>
    public const string HostHandlerList = "Keryx.Host.HandlerList";

    /// <summary>Every type declaration with a base list projected to a request or stream request model, or nothing.</summary>
    public const string HostRequestCandidates = "Keryx.Host.RequestCandidates";

    /// <summary>The <see cref="HostRequestCandidates" /> that are requests or stream requests (still typed as nullable).</summary>
    public const string HostRequestModels = "Keryx.Host.RequestModels";

    /// <summary>The request and stream request types declared in the host.</summary>
    public const string HostRequests = "Keryx.Host.Requests";

    /// <summary><see cref="HostRequests" />, collected.</summary>
    public const string HostRequestList = "Keryx.Host.RequestList";

    /// <summary>Every type declaration with a base list projected to a concrete notification model, or nothing.</summary>
    public const string HostNotificationCandidates = "Keryx.Host.NotificationCandidates";

    /// <summary>The <see cref="HostNotificationCandidates" /> that are concrete notifications (still typed as nullable).</summary>
    public const string HostNotificationModels = "Keryx.Host.NotificationModels";

    /// <summary>The notification types declared in the host, with their publish strategy and per-publish behaviors.</summary>
    public const string HostNotifications = "Keryx.Host.Notifications";

    /// <summary><see cref="HostNotifications" />, collected.</summary>
    public const string HostNotificationList = "Keryx.Host.NotificationList";

    /// <summary>The settings combined with <see cref="HostHandlerList" />.</summary>
    public const string HostSettingsAndHandlers = "Keryx.Host.SettingsAndHandlers";

    /// <summary><see cref="HostSettingsAndHandlers" /> combined with <see cref="HostRequestList" />.</summary>
    public const string HostSettingsHandlersAndRequests = "Keryx.Host.SettingsHandlersAndRequests";

    /// <summary>The settings, handlers, request types and notification types combined: the input of the host's source output.</summary>
    public const string HostSources = "Keryx.Host.Sources";

    /// <summary>The host diagnostics (KRX008, KRX030, KRX031, KRX035, KRX036, KRX037), computed from <see cref="HostSources" />.</summary>
    public const string HostDiagnostics = "Keryx.Host.Diagnostics";

    /// <summary><see cref="HostDiagnostics" /> combined with the compilation, to report them on their syntax trees (fed by the compilation).</summary>
    public const string HostDiagnosticsAndCompilation = "Keryx.Host.DiagnosticsAndCompilation";
}
