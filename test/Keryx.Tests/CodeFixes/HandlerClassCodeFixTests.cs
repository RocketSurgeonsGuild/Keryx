using Keryx.Analyzers.CodeFixes;
using Keryx.Analyzers.Diagnostics;

namespace Keryx.Tests.CodeFixes;

/// <summary>
///     KRX004 (add <c>partial</c>, make <c>public</c>) and KRX014 (remove <c>static</c>): before/after snapshots.
/// </summary>
[Timeout(ModuleInitializer.TestTimeout)]
public class HandlerClassCodeFixTests : CodeFixTest
{
    [Test]
    public async Task Should_Add_Partial_To_The_Handler_Class_And_Its_Containing_Type(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerClassCodeFixProvider>(
            """
            using Keryx;

            namespace Sample;

            public static class Orders
            {
                public class Create
                {
                    public sealed record Command : IRequest<int>;

                    private static int Handle(Command request) => 1;
                }
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Make_The_Handler_Class_Public_Outside_A_Host(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerClassCodeFixProvider>(
            """
            using Keryx;

            namespace Sample;

            public sealed record Command : IRequest<int>;

            partial class CommandHandler
            {
                private static int Handle(Command request) => 1;
            }

            internal static partial class Container
            {
                public static partial class Nested
                {
                    private static int Handle(Command request) => 1;
                }
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }

    [Test]
    public async Task Should_Remove_Static_From_A_Handler_Class_With_Injected_Parameters(CancellationToken cancellationToken)
    {
        var result = await FixAsync<HandlerAnalyzer, HandlerClassCodeFixProvider>(
            """
            using System;
            using Keryx;

            namespace Sample;

            public sealed record Command : IRequest<int>;

            public static partial class CommandHandler
            {
                private static int Handle(Command request, IServiceProvider services) => 1;
            }
            """,
            cancellationToken
        );

        await Verify(result);
    }
}
