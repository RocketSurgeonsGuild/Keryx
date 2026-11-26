using System.Runtime.CompilerServices;
using DiffEngine;
using Keryx.Analyzers.Handlers;
using Microsoft.CodeAnalysis;
using Rocket.Surgery.Extensions.Testing.SourceGenerators;

namespace Keryx.Tests;

internal static class ModuleInitializer
{
    public const int TestTimeout = 60 * 60 * 1000;

    [ModuleInitializer]
    public static void Init()
    {
        VerifyGeneratorTextContext.Initialize(DiagnosticSeverity.Warning, Customizers.Default, Customizers.ExcludeParseOptions);
        VerifierSettings.ScrubInlineGuids();
        // The analyzer assembly's version changes with every commit; generated [GeneratedCode] attributes carry it.
        VerifierSettings.AddScrubber(static builder => builder.Replace($"\"Keryx\", \"{HandlerEmitter.Version}\"", "\"Keryx\", \"{KeryxVersion}\""));

        DiffRunner.Disabled = true;
        DerivePathInfo(
            static (sourceFile, _, type, method) =>
            {
                static string GetTypeName(Type type) => type.IsNested ? $"{type.ReflectedType!.Name}.{type.Name}" : type.Name;

                return new(Path.Combine(Path.GetDirectoryName(sourceFile)!, "snapshots"), GetTypeName(type), method.Name);
            }
        );
    }
}
