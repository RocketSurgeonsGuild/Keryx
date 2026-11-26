# Future requirements

Designs that were part of [ADR 0001](adr/0001-keryx-mediator.md) and were removed from the current design, kept here so they can come
back if a requirement needs them. Nothing here is implemented. The ADR is authoritative for the current design; bringing a design back
is a new decision, recorded in [open-questions.md](adr/open-questions.md) and folded into the ADR.

## Requests handled in another assembly

Removed 2026-11-26 (owner decision; [open-questions "Requests are handled in their own
assembly"](adr/open-questions.md#requests-are-handled-in-their-own-assembly-2026-11-26)). The current rule: a request's or stream request's
one handler lives in the assembly that declares the request. KRX001 (a request without a handler in its assembly) applies in every assembly,
hosts included, and a handler of a request declared in another assembly is KRX023. Notifications are not affected: they may be declared
anywhere and handled in any assembly. Host KRX030 stays as a safety net for referenced requests (a handler the host excludes, or an internal
handler of a referenced host).

Two attributes existed to let a request be declared in one assembly and handled in another. Both are removed, with everything tied to
them: their `KnownTypeNames` and `KeryxSymbols` entries, `KeryxSymbols.IsContracts`, the KRX001 code-fix action "Mark this assembly as a
contracts assembly", the host's `ExternallyHandled` model and its KRX030 filtering, the `ExternallyHandled` reasons of KRX036 and its
mention in KRX043, and their tests and documentation.

### `[assembly: KeryxContracts]`

```csharp
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KeryxContractsAttribute : Attribute;
```

- **Purpose:** mark a contracts assembly, one that declares requests (and notifications) whose handlers live in another assembly. KRX001
  was not reported there. The draft ADR's answer to "a request in a contracts assembly triggers MED001 there" (ADR, Answered open
  questions) was this attribute, with the host as the authority: KRX030 reported any request visible to the host with no handler.
- **Rules:** KRX001 was not reported in an assembly with the attribute, nor in a host, where KRX030 covered the host's own request types
  (from its syntax pipeline) and every referenced one. Every project that referenced the `Keryx` package still ran the generator,
  including contracts projects (Package and assemblies). The attribute was read only by the analyzers; the generators ignored it.
- **Code fix:** KRX001 had a second action, _Mark this assembly as a contracts assembly_, which added `[assembly: KeryxContracts]` to the
  document (fully qualified, simplified by the formatter). It never appeared in a host, because KRX001 was never reported there.
- **Samples:** `Keryx.Samples.Contracts` (`netstandard2.0`) declared the order requests and notifications with the attribute, and
  `Keryx.Samples.Orders` handled them.

### `[assembly: ExternallyHandled(typeof(X))]`

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ExternallyHandledAttribute(Type requestType) : Attribute { public Type RequestType { get; } = requestType; }
```

- **Purpose:** in a host, name a request or stream request type that is handled outside this host (for example out of process), so the
  host does not report KRX030 for it. One attribute per type; every other request kept the error. It replaced the per-assembly relief
  proposed in OQ-10 (`[assembly: KeryxIgnoreUnhandledFrom(typeof(MarkerInThatAssembly))]`), which was rejected because it would have
  turned the error off for every request of an assembly.
- **Rules (OQ-10):** the host read the entries in declaration order and left the named types out of KRX030, for its own request types
  and referenced ones, including those declared in a referenced host (OQ-16: a request type the referencing host never dispatches could
  be named like any other). Naming a type the host handles, or a type that is not a concrete request or stream request of a supported
  shape (an interface, an abstract base, an open generic, a notification, any other type), had no effect and was warning KRX036 on the
  attribute, with the reasons "'X' has a handler in this host" and "'X' is not a request or stream request". KRX030's severity could
  still be changed for the whole host in `.editorconfig`.
- **Outside a host (OQ-54):** the attribute had no effect, and the analyzer reported warning KRX043 on it, as on `ExcludeHandler` and
  `ExcludeBehavior` ("[assembly: ExternallyHandled] has no effect, because this assembly is not a host").
- **Host model:** `HostComposer` projected each entry to an equatable `ExternallyHandledModel(Type, Display, IsRequest, Location)` on
  the `HostModel`; the diagnostics step skipped those types for KRX030 and reported KRX036 for the entries without an effect.

### Why they were removed

With a request's handler required in the request's own assembly:

- every assembly checks its own requests (KRX001), so there is nothing for a contracts opt-out to turn off, and a request declared in a
  library is always handled where every host that references the library composes it;
- a host always finds the handler of a referenced request in that request's assembly, so there is no request handled elsewhere for
  `ExternallyHandled` to name. What KRX030 still catches (an excluded handler, an internal handler of a referenced host) is a mistake
  in the host, not a request handled elsewhere.

Contracts assemblies keep a role: they declare notifications, which may be handled in any assembly (`Keryx.Samples.Contracts`).

### What bringing them back would need

- A decision on where a request may be handled: KRX023 relaxed for assemblies that reference a contracts assembly, or for hosts.
- KRX001 turned off again in contracts assemblies, and the "mark as contracts assembly" code-fix action restored.
- The host's handling of a referenced request handled in a third assembly: the handler generator skips a handler that breaks KRX023, so
  today no manifest describes such a handler, and the host's cross-assembly KRX005 cannot be reached from generated manifests (OQ-60).
- `ExternallyHandled`'s KRX030 filtering, its KRX036 reasons and its KRX043 entry, as described above, and a public API snapshot with
  both attributes.
