using Keryx.Analyzers.CodeFixes;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     KRX024 (make a request or stream request type <c>sealed</c>): before/after snapshots of classes and records, with and without
///     modifiers, a partial type and a nested one.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class SealRequestCodeFixTests : CodeFixTest
{
    [Test]
    public async Task Should_Make_A_Request_Sealed(CancellationToken cancellationToken)
    {
        var result = await FixAsync<MessageTypeAnalyzer, SealRequestCodeFixProvider>(
            """
            using Keryx;

            namespace Sample;

            /// <summary>A class request.</summary>
            public class Plain : IRequest<int> { }

            public record Positional(int Value) : IRequest<int>;

            public partial record Split : IStreamRequest<int>;

            public partial record Split
            {
                public int Value { get; init; }
            }

            public abstract record Command : IRequest;

            [System.Serializable]
            public record Ship : Command;

            record Bare : IRequest<string>;

            public static partial class Outer
            {
                // A nested request.
                internal partial class Nested : IRequest<string> { }
            }
            """,
            cancellationToken,
            host: true
        );

        await Verify(result);
    }

    /// <summary>
    ///     Sealing a type that declares a <c>virtual</c> or <c>protected</c> member of its own would not compile (CS0549, CS8879) or would warn
    ///     (CS0628), so the fix is offered only for <c>Overrides</c>, whose members override base members.
    /// </summary>
    [Test]
    public async Task Should_Not_Offer_Make_Sealed_For_A_Type_With_Virtual_Or_Protected_Members(CancellationToken cancellationToken)
    {
        var result = await FixAsync<MessageTypeAnalyzer, SealRequestCodeFixProvider>(
            """
            using Keryx;

            namespace Sample;

            public class WithVirtual : IRequest<int>
            {
                public virtual int Value { get; set; }
            }

            public class WithProtected : IRequest<int>
            {
                protected int Value;
            }

            public record WithPrintMembers(int Value) : IRequest<int>
            {
                protected virtual bool PrintMembers(System.Text.StringBuilder builder) => true;
            }

            public record WithCopyConstructor : IRequest<int>
            {
                protected WithCopyConstructor(WithCopyConstructor original) { }
            }

            public class Overrides : IRequest<int>
            {
                public override string ToString() => "Overrides";
            }
            """,
            cancellationToken,
            host: true
        );

        await Verify(result);
    }
}
