namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), "Unpublish results older than N years": a
/// session is judged by the same year the results index files it under -
/// the local race night - so a New Year race is kept or taken down with the
/// index year that lists it. Times are built in local time so the tests
/// hold in any time zone.
/// </summary>
public sealed class BugHunt24RelayPrivacyDocumentTests
{
    private static SessionRecord StartedLocal(int year, int month, int day, int hour, int minute) => new()
    {
        StartedAtUtc = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime(),
    };

    // --- 47 (F275): one year decides the session and its index year ---

    [Fact]
    public void A_race_just_after_midnight_on_New_Years_Day_goes_with_the_year_before()
    {
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);

        // 00:10 on 1 Jan 2025 is the race night of 31 Dec 2024: index year
        // 2024, which keep-2 in 2026 takes down. The calendar date said 2025
        // and kept it online.
        var session = StartedLocal(2025, 1, 1, 0, 10);

        Assert.Equal("2024", RelayResultsIndex.YearOf(session));
        Assert.True(RelayPrivacyDocument.SessionTooOld(session, 2, now));
    }

    [Fact]
    public void A_session_is_too_old_exactly_when_its_index_year_is()
    {
        var nows = new[]
        {
            new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Local),
            new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Local),
        };

        var sessions = new[]
        {
            StartedLocal(2024, 12, 31, 20, 0),
            StartedLocal(2025, 1, 1, 0, 10),
            StartedLocal(2025, 1, 1, 9, 0),
            StartedLocal(2025, 1, 1, 19, 0),
            StartedLocal(2025, 12, 31, 22, 0),
            StartedLocal(2026, 1, 1, 9, 0),
        };

        foreach (var now in nows)
            foreach (var keep in new[] { 0, 1, 2 })
                foreach (var session in sessions)
                    Assert.Equal(
                        RelayPrivacyDocument.YearTooOld(RelayResultsIndex.YearOf(session), keep, now),
                        RelayPrivacyDocument.SessionTooOld(session, keep, now));
    }

    [Fact]
    public void A_session_with_no_time_is_never_too_old()
    {
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.False(RelayPrivacyDocument.SessionTooOld(new SessionRecord(), 1, now));
    }

    [Fact]
    public void A_session_saved_without_a_start_is_judged_by_when_it_ended()
    {
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        var ended = new SessionRecord
        {
            EndedAtUtc = new DateTime(2023, 7, 1, 20, 0, 0, DateTimeKind.Local).ToUniversalTime(),
        };

        Assert.True(RelayPrivacyDocument.SessionTooOld(ended, 2, now));
        Assert.False(RelayPrivacyDocument.SessionTooOld(ended, 0, now));
    }
}
