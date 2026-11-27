using BenchmarkDotNet.Validators;

namespace Keryx.Benchmarks.Comparison;

/// <summary>
///     Fails a benchmark run that finds a comparison scenario missing from <see cref="Verification.Scenarios" />, so a new scenario cannot
///     skip <c>--verify</c> (the list replaces an assembly scan, which a NativeAOT build cannot do without trim warnings). CI's Dry jobs run
///     every comparison benchmark, so they catch it.
/// </summary>
internal sealed class ScenarioListValidator : IValidator
{
    public static readonly ScenarioListValidator Instance = new();

    private ScenarioListValidator() { }

    public bool TreatsWarningsAsErrors => true;

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
    {
        var listed = Verification.Scenarios.Select(static scenario => scenario.Type).ToHashSet();
        return validationParameters
              .Benchmarks.Select(static benchmark => benchmark.Descriptor.Type)
              .Distinct()
              .Where(type => type.IsSubclassOf(typeof(ComparisonBenchmark)) && !listed.Contains(type))
              .Select(static type => new ValidationError(true, $"{type.Name} is not in Verification.Scenarios: --verify would not check it."));
    }
}
