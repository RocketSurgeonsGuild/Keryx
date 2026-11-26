using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Keryx.Analyzers.Handlers;
using Specular.Scanning;

namespace Keryx.Analyzers.Hosts;

/// <summary>
///     What the host reads from its references (ADR 0001, How the host reads it): one projection per referenced assembly that references
///     Keryx, ordered by assembly name. It is the output of the host's one <c>Scan</c> call, so it holds names, never symbols; the
///     composition step resolves them in the host's compilation.
/// </summary>
/// <param name="Assemblies">The referenced assemblies that reference Keryx (the host itself excluded), ordered by name.</param>
internal sealed record KeryxReferenceModel(EquatableArray<ReferencedAssemblyModel> Assemblies)
{
    public static KeryxReferenceModel Empty { get; } = new(ImmutableArray<ReferencedAssemblyModel>.Empty);
}

/// <summary>
///     The projection of one referenced assembly's manifest (ADR 0001, Manifest): its <c>[assembly: KeryxManifest]</c> format version,
///     its handler classes with their <c>[KeryxHandler]</c> arguments, its global behaviors, and its request,
///     stream request and notification types. Stored per assembly in Specular's ScanCache (keyed by MVID), so the format version is known on a
///     cache hit too (KRX032).
/// </summary>
/// <param name="Name">The assembly's metadata name.</param>
/// <param name="FormatVersion">The manifest's <c>FormatVersion</c>, or <see langword="null" /> when the assembly has no manifest (KRX033).</param>
/// <param name="Handlers">The public handler classes, ordered by metadata name.</param>
/// <param name="GlobalBehaviors">
///     The public behavior classes with <c>[GlobalBehavior]</c> on the class or on a behavior method, ordered by metadata name.
/// </param>
/// <param name="Messages">The public, concrete, non-generic request, stream request and notification types, ordered by metadata name.</param>
/// <param name="AssemblyBehaviors">Its <c>[assembly: Behavior(typeof(X))]</c> types, in declaration order (OQ-3).</param>
internal sealed record ReferencedAssemblyModel(
    string Name,
    int? FormatVersion,
    EquatableArray<ReferencedHandlerModel> Handlers,
    EquatableArray<KeryxTypeReference> GlobalBehaviors,
    EquatableArray<KeryxTypeReference> Messages,
    EquatableArray<KeryxTypeReference> AssemblyBehaviors
);

/// <summary>
///     One <c>[KeryxHandler]</c> entry of a referenced assembly (ADR 0001, Manifest).
/// </summary>
/// <param name="HandlerClass">The handler class.</param>
/// <param name="TargetType">
///     <c>KeryxHandler.TargetType</c>: the request, stream request or notification parameter type, or <see langword="null" /> when the
///     attribute has none.
/// </param>
/// <param name="Kind"><c>KeryxHandler.Kind</c>.</param>
/// <param name="Behaviors">
///     <c>KeryxHandler.Behaviors</c>: the behavior classes attached to a request or stream handler method, in declared order.
/// </param>
/// <param name="Order"><c>KeryxHandler.Order</c>: the <c>[HandlerOrder]</c> of a notification handler.</param>
/// <param name="StaticInvoker"><c>KeryxHandler.StaticInvoker</c>: the handler has no injected parameters.</param>
internal sealed record ReferencedHandlerModel(
    KeryxTypeReference HandlerClass,
    KeryxTypeReference? TargetType,
    HandlerKind Kind,
    EquatableArray<KeryxTypeReference> Behaviors,
    int Order,
    bool StaticInvoker
);

/// <summary>
///     A type named in a manifest, by metadata name and assembly, so a projection stays equatable and serializable and can be resolved in a
///     later compilation. Keeping the assembly name lets KRX034 name the assembly to reference when the type no longer resolves.
/// </summary>
/// <param name="Assembly">The containing assembly's name (for a type whose assembly is not referenced, the name the metadata expects).</param>
/// <param name="MetadataName">The full metadata name (<c>Ns.Outer+Inner`1</c>).</param>
/// <param name="Unbound">Whether it is an unbound generic (<c>typeof(X&lt;,&gt;)</c>).</param>
/// <param name="Arguments">The type arguments of a closed generic, outermost containing type first.</param>
/// <param name="ArrayRanks">For an array (only as a type argument), its ranks, outermost first.</param>
internal sealed record KeryxTypeReference(
    string Assembly,
    string MetadataName,
    bool Unbound,
    EquatableArray<KeryxTypeReference> Arguments,
    EquatableArray<int> ArrayRanks
)
{
    /// <summary>A name for messages: the metadata name, with its arguments.</summary>
    public string Display =>
        ( Arguments.Count == 0 ? MetadataName : $"{MetadataName}[{string.Join(", ", Arguments.Select(static a => a.Display))}]" )
      + string.Concat(ArrayRanks.Select(static rank => "[" + new string(',', rank - 1) + "]"));

    /// <summary>The Specular type reference the scan's resolver resolves.</summary>
    public TypeReference ToTypeReference() =>
        new(Assembly, MetadataName, Unbound)
        {
            TypeArguments = Arguments.Select(static a => a.ToTypeReference()).ToImmutableArray(),
            ArrayRanks = ArrayRanks,
        };
}

/// <summary>
///     The JSON shape of <see cref="ReferencedAssemblyModel" /> in the ScanCache. Decoupled from the pipeline record, which holds equatable
///     arrays; bump <see cref="ReferenceScan.ProjectionDataVersion" /> whenever it, or what is derived into it, changes.
/// </summary>
internal sealed class AssemblyProjection
{
    public string Name { get; set; } = "";
    public int? FormatVersion { get; set; }
    public List<HandlerProjection> Handlers { get; set; } = [];
    public List<TypeReferenceProjection> GlobalBehaviors { get; set; } = [];
    public List<TypeReferenceProjection> Messages { get; set; } = [];
    public List<TypeReferenceProjection> AssemblyBehaviors { get; set; } = [];

    public static AssemblyProjection From(ReferencedAssemblyModel model) =>
        new()
        {
            Name = model.Name,
            FormatVersion = model.FormatVersion,
            Handlers = [.. model.Handlers.Select(HandlerProjection.From)],
            GlobalBehaviors = [.. model.GlobalBehaviors.Select(TypeReferenceProjection.From)],
            Messages = [.. model.Messages.Select(TypeReferenceProjection.From)],
            AssemblyBehaviors = [.. model.AssemblyBehaviors.Select(TypeReferenceProjection.From)],
        };

    public ReferencedAssemblyModel ToModel() =>
        new(
            Name,
            FormatVersion,
            Handlers.Select(static h => h.ToModel()).ToImmutableArray(),
            GlobalBehaviors.Select(static t => t.ToModel()).ToImmutableArray(),
            Messages.Select(static t => t.ToModel()).ToImmutableArray(),
            AssemblyBehaviors.Select(static t => t.ToModel()).ToImmutableArray()
        );
}

/// <summary>The JSON shape of <see cref="ReferencedHandlerModel" />.</summary>
internal sealed class HandlerProjection
{
    public TypeReferenceProjection HandlerClass { get; set; } = new();
    public TypeReferenceProjection? TargetType { get; set; }
    public int Kind { get; set; }
    public List<TypeReferenceProjection> Behaviors { get; set; } = [];
    public int Order { get; set; }
    public bool StaticInvoker { get; set; }

    public static HandlerProjection From(ReferencedHandlerModel model) =>
        new()
        {
            HandlerClass = TypeReferenceProjection.From(model.HandlerClass),
            TargetType = model.TargetType is { } target ? TypeReferenceProjection.From(target) : null,
            Kind = (int)model.Kind,
            Behaviors = [.. model.Behaviors.Select(TypeReferenceProjection.From)],
            Order = model.Order,
            StaticInvoker = model.StaticInvoker,
        };

    public ReferencedHandlerModel ToModel() =>
        new(
            HandlerClass.ToModel(),
            TargetType?.ToModel(),
            (HandlerKind)Kind,
            Behaviors.Select(static b => b.ToModel()).ToImmutableArray(),
            Order,
            StaticInvoker
        );
}

/// <summary>The JSON shape of <see cref="KeryxTypeReference" />.</summary>
internal sealed class TypeReferenceProjection
{
    public string Assembly { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Unbound { get; set; }
    public List<TypeReferenceProjection> Arguments { get; set; } = [];
    public List<int> ArrayRanks { get; set; } = [];

    public static TypeReferenceProjection From(KeryxTypeReference model) =>
        new()
        {
            Assembly = model.Assembly,
            Name = model.MetadataName,
            Unbound = model.Unbound,
            Arguments = [.. model.Arguments.Select(From)],
            ArrayRanks = [.. model.ArrayRanks],
        };

    public KeryxTypeReference ToModel() =>
        new(Assembly, Name, Unbound, Arguments.Select(static a => a.ToModel()).ToImmutableArray(), ArrayRanks.ToImmutableArray());
}

/// <summary>Source-generated serialization of the per-assembly projection, so the ScanCache involves no runtime reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(AssemblyProjection))]
internal sealed partial class KeryxProjectionJsonContext : JsonSerializerContext;
