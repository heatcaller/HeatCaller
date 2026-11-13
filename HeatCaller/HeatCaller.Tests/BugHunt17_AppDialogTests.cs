namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: AppDialog.Choose promises that Escape, Alt+F4 and the last
/// button all mean the LAST label. With four buttons the third carried
/// DialogResult.Cancel - which is also what WinForms hands back for a window
/// closed without a button - so closing "We're Done After This Round" with
/// Alt+F4 was taken for "...and one main fewer" rather than Cancel.
/// </summary>
public sealed class BugHunt17_AppDialogTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_window_closed_without_a_button_is_the_last_label(int labels)
    {
        Assert.Equal(labels - 1, AppDialog.IndexChosen(DialogResult.Cancel, labels));
        Assert.Equal(labels - 1, AppDialog.IndexChosen(DialogResult.None, labels));
    }

    [Fact]
    public void No_button_carries_the_result_a_closed_window_gives()
    {
        Assert.DoesNotContain(DialogResult.Cancel, AppDialog.CustomResults);
        Assert.DoesNotContain(DialogResult.None, AppDialog.CustomResults);
        Assert.Equal(AppDialog.CustomResults.Length, AppDialog.CustomResults.Distinct().Count());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Each_button_is_its_own_place_in_the_labels(int labels)
    {
        for (var i = 0; i < labels; i++)
        {
            Assert.Equal(i, AppDialog.IndexChosen(AppDialog.CustomResults[i], labels));
        }
    }

    [Fact]
    public void A_result_for_a_button_the_question_does_not_have_is_the_last_label()
    {
        Assert.Equal(1, AppDialog.IndexChosen(AppDialog.CustomResults[3], 2));
    }
}
