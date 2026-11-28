#!/usr/bin/env dotnet
#:sdk Joinery.Dovetail.Sdk
#:package Sourcy.Git
#:package Sourcy.DotNet
#:package Rocket.Surgery.ModularPipelines.Extensions
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property ExperimentalFileBasedProgramEnableTransitiveDirectives=true

// Dovetail generates ConfigureDovetail() into the build host's root namespace, Build.
using Build;
using ModularPipelines;
using Rocket.Surgery.ModularPipelines.Extensions;
using Rocket.Surgery.ModularPipelines.Extensions.Modules;

// Joinery.Dovetail.ModularPipelines and Specular are direct references because their generators write ConfigureDovetail() and the
// module scan into this build host; they don't flow through Rocket.Surgery.ModularPipelines.Extensions. Their versions are the
// repository's (Directory.Packages.props). Dovetail reads the settings (appsettings.yaml) from this file's folder.
// docs/ holds Markdown (ADRs), not a docs site, so the docs build (`mise run docs:build`) stays out of the pipeline.
await using var pipeline = await Pipeline
                                 .CreateBuilder(args)
                                 .ConfigureDovetail(static z => z.Set<ModuleDelegate>(static type => type != typeof(DocsModule)));
await pipeline.RunAsync();
