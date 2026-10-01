namespace HeatCaller.Tests;

/// <summary>
/// NaturalOrder: numbers in names ordered as numbers, the words between
/// them ignoring case, and a fixed order for names that come out the same.
/// </summary>
public class NaturalOrderTests
{
    [Fact]
    public void NumbersAreOrderedAsNumbers()
    {
        var packs = new[] { "Pack 10", "Pack 2", "Pack 1", "Pack 11", "Pack 9", "Pack 100" };

        Assert.Equal(
            new[] { "Pack 1", "Pack 2", "Pack 9", "Pack 10", "Pack 11", "Pack 100" },
            packs.OrderBy(p => p, NaturalOrder.Instance).ToArray());
    }

    [Fact]
    public void TheWordsAroundThemStillCount()
    {
        var names = new[] { "Spare 2", "pack 3", "Pack 2 LiPo", "Pack 2", "Club 10", "Club 9" };

        Assert.Equal(
            new[] { "Club 9", "Club 10", "Pack 2", "Pack 2 LiPo", "pack 3", "Spare 2" },
            names.OrderBy(n => n, NaturalOrder.Instance).ToArray());
    }

    [Fact]
    public void ANumberTooLongToParseIsStillOrdered()
    {
        Assert.True(NaturalOrder.Instance.Compare("Pack 99999999999999999999998", "Pack 99999999999999999999999") < 0);
        Assert.True(NaturalOrder.Instance.Compare("Pack 100000000000000000000000", "Pack 99999999999999999999999") > 0);
    }

    [Fact]
    public void NamesThatComeOutTheSameHaveOneOrder()
    {
        var order = NaturalOrder.Instance;

        Assert.Equal(0, order.Compare("Pack 1", "Pack 1"));
        Assert.Equal(-order.Compare("Pack 01", "Pack 1"), order.Compare("Pack 1", "Pack 01"));
        Assert.NotEqual(0, order.Compare("Pack 01", "Pack 1"));
        Assert.NotEqual(0, order.Compare("pack 1", "Pack 1"));
        Assert.True(order.Compare(null, "Pack 1") < 0);
        Assert.True(order.Compare("Pack 1", null) > 0);
    }
}
