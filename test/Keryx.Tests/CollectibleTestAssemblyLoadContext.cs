using System.Reflection;
using System.Runtime.Loader;

namespace Keryx.Tests;

internal sealed class CollectibleTestAssemblyLoadContext() : AssemblyLoadContext(isCollectible: true), IDisposable
{
    protected override Assembly? Load(AssemblyName assemblyName) => null;

    public void Dispose() => Unload();
}
