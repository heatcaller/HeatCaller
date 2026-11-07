using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026): the relay publisher.
///
/// Shout-outs from the club's page were confirmed - deleted on the relay -
/// when club-notes.json could not hold them; listening's open backoff
/// outlived a corrected club id; and a correction queued behind its own
/// uncorrected copy was dropped from the Owed ledger when the old copy went.
/// </summary>
public sealed class BugHunt11_RelayPublisherTests : IDisposable
{
    private static readonly DateTime Raced = new(2026, 11, 4, 19, 0, 0, DateTimeKind.Utc);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static AppSettings Relay(string root, string club) => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = root,
        RelayClub = club,
        RelayToken = "hg_test",
        ClubNotesFromTheRelay = true,
        ListenAlongOnTheRelay = true,
    };

    // ------------------------------------------------ shout-outs (notes)

    private ClubNotes LoadUnreadableNotes()
    {
        File.WriteAllText(_data.PathOf("club-notes.json"), "{\"DayId\":\"\",\"Notes\":[]}");

        // Held by a sync tool or antivirus as the app starts.
        using (new FileStream(_data.PathOf("club-notes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return ClubNotes.Load();
        }
    }

    [Fact]
    public void A_shout_out_that_could_not_be_saved_is_left_on_the_relay()
    {
        var notes = LoadUnreadableNotes();
        Assert.True(notes.LoadedUnreadable);

        using var relay = new FakeRelay((method, path) => path switch
        {
            _ when path.EndsWith("/notes/inbox", StringComparison.Ordinal) =>
                (200, "{\"items\":[{\"id\":\"n1\",\"m\":\"Happy birthday Dad\",\"n\":\"Jess\"}]}"),
            _ => (200, "{}"),
        });

        using var publisher = new RelayPublisher(Relay(relay.Root, "acme-rc"));
        publisher.TonightsNotes = () => notes;

        Call(publisher, "CollectClubNotes", CancellationToken.None);

        // Still on Race Control for this session...
        Assert.Contains(notes.All, n => n.Text == "Happy birthday Dad");

        // ...but not deleted on the relay, because a restart would lose it.
        Assert.DoesNotContain(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal));
        Assert.Contains("left on the relay", publisher.NotesProblem ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shout_out_that_was_saved_is_still_confirmed()
    {
        var notes = ClubNotes.Load();

        using var relay = new FakeRelay((method, path) => path switch
        {
            _ when path.EndsWith("/notes/inbox", StringComparison.Ordinal) =>
                (200, "{\"items\":[{\"id\":\"n1\",\"m\":\"Happy birthday Dad\",\"n\":\"Jess\"}]}"),
            _ => (200, "{}"),
        });

        using var publisher = new RelayPublisher(Relay(relay.Root, "acme-rc"));
        publisher.TonightsNotes = () => notes;

        Call(publisher, "CollectClubNotes", CancellationToken.None);

        Assert.Contains(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal) && r.Contains("n1", StringComparison.Ordinal));
        Assert.Null(publisher.NotesProblem);
    }

    [Fact]
    public void The_store_says_when_a_note_from_the_room_is_held_in_memory_only()
    {
        var unreadable = LoadUnreadableNotes();

        Assert.Null(unreadable.AddFromTheRoom("Happy birthday Dad", "Jess", out var notOnDisk));
        Assert.True(notOnDisk);

        // Offered again by the relay: still only in memory.
        Assert.Null(unreadable.AddFromTheRoom("Happy birthday Dad", "Jess", out notOnDisk));
        Assert.True(notOnDisk);

        // Refused outright: nothing to keep, so nothing to hold back.
        Assert.NotNull(unreadable.AddFromTheRoom(new string('x', ClubNotes.MostChars + 1), null, out notOnDisk));
        Assert.False(notOnDisk);
    }

    [Fact]
    public void A_note_whose_write_failed_is_written_and_released_when_offered_again()
    {
        var notes = ClubNotes.Load();
        Assert.Null(notes.Add("Raffle closes at eight"));

        bool notOnDisk;

        using (new FileStream(_data.PathOf("club-notes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out notOnDisk));
            Assert.True(notOnDisk);
        }

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out notOnDisk));
        Assert.False(notOnDisk);
        Assert.Contains(ClubNotes.Load().All, n => n.Text == "Happy birthday Dad");
    }

    // Bug hunt 12, #25: a Save landing while the old club's open was in
    // flight had its reset overwritten, and the new club was never opened.
    [Fact]
    public void A_club_saved_while_shout_outs_open_on_the_old_one_is_still_opened()
    {
        RelayPublisher? publisher = null;
        var settings = Relay("http://127.0.0.1:1", "old-club");

        using var relay = new FakeRelay((method, path) =>
        {
            if (path.Contains("/c/old-club/notes/open", StringComparison.Ordinal))
            {
                // The operator saves a different club mid-request.
                settings.RelayClub = "new-club";
                publisher!.SettingsChanged();
            }

            return (200, "{}");
        });

        settings.RelayBaseUrl = relay.Root;
        using var made = new RelayPublisher(settings);
        publisher = made;

        var old = Field(made, "_target");
        Assert.Equal(true, Call(made, "TellTheRelayNotesAreOpen", old, "old-club", true, false, CancellationToken.None));

        var current = Field(made, "_target");
        Assert.NotEqual(old, current);
        Assert.Equal(true, Call(made, "TellTheRelayNotesAreOpen", current, "new-club", true, false, CancellationToken.None));
        Assert.Contains(relay.Requests, r => r.Contains("/c/new-club/notes/open", StringComparison.Ordinal));
    }

    // ------------------------------------------------ listening along

    [Fact]
    public void A_corrected_club_is_asked_to_open_listening_at_once_not_after_the_old_backoff()
    {
        using var relay = new FakeRelay((method, path) =>
            path.Contains("/c/wrong-club/", StringComparison.Ordinal) ? (404, "{}") : (200, "{}"));

        var settings = Relay(relay.Root, "wrong-club");
        using var publisher = new RelayPublisher(settings);

        var wrong = Field(publisher, "_target");
        Assert.Equal(false, Call(publisher, "TellTheRelayListeningIsOpen", wrong, "wrong-club", true, CancellationToken.None));

        // The operator fixes the id and presses Save.
        settings.RelayClub = "right-club";
        publisher.SettingsChanged();

        var right = Field(publisher, "_target");
        Assert.Equal(true, Call(publisher, "TellTheRelayListeningIsOpen", right, "right-club", true, CancellationToken.None));
        Assert.Contains(relay.Requests, r => r.Contains("/c/right-club/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_failing_club_is_still_backed_off()
    {
        using var relay = new FakeRelay((method, path) => (404, "{}"));

        using var publisher = new RelayPublisher(Relay(relay.Root, "wrong-club"));
        var target = Field(publisher, "_target");

        Assert.Equal(false, Call(publisher, "TellTheRelayListeningIsOpen", target, "wrong-club", true, CancellationToken.None));
        var asked = relay.Requests.Count;

        Assert.Equal(false, Call(publisher, "TellTheRelayListeningIsOpen", target, "wrong-club", true, CancellationToken.None));
        Assert.Equal(asked, relay.Requests.Count);
    }

    // ------------------------------------------------ results ledger (Owed)

    /// <summary>
    /// Publishing on, to a relay nothing answers at, so both copies stay
    /// queued while the test plays the worker's part.
    /// </summary>
    private static AppSettings Unreachable() => new()
    {
        RelayEnabled = true,
        RelayBaseUrl = "http://127.0.0.1:1",
        RelayClub = "acme-rc",
        RelayToken = "hg_test",
    };

    /// <summary>Takes the head of the queue off as the worker does once the relay has answered for it, and returns its note.</summary>
    private static object TakeHead(RelayPublisher publisher)
    {
        var head = Call(publisher, "PeekResult")!;
        Call(publisher, "CompleteResult", head);

        return head.GetType().GetProperty("Result")!.GetValue(head)!;
    }

    [Fact]
    public void Delivering_the_old_copy_keeps_a_queued_correction_owed()
    {
        using var publisher = new RelayPublisher(Unreachable());

        publisher.PublishResult("x", "{\"v\":1}", Raced);
        publisher.PublishResult("x", "{\"v\":2}", Raced, correction: true);
        Assert.True(publisher.ResultIsOwed("x"));

        var old = TakeHead(publisher);
        Call(publisher, "NoteResultDelivered", old, Field(publisher, "_target"));

        Assert.True(publisher.ResultIsOwed("x"));
        Assert.Contains("x", RelayResultsIndex.Ledger.Load().Owed);

        // And the correction going clears it.
        var corrected = TakeHead(publisher);
        Call(publisher, "NoteResultDelivered", corrected, Field(publisher, "_target"));

        Assert.False(publisher.ResultIsOwed("x"));
    }

    [Fact]
    public void Refusing_the_old_copy_keeps_a_queued_correction_owed()
    {
        using var publisher = new RelayPublisher(Unreachable());

        publisher.PublishResult("x", "{\"v\":1}", Raced);
        publisher.PublishResult("x", "{\"v\":2}", Raced, correction: true);

        var old = TakeHead(publisher);
        Call(publisher, "NoteResultRefused", old);

        Assert.True(publisher.ResultIsOwed("x"));
        Assert.Contains("x", RelayResultsIndex.Ledger.Load().Owed);
    }

    [Fact]
    public void Delivering_the_only_copy_still_clears_owed()
    {
        using var publisher = new RelayPublisher(Unreachable());

        publisher.PublishResult("x", "{\"v\":1}", Raced);
        Assert.True(publisher.ResultIsOwed("x"));

        var only = TakeHead(publisher);
        Call(publisher, "NoteResultDelivered", only, Field(publisher, "_target"));

        Assert.False(publisher.ResultIsOwed("x"));
    }

    /// <summary>
    /// A relay on the loopback that answers each request from a rule and
    /// records "METHOD path body" for every one it was sent.
    /// </summary>
    private sealed class FakeRelay : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Func<string, string, (int Status, string Body)> _answer;
        private readonly CancellationTokenSource _stop = new();

        public ConcurrentQueue<string> RequestQueue { get; } = new();

        public IReadOnlyList<string> Requests => RequestQueue.ToList();

        public string Root { get; }

        public FakeRelay(Func<string, string, (int Status, string Body)> answer)
        {
            _answer = answer;
            _listener.Start();
            Root = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new List<byte>();
                    var one = new byte[1];

                    while (!EndsWithBlankLine(buffer) && stream.Read(one, 0, 1) == 1)
                    {
                        buffer.Add(one[0]);
                    }

                    var head = Encoding.ASCII.GetString(buffer.ToArray());
                    var lines = head.Split("\r\n");
                    var parts = lines[0].Split(' ');
                    var length = lines
                        .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => int.Parse(l["Content-Length:".Length..].Trim()))
                        .FirstOrDefault();

                    var body = new byte[length];
                    var read = 0;

                    while (read < length)
                    {
                        var got = stream.Read(body, read, length - read);

                        if (got == 0)
                        {
                            break;
                        }

                        read += got;
                    }

                    RequestQueue.Enqueue($"{parts[0]} {parts[1]} {Encoding.UTF8.GetString(body, 0, read)}");

                    var (status, text) = _answer(parts[0], parts[1]);
                    var payload = Encoding.UTF8.GetBytes(text);
                    var reply = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");

                    stream.Write(reply);
                    stream.Write(payload);
                    stream.Flush();
                }
                catch
                {
                    // A test that has finished with it.
                }
            }
        }

        private static bool EndsWithBlankLine(List<byte> buffer) =>
            buffer.Count >= 4
            && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n';

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
