namespace Keryx.Benchmarks.Common;

/// <summary>
///     A hand-written validator contract, the shape FluentValidation-style validators have: each subject implements it for its own
///     <c>TypicalPing</c> and its validation behavior resolves it from DI.
/// </summary>
public interface IValidator<in T>
{
    /// <summary>Returns the validation error, or <see langword="null" /> when <paramref name="instance" /> is valid.</summary>
    string? Validate(T instance);
}
