using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Keryx.Analyzers.Handlers;

/// <summary>
///     Writes the generated half of a handler class (ADR 0001, Handlers, Example) and the assembly manifest, as text through a
///     <see cref="CodeWriter" />.
/// </summary>
internal static class HandlerEmitter
{
    /// <summary>The manifest format this Keryx writes and reads (ADR 0001, Versioning).</summary>
    public const int ManifestFormatVersion = 1;

    /// <summary>The hint name of the assembly manifest. It does not end in <c>.Keryx.g.cs</c>, so no handler class can take it.</summary>
    public const string ManifestHintName = "KeryxManifest.g.cs";

    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";
    private const string Unit = "global::Keryx.Unit";
    private const string CancellationToken = "global::System.Threading.CancellationToken";

    /// <summary>The version in <c>[GeneratedCode("Keryx", version)]</c>.</summary>
    public static string Version => CodeWriter.Version;

    /// <summary><c>[assembly: KeryxManifest(FormatVersion = n)]</c>, which marks an assembly whose handlers the host reads.</summary>
    public static SourceText Manifest()
    {
        var writer = new CodeWriter(pragma: null, capacity: 128);
        writer.Line("[assembly: global::Keryx.Internal.KeryxManifest(FormatVersion = " + ManifestFormatVersion.ToString(CultureInfo.InvariantCulture) + ")]");
        return writer.ToSourceText();
    }

    /// <summary>
    ///     The source of the handler class's generated half, or <see langword="null" /> when nothing may be generated for it in this
    ///     compilation.
    /// </summary>
    public static SourceText? Emit(HandlerModel model, bool isHost)
    {
        // In a host, generated code outside the class must reach the class and every type its members mention (OQ-24, OQ-29).
        if (isHost ? model.SignatureAccess == AccessLevel.Restricted || model.ClassAccess == AccessLevel.Restricted : !model.ValidOutsideHost)
        {
            return null;
        }

        // Member accessibility follows the signature alone: a signature that mentions internal types gets internal members, which compile
        // in a class of any accessibility (a public or protected internal class's public members could not take an internal type). Such a
        // handler stays out of the manifest with the host's other internal handlers.
        var memberAccess = model.SignatureAccess == AccessLevel.Public ? "public" : "internal";
        var inManifest = model is { ClassAccess: AccessLevel.Public, SignatureAccess: AccessLevel.Public };

        var writer = new CodeWriter(capacity: 2048);
        if (model.Namespace is { } ns)
        {
            writer.Namespace(ns);
        }

        // The chain of partial declarations, outermost first; the last one is the handler class.
        for (var i = 0; i < model.Types.Count; i++)
        {
            writer.Member();
            if (i == model.Types.Count - 1 && inManifest)
            {
                WriteHandlerAttribute(writer, model);
            }

            writer.Line(Declaration(model.Types[i]));
            writer.Open();
        }

        WriteMembers(writer, model, memberAccess, isHost);

        for (var i = 0; i < model.Types.Count; i++)
        {
            writer.Close();
        }

        if (model.Namespace is not null)
        {
            writer.Close();
        }

        return writer.ToSourceText();
    }

    /// <summary>The partial declaration of one type of the chain, with the modifiers and keyword the user declared it with.</summary>
    private static string Declaration(TypeDeclarationModel type) =>
        type.Modifiers.Length > 0 ? $"{type.Modifiers} partial {type.Keyword} {type.Name}" : $"partial {type.Keyword} {type.Name}";

    /// <summary>
    ///     The handler class's <c>[KeryxHandler]</c> manifest entry. A <c>typeof</c> operand cannot carry a top-level nullable annotation, and
    ///     dropping a nested one (<c>Tag&lt;Ping, string&gt;</c> for a request that responds with <c>string?</c>) breaks a nullable constraint
    ///     (CS8631), so with behaviors the attribute is written with the nullable context disabled (<c>#nullable disable</c> before it and
    ///     <c>#nullable enable</c>, not <c>restore</c>, which returns to the project default, disabled for generated code, after it).
    ///     Attribute metadata keeps no annotations anyway.
    /// </summary>
    private static void WriteHandlerAttribute(CodeWriter writer, HandlerModel model)
    {
        var attribute = new StringBuilder(256)
                       .Append("[global::Keryx.Internal.KeryxHandler(typeof(")
                       .Append(TypeOfOperand(model.MessageType))
                       .Append("), global::Keryx.Internal.HandlerKind.")
                       .Append(model.Kind.ToString());
        if (model.Behaviors.Count > 0)
        {
            attribute.Append(", Behaviors = new global::System.Type[] { ");
            for (var i = 0; i < model.Behaviors.Count; i++)
            {
                attribute.Append(i == 0 ? "typeof(" : ", typeof(").Append(model.Behaviors[i]).Append(')');
            }

            attribute.Append(" }");
        }

        if (model.Order != 0)
        {
            attribute.Append(", Order = ").Append(model.Order.ToString(CultureInfo.InvariantCulture));
        }

        if (model.StaticInvoker)
        {
            attribute.Append(", StaticInvoker = true");
        }

        attribute.Append(")]");
        if (model.Behaviors.Count == 0)
        {
            writer.Line(attribute.ToString());
            return;
        }

        writer.Directive("#nullable disable");
        writer.Line(attribute.ToString());
        writer.Directive("#nullable enable");
    }

    /// <summary>
    ///     The members of the handler class's generated half: the fields and constructor of an injecting handler, then the invoker, and in a
    ///     host, for an injecting handler, the static entry its pipelines call (owner decision 44).
    /// </summary>
    private static void WriteMembers(CodeWriter writer, HandlerModel model, string memberAccess, bool isHost)
    {
        var className = model.Types[^1].Name;
        var injected = model.Parameters.Where(static p => p.Role == ParameterRole.Injected).ToArray();

        foreach (var parameter in injected)
        {
            writer.Member();
            writer.Line(CodeWriter.GeneratedCodeAttribute);
            writer.Line($"private readonly {parameter.Type} {FieldName(parameter)};");
        }

        if (injected.Length > 0)
        {
            writer.Member();
            writer.Documentation("<summary>Creates the handler with the services its handler method injects.</summary>");
            writer.Line(CodeWriter.GeneratedCodeAttribute);
            writer.Parameters(memberAccess + " " + className, [.. injected.Select(ConstructorParameter)]);
            writer.Open();
            foreach (var parameter in injected)
            {
                writer.Line($"{FieldName(parameter)} = {CSharpText.Identifier(parameter.Name)};");
            }

            writer.Close();
        }

        // The handler method's arguments: the invoker passes the fields; the static entry takes parameters with the fields' names, so the
        // call reads the same in both.
        var messageName = model.Kind == HandlerKind.Notification ? "notification" : "request";
        var call = new StringBuilder(64).Append(model.MethodName).Append('(');
        for (var i = 0; i < model.Parameters.Count; i++)
        {
            var parameter = model.Parameters[i];
            call.Append(i == 0 ? "" : ", ")
                .Append(
                     parameter.Role switch
                     {
                         ParameterRole.Message => messageName,
                         ParameterRole.CancellationToken => "cancellationToken",
                         ParameterRole.Injected or ParameterRole.Invalid => FieldName(parameter),
                         _ => FieldName(parameter),
                     }
                 );
        }

        var invocation = call.Append(')').ToString();

        writer.Member();
        writer.Documentation("<summary>Runs the handler only. No behaviors run.</summary>");
        writer.Line(CodeWriter.GeneratedCodeAttribute);
        WriteInvoker(
            writer,
            model,
            $"{memberAccess}{( model.StaticInvoker ? " static" : "" )} {InvokerReturnType(model)} {HandlerAnalysis.InvokerName}({model.MessageType} {messageName}, {CancellationToken} cancellationToken)",
            invocation
        );

        if (!isHost || injected.Length == 0)
        {
            return;
        }

        // A host's pipelines hold the services and call this, so they never construct the handler class (owner decision 44). It is internal:
        // only the host's own generated code calls it, and referenced handlers keep their public constructor and InvokeAsync.
        var parameters = new StringBuilder(64 * injected.Length);
        foreach (var parameter in injected)
        {
            parameters.Append(", ").Append(parameter.Type).Append(' ').Append(FieldName(parameter));
        }

        writer.Member();
        writer.Documentation(
            "<summary>",
            "    Runs the handler only, with the services its handler method injects. The host's pipelines call it with the services their",
            "    context resolved, so they never construct this class. No behaviors run.",
            "</summary>"
        );
        writer.Line(CodeWriter.GeneratedCodeAttribute);
        WriteInvoker(
            writer,
            model,
            $"internal static {InvokerReturnType(model)} {HandlerAnalysis.StaticEntryName}({model.MessageType} {messageName}{parameters}, {CancellationToken} cancellationToken)",
            invocation
        );
    }

    /// <summary>
    ///     An invoker (<c>InvokeAsync</c> or the static entry): the call of the handler method with the return adaptation of its shape (ADR
    ///     0001, Handlers, return adaptation).
    /// </summary>
    private static void WriteInvoker(CodeWriter writer, HandlerModel model, string declaration, string invocation)
    {
        var result = model.NullabilityDiffers ? invocation + "!" : invocation;
        switch (model.ReturnShape)
        {
            case ReturnShape.Void:
                writer.Line(declaration);
                writer.Open();
                writer.Line(invocation + ";");
                writer.Line($"return new {ValueTask}<{Unit}>({Unit}.Value);");
                writer.Close();
                break;
            case ReturnShape.ValueTaskOfT or ReturnShape.AsyncEnumerable:
                writer.Arrow(declaration, result);
                break;
            case ReturnShape.TaskOfT or ReturnShape.Synchronous:
                writer.Arrow(declaration, $"new {ValueTask}<{model.ResponseType}>({result})");
                break;
            case ReturnShape.ValueTask or ReturnShape.Task when model.Kind == HandlerKind.Request:
                writer.Arrow(declaration, $"global::Keryx.Internal.UnitAdapter.ToUnit({invocation})");
                break;
            case ReturnShape.ValueTask:
                writer.Arrow(declaration, invocation);
                break;
            default:
                writer.Arrow(declaration, $"new {ValueTask}({invocation})");
                break;
        }
    }

    private static string InvokerReturnType(HandlerModel model) =>
        model.Kind switch
        {
            HandlerKind.Notification => ValueTask,
            HandlerKind.Stream => $"global::System.Collections.Generic.IAsyncEnumerable<{model.ResponseType}>",
            HandlerKind.Request => $"{ValueTask}<{model.ResponseType}>",
            _ => $"{ValueTask}<{model.ResponseType}>",
        };

    /// <summary>
    ///     A parameter of the generated constructor: the handler method's parameter with its <c>[FromKeyedServices]</c> key and default value.
    /// </summary>
    private static string ConstructorParameter(ParameterModel parameter)
    {
        var text = $"{parameter.Type} {CSharpText.Identifier(parameter.Name)}";
        if (parameter.DefaultValue is { } defaultValue)
        {
            text += " = " + defaultValue;
        }

        return parameter.ServiceKey is { } key ? $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute({key})] {text}" : text;
    }

    private static string FieldName(ParameterModel parameter) => HandlerAnalysis.ReservedFieldPrefix + parameter.Name;

    /// <summary>A <c>typeof</c> operand cannot carry a nullable reference annotation.</summary>
    private static string TypeOfOperand(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type[..^1] : type;
}
