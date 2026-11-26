; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------------------------------------------
KRX001  | Keryx    | Error    | MissingHandlerAnalyzer, request without a handler in its assembly
KRX002  | Keryx    | Error    | HandlerAnalyzer, handler not private static or with a wrong return type
KRX003  | Keryx    | Error    | HandlerAnalyzer, no single concrete request or notification parameter
KRX004  | Keryx    | Error    | HandlerAnalyzer, handler class or containing type not partial, not public (narrower than internal in a host), file-local, or not a class
KRX005  | Keryx    | Error    | MissingHandlerAnalyzer, request with more than one handler in its assembly
KRX006  | Keryx    | Error    | HandlerAnalyzer, class with more than one handler
KRX007  | Keryx    | Error    | HandlerAnalyzer, handler class constructor or base constructor
KRX008  | Keryx    | Error    | BehaviorAnalyzer and HostGenerator, behavior ordering cycle or RunsAfter/RunsBefore contradicting phase or order
KRX009  | Keryx    | Error    | BehaviorAnalyzer, attached behavior does not fit its target
KRX011  | Keryx    | Error    | MessageTypeAnalyzer, conflicting request or notification interfaces
KRX012  | Keryx    | Error    | MessageTypeAnalyzer and HandlerAnalyzer, generic or ref struct request, generic handler class
KRX013  | Keryx    | Error    | HandlerAnalyzer, ref/in/out/params parameter, ref struct or pointer service, or second CancellationToken
KRX014  | Keryx    | Error    | HandlerAnalyzer, static handler class with injected parameters
KRX015  | Keryx    | Error    | HandlerAnalyzer, reserved member name in a handler class
KRX016  | Keryx    | Error    | MessageTypeAnalyzer and HandlerAnalyzer, type a pipeline needs (including attached and global behaviors) is not public outside a host
KRX017  | Keryx    | Warning  | HandlerAnalyzer, response type differs only in nullability
KRX018  | Keryx    | Error    | HandlerAnalyzer and BehaviorAnalyzer, keyed service key is not a constant
KRX019  | Keryx    | Error    | BehaviorAnalyzer, unsupported behavior class shape
KRX020  | Keryx    | Error    | BehaviorAnalyzer, behavior class without exactly one public constructor, or with a parameter that cannot be injected
KRX021  | Keryx    | Info     | StructRequestAnalyzer, struct request boxed through ISender
KRX022  | Keryx    | Error    | LanguageVersionAnalyzer, C# language version below 11
KRX030  | Keryx    | Error    | HostGenerator, request without a handler in the host
KRX031  | Keryx    | Error    | HostGenerator, host targets a framework older than net10.0
KRX035  | Keryx    | Info     | HostGenerator, global behavior applied in the host
KRX036  | Keryx    | Warning  | HostGenerator, ExcludeBehavior names a type that is not part of any pipeline
KRX040  | Keryx    | Warning  | HandlerAnalyzer, [HandlerOrder] on a method that is not a notification handler
KRX041  | Keryx    | Warning  | MessageTypeAnalyzer, [PublishStrategy] on a type that is not a notification
KRX042  | Keryx    | Warning  | BehaviorAnalyzer, [Behavior] or [GlobalBehavior] where it has no effect
