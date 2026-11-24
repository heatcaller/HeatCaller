namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): on Add a Class, Qualifier Format... follows the
/// race length and finish rule just typed, not the suggested ones the class
/// was made with.
/// </summary>
public sealed class BugHunt24_RaceClassFormsTests
{
    [Fact]
    public void A_new_class_s_qualifier_format_follows_the_race_length_just_typed()
    {
        var appDefault = new RaceFormat();
        var madeWith = RaceFormat.ForNewClass(appDefault);
        var typed = RaceFormat.ForNewClass(appDefault, 8, finishTheLap: false);

        var fallback = RaceClassEditForm.QualifierFallback(
            lengthRowsLive: true, typedFormat: typed, classFormat: madeWith, appDefault: appDefault);

        Assert.NotNull(fallback);
        Assert.Equal(8, fallback!.TimeLimitMinutes);
        Assert.False(fallback.FinishOnNextCrossing);
    }

    [Fact]
    public void A_new_class_left_on_the_suggested_length_falls_back_to_the_app_default()
    {
        var appDefault = new RaceFormat { TimeLimitMinutes = 6 };
        var typed = RaceFormat.ForNewClass(appDefault, 6, finishTheLap: appDefault.FinishOnNextCrossing);

        var fallback = RaceClassEditForm.QualifierFallback(
            lengthRowsLive: true, typedFormat: typed, classFormat: null, appDefault: appDefault);

        Assert.Same(appDefault, fallback);
    }

    [Fact]
    public void Once_Format_has_set_the_class_s_format_the_qualifiers_follow_that()
    {
        var appDefault = new RaceFormat();
        var setUnderFormat = new RaceFormat { TimeLimitMinutes = 10 };
        var typed = RaceFormat.ForNewClass(appDefault, 8, finishTheLap: true);

        var fallback = RaceClassEditForm.QualifierFallback(
            lengthRowsLive: false, typedFormat: typed, classFormat: setUnderFormat, appDefault: appDefault);

        Assert.Same(setUnderFormat, fallback);
    }

    [Fact]
    public void An_edited_class_s_qualifiers_follow_its_own_format_or_the_app_default()
    {
        var appDefault = new RaceFormat { TimeLimitMinutes = 5 };
        var own = new RaceFormat { TimeLimitMinutes = 7 };

        Assert.Same(own, RaceClassEditForm.QualifierFallback(
            lengthRowsLive: false, typedFormat: null, classFormat: own, appDefault: appDefault));
        Assert.Same(appDefault, RaceClassEditForm.QualifierFallback(
            lengthRowsLive: false, typedFormat: null, classFormat: null, appDefault: appDefault));
    }
}
