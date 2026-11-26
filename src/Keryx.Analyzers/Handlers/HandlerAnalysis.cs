using System.Collections.Immutable;
using Keryx.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keryx.Analyzers.Handlers;

/// <summary>What a handler handles. Mirrors <c>Keryx.Internal.HandlerKind</c>, which the analyzers cannot reference.</summary>
internal enum HandlerKind
{
    Request,
    Stream,
    Notification,
}

/// <summary>How the handler's return value becomes the invoker's result (ADR 0001, Handlers, return adaptation).</summary>
internal enum ReturnShape
{
    /// <summary><c>ValueTask&lt;T&gt;</c>, returned as is.</summary>
    ValueTaskOfT,

    /// <summary><c>Task&lt;T&gt;</c>, wrapped in a <c>ValueTask&lt;T&gt;</c>.</summary>
    TaskOfT,

    /// <summary>Synchronous <c>T</c>, wrapped in a completed <c>ValueTask&lt;T&gt;</c>.</summary>
    Synchronous,

    /// <summary>Non-generic <c>ValueTask</c>: <c>UnitAdapter.ToUnit</c> for an <c>IRequest</c>, returned as is for a notification.</summary>
    ValueTask,

    /// <summary>Non-generic <c>Task</c>: <c>UnitAdapter.ToUnit</c> for an <c>IRequest</c>, wrapped in a <c>ValueTask</c> for a notification.</summary>
    Task,

    /// <summary>Synchronous <c>void</c> for an <c>IRequest</c>: called, then a completed <c>ValueTask&lt;Unit&gt;</c>.</summary>
    Void,

    /// <summary><c>IAsyncEnumerable&lt;T&gt;</c>, returned as is.</summary>
    AsyncEnumerable,
}

/// <summary>What one parameter of a handler is.</summary>
internal enum ParameterRole
{
    Message,
    CancellationToken,
    Injected,

    /// <summary>
    ///     A parameter that can be neither passed nor injected (KRX013: <c>ref</c>/<c>in</c>/<c>out</c>/<c>params</c>, a ref struct or a
    ///     pointer). It does not count as an injected service, so it never also reports KRX014 or KRX007.
    /// </summary>
    Invalid,
}

/// <summary>
///     The analysed signature of one handler candidate. Holds symbols, so it never reaches an incremental model; <see cref="HandlerModelFactory" />
///     projects it.
/// </summary>
internal sealed class HandlerSignature(IMethodSymbol method)
{
    public IMethodSymbol Method { get; } = method;
    public IParameterSymbol? MessageParameter { get; set; }
    public HandlerKind Kind { get; set; }

    /// <summary>The request's <c>TResponse</c> or the stream request's <c>TItem</c>; <see langword="null" /> for a notification.</summary>
    public ITypeSymbol? ResponseType { get; set; }

    public ReturnShape ReturnShape { get; set; }

    /// <summary>Whether the handler's result type differs from <see cref="ResponseType" /> in nullability only (KRX017).</summary>
    public bool NullabilityDiffers { get; set; }

    public List<(IParameterSymbol Parameter, ParameterRole Role)> Parameters { get; } = [];

    public IEnumerable<IParameterSymbol> InjectedParameters =>
        Parameters.Where(static p => p.Role == ParameterRole.Injected).Select(static p => p.Parameter);

    public bool HasInjectedParameters => Parameters.Any(static p => p.Role == ParameterRole.Injected);
}

/// <summary>
///     The per-assembly handler rules (ADR 0001, Handlers; Diagnostics and code fixes), shared by the analyzers, which report them, and the
///     handler generator, which skips anything they reject.
/// </summary>
internal static class HandlerAnalysis
{
    public const string HandleName = "Handle";
    public const string HandleAsyncName = "HandleAsync";
    public const string InvokerName = "InvokeAsync";
    public const string ReservedFieldPrefix = "__keryx_";

    /// <summary>
    ///     Whether a method is a handler candidate (ADR 0001, Which methods are handler candidates). Every other method is ignored.
    /// </summary>
    public static bool IsCandidate(IMethodSymbol method, KeryxSymbols symbols)
    {
        if (method is not { MethodKind: MethodKind.Ordinary, Name: HandleName or HandleAsyncName, IsOverride: false, IsAbstract: false, IsGenericMethod: false }
         || !method.ExplicitInterfaceImplementations.IsEmpty
         || method.ContainingType is not { } containingType
         || ImplementsInterfaceMember(method, containingType))
        {
            return false;
        }

        var hasMessageParameter = false;
        foreach (var parameter in method.Parameters)
        {
            if (!MessageType.Classify(parameter.Type, symbols).IsMessage)
            {
                continue;
            }

            // A ref struct request or notification is KRX012, on the type, and never a handler parameter.
            if (parameter.Type.IsRefLikeType)
            {
                return false;
            }

            hasMessageParameter = true;
        }

        return hasMessageParameter || ( method.IsStatic && containingType.GetTypeMembers().Any(t => IsNestedRequest(t, symbols)) );
    }

    /// <summary>
    ///     The concrete request or stream request type a handler candidate takes, whether or not the candidate breaks other rules, or
    ///     <see langword="null" /> when it takes a notification or has no single concrete request parameter. KRX001, KRX005 and KRX030 count
    ///     a malformed handler as handling its request, so the request does not get a second error on top of the handler's.
    /// </summary>
    public static ITypeSymbol? HandledRequestType(IMethodSymbol handler, KeryxSymbols symbols)
    {
        MessageType? single = null;
        foreach (var parameter in handler.Parameters)
        {
            var messageType = MessageType.Classify(parameter.Type, symbols);
            if (!messageType.IsMessage)
            {
                continue;
            }

            if (single is not null)
            {
                return null;
            }

            single = messageType;
        }

        return single is { IsConcrete: true } request && ( request.IsRequest || request.IsStream ) ? request.Type : null;
    }

    /// <summary>
    ///     The handler candidates among a type's direct members, in declaration order.
    /// </summary>
    public static ImmutableArray<IMethodSymbol> GetCandidates(INamedTypeSymbol type, KeryxSymbols symbols)
    {
        var builder = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var member in type.GetMembers(HandleName).Concat(type.GetMembers(HandleAsyncName)))
        {
            if (member is IMethodSymbol method && IsCandidate(method, symbols))
            {
                builder.Add(method);
            }
        }

        builder.Sort(static (left, right) => CompareLocations(left, right));
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Whether the type is a handler class or contains one, at any depth: Keryx generates into it, so it must be partial (KRX004).
    /// </summary>
    public static bool IsOrContainsHandlerClass(INamedTypeSymbol type, KeryxSymbols symbols) =>
        !GetCandidates(type, symbols).IsEmpty || type.GetTypeMembers().Any(nested => IsOrContainsHandlerClass(nested, symbols));

    /// <summary>
    ///     The method-level rules: KRX002, KRX003, KRX013, KRX016 (injected and polymorphic notification parameters), KRX017, KRX018.
    /// </summary>
    public static HandlerSignature AnalyzeMethod(IMethodSymbol method, KeryxSymbols symbols, IssueCollector issues)
    {
        var signature = new HandlerSignature(method);
        var methodName = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var methodLocation = method.Locations.FirstOrDefault();

        if (method.DeclaredAccessibility != Accessibility.Private || !method.IsStatic)
        {
            issues.Add(
                new(
                    Descriptors.InvalidHandlerSignature,
                    methodLocation,
                    false,
                    ImmutableDictionary<string, string?>.Empty.Add(Descriptors.ReasonProperty, Descriptors.ModifiersReason),
                    methodName,
                    "must be declared 'private static'"
                )
            );
        }

        var messageParameters = method.Parameters.Where(p => MessageType.Classify(p.Type, symbols).IsMessage).ToArray();
        if (messageParameters.Length == 0)
        {
            issues.Add(Descriptors.InvalidHandlerParameter, methodLocation, methodName, "has no request or notification parameter");
        }
        else if (messageParameters.Length > 1)
        {
            issues.Add(
                Descriptors.InvalidHandlerParameter,
                messageParameters[1].Locations.FirstOrDefault() ?? methodLocation,
                methodName,
                "has more than one request or notification parameter"
            );
        }
        else
        {
            AnalyzeMessageParameter(signature, messageParameters[0], symbols, issues, methodName);
        }

        var seenCancellationToken = false;
        foreach (var parameter in method.Parameters)
        {
            var role = messageParameters.Contains(parameter, SymbolEqualityComparer.Default)
                ? ParameterRole.Message
                : symbols.IsCancellationToken(parameter.Type)
                    ? ParameterRole.CancellationToken
                    : ParameterRole.Injected;

            var location = parameter.Locations.FirstOrDefault() ?? methodLocation;
            var invalid = false;
            if (parameter.RefKind != RefKind.None)
            {
                issues.Add(Descriptors.InvalidHandlerParameterKind, location, parameter.Name, $"is '{RefKindText(parameter.RefKind)}'; handler parameters are passed by value");
                invalid = true;
            }

            if (parameter.IsParams)
            {
                issues.Add(Descriptors.InvalidHandlerParameterKind, location, parameter.Name, "is 'params'; handler parameters are passed one by one");
                invalid = true;
            }

            if (role == ParameterRole.Injected && UnstorableKind(parameter.Type) is { } unstorable)
            {
                // OQ-30: an injected service is stored in a field of the handler class, which a ref struct or a pointer cannot be.
                issues.Add(
                    Descriptors.InvalidHandlerParameterKind,
                    location,
                    parameter.Name,
                    $"is {unstorable}; an injected service is stored in a field of the handler class"
                );
                invalid = true;
            }

            if (invalid && role != ParameterRole.Message)
            {
                role = ParameterRole.Invalid;
            }

            signature.Parameters.Add((parameter, role));

            if (role == ParameterRole.CancellationToken)
            {
                if (seenCancellationToken)
                {
                    issues.Add(Descriptors.InvalidHandlerParameterKind, location, parameter.Name, "is a second CancellationToken; a handler takes at most one");
                }

                seenCancellationToken = true;
            }
            else if (role == ParameterRole.Injected)
            {
                AnalyzeInjectedParameter(parameter, location, issues);
            }
        }

        if (symbols.BehaviorAttribute is { } behaviorAttribute)
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (KeryxSymbols.IsAttribute(attribute, behaviorAttribute)
                 && attribute.ConstructorArguments is [{ Value: ITypeSymbol behaviorType }]
                 && TypeAccessibility.Of(behaviorType) != AccessLevel.Public)
                {
                    issues.Add(
                        new(
                            Descriptors.NotEffectivelyPublic,
                            AttributeLocation(attribute) ?? methodLocation,
                            true,
                            null,
                            behaviorType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                            Descriptors.Public,
                            $"the handler '{methodName}' attaches it and this assembly is not a host"
                        )
                    );
                }
            }
        }

        AnalyzeHostReach(signature, methodName, issues);
        return signature;
    }

    /// <summary>
    ///     OQ-24: in a host, the generated mediator and <c>AddKeryx()</c> live outside the handler class and name the request, its response
    ///     and every injected type, so each must be at least <c>internal</c>. A type narrower than that is KRX016 in a host (the handler is
    ///     skipped, and its request still counts as handled, so it does not also get KRX030). Outside a host the outside-host KRX016 rules
    ///     already report these types as not public.
    /// </summary>
    private static void AnalyzeHostReach(HandlerSignature signature, string methodName, IssueCollector issues)
    {
        const string Reason = "the host's generated mediator and AddKeryx(), outside the handler class, use it";
        if (signature.MessageParameter is { } message && TypeAccessibility.Of(message.Type) == AccessLevel.Restricted)
        {
            AddHostOnly(message.Locations.FirstOrDefault() ?? signature.Method.Locations.FirstOrDefault(), message.Type, $"the handler '{methodName}' takes it and {Reason}");
        }

        if (signature.ResponseType is { } response && TypeAccessibility.Of(response) == AccessLevel.Restricted)
        {
            AddHostOnly(ReturnTypeLocation(signature.Method) ?? signature.Method.Locations.FirstOrDefault(), response, $"the handler '{methodName}' responds with it and {Reason}");
        }

        foreach (var parameter in signature.InjectedParameters)
        {
            if (TypeAccessibility.Of(parameter.Type) == AccessLevel.Restricted)
            {
                AddHostOnly(
                    parameter.Locations.FirstOrDefault() ?? signature.Method.Locations.FirstOrDefault(),
                    parameter.Type,
                    $"the handler injects it through parameter '{parameter.Name}' and {Reason}"
                );
            }
        }

        void AddHostOnly(Location? location, ITypeSymbol type, string reason) =>
            issues.Add(
                new(
                    Descriptors.NotEffectivelyPublic,
                    location,
                    false,
                    null,
                    type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    Descriptors.AtLeastInternal,
                    reason
                )
                { HostOnly = true }
            );
    }

    /// <summary>
    ///     The class-level rules for a type with handler candidates: KRX004 (not a class), KRX006, KRX007, KRX012 (handler class), KRX014,
    ///     KRX015.
    /// </summary>
    public static void AnalyzeClass(INamedTypeSymbol type, IReadOnlyList<HandlerSignature> handlers, KeryxSymbols symbols, IssueCollector issues)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var typeLocation = type.Locations.FirstOrDefault();

        if (type.TypeKind != TypeKind.Class)
        {
            var kind = type switch
            {
                { TypeKind: TypeKind.Struct, IsRecord: true } => "a record struct",
                { TypeKind: TypeKind.Struct } => "a struct",
                { TypeKind: TypeKind.Interface } => "an interface",
                _ => "a " + type.TypeKind.ToString().ToLowerInvariant(),
            };
            issues.Add(Descriptors.InvalidHandlerClass, typeLocation, typeName, $"is a handler class, so it must be a class or record class, not {kind}");
        }

        if (handlers.Count > 1)
        {
            foreach (var handler in handlers)
            {
                issues.Add(Descriptors.MultipleHandlersInClass, handler.Method.Locations.FirstOrDefault(), typeName);
            }
        }

        if (MessageType.HasTypeParameters(type))
        {
            issues.Add(Descriptors.UnsupportedTypeShape, typeLocation, typeName, "is a handler class, so it cannot have type parameters or be nested in a generic type");
        }

        var injected = handlers.SelectMany(static h => h.InjectedParameters).ToArray();
        if (type.IsStatic && injected.Length > 0)
        {
            issues.Add(Descriptors.StaticHandlerClassWithDependencies, typeLocation, typeName, string.Join(", ", injected.Select(static p => $"'{p.Name}'")));
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.IsImplicitlyDeclared || KeryxSymbols.IsKeryxGenerated(constructor))
            {
                continue;
            }

            issues.Add(
                Descriptors.InvalidHandlerConstructor,
                constructor.Locations.FirstOrDefault() ?? typeLocation,
                typeName,
                IsPrimaryConstructor(constructor)
                    ? "declares a primary constructor; Keryx generates the constructor of a handler class from the handler's injected parameters"
                    : "declares an instance constructor; Keryx generates the constructor of a handler class from the handler's injected parameters"
            );
        }

        if (!type.IsStatic
         && type.TypeKind == TypeKind.Class
         && injected.Length > 0
         && type.BaseType is { SpecialType: not SpecialType.System_Object } baseType
         && !baseType.InstanceConstructors.Any(c => c.Parameters.All(static p => p.IsOptional || p.IsParams)
                                                  && symbols.Compilation.IsSymbolAccessibleWithin(c, type)))
        {
            issues.Add(
                Descriptors.InvalidHandlerConstructor,
                typeLocation,
                typeName,
                $"has injected parameters, but its base type '{baseType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}' has no accessible parameterless constructor for the generated constructor to call"
            );
        }

        if (!type.IsStatic && type.TypeKind == TypeKind.Class && injected.Length > 0)
        {
            // OQ-34: the host constructs a handler class with injected parameters with `new` in a generated factory.
            if (type.IsAbstract)
            {
                issues.Add(
                    Descriptors.InvalidHandlerConstructor,
                    typeLocation,
                    typeName,
                    "has injected parameters, so a host constructs it, but it is abstract"
                );
            }

            if (RequiredMembers(type) is { Count: > 0 } required)
            {
                issues.Add(
                    Descriptors.InvalidHandlerConstructor,
                    typeLocation,
                    typeName,
                    $"has injected parameters, so a host constructs it, but it has required members ({string.Join(", ", required.Select(static m => $"'{m}'"))}) that the generated constructor does not set"
                );
            }
        }

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared
             || ( member.Name != InvokerName && !member.Name.StartsWith(ReservedFieldPrefix, StringComparison.Ordinal) )
             || KeryxSymbols.IsKeryxGenerated(member))
            {
                continue;
            }

            issues.Add(Descriptors.ReservedHandlerMember, member.Locations.FirstOrDefault() ?? typeLocation, typeName, member.Name);
        }
    }

    /// <summary>
    ///     The KRX004 rules for one type that is or contains a handler class: <c>partial</c> always, <c>public</c> outside a host, at least
    ///     <c>internal</c> in a host (OQ-29), never file-local.
    /// </summary>
    public static void AnalyzeContainingChainType(INamedTypeSymbol type, bool isHandlerClass, IssueCollector issues)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        var location = type.Locations.FirstOrDefault();
        var subject = isHandlerClass ? "Keryx generates the other half of this handler class" : "it contains a handler class Keryx generates into";

        if (!IsPartial(type))
        {
            issues.Add(
                new(
                    Descriptors.InvalidHandlerClass,
                    location,
                    false,
                    ImmutableDictionary<string, string?>.Empty.Add(Descriptors.AddPartialProperty, "true"),
                    typeName,
                    $"must be partial, because {subject}"
                )
            );
        }

        if (type.IsFileLocal)
        {
            issues.Add(Descriptors.InvalidHandlerClass, location, typeName, $"cannot be file-local, because {subject} from another file");
        }

        if (TypeAccessibility.OfDeclaredAccessibility(type.DeclaredAccessibility) == AccessLevel.Restricted)
        {
            // OQ-29: generated host code outside the class reaches a handler class, so even a host cannot keep it private or protected.
            issues.Add(
                new(
                    Descriptors.InvalidHandlerClass,
                    location,
                    false,
                    ImmutableDictionary<string, string?>.Empty.Add(Descriptors.MakePublicProperty, "true"),
                    typeName,
                    isHandlerClass
                        ? "must be public (a host may keep it internal, but not private or protected), because generated code outside the class uses its handler"
                        : "must be public (a host may keep it internal, but not private or protected), because it contains a handler class that generated code outside it uses"
                )
            );
        }
        else if (type.DeclaredAccessibility != Accessibility.Public)
        {
            issues.Add(
                new(
                    Descriptors.InvalidHandlerClass,
                    location,
                    true,
                    ImmutableDictionary<string, string?>.Empty.Add(Descriptors.MakePublicProperty, "true"),
                    typeName,
                    isHandlerClass
                        ? "must be public, because this assembly is not a host and a host uses its handler classes"
                        : "must be public, because it contains a handler class, this assembly is not a host and a host uses its handler classes"
                )
            );
        }
    }

    /// <summary>
    ///     Analyses one handler candidate the way the generator needs it: every rule that decides whether Keryx can generate its partial
    ///     (chain, class and method rules; not the compilation-wide KRX001/KRX005).
    /// </summary>
    public static HandlerSignature AnalyzeForGeneration(IMethodSymbol method, KeryxSymbols symbols, IssueCollector issues)
    {
        var type = method.ContainingType;
        var candidates = GetCandidates(type, symbols);
        if (candidates.Length != 1)
        {
            // KRX006: two InvokeAsync methods cannot be generated into one class.
            issues.Invalidate();
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            AnalyzeContainingChainType(current, SymbolEqualityComparer.Default.Equals(current, type), issues);
        }

        var signature = AnalyzeMethod(method, symbols, issues);
        AnalyzeClass(type, [signature], symbols, issues);
        return signature;
    }

    /// <summary>The required fields and properties of a type and its base types, by name, nearest first.</summary>
    private static List<string> RequiredMembers(INamedTypeSymbol type)
    {
        var names = new List<string>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true } && !names.Contains(member.Name))
                {
                    names.Add(member.Name);
                }
            }
        }

        return names;
    }

    public static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    public static Location? AttributeLocation(AttributeData attribute) => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

    /// <summary>
    ///     Whether two types are equal and differ only in nullable annotations. Oblivious annotations (from <c>#nullable disable</c> code)
    ///     match anything.
    /// </summary>
    public static bool DiffersInNullabilityOnly(ITypeSymbol actual, ITypeSymbol expected) =>
        SymbolEqualityComparer.Default.Equals(actual, expected) && NullabilityDiffers(actual, expected);

    private static bool NullabilityDiffers(ITypeSymbol actual, ITypeSymbol expected)
    {
        return ( actual.NullableAnnotation != NullableAnnotation.None
                && expected.NullableAnnotation != NullableAnnotation.None
                && actual.NullableAnnotation != expected.NullableAnnotation
                && !actual.IsValueType )
            || (actual, expected) switch
            {
                (IArrayTypeSymbol left, IArrayTypeSymbol right) => NullabilityDiffers(left.ElementType, right.ElementType),
                (INamedTypeSymbol left, INamedTypeSymbol right) => left.TypeArguments.Zip(right.TypeArguments, NullabilityDiffers).Any(static d => d),
                _ => false,
            };
    }

    private static void AnalyzeMessageParameter(
        HandlerSignature signature,
        IParameterSymbol parameter,
        KeryxSymbols symbols,
        IssueCollector issues,
        string methodName
    )
    {
        signature.MessageParameter = parameter;
        var method = signature.Method;
        var messageType = MessageType.Classify(parameter.Type, symbols);
        var parameterLocation = parameter.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault();
        var parameterTypeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

        if (messageType.IsConflicting || ( messageType.IsConcrete && messageType.IsUnsupportedShape ))
        {
            // KRX011/KRX012 are reported on the type itself.
            issues.Invalidate();
            return;
        }

        if (messageType.IsNotificationOnly)
        {
            signature.Kind = HandlerKind.Notification;
            if (parameter.Type.TypeKind == TypeKind.TypeParameter)
            {
                issues.Invalidate();
                return;
            }

            if (TypeAccessibility.Of(parameter.Type) != AccessLevel.Public && !messageType.IsConcrete)
            {
                issues.Add(
                    new(
                        Descriptors.NotEffectivelyPublic,
                        parameterLocation,
                        true,
                        null,
                        parameterTypeName,
                        Descriptors.Public,
                        $"the notification handler '{methodName}' takes it and this assembly is not a host"
                    )
                );
            }

            AnalyzeNotificationReturn(signature, symbols, issues, methodName);
            return;
        }

        signature.Kind = messageType.IsStream ? HandlerKind.Stream : HandlerKind.Request;
        signature.ResponseType = messageType.IsStream ? messageType.ItemType : messageType.ResponseType;
        if (!messageType.IsConcrete)
        {
            issues.Add(
                Descriptors.InvalidHandlerParameter,
                parameterLocation,
                methodName,
                $"must take the concrete request type, not '{parameterTypeName}'; dispatch is by exact request type"
            );
            return;
        }

        if (signature.Kind == HandlerKind.Stream)
        {
            AnalyzeStreamReturn(signature, symbols, issues, methodName, parameterTypeName);
        }
        else
        {
            AnalyzeRequestReturn(signature, symbols, issues, methodName, parameterTypeName);
        }
    }

    private static void AnalyzeRequestReturn(HandlerSignature signature, KeryxSymbols symbols, IssueCollector issues, string methodName, string requestName)
    {
        var method = signature.Method;
        var response = signature.ResponseType!;
        var isUnit = symbols.IsUnit(response);
        var returnType = method.ReturnType;
        ITypeSymbol? result = null;

        if (method.ReturnsVoid)
        {
            if (isUnit && !method.IsAsync)
            {
                signature.ReturnShape = ReturnShape.Void;
                return;
            }
        }
        else if (SymbolEqualityComparer.Default.Equals(returnType, response))
        {
            signature.ReturnShape = ReturnShape.Synchronous;
            result = returnType;
        }
        else if (KeryxSymbols.Is(returnType, symbols.ValueTaskOfT) && SymbolEqualityComparer.Default.Equals(TypeArgument(returnType), response))
        {
            signature.ReturnShape = ReturnShape.ValueTaskOfT;
            result = TypeArgument(returnType);
        }
        else if (KeryxSymbols.Is(returnType, symbols.TaskOfT) && SymbolEqualityComparer.Default.Equals(TypeArgument(returnType), response))
        {
            signature.ReturnShape = ReturnShape.TaskOfT;
            result = TypeArgument(returnType);
        }
        else if (isUnit && SymbolEqualityComparer.Default.Equals(returnType, symbols.ValueTask))
        {
            signature.ReturnShape = ReturnShape.ValueTask;
            return;
        }
        else if (isUnit && SymbolEqualityComparer.Default.Equals(returnType, symbols.Task))
        {
            signature.ReturnShape = ReturnShape.Task;
            return;
        }

        if (result is null)
        {
            var responseName = response.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            var allowed = isUnit
                ? $"ValueTask<{responseName}>, Task<{responseName}>, {responseName}, ValueTask, Task or void (not async void)"
                : $"ValueTask<{responseName}>, Task<{responseName}> or {responseName}";
            AddReturnTypeIssue(signature, issues, methodName, $"returns '{ReturnTypeText(method)}', but a handler of request '{requestName}' returns {allowed}");
            return;
        }

        CheckNullability(signature, result, issues, methodName, requestName);
    }

    private static void AnalyzeStreamReturn(HandlerSignature signature, KeryxSymbols symbols, IssueCollector issues, string methodName, string requestName)
    {
        var method = signature.Method;
        var item = signature.ResponseType!;
        if (KeryxSymbols.Is(method.ReturnType, symbols.AsyncEnumerableOfT) && SymbolEqualityComparer.Default.Equals(TypeArgument(method.ReturnType), item))
        {
            signature.ReturnShape = ReturnShape.AsyncEnumerable;
            CheckNullability(signature, TypeArgument(method.ReturnType), issues, methodName, requestName);
            return;
        }

        AddReturnTypeIssue(
            signature,
            issues,
            methodName,
            $"returns '{ReturnTypeText(method)}', but a handler of stream request '{requestName}' returns IAsyncEnumerable<{item.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)}>"
        );
    }

    private static void AnalyzeNotificationReturn(HandlerSignature signature, KeryxSymbols symbols, IssueCollector issues, string methodName)
    {
        var returnType = signature.Method.ReturnType;
        if (SymbolEqualityComparer.Default.Equals(returnType, symbols.ValueTask))
        {
            signature.ReturnShape = ReturnShape.ValueTask;
        }
        else if (SymbolEqualityComparer.Default.Equals(returnType, symbols.Task))
        {
            signature.ReturnShape = ReturnShape.Task;
        }
        else
        {
            AddReturnTypeIssue(signature, issues, methodName, $"returns '{ReturnTypeText(signature.Method)}', but a notification handler returns ValueTask or Task");
        }
    }

    private static void CheckNullability(HandlerSignature signature, ITypeSymbol result, IssueCollector issues, string methodName, string requestName)
    {
        if (!DiffersInNullabilityOnly(result, signature.ResponseType!))
        {
            return;
        }

        signature.NullabilityDiffers = true;
        issues.Add(
            Descriptors.ResponseNullabilityMismatch,
            ReturnTypeLocation(signature.Method) ?? signature.Method.Locations.FirstOrDefault(),
            methodName,
            result.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
            requestName,
            signature.ResponseType!.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier))
        );
    }

    private static void AddReturnTypeIssue(HandlerSignature signature, IssueCollector issues, string methodName, string reason) =>
        issues.Add(
            new(
                Descriptors.InvalidHandlerSignature,
                ReturnTypeLocation(signature.Method) ?? signature.Method.Locations.FirstOrDefault(),
                false,
                ImmutableDictionary<string, string?>.Empty.Add(Descriptors.ReasonProperty, Descriptors.ReturnTypeReason),
                methodName,
                signature.Method.IsAsync && signature.Method.ReturnsVoid ? "cannot be 'async void'" : reason
            )
        );

    private static void AnalyzeInjectedParameter(IParameterSymbol parameter, Location? location, IssueCollector issues)
    {
        if (TypeAccessibility.Of(parameter.Type) != AccessLevel.Public)
        {
            issues.Add(
                new(
                    Descriptors.NotEffectivelyPublic,
                    location,
                    true,
                    null,
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    Descriptors.Public,
                    $"the handler injects it through parameter '{parameter.Name}', the generated public constructor takes it, and this assembly is not a host"
                )
            );
        }

        foreach (var attribute in parameter.GetAttributes())
        {
            if (!IsFromKeyedServices(attribute))
            {
                continue;
            }

            var attributeLocation = AttributeLocation(attribute) ?? location;
            if (attribute.ConstructorArguments.IsEmpty)
            {
                issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses [FromKeyedServices] without a key; give the key as a compile-time constant");
            }
            else if (attribute.ConstructorArguments[0].Kind is TypedConstantKind.Array or TypedConstantKind.Error)
            {
                issues.Add(Descriptors.InvalidServiceKey, attributeLocation, parameter.Name, "uses a [FromKeyedServices] key that is not a compile-time constant");
            }
        }
    }

    public static bool IsFromKeyedServices(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() == WellKnownTypeNames.FromKeyedServicesAttribute;

    private static bool IsNestedRequest(INamedTypeSymbol nested, KeryxSymbols symbols)
    {
        var messageType = MessageType.Classify(nested, symbols);
        return messageType.RequestClosings + messageType.StreamClosings > 0 && messageType.IsConcrete;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        foreach (var @interface in containingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(method.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(member), method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPrimaryConstructor(IMethodSymbol constructor) =>
        constructor.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is TypeDeclarationSyntax);

    private static ITypeSymbol TypeArgument(ITypeSymbol type) => ( (INamedTypeSymbol)type ).TypeArguments[0];

    private static string ReturnTypeText(IMethodSymbol method) =>
        method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private static Location? ReturnTypeLocation(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is MethodDeclarationSyntax declaration ? declaration.ReturnType.GetLocation() : null;

    /// <summary>What makes a type impossible to store in a field of a class, or <see langword="null" /> when it can be stored.</summary>
    private static string? UnstorableKind(ITypeSymbol type) =>
        type switch
        {
            { TypeKind: TypeKind.Pointer } => "a pointer",
            { TypeKind: TypeKind.FunctionPointer } => "a function pointer",
            { IsRefLikeType: true } => "a ref struct",
            _ => null,
        };

    private static string RefKindText(RefKind refKind) =>
        refKind switch
        {
            RefKind.Ref => "ref",
            RefKind.Out => "out",
            RefKind.In => "in",
            RefKind.RefReadOnlyParameter or RefKind.None => "ref readonly",
            _ => "ref readonly",
        };

    private static int CompareLocations(ISymbol left, ISymbol right)
    {
        var leftLocation = left.Locations.FirstOrDefault();
        var rightLocation = right.Locations.FirstOrDefault();
        var byFile = string.CompareOrdinal(leftLocation?.SourceTree?.FilePath, rightLocation?.SourceTree?.FilePath);
        return byFile != 0 ? byFile : ( leftLocation?.SourceSpan.Start ?? 0 ).CompareTo(rightLocation?.SourceSpan.Start ?? 0);
    }
}
