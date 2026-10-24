namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8: the shout-out page told nobody when the club was not taking
/// messages - it always showed the box and the button, and a message typed
/// into it was lost to a bare "Not found".
/// </summary>
public sealed class BugHunt8_WebServerService_Notes_csTests : IDisposable
{
    // Declared first: ClubNotes.Load reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static WebServerService Server() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    [Fact]
    public void A_club_not_taking_messages_gets_a_sentence_and_no_form()
    {
        var server = Server();
        server.TonightsNotes = () => ClubNotes.Load();
        server.NotesFromTheRoomOpen = () => false;

        var page = server.BuildNotePage();

        Assert.Contains("Messages to the announcer aren't being taken right now.", page);
        Assert.DoesNotContain("<textarea", page);
        Assert.DoesNotContain("SEND IT", page);
        Assert.DoesNotContain("switched off", page);

        // Still the same page shell, look-watch and all.
        Assert.Contains("MESSAGE THE ANNOUNCER", page);
        Assert.Contains("<script>(function(){", page);
    }

    [Fact]
    public void An_older_wiring_with_no_store_is_treated_as_closed()
    {
        var page = Server().BuildNotePage();

        Assert.DoesNotContain("<textarea", page);
        Assert.Contains("aren't being taken right now", page);
    }

    [Fact]
    public void A_club_taking_messages_gets_the_form()
    {
        var notes = ClubNotes.Load();
        var server = Server();
        server.TonightsNotes = () => notes;
        server.NotesFromTheRoomOpen = () => true;

        var page = server.BuildNotePage();

        Assert.Contains("<textarea", page);
        Assert.Contains("SEND IT", page);
        Assert.Contains("maxlength=\"" + ClubNotes.MostChars + "\"", page);
        Assert.Contains("<script>(function(){", page);
    }
}
