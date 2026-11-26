namespace Keryx.Tests;

[Timeout(ModuleInitializer.TestTimeout)]
public class KeryxGeneratorTests : GeneratorTest
{
    [Test]
    public async Task Should_Run_Without_Diagnostics_On_A_Compilation_Without_Handlers(CancellationToken cancellationToken)
    {
        var result = await Builder
                          .AddSources(
                               """
                               namespace Sample;

                               public sealed class NotAHandler
                               {
                                   public int Value { get; set; }
                               }
                               """
                           )
                          .Build()
                          .GenerateAsync(cancellationToken);

        await Verify(result);
    }
}
