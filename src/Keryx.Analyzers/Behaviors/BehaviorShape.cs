using System.Collections.Immutable;
using Keryx.Analyzers.Diagnostics;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;

namespace Keryx.Analyzers.Behaviors;

/// <summary>The kind of a behavior method, named by the kind attribute its class carries (ADR 0001, Behavior kinds).</summary>
internal enum BehaviorKind
{
    /// <summary><c>[RequestBehavior]</c>: wraps one <c>Send</c> of a request; <c>next</c> is <c>INext&lt;TResponse&gt;</c>.</summary>
    Request,

    /// <summary><c>[StreamBehavior]</c>: wraps the inner stream of a stream request; <c>next</c> is <c>IStreamNext&lt;TItem&gt;</c>.</summary>
    Stream,

    /// <summary><c>[NotificationBehavior]</c>: wraps one whole publish; <c>next</c> is <c>INext</c>.</summary>
    Notification,
}

/// <summary>What a behavior method parameter receives in a generated call.</summary>
internal enum BehaviorParameterRole
{
    /// <summary>The request, stream request or notification.</summary>
    Message,

    /// <summary>The link to the rest of the pipeline.</summary>
    Next,

    /// <summary>The call's <c>CancellationToken</c>.</summary>
    CancellationToken,

    /// <summary>An injected service, resolved once per scope into the pipeline context.</summary>
    Service,
}

/// <summary>
///     One behavior method Keryx can call (ADR 0001, Behavior methods): a <see langword="static" /> <c>Handle</c>/<c>HandleAsync</c> of a kind
///     its class carries, fully open or fully closed. Holds symbols, so it never reaches an incremental model.
/// </summary>
/// <param name="Method">The method.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Next">The <c>TNext</c> type parameter.</param>
/// <param name="Message">The message type: the message type parameter of an open method, the concrete type of a closed one.</param>
/// <param name="Response">
///     The response (request) or item (stream) type as its <c>next</c> carries it: for an open method, its response type parameter, bare
///     (<c>TResponse</c>), annotated (<c>TResponse?</c>, OQ-58) or, when it is constrained to <c>struct</c>, <c>Nullable&lt;TResponse&gt;</c>;
///     the concrete type of a closed one; <see langword="null" /> for a notification behavior method.
/// </param>
/// <param name="ResponseParameter">
///     The response (or item) type parameter of an open request or stream method, which <paramref name="Response" /> carries; otherwise
///     <see langword="null" />.
/// </param>
/// <param name="Roles">What each parameter receives, in declared order.</param>
internal sealed record BehaviorMethod(
    IMethodSymbol Method,
    BehaviorKind Kind,
    ITypeParameterSymbol Next,
    ITypeSymbol Message,
    ITypeSymbol? Response,
    ITypeParameterSymbol? ResponseParameter,
    ImmutableArray<BehaviorParameterRole> Roles
)
{
    /// <summary>Whether the method is fully open (its message type is a type parameter).</summary>
    public bool IsOpen => Message is ITypeParameterSymbol;
}

/// <summary>
///     A behavior class whose shape Keryx supports (ADR 0001, Behavior methods): a non-generic <see langword="static" /> class with kind
///     attributes and exactly one valid behavior method per kind it carries. Holds symbols, so it never reaches an incremental model.
/// </summary>
/// <param name="Type">The behavior class.</param>
/// <param name="Methods">Its behavior methods, one per kind it carries.</param>
internal sealed record BehaviorShape(INamedTypeSymbol Type, ImmutableArray<BehaviorMethod> Methods)
{
    /// <summary>The behavior method of a kind, or <see langword="null" /> when the class carries no such kind.</summary>
    public BehaviorMethod? Method(BehaviorKind kind)
    {
        foreach (var method in Methods)
        {
            if (method.Kind == kind)
            {
                return method;
            }
        }

        return null;
    }
}

/// <summary>
///     The behavior class rules (ADR 0001, Behavior methods): KRX019, and KRX013 and KRX018 on behavior method parameters, shared by the
///     analyzer, which reports them, and the host generator, which skips a behavior class they reject.
/// </summary>
internal static class BehaviorShapes
{
    /// <summary>The kind attributes a type carries, each once, in <see cref="BehaviorKind" /> order, with the attribute.</summary>
    public static ImmutableArray<(BehaviorKind Kind, AttributeData Attribute)> Kinds(ITypeSymbol type, KeryxSymbols symbols)
    {
        var kinds = new (BehaviorKind, AttributeData)?[3];
        foreach (var attribute in type.GetAttributes())
        {
            if (KindOfAttribute(attribute, symbols) is { } kind && kinds[(int)kind] is null)
            {
                kinds[(int)kind] = (kind, attribute);
            }
        }

        return [.. kinds.Where(static k => k is not null).Select(static k => k!.Value)];
    }

    /// <summary>Whether a type carries a kind attribute, which makes it a behavior class held to the behavior rules.</summary>
    public static bool IsBehaviorClass(ITypeSymbol type, KeryxSymbols symbols) => type.GetAttributes().Any(a => KindOfAttribute(a, symbols) is not null);

    /// <summary>The attribute's kind when it is a kind attribute.</summary>
    public static BehaviorKind? KindOfAttribute(AttributeData attribute, KeryxSymbols symbols) =>
        KeryxSymbols.IsAttribute(attribute, symbols.RequestBehaviorAttribute) ? BehaviorKind.Request
        : KeryxSymbols.IsAttribute(attribute, symbols.StreamBehaviorAttribute) ? BehaviorKind.Stream
        : KeryxSymbols.IsAttribute(attribute, symbols.NotificationBehaviorAttribute) ? BehaviorKind.Notification
        : null;

    /// <summary>The kind attribute's name, for messages.</summary>
    public static string AttributeName(BehaviorKind kind) =>
        kind switch
        {
            BehaviorKind.Request => "[RequestBehavior]",
            BehaviorKind.Stream => "[StreamBehavior]",
            _ => "[NotificationBehavior]",
        };

    /// <summary>The kind's name in messages: "request", "stream" or "notification".</summary>
    public static string KindName(BehaviorKind kind) =>
        kind switch
        {
            BehaviorKind.Request => "request",
            BehaviorKind.Stream => "stream",
            _ => "notification",
        };

    /// <summary>The <c>static</c> <c>Handle</c>/<c>HandleAsync</c> methods a class declares: its behavior method candidates.</summary>
    public static IEnumerable<IMethodSymbol> Candidates(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static m => m is { IsStatic: true, MethodKind: MethodKind.Ordinary, Name: "Handle" or "HandleAsync" });

    /// <summary>
    ///     The kind of a behavior method: the kind its <c>next</c> constraint names, when the method is a <see langword="static" />
    ///     <c>Handle</c>/<c>HandleAsync</c> of a class that carries that kind. <see langword="null" /> for any other method.
    /// </summary>
    public static BehaviorKind? BehaviorMethodKind(IMethodSymbol method, KeryxSymbols symbols) =>
        method is { IsStatic: true, MethodKind: MethodKind.Ordinary, Name: "Handle" or "HandleAsync", ContainingType: { } type }
     && FindNext(method, symbols) is { Count: 1 } next
     && next[0].Kind is { } kind
     && Kinds(type, symbols).Any(k => k.Kind == kind)
            ? kind
            : null;

    /// <summary>
    ///     The class's shape, or <see langword="null" /> when it is not a behavior class Keryx can use. Every rule it breaks is added to
    ///     <paramref name="issues" /> (KRX019, and KRX013 and KRX018 on behavior method parameters).
    /// </summary>
    /// <param name="type">The behavior class.</param>
    /// <param name="symbols">The compilation's Keryx symbols.</param>
    /// <param name="issues">Collects the rule violations; the host generator discards them.</param>
    public static BehaviorShape? Analyze(INamedTypeSymbol type, KeryxSymbols symbols, IssueCollector issues)
    {
        var kinds = Kinds(type, symbols);
        if (kinds.IsEmpty)
        {
            return null;
        }

        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var location = type.Locations.FirstOrDefault();
        var valid = true;

        void Shape(Location? at, string subject, string reason)
        {
            issues.Add(Descriptors.InvalidBehaviorShape, at ?? location, subject, reason);
            valid = false;
        }

        if (!type.IsStatic)
        {
            Shape(location, name, "is not static; a behavior is a static class whose behavior methods the pipeline calls directly");
        }

        if (MessageType.HasTypeParameters(type))
        {
            Shape(location, name, "is generic; a behavior method carries the type parameters, its class does not");
        }

        var methods = ImmutableArray.CreateBuilder<BehaviorMethod>();
        var counts = new int[3];
        foreach (var candidate in Candidates(type))
        {
            var methodName = $"{name}.{candidate.Name}";
            var methodLocation = candidate.Locations.FirstOrDefault();
            var problems = new List<string>();
            var method = AnalyzeMethod(candidate, kinds, symbols, problems, issues, out var kind);
            if (kind is { } counted && kinds.Any(k => k.Kind == counted))
            {
                counts[(int)counted]++;
            }

            foreach (var problem in problems)
            {
                Shape(methodLocation, methodName, problem);
            }

            if (method is null)
            {
                valid = false;
            }
            else
            {
                methods.Add(method);
            }
        }

        foreach (var (kind, attribute) in kinds)
        {
            if (counts[(int)kind] == 0)
            {
                Shape(
                    HandlerAnalysis.AttributeLocation(attribute),
                    name,
                    $"carries {AttributeName(kind)} but declares no {KindName(kind)} behavior method; add a static Handle or HandleAsync whose 'next' is a type parameter constrained 'struct, {NextName(kind)}'"
                );
            }
            else if (counts[(int)kind] > 1)
            {
                Shape(location, name, $"declares more than one {KindName(kind)} behavior method; a behavior class declares exactly one behavior method of each kind it carries");
            }
        }

        return valid ? new(type, methods.ToImmutable()) : null;
    }

    /// <summary>The <c>next</c> interface of a kind, for messages.</summary>
    public static string NextName(BehaviorKind kind) =>
        kind switch
        {
            BehaviorKind.Request => "INext<TResponse>",
            BehaviorKind.Stream => "IStreamNext<TItem>",
            _ => "INext",
        };

    private static string MessageConstraint(BehaviorKind kind, string message, string? response) =>
        kind switch
        {
            BehaviorKind.Request => $"where {message} : IRequest<{response}>",
            BehaviorKind.Stream => $"where {message} : IStreamRequest<{response}>",
            _ => $"where {message} : INotification",
        };

    private static string ReturnName(BehaviorKind kind, string? response) =>
        kind switch
        {
            BehaviorKind.Request => $"ValueTask<{response}>",
            BehaviorKind.Stream => $"IAsyncEnumerable<{response}>",
            _ => "ValueTask",
        };

    /// <summary>A <c>next</c> candidate: a parameter whose type is, or is constrained to, a <c>next</c> interface.</summary>
    private readonly record struct NextCandidate(IParameterSymbol Parameter, BehaviorKind? Kind, ITypeSymbol? Argument, string? Problem);

    /// <summary>
    ///     The parameters that look like <c>next</c>: typed as a method type parameter constrained to one of the <c>next</c> interfaces, or as
    ///     a <c>next</c> interface itself (which is wrong, but tells the method's kind).
    /// </summary>
    private static List<NextCandidate> FindNext(IMethodSymbol method, KeryxSymbols symbols)
    {
        var candidates = new List<NextCandidate>();
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } typeParameter
             && SymbolEqualityComparer.Default.Equals(typeParameter.DeclaringMethod?.OriginalDefinition, method.OriginalDefinition))
            {
                var found = NextConstraints(typeParameter, symbols);
                if (found.Count == 1)
                {
                    candidates.Add(
                        new(
                            parameter,
                            found[0].Kind,
                            found[0].Argument,
                            typeParameter.HasValueTypeConstraint
                                ? null
                                : $"has a 'next' type parameter '{typeParameter.Name}' without the 'struct' constraint; 'next' is the generated link struct, constrained 'struct, {NextName(found[0].Kind!.Value)}'"
                        )
                    );
                }
                else if (found.Count > 1)
                {
                    candidates.Add(new(parameter, null, null, $"constrains '{typeParameter.Name}' to more than one next interface; 'next' is constrained to exactly one"));
                }
            }
            else if (NextKind(parameter.Type, symbols) is { Kind: { } kind } next)
            {
                candidates.Add(
                    new(
                        parameter,
                        kind,
                        next.Argument,
                        $"takes 'next' as '{parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}'; 'next' is a method type parameter constrained 'struct, {NextName(kind)}'"
                    )
                );
            }
        }

        return candidates;
    }

    /// <summary>The <c>next</c> interfaces a type parameter is constrained to.</summary>
    private static List<(BehaviorKind? Kind, ITypeSymbol? Argument)> NextConstraints(ITypeParameterSymbol typeParameter, KeryxSymbols symbols)
    {
        var found = new List<(BehaviorKind? Kind, ITypeSymbol? Argument)>();
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (NextKind(constraint, symbols) is { Kind: not null } next)
            {
                found.Add(next);
            }
        }

        return found;
    }

    /// <summary>The behavior method of a kind among a class's candidates, whatever its shape, or <see langword="null" />.</summary>
    public static IMethodSymbol? CandidateOfKind(INamedTypeSymbol type, BehaviorKind kind, KeryxSymbols symbols)
    {
        foreach (var method in Candidates(type))
        {
            if (BehaviorMethodKind(method, symbols) == kind)
            {
                return method;
            }
        }

        return null;
    }

    private static (BehaviorKind? Kind, ITypeSymbol? Argument) NextKind(ITypeSymbol type, KeryxSymbols symbols) =>
        type is not INamedTypeSymbol named ? (null, null)
        : KeryxSymbols.Is(named, symbols.NextOfT) ? (BehaviorKind.Request, named.TypeArguments[0])
        : KeryxSymbols.Is(named, symbols.StreamNext) ? (BehaviorKind.Stream, named.TypeArguments[0])
        : KeryxSymbols.Is(named, symbols.Next) ? (BehaviorKind.Notification, null)
        : (null, null);

    /// <summary>
    ///     Analyzes one behavior method candidate. Returns the method when it is valid and its kind is carried; adds every shape problem to
    ///     <paramref name="problems" /> and every parameter problem (KRX013, KRX018) to <paramref name="issues" />.
    /// </summary>
    private static BehaviorMethod? AnalyzeMethod(
        IMethodSymbol method,
        ImmutableArray<(BehaviorKind Kind, AttributeData Attribute)> carried,
        KeryxSymbols symbols,
        List<string> problems,
        IssueCollector issues,
        out BehaviorKind? kind
    )
    {
        kind = null;
        var nextCandidates = FindNext(method, symbols);
        if (nextCandidates.Count == 0)
        {
            problems.Add(
                "has no 'next' parameter; a behavior method takes 'next' as a method type parameter constrained 'struct' and INext<TResponse> (request), IStreamNext<TItem> (stream) or INext (notification)"
            );
            return null;
        }

        if (nextCandidates.Count > 1)
        {
            problems.Add("has more than one 'next' parameter; a behavior method takes exactly one");
            return null;
        }

        var next = nextCandidates[0];
        if (next.Kind is not { } methodKind)
        {
            problems.Add(next.Problem!);
            return null;
        }

        kind = methodKind;
        if (!carried.Any(k => k.Kind == methodKind))
        {
            problems.Add(
                $"is a {KindName(methodKind)} behavior method (its 'next' is {NextName(methodKind)}), but the class does not carry {AttributeName(methodKind)}"
            );
            return null;
        }

        var valid = true;
        if (next.Problem is { } nextProblem)
        {
            problems.Add(nextProblem);
            return null;
        }

        var nextParameter = (ITypeParameterSymbol)next.Parameter.Type;

        // The message: the one parameter, besides next and the token, typed as a method type parameter or as a request, stream request or
        // notification type.
        var messages = method.Parameters
                             .Where(p => !SymbolEqualityComparer.Default.Equals(p, next.Parameter) && !symbols.IsCancellationToken(p.Type))
                             .Where(p => p.Type is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } || MessageType.Classify(p.Type, symbols).IsMessage)
                             .ToArray();
        if (messages.Length != 1)
        {
            problems.Add(
                messages.Length == 0
                    ? $"has no message parameter; a {KindName(methodKind)} behavior method takes the {MessageName(methodKind)}, as a type parameter or as a concrete type"
                    : $"has more than one message parameter ({string.Join(", ", messages.Select(static p => $"'{p.Name}'"))}); a behavior method takes exactly one"
            );
            return null;
        }

        var message = messages[0];
        var response = next.Argument;
        var others = method.TypeParameters.Where(t => !SymbolEqualityComparer.Default.Equals(t, nextParameter)).ToList();
        ITypeSymbol? responseType;
        ITypeParameterSymbol? openResponse = null;
        if (message.Type is ITypeParameterSymbol messageParameter)
        {
            // Fully open: the message and the response (or item) are type parameters of the method, and nothing else is. 'next' may carry
            // the response type parameter annotated, 'TResponse?' (OQ-58), which is Nullable<TResponse> when it is constrained to struct.
            others.Remove(messageParameter);
            if (methodKind != BehaviorKind.Notification)
            {
                if (ResponseParameterOf(response) is not { } responseParameter
                 || SymbolEqualityComparer.Default.Equals(responseParameter, messageParameter))
                {
                    problems.Add(
                        $"is partially open: its message is the type parameter '{messageParameter.Name}', but its 'next' carries '{Display(response)}'; an open behavior method's 'next' carries the response (or item) type parameter, as in {NextName(methodKind)}"
                    );
                    return null;
                }

                others.Remove(responseParameter);
                openResponse = responseParameter;
            }

            if (others.Count > 0)
            {
                problems.Add(
                    $"has type parameters other than its message, response and 'next' ones ({string.Join(", ", others.Select(static t => $"'{t.Name}'"))}); an open behavior method is generic only in those"
                );
                return null;
            }

            if (OpenMessageConstraint(messageParameter, response, methodKind, symbols) is { } constrainedTo)
            {
                problems.Add(
                    constrainedTo == methodKind
                        ? $"has an open message type parameter '{messageParameter.Name}' without the {KindName(methodKind)} message constraint; add '{MessageConstraint(methodKind, messageParameter.Name, response is null ? null : Display(response))}'"
                        : $"has a 'next' constraint and a message type of different kinds: '{messageParameter.Name}' is constrained to a {KindName(constrainedTo)} message, but its 'next' is {NextName(methodKind)}"
                );
                valid = false;
            }

            // 'TResponse?' in 'next', the return type or the message constraint is allowed (OQ-58): matching substitutes the annotated type
            // there and checks it, with its nullability, against the target.
            responseType = response;
        }
        else
        {
            // Fully closed: generic only in TNext, over a concrete message of the method's kind, whose next carries exactly its response.
            var classified = MessageType.Classify(message.Type, symbols);
            var messageKind = classified.IsRequest ? BehaviorKind.Request
                : classified.IsStream ? BehaviorKind.Stream
                : classified.IsNotificationOnly ? BehaviorKind.Notification
                : (BehaviorKind?)null;
            if (messageKind != methodKind)
            {
                problems.Add(
                    messageKind is { } otherKind
                        ? $"has a 'next' constraint and a message type of different kinds: '{Display(message.Type)}' is a {KindName(otherKind)} message, but its 'next' is {NextName(methodKind)}"
                        : $"takes '{Display(message.Type)}', which is not a {MessageName(methodKind)}"
                );
                return null;
            }

            if (response is not null && ContainsMethodTypeParameter(response))
            {
                problems.Add(
                    $"is partially open: its message '{Display(message.Type)}' is closed, but its 'next' carries '{Display(response)}'; a closed behavior method is generic only in 'next'"
                );
                return null;
            }

            if (others.Count > 0)
            {
                problems.Add(
                    $"has type parameters other than 'next' ({string.Join(", ", others.Select(static t => $"'{t.Name}'"))}); a closed behavior method is generic only in 'next'"
                );
                return null;
            }

            var expected = methodKind == BehaviorKind.Request ? classified.ResponseType : methodKind == BehaviorKind.Stream ? classified.ItemType : null;
            if (expected is not null
             && response is not null
             && ( !SymbolEqualityComparer.Default.Equals(response, expected) || HandlerAnalysis.DiffersInNullabilityOnly(response, expected) ))
            {
                problems.Add(
                    $"is closed over '{Display(message.Type)}', but its 'next' carries '{Display(response)}' instead of '{Display(expected)}'; a closed behavior method's 'next' carries exactly the {( methodKind == BehaviorKind.Stream ? "stream request's item" : "request's response" )} type"
                );
                return null;
            }

            responseType = expected;
        }

        if (!ReturnsKindType(method.ReturnType, methodKind, responseType, symbols))
        {
            problems.Add(
                $"returns '{Display(method.ReturnType)}'; a {KindName(methodKind)} behavior method returns {ReturnName(methodKind, responseType is null ? null : Display(responseType))}"
            );
            valid = false;
        }

        // Parameters (ADR 0001, Behavior methods: they mirror a handler's): passed by value, one token at most, and injected services that a
        // field can hold, with constant keys.
        var roles = ImmutableArray.CreateBuilder<BehaviorParameterRole>(method.Parameters.Length);
        var seenToken = false;
        foreach (var parameter in method.Parameters)
        {
            var parameterLocation = parameter.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault();
            var role = SymbolEqualityComparer.Default.Equals(parameter, next.Parameter) ? BehaviorParameterRole.Next
                : SymbolEqualityComparer.Default.Equals(parameter, message) ? BehaviorParameterRole.Message
                : symbols.IsCancellationToken(parameter.Type) ? BehaviorParameterRole.CancellationToken
                : BehaviorParameterRole.Service;
            roles.Add(role);

            void Parameter(string reason)
            {
                issues.Add(Descriptors.InvalidBehaviorParameterKind, parameterLocation, parameter.Name, Owner(method), reason);
                valid = false;
            }

            if (parameter.RefKind != RefKind.None)
            {
                Parameter($"is '{RefKindText(parameter.RefKind)}'; behavior method parameters are passed by value");
            }
            else if (parameter.IsParams)
            {
                Parameter("is 'params'; behavior method parameters are passed one by one");
            }

            if (role == BehaviorParameterRole.CancellationToken)
            {
                if (seenToken)
                {
                    Parameter("is a second CancellationToken; a behavior method takes at most one");
                }

                seenToken = true;
            }
            else if (role == BehaviorParameterRole.Service)
            {
                if (parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                {
                    Parameter("is a pointer, which a service cannot be");
                }
                else if (parameter.Type.IsRefLikeType)
                {
                    Parameter("is a ref struct, which the pipeline context cannot hold");
                }
                else if (Mentions(parameter.Type, nextParameter))
                {
                    problems.Add($"injects '{parameter.Name}' of a type that mentions '{nextParameter.Name}', the generated link, which no service can be");
                    valid = false;
                }

                foreach (var attribute in parameter.GetAttributes())
                {
                    if (!HandlerAnalysis.IsFromKeyedServices(attribute))
                    {
                        continue;
                    }

                    var attributeLocation = HandlerAnalysis.AttributeLocation(attribute) ?? parameterLocation;
                    if (attribute.ConstructorArguments.IsEmpty)
                    {
                        issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses [FromKeyedServices] without a key; give the key as a compile-time constant");
                        valid = false;
                    }
                    else if (attribute.ConstructorArguments[0].Kind is TypedConstantKind.Array or TypedConstantKind.Error)
                    {
                        issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses a [FromKeyedServices] key that is not a compile-time constant");
                        valid = false;
                    }
                }
            }
        }

        return valid ? new(method, methodKind, nextParameter, message.Type, responseType, openResponse, roles.MoveToImmutable()) : null;
    }

    /// <summary>
    ///     Checks an open method's message constraint: <see langword="null" /> when the message type parameter is constrained to the kind's
    ///     message interface (directly or through an interface that implements it); otherwise the kind whose message interface it is
    ///     constrained to, or <paramref name="kind" /> when it names none.
    /// </summary>
    private static BehaviorKind? OpenMessageConstraint(ITypeParameterSymbol message, ITypeSymbol? response, BehaviorKind kind, KeryxSymbols symbols)
    {
        BehaviorKind? other = null;
        foreach (var constraint in message.ConstraintTypes)
        {
            foreach (var candidate in constraint.AllInterfaces.Prepend(constraint as INamedTypeSymbol).OfType<INamedTypeSymbol>())
            {
                var candidateKind = KeryxSymbols.Is(candidate, symbols.RequestOfT) ? BehaviorKind.Request
                    : KeryxSymbols.Is(candidate, symbols.StreamRequest) ? BehaviorKind.Stream
                    : SymbolEqualityComparer.Default.Equals(candidate, symbols.Notification) ? BehaviorKind.Notification
                    : (BehaviorKind?)null;
                if (candidateKind is not { } found)
                {
                    continue;
                }

                if (found == kind && ( kind == BehaviorKind.Notification || SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], response) ))
                {
                    return null;
                }

                other ??= found == kind ? null : found;
            }
        }

        return other ?? kind;
    }

    /// <summary>
    ///     The response (or item) type parameter a <c>next</c> argument carries: a method type parameter, bare or annotated, or
    ///     <c>Nullable&lt;T&gt;</c> of one (<c>TResponse?</c> on a type parameter constrained to <c>struct</c>, OQ-58).
    /// </summary>
    internal static ITypeParameterSymbol? ResponseParameterOf(ITypeSymbol? response) =>
        response switch
        {
            ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter => parameter,
            INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter] } => parameter,
            _ => null,
        };

    private static bool ReturnsKindType(ITypeSymbol returnType, BehaviorKind kind, ITypeSymbol? response, KeryxSymbols symbols) =>
        kind switch
        {
            BehaviorKind.Request => KeryxSymbols.Is(returnType, symbols.ValueTaskOfT)
                                 && SymbolEqualityComparer.Default.Equals(( (INamedTypeSymbol)returnType ).TypeArguments[0], response),
            BehaviorKind.Stream => KeryxSymbols.Is(returnType, symbols.AsyncEnumerableOfT)
                                && SymbolEqualityComparer.Default.Equals(( (INamedTypeSymbol)returnType ).TypeArguments[0], response),
            _ => SymbolEqualityComparer.Default.Equals(returnType, symbols.ValueTask),
        };

    /// <summary>Whether a type is or contains a method type parameter.</summary>
    private static bool ContainsMethodTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } => true,
            IArrayTypeSymbol array => ContainsMethodTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsMethodTypeParameter) || ( named.ContainingType is { } containing && ContainsMethodTypeParameter(containing) ),
            _ => false,
        };

    /// <summary>Whether a type is or contains <paramref name="parameter" />.</summary>
    private static bool Mentions(ITypeSymbol type, ITypeParameterSymbol parameter) =>
        type switch
        {
            ITypeParameterSymbol other => SymbolEqualityComparer.Default.Equals(other, parameter),
            IArrayTypeSymbol array => Mentions(array.ElementType, parameter),
            IPointerTypeSymbol pointer => Mentions(pointer.PointedAtType, parameter),
            INamedTypeSymbol named => named.TypeArguments.Any(a => Mentions(a, parameter)) || ( named.ContainingType is { } containing && Mentions(containing, parameter) ),
            _ => false,
        };

    private static string MessageName(BehaviorKind kind) =>
        kind switch
        {
            BehaviorKind.Request => "request",
            BehaviorKind.Stream => "stream request",
            _ => "notification",
        };

    private static string RefKindText(RefKind refKind) =>
        refKind switch
        {
            RefKind.Out => "out",
            RefKind.In => "in",
            RefKind.RefReadOnlyParameter => "ref readonly",
            _ => "ref",
        };

    /// <summary>
    ///     How widely generated code can call a behavior: the narrower of its class's declared accessibility (file-local counts as narrower
    ///     than <c>internal</c>) and its method's.
    /// </summary>
    public static AccessLevel Access(INamedTypeSymbol type, IMethodSymbol? method) =>
        TypeAccessibility.Min(
            TypeAccessibility.OfDeclaration(type),
            method is null ? AccessLevel.Public : TypeAccessibility.OfDeclaredAccessibility(method.DeclaredAccessibility)
        );

    /// <summary>
    ///     What KRX016 names when a behavior is narrower than <paramref name="required" />: its class when the class is, else its behavior
    ///     method (<c>Class.Handle</c>); <see langword="null" /> when both are wide enough.
    /// </summary>
    public static string? NarrowerThan(INamedTypeSymbol type, IMethodSymbol? method, AccessLevel required) =>
        TypeAccessibility.OfDeclaration(type) < required ? type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
        : method is not null && TypeAccessibility.OfDeclaredAccessibility(method.DeclaredAccessibility) < required ? Owner(method)
        : null;

    /// <summary>The method as KRX013 names it: <c>Class.Handle</c>.</summary>
    internal static string Owner(IMethodSymbol method) => $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}.{method.Name}";

    private static string Display(ITypeSymbol? type) =>
        type?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)) ?? "nothing";
}
