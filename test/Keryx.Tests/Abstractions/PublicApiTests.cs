using PublicApiGenerator;

namespace Keryx.Tests.Abstractions;

public class PublicApiTests
{
    [Test]
    public async Task Keryx_Public_Api_Has_Not_Changed()
    {
        var api = typeof(Unit).Assembly.GeneratePublicApi(
            new()
            {
                ExcludeAttributes =
                [
                    "System.Runtime.Versioning.TargetFrameworkAttribute",
                    "System.Reflection.AssemblyMetadataAttribute",
                ],
            }
        );

        await Verify(api);
    }
}
