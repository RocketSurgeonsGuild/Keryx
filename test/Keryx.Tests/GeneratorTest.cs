using System.Runtime.Loader;
using Rocket.Surgery.Extensions.Testing;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

public abstract class GeneratorTest() : LoggerTest(Defaults.LoggerTest)
{
    [Before(Test)]
    public void InitializeBuilder()
    {
        Builder = GeneratorBuilderConstants.Builder.WithAssemblyLoadContext(AssemblyLoadContext);
        if (AssemblyLoadContext is IDisposable disposable)
        {
            Disposables.Add(disposable);
        }
    }

    protected AssemblyLoadContext AssemblyLoadContext { get; } = new CollectibleTestAssemblyLoadContext();
    protected GeneratorTestContextBuilder Builder { get; private set; } = null!;
}
