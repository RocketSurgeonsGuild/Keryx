namespace Keryx.Tests.Abstractions;

public class PhaseTests
{
    [Test]
    [Arguments(Phase.Outermost, 0)]
    [Arguments(Phase.Observability, 100)]
    [Arguments(Phase.Security, 200)]
    [Arguments(Phase.Validation, 300)]
    [Arguments(Phase.Default, 400)]
    [Arguments(Phase.Caching, 450)]
    [Arguments(Phase.Transaction, 500)]
    [Arguments(Phase.Innermost, 600)]
    public async Task Phase_Has_The_Declared_Value(Phase phase, int expected) => await Assert.That((int)phase).IsEqualTo(expected);

    [Test]
    public async Task Phase_Has_Exactly_The_Declared_Members()
    {
        await Assert.That(Enum.GetNames<Phase>())
                    .IsEquivalentTo(["Outermost", "Observability", "Security", "Validation", "Default", "Caching", "Transaction", "Innermost"]);
        await Assert.That(Enum.GetUnderlyingType(typeof(Phase))).IsEqualTo(typeof(int));
        await Assert.That(typeof(Phase).IsDefined(typeof(FlagsAttribute), false)).IsFalse();
    }

    [Test]
    public async Task Phases_Run_Outermost_First_With_Caching_Between_Default_And_Transaction() =>
        // Enum.GetValues sorts by value: lower values run first (outermost).
        await Assert.That(string.Join(",", Enum.GetValues<Phase>().Select(static p => p.ToString())))
                    .IsEqualTo("Outermost,Observability,Security,Validation,Default,Caching,Transaction,Innermost");

    [Test]
    [Arguments(250, Phase.Security, Phase.Validation)]
    [Arguments(425, Phase.Default, Phase.Caching)]
    [Arguments(475, Phase.Caching, Phase.Transaction)]
    public async Task Phases_Leave_Gaps_For_Values_In_Between(int value, Phase before, Phase after)
    {
        var between = (Phase)value;

        await Assert.That((int)between).IsGreaterThan((int)before);
        await Assert.That((int)between).IsLessThan((int)after);
        await Assert.That(Enum.IsDefined(between)).IsFalse();
    }

    [Test]
    [Arguments(PublishStrategy.Sequential, 0)]
    [Arguments(PublishStrategy.Parallel, 1)]
    public async Task PublishStrategy_Has_The_Declared_Value(PublishStrategy strategy, int expected)
    {
        await Assert.That((int)strategy).IsEqualTo(expected);
        await Assert.That(Enum.GetValues<PublishStrategy>()[0]).IsEqualTo(PublishStrategy.Sequential);
        await Assert.That(Enum.GetValues<PublishStrategy>().Length).IsEqualTo(2);
    }

    [Test]
    [Arguments(Internal.HandlerKind.Request, 0)]
    [Arguments(Internal.HandlerKind.Stream, 1)]
    [Arguments(Internal.HandlerKind.Notification, 2)]
    public async Task HandlerKind_Has_The_Declared_Value(Internal.HandlerKind kind, int expected)
    {
        await Assert.That((int)kind).IsEqualTo(expected);
        await Assert.That(Enum.GetValues<Internal.HandlerKind>().Length).IsEqualTo(3);
    }
}
