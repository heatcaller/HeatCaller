namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the night before and the notes: the date a preview is
/// offered for, and tonight's notes and vote surviving the day being
/// opened after they were added.
/// </summary>
public sealed class BugHunt7NightBeforeNotesTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    // ------------------------------------------------------------------
    //  Which day a preview is for
    // ------------------------------------------------------------------

    [Fact]
    public void Written_the_evening_before_it_is_offered_for_tomorrow_first()
    {
        // The day was opened on Friday and the preview written on Friday
        // evening, after a 10:00 start would have gone. Friday is no use.
        var friday = new DateTime(2026, 10, 16, 20, 0, 0);

        var days = NightBefore.DaysItCouldBeFor(friday, TimeSpan.FromHours(10));

        Assert.Equal(new DateTime(2026, 10, 17), days[0]);
        Assert.Equal(new DateTime(2026, 10, 18), days[1]);
    }

    [Fact]
    public void Written_on_the_morning_before_the_first_race_today_is_still_offered()
    {
        var saturday = new DateTime(2026, 10, 17, 8, 0, 0);

        var days = NightBefore.DaysItCouldBeFor(saturday, TimeSpan.FromHours(10));

        Assert.Equal(new DateTime(2026, 10, 18), days[0]);
        Assert.Equal(new DateTime(2026, 10, 17), days[1]);
    }

    [Fact]
    public void A_day_with_no_first_race_time_still_offers_today()
    {
        var saturday = new DateTime(2026, 10, 17, 21, 0, 0);

        var days = NightBefore.DaysItCouldBeFor(saturday, null);

        Assert.Equal(new DateTime(2026, 10, 18), days[0]);
        Assert.Equal(new DateTime(2026, 10, 17), days[1]);
    }

    [Fact]
    public void A_preview_for_either_day_offered_has_not_expired_when_it_is_written()
    {
        // The fault this replaces: the date came from when the day was
        // opened, so the expiry had passed before the preview went up and
        // the page never drew it.
        var now = DateTime.Now;

        foreach (var first in new TimeSpan?[] { null, TimeSpan.FromHours(10), TimeSpan.FromHours(19) })
        {
            foreach (var day in NightBefore.DaysItCouldBeFor(now, first))
            {
                var whenLocal = day.Date + (first ?? TimeSpan.Zero);

                Assert.True(NightBefore.UntilUtc(whenLocal, first) > DateTime.UtcNow);
            }
        }
    }

    [Fact]
    public void The_offered_days_are_named_as_a_club_would_say_them()
    {
        var today = new DateTime(2026, 10, 16, 20, 0, 0);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        Assert.Equal("Today, " + today.ToString("dddd d MMMM", culture), NightBefore.DayCaption(today.Date, today));
        Assert.Equal("Tomorrow, " + today.AddDays(1).ToString("dddd d MMMM", culture), NightBefore.DayCaption(today.Date.AddDays(1), today));
        Assert.Equal(today.AddDays(2).ToString("dddd d MMMM", culture), NightBefore.DayCaption(today.Date.AddDays(2), today));
    }

    // ------------------------------------------------------------------
    //  Tonight's notes and vote, added before the day is opened
    // ------------------------------------------------------------------

    [Fact]
    public void A_note_typed_before_the_day_is_opened_is_still_there_once_it_is()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting("last-week");
        notes.Add("about last week");

        // The app starts with no day open: last week's go.
        notes.NewMeeting(null);
        Assert.Empty(notes.All);

        // Tonight's, typed and sent in before anybody opens the day.
        Assert.Null(notes.Add("Raffle closes at eight"));
        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess"));

        notes.NewMeeting("tonight");

        Assert.Equal(2, notes.All.Count);
        Assert.Equal("tonight", notes.DayId);
        Assert.NotNull(notes.ToSay());

        // And the gate still holds on the one from the room.
        Assert.Single(notes.Waiting);
    }

    [Fact]
    public void A_note_left_under_no_day_from_long_ago_is_not_taken_onto_tonight()
    {
        // A night called off: notes typed with no day open, and no day
        // ever opened after them. They must not be read out at the next
        // meeting just because the file still says no day.
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            _appData.PathOf("club-notes.json"),
            "{\"DayId\":\"\",\"Notes\":[{\"Id\":\"old1\",\"Text\":\"Raffle closes at eight\",\"From\":\"\",\"AddedAtUtc\":\"2026-01-02T19:00:00Z\",\"Approved\":true}]}");

        var notes = ClubNotes.Load();

        Assert.Single(notes.All);
        Assert.Null(notes.Add("Happy birthday Dot"));

        notes.NewMeeting("tonight");

        Assert.Equal("Happy birthday Dot", Assert.Single(notes.All).Text);
    }

    [Fact]
    public void Moving_from_one_day_to_another_still_clears_the_notes()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting("last-week");
        notes.Add("about last week");

        notes.NewMeeting("tonight");

        Assert.Empty(notes.All);
    }

    [Fact]
    public void A_vote_opened_before_the_day_is_opened_is_still_open_once_it_is()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting(null);

        Assert.Null(vote.OpenFor(
            "round-stock-a",
            "Stock 17.5 A-Main",
            "Stock 17.5",
            new[] { new VoteOption("t1", "Bill Hargrove"), new VoteOption("t2", "Sam Teague") }));

        Assert.Null(vote.Cast("phone-1", "t1"));

        vote.NewMeeting("tonight");

        Assert.True(vote.Taking);
        Assert.Equal(1, vote.Votes);
    }

    [Fact]
    public void Last_weeks_vote_still_goes_when_a_new_day_opens()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("last-week");

        Assert.Null(vote.OpenFor(
            "round-stock-a",
            "Stock 17.5 A-Main",
            "Stock 17.5",
            new[] { new VoteOption("t1", "Bill Hargrove"), new VoteOption("t2", "Sam Teague") }));

        vote.NewMeeting("tonight");

        Assert.False(vote.Taking);
    }
}
