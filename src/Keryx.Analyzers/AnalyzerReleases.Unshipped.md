; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------------------------------------------
KRX001  | Keryx    | Error    | MissingHandlerAnalyzer, request without a handler in its assembly (every assembly, hosts included)
KRX002  | Keryx    | Error    | HandlerAnalyzer, handler not private static or with a wrong return type
KRX003  | Keryx    | Error    | HandlerAnalyzer, no single concrete request or notification parameter
KRX004  | Keryx    | Error    | HandlerAnalyzer, handler class or containing type not partial, not public (narrower than internal in a host), file-local, or not a class
KRX005  | Keryx    | Error    | MissingHandlerAnalyzer and HostGenerator, request with more than one handler in its assembly or across the host's assemblies
KRX006  | Keryx    | Error    | HandlerAnalyzer, class with more than one handler
KRX007  | Keryx    | Error    | HandlerAnalyzer, handler class constructor or base constructor
KRX008  | Keryx    | Error    | BehaviorAnalyzer and HostGenerator, behavior ordering cycle or RunsAfter/RunsBefore contradicting phase or order
KRX009  | Keryx    | Error    | BehaviorAnalyzer and HostGenerator, attached behavior does not fit its target (in the host: behaviors a referenced handler's manifest attaches or a referenced notification type carries)
KRX011  | Keryx    | Error    | MessageTypeAnalyzer, conflicting request or notification interfaces
KRX012  | Keryx    | Error    | MessageTypeAnalyzer and HandlerAnalyzer, generic or ref struct request, generic handler class
KRX013  | Keryx    | Error    | HandlerAnalyzer and BehaviorAnalyzer, ref/in/out/params handler or behavior method parameter, ref struct or pointer service, or second CancellationToken
KRX014  | Keryx    | Error    | HandlerAnalyzer, static handler class with injected parameters
KRX015  | Keryx    | Error    | HandlerAnalyzer, reserved member name in a handler class
KRX016  | Keryx    | Error    | MessageTypeAnalyzer, HandlerAnalyzer and BehaviorAnalyzer, type, behavior class or behavior method a pipeline needs is not public outside a host, or narrower than internal in a host
KRX017  | Keryx    | Warning  | HandlerAnalyzer, response type differs only in nullability
KRX018  | Keryx    | Error    | HandlerAnalyzer and BehaviorAnalyzer, keyed service key is not a constant
KRX019  | Keryx    | Error    | BehaviorAnalyzer, unsupported static behavior class or behavior method shape, including an open behavior method without the message constraint
KRX021  | Keryx    | Info     | StructRequestAnalyzer, struct request boxed through ISender
KRX022  | Keryx    | Error    | LanguageVersionAnalyzer, C# language version below 14
KRX023  | Keryx    | Error    | HandlerAnalyzer, handler of a request or stream request declared in another assembly
KRX030  | Keryx    | Error    | HostGenerator, referenced request without a handler in the host
KRX031  | Keryx    | Error    | HostGenerator, host targets a framework older than net10.0
KRX032  | Keryx    | Error    | HostGenerator, referenced assembly has a newer manifest format version
KRX033  | Keryx    | Warning  | HostGenerator, referenced assembly uses Keryx but has no manifest
KRX034  | Keryx    | Error    | HostGenerator, type named in a referenced manifest (or an injected parameter type of a referenced behavior method) cannot be resolved (per behavior method and kind), or a manifest entry does not fit the types the host resolves
KRX035  | Keryx    | Info     | HostGenerator, global behavior applied in the host, per kind
KRX036  | Keryx    | Warning  | HostGenerator, ExcludeHandler or ExcludeBehavior names a type that is not part of any pipeline
KRX037  | Keryx    | Info     | HostGenerator, struct notification boxed for handlers typed as an interface
KRX040  | Keryx    | Warning  | HandlerAnalyzer, [HandlerOrder] on a method that is not a notification handler
KRX041  | Keryx    | Warning  | MessageTypeAnalyzer, [PublishStrategy] on a type that is not a notification
KRX042  | Keryx    | Warning  | BehaviorAnalyzer, [Behavior] or [GlobalBehavior] where it has no effect, including [Behavior] on a notification handler method
KRX043  | Keryx    | Warning  | HostOnlyAttributeAnalyzer, [assembly: ExcludeHandler] or [assembly: ExcludeBehavior] outside a host
