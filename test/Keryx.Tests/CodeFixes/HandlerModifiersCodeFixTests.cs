using Keryx.Analyzers.CodeFixes;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     KRX002: before/after snapshots of "Make the handler private static".
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerModifiersCodeFixTests : CodeFixTest
{
    [Test]
    public async Task Should_Make_Handler_Private_Static(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerModifiersCodeFixProvider>(
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Sample;

            public partial class Ping
            {
                public sealed record Query : IRequest<string>;

                public ValueTask<string> Handle(Query request) => new("pong");
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Keep_Async_And_Drop_Virtual(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerModifiersCodeFixProvider>(
            """
            using System.Threading.Tasks;
            using Keryx;

            namespace Sample;

            public sealed record Query : IRequest<string>;

            public partial class QueryHandler
            {
                // Handles the query.
                protected internal virtual async Task<string> HandleAsync(Query request)
                {
                    await Task.Yield();
                    return "pong";
                }
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Not_Offer_A_Fix_For_A_Wrong_Return_Type(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerModifiersCodeFixProvider>(
            """
            using Keryx;

            namespace Sample;

            public sealed record Query : IRequest<string>;

            public static partial class QueryHandler
            {
                private static int Handle(Query request) => 1;
            }
            """,
            cancellationToken
        );

        await Assert.That(result.Actions).IsEmpty();
    }
}
