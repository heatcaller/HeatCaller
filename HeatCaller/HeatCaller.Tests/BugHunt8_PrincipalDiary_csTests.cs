namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): a sentence kept under one principal's name is
/// not put back to a principal the club has since named differently.
/// </summary>
public sealed class BugHunt8_PrincipalDiary_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DateTime Night(int day) => new(2026, 10, day, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_same_principal_gets_his_words_back()
    {
        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "A-Main", "We will look at our starts.", Night(4));

        var found = PrincipalDiary.Load().LastSaidBy("sam", " alex ", Night(18));

        Assert.NotNull(found);
        Assert.Equal("We will look at our starts.", found!.Said);
    }

    [Fact]
    public void A_renamed_principal_is_not_quoted_the_old_ones_words()
    {
        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "A-Main", "We will look at our starts.", Night(4));

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Morgan", Night(18)));
    }

    [Fact]
    public void Naming_a_principal_who_was_blank_counts_as_a_different_principal()
    {
        PrincipalDiary.Load().Keep("sam", "Sam", null, "A-Main", "We will look at our starts.", Night(4));

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Morgan", Night(18)));
        Assert.NotNull(PrincipalDiary.Load().LastSaidBy("sam", null, Night(18)));
        Assert.NotNull(PrincipalDiary.Load().LastSaidBy("sam", "  ", Night(18)));
    }

    [Fact]
    public void Tonight_and_lapsed_rules_still_apply_with_the_name()
    {
        PrincipalDiary.Load().Keep("sam", "Sam", "Alex", "Heat 1", "Tidy the second sector.", Night(18));

        Assert.Null(PrincipalDiary.Load().LastSaidBy("sam", "Alex", Night(18).AddHours(1)));
    }
}
