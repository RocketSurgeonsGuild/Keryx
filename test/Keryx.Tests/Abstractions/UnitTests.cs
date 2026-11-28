namespace Keryx.Tests.Abstractions;

public class UnitTests
{
    [Test]
    public async Task Value_Is_The_Default()
    {
        await Assert.That(Unit.Value).IsEqualTo(default);
        await Assert.That(new Unit()).IsEqualTo(Unit.Value);
    }

    [Test]
    public async Task Every_Value_Is_Equal()
    {
        var left = Unit.Value;
        var right = default(Unit);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(( (IEquatable<Unit>)left ).Equals(right)).IsTrue();
        await Assert.That(left == right).IsTrue();
        await Assert.That(left != right).IsFalse();
        await Assert.That(left.CompareTo(right)).IsEqualTo(0);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left.GetHashCode()).IsEqualTo(0);
    }

    [Test]
    public async Task Equals_Object_Is_True_Only_For_A_Boxed_Unit()
    {
        object boxed = Unit.Value;

        await Assert.That(Unit.Value.Equals(boxed)).IsTrue();
        await Assert.That(Unit.Value.Equals(null)).IsFalse();
        await Assert.That(Unit.Value.Equals(0)).IsFalse();
        await Assert.That(Unit.Value.Equals("()")).IsFalse();
    }

    [Test]
    public async Task ToString_Is_Empty_Parentheses() => await Assert.That(Unit.Value.ToString()).IsEqualTo("()");

    [Test]
    public async Task Is_A_Readonly_Struct_With_The_Declared_Interfaces()
    {
        var type = typeof(Unit);

        await Assert.That(type.IsValueType).IsTrue();
        await Assert.That(type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false)).IsTrue();
        await Assert.That(type.GetInterfaces().OrderBy(static t => t.FullName, StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo([typeof(IComparable<Unit>), typeof(IEquatable<Unit>)]);
    }

    [Test]
    public async Task Works_As_A_Dictionary_Key()
    {
        var set = new HashSet<Unit> { Unit.Value, default };

        await Assert.That(set.Count).IsEqualTo(1);
    }
}
