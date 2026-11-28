namespace Keryx.Analyzers;

/// <summary>
///     Metadata names of the non-Keryx types the analyzers and generators look for. <see cref="KnownTypeNames" /> holds the Keryx ones (and
///     only those: a test pins it to the types <c>Keryx.dll</c> exports).
/// </summary>
internal static class WellKnownTypeNames
{
    public const string CancellationToken = "System.Threading.CancellationToken";
    public const string Task = "System.Threading.Tasks.Task";
    public const string TaskOfT = "System.Threading.Tasks.Task`1";
    public const string ValueTask = "System.Threading.Tasks.ValueTask";
    public const string ValueTaskOfT = "System.Threading.Tasks.ValueTask`1";
    public const string IAsyncEnumerableOfT = "System.Collections.Generic.IAsyncEnumerable`1";
    public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    /// <summary>
    ///     Compared by name, not resolved: the attribute can come from any version of <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>.
    /// </summary>
    public const string FromKeyedServicesAttribute = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
}
