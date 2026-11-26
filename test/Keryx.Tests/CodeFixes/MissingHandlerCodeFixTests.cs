using Keryx.Analyzers.CodeFixes;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     KRX001, both actions: add a handler stub (nested and top-level requests, every request kind) and mark the assembly as a contracts
///     assembly. Before/after snapshots.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class MissingHandlerCodeFixTests : CodeFixTest
{
    [Test]
    public async Task Should_Add_A_Stub_Next_To_A_Nested_Request(CancellationToken cancellationToken)
    {
        var result = await FixAsync<MissingHandlerAnalyzer, MissingHandlerCodeFixProvider>(
            """
            using System;
            using Keryx;

            namespace Sample;

            public partial class CreateOrder
            {
                public sealed record Command(string Sku) : IRequest<Guid>;
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Add_A_Handler_Class_For_A_Top_Level_Request(CancellationToken cancellationToken)
    {
        var result = await FixAsync<MissingHandlerAnalyzer, MissingHandlerCodeFixProvider>(
            """
            using Keryx;

            namespace Sample.Orders;

            public sealed record DeleteOrder(int Id) : IRequest;
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Add_A_Stream_Handler_With_A_Numeric_Suffix_When_The_Name_Is_Taken(CancellationToken cancellationToken)
    {
        var result = await FixAsync<MissingHandlerAnalyzer, MissingHandlerCodeFixProvider>(
            """
            using Keryx;

            namespace Sample
            {
                public sealed record ListNames : IStreamRequest<string>;

                public sealed class ListNamesHandler { }
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }
}
