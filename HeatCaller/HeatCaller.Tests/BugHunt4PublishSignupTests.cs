namespace HeatCaller.Tests;

/// <summary>
/// Publishing results and online sign-up, bug hunt 4: a re-send that can't
/// be queued no longer drops a result the relay holds from the archive; the
/// results ledger and the sign-up invites, locked for a moment at startup,
/// are no longer written over with empty ones; an invite re-coded or
/// cancelled while its email batch runs is not marked as emailed; and
/// invites that couldn't be saved are not kept.
/// </summary>
public sealed class BugHunt4PublishSignupTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Jan10 = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb07 = new(2026, 2, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string id, DateTime startedUtc) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(5),
        Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "T1", DriverName = "Alex Kent", Laps = 10 } },
    };

    /// <summary>
    /// A relay the publisher can't queue anything for - no token, as after a
    /// restore onto a new PC - whose window opened on 10 January. Switched off
    /// too, so nothing ever tries the network.
    /// </summary>
    private static AppSettings UnqueueableRelay() => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = "https://relay.example",
        RelayClub = "acme-rc",
        RelayToken = string.Empty,
        RelayResultsFromUtc = Jan10,
    };

    // ------------------------------------------------ re-sends (publish-1)

    [Fact]
    public void A_re_send_that_cannot_be_queued_keeps_a_result_the_relay_holds_in_the_index()
    {
        using var publisher = new RelayPublisher(UnqueueableRelay());
        var session = Race("feb07", Feb07);

        Assert.True(publisher.ResultIsOnRelay("feb07", Feb07));

        // What the startup re-send passes for a heat that was never corrected.
        // It used to pass "not corrected", and the heat was withheld.
        publisher.PublishResult("feb07", "{}", Feb07, correction: RelayResultsIndex.MayAlreadyBeOnRelay(session, firstPublish: false));

        Assert.True(publisher.ResultIsOnRelay("feb07", Feb07));
        Assert.True(publisher.ResultIsOwed("feb07"));

        // Owed on disk, so it goes at the next pass that works, whatever its age.
        Assert.Contains("feb07", RelayResultsIndex.Ledger.Load().Owed);
        Assert.DoesNotContain("feb07", RelayResultsIndex.Ledger.Load().Withheld);
    }

    [Fact]
    public void A_first_publish_that_cannot_be_queued_is_still_left_out_of_the_index()
    {
        using var publisher = new RelayPublisher(UnqueueableRelay());
        var session = Race("feb07", Feb07);

        publisher.PublishResult("feb07", "{}", Feb07, correction: RelayResultsIndex.MayAlreadyBeOnRelay(session, firstPublish: true));

        Assert.False(publisher.ResultIsOnRelay("feb07", Feb07));
        Assert.False(publisher.ResultIsOwed("feb07"));
    }

    [Fact]
    public void Only_an_uncorrected_first_publish_can_be_a_result_the_relay_never_had()
    {
        var plain = Race("plain", Feb07);
        var corrected = Race("corrected", Feb07);
        corrected.Edits.Add(new ResultEdit());

        // An undo that took the only correction off leaves the edit log empty.
        var undone = Race("undone", Feb07);
        undone.Edits = new List<ResultEdit>();

        Assert.False(RelayResultsIndex.MayAlreadyBeOnRelay(plain, firstPublish: true));
        Assert.True(RelayResultsIndex.MayAlreadyBeOnRelay(corrected, firstPublish: true));
        Assert.True(RelayResultsIndex.MayAlreadyBeOnRelay(plain, firstPublish: false));
        Assert.True(RelayResultsIndex.MayAlreadyBeOnRelay(undone, firstPublish: false));
    }

    // ------------------------------------ the results ledger (publish-3)

    /// <summary>A ledger with one of each exception, saved: what a club has after its first nights.</summary>
    private static void SaveLedger()
    {
        var ledger = new RelayResultsIndex.Ledger();
        ledger.Extra.Add("jan03");
        ledger.Owed.Add("feb07");
        ledger.Withheld.Add("feb28");
        ledger.Save();
    }

    private RelayResultsIndex.Ledger LoadLocked()
    {
        using (new FileStream(_data.PathOf("relay-results.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return RelayResultsIndex.Ledger.Load();
        }
    }

    [Fact]
    public void A_results_ledger_locked_at_startup_is_not_written_over()
    {
        SaveLedger();
        var before = File.ReadAllText(_data.PathOf("relay-results.json"));

        // Released before anything is saved: a save into a file still held
        // would fail anyway and hide the bug.
        var tonight = LoadLocked();

        Assert.True(tonight.HoldWrites);
        Assert.Empty(tonight.Extra);

        // What the publisher does in its first seconds: records the relay,
        // then notes the startup re-send.
        tonight.Adopt("https://relay.example", "acme-rc");
        tonight.Owed.Add("mar01");
        tonight.Save();

        Assert.Equal(before, File.ReadAllText(_data.PathOf("relay-results.json")));

        var next = RelayResultsIndex.Ledger.Load();
        Assert.Contains("jan03", next.Extra);
        Assert.Contains("feb07", next.Owed);
        Assert.Contains("feb28", next.Withheld);
    }

    [Fact]
    public void The_publisher_does_not_write_an_empty_ledger_over_one_locked_at_startup()
    {
        SaveLedger();
        var before = File.ReadAllText(_data.PathOf("relay-results.json"));

        var settings = UnqueueableRelay();
        settings.RelayToken = "hg_test";

        RelayPublisher publisher;
        using (new FileStream(_data.PathOf("relay-results.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            publisher = new RelayPublisher(settings);
        }

        using (publisher)
        {
            // Can't be queued (publishing is off), and inside the window: noted
            // as withheld, which saves the ledger.
            publisher.PublishResult("mar01", "{}", Feb28.AddDays(1));
        }

        Assert.Equal(before, File.ReadAllText(_data.PathOf("relay-results.json")));
        Assert.Contains("jan03", RelayResultsIndex.Ledger.Load().Extra);
    }

    [Fact]
    public void Delete_everything_published_still_clears_a_ledger_held_at_startup()
    {
        SaveLedger();
        var tonight = LoadLocked();

        // What ForgetLedger does after the delete.
        Assert.True(tonight.Clear());
        tonight.Save();

        var next = RelayResultsIndex.Ledger.Load();
        Assert.Empty(next.Extra);
        Assert.Empty(next.Owed);
        Assert.Empty(next.Withheld);

        // Written from then on: the file now says what memory does.
        Assert.False(tonight.HoldWrites);
        tonight.Owed.Add("mar01");
        tonight.Save();
        Assert.Contains("mar01", RelayResultsIndex.Ledger.Load().Owed);
    }

    [Fact]
    public void A_readable_results_ledger_is_not_held()
    {
        SaveLedger();

        var ledger = RelayResultsIndex.Ledger.Load();

        Assert.False(ledger.HoldWrites);
        ledger.Owed.Add("mar01");
        ledger.Save();
        Assert.Contains("mar01", RelayResultsIndex.Ledger.Load().Owed);
    }

    // ------------------------------------------ sign-up invites (signup-1)

    private static AppSettings SignupSettings() => new()
    {
        RelayClub = "acme-rc",

        // Already made, so making a code doesn't make one and save settings.
        RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
    };

    /// <summary>signups.json with sign-up on and one invite out. Written by hand: sealing a code needs Windows.</summary>
    private void WriteSignups()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var expires = DateTime.UtcNow.AddDays(7).ToString("o");
        File.WriteAllText(_data.PathOf("signups.json"),
            "{ \"Enabled\": true, \"SealedCodes\": \"\", \"Invites\": ["
            + "{ \"Id\": \"sam-invite\", \"Club\": \"acme-rc\", \"Email\": \"sam@example.com\", \"ExpiresUtc\": \"" + expires + "\" } ] }");
    }

    private RelaySignupDesk Desk(AppSettings settings, RelayPublisher publisher) =>
        new(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

    [Fact]
    public void Sign_up_invites_locked_at_startup_are_not_written_over()
    {
        WriteSignups();
        var before = File.ReadAllText(_data.PathOf("signups.json"));

        SignupStore tonight;
        using (new FileStream(_data.PathOf("signups.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = SignupStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Empty(tonight.All);

        // What ticking "Let new drivers sign up" used to write.
        tonight.Enabled = true;
        Assert.False(tonight.Save());

        Assert.Equal(before, File.ReadAllText(_data.PathOf("signups.json")));
        Assert.NotNull(SignupStore.Load().Get("sam-invite"));
    }

    [Fact]
    public void The_desk_makes_no_invites_and_publishes_nothing_while_signups_json_is_held()
    {
        WriteSignups();
        var before = File.ReadAllText(_data.PathOf("signups.json"));
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });

        RelaySignupDesk desk;
        using (new FileStream(_data.PathOf("signups.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            desk = Desk(settings, publisher);
        }

        Assert.NotNull(desk.Held);

        desk.SetEnabled(true);
        Assert.Throws<InvalidOperationException>(() => desk.CreateInvites(new[] { ("jo@example.com", "Jo") }));

        // Nothing to publish, so the relay keeps the codes it already has.
        Assert.False(desk.Store.Enabled);
        Assert.Empty(desk.Store.All);
        Assert.False(desk.InUse);
        Assert.Equal(before, File.ReadAllText(_data.PathOf("signups.json")));
    }

    [Fact]
    public void A_readable_signups_file_is_not_held()
    {
        WriteSignups();

        var store = SignupStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.NotNull(store.Get("sam-invite"));
    }

    // ------------------------------- marked as emailed (signup-3)

    [Fact]
    public void An_invite_given_a_new_code_while_its_email_went_is_not_marked_emailed()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);

        var invite = desk.Store.Create(settings, "sam@example.org", "Sam");
        var oldCode = invite.Code;

        // Cancelled and undone part-way through the batch: a new code, not
        // emailed yet. The batch then reports the old code as sent.
        desk.Store.NewCode(invite, settings);
        desk.MarkEmailed(new MailResult { Sent = new[] { new PinMailed(invite.Id, oldCode) } });

        Assert.Null(invite.EmailedUtc);
    }

    [Fact]
    public void A_cancelled_invite_is_not_marked_emailed()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);

        var invite = desk.Store.Create(settings, "sam@example.org", "Sam");

        // What Cancel Invite does to it, part-way through the batch.
        invite.State = SignupInviteState.Cancelled;
        desk.MarkEmailed(new MailResult { Sent = new[] { new PinMailed(invite.Id, invite.Code) } });

        Assert.Null(invite.EmailedUtc);
    }

    /// <summary>Windows only: marking saves, and saving seals the code with DPAPI.</summary>
    [Fact]
    public void An_invite_emailed_with_its_own_code_is_marked_emailed()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);

        var invite = desk.Store.Create(settings, "sam@example.org", "Sam");
        desk.MarkEmailed(new MailResult { Sent = new[] { new PinMailed(invite.Id, invite.Code) } });

        Assert.NotNull(invite.EmailedUtc);
    }

    // --------------------------------- invites not saved (signup-4)

    [Fact]
    public void Save_says_when_signups_json_was_not_written()
    {
        // A folder where the file should be: nothing can be written there.
        Directory.CreateDirectory(_data.PathOf("signups.json"));

        var store = SignupStore.Load();
        store.Enabled = true;

        Assert.False(store.Save());

        Directory.Delete(_data.PathOf("signups.json"));
        Assert.True(store.Save());
    }

    /// <summary>Windows only: a held file stops the write there, and saving seals the codes with DPAPI.</summary>
    [Fact]
    public void Invites_that_cannot_be_saved_are_not_kept_or_published()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);

        // So the write below is a replace of a file that is there.
        desk.SetEnabled(true);

        using (new FileStream(_data.PathOf("signups.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<InvalidOperationException>(() => desk.CreateInvites(new[] { ("jo@example.com", "Jo") }));
        }

        Assert.Empty(desk.Store.All);
        Assert.True(desk.Store.Enabled);
        Assert.DoesNotContain("\"h\"", desk.Document());
    }

    /// <summary>Windows only, as above.</summary>
    [Fact]
    public void A_failed_invite_save_keeps_codes_sealed_on_another_pc()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var expires = DateTime.UtcNow.AddDays(7).ToString("o");
        File.WriteAllText(_data.PathOf("signups.json"),
            "{ \"Enabled\": true, \"SealedCodes\": \"sealed-on-another-pc\", \"Invites\": ["
            + "{ \"Id\": \"elsewhere\", \"Club\": \"acme-rc\", \"Email\": \"sam@example.com\", \"ExpiresUtc\": \"" + expires + "\" } ] }");

        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);

        using (new FileStream(_data.PathOf("signups.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<InvalidOperationException>(() => desk.CreateInvites(new[] { ("jo@example.com", "Jo") }));
        }

        // The next thing saved - here, switching sign-up off.
        desk.SetEnabled(false);

        Assert.Contains("sealed-on-another-pc", File.ReadAllText(_data.PathOf("signups.json")));
    }
}
