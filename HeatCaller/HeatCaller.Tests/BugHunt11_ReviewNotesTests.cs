using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 review (5 Nov 2026): a shout-out left on the relay because
/// club-notes.json could not hold it came back as a new note once the race
/// director removed it (or NewMeeting cleared the list), on every poll.
/// </summary>
public sealed class BugHunt11_ReviewNotesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            method.Invoke(owner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static AppSettings Relay(string root) => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = root,
        RelayClub = "acme-rc",
        RelayToken = "hg_test",
        ClubNotesFromTheRelay = true,
    };

    private ClubNotes LoadUnreadableNotes()
    {
        File.WriteAllText(_data.PathOf("club-notes.json"), "{\"DayId\":\"\",\"Notes\":[]}");

        using (new FileStream(_data.PathOf("club-notes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return ClubNotes.Load();
        }
    }

    private static FakeRelay InboxWithOne() => new((_, path) =>
        path.EndsWith("/notes/inbox", StringComparison.Ordinal)
            ? (200, "{\"items\":[{\"id\":\"n1\",\"m\":\"Happy birthday Dad\",\"n\":\"Jess\"}]}")
            : (200, "{}"));

    [Fact]
    public void A_held_shout_out_the_director_removed_is_not_filed_again_and_is_confirmed()
    {
        var notes = LoadUnreadableNotes();
        Assert.True(notes.LoadedUnreadable);

        using var relay = InboxWithOne();
        using var publisher = new RelayPublisher(Relay(relay.Root));
        publisher.TonightsNotes = () => notes;

        Call(publisher, "CollectClubNotes", CancellationToken.None);

        var held = Assert.Single(notes.All);
        Assert.DoesNotContain(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal));

        notes.Remove(held.Id);
        Assert.Empty(notes.All);

        Call(publisher, "CollectClubNotes", CancellationToken.None);

        Assert.Empty(notes.All);
        Assert.Contains(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal) && r.Contains("n1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_held_shout_out_cleared_by_a_new_meeting_is_not_filed_again()
    {
        var notes = LoadUnreadableNotes();

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out var notOnDisk));
        Assert.True(notOnDisk);

        notes.NewMeeting("last-week");
        notes.NewMeeting("tonight");
        Assert.Empty(notes.All);

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out notOnDisk));
        Assert.False(notOnDisk);
        Assert.Empty(notes.All);
    }

    [Fact]
    public void A_removed_note_that_was_on_disk_can_be_sent_again()
    {
        var notes = ClubNotes.Load();

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out var notOnDisk));
        Assert.False(notOnDisk);

        notes.Remove(Assert.Single(notes.All).Id);

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess", out notOnDisk));
        Assert.Single(notes.All);
    }

    /// <summary>
    /// 6 Nov 2026, Bug hunt 11b review: the room's cap was asked before the
    /// duplicate check, so with the file unreadable and the list full every
    /// held note's relay copy was refused for room, confirmed and deleted.
    /// </summary>
    [Fact]
    public void A_full_list_of_held_shout_outs_is_never_confirmed_while_the_file_is_unreadable()
    {
        var notes = LoadUnreadableNotes();

        var items = string.Join(",", Enumerable.Range(1, ClubNotes.MostFromTheRoom)
            .Select(i => $"{{\"id\":\"n{i}\",\"m\":\"Shout-out number {i}\",\"n\":\"Jess\"}}"));

        using var relay = new FakeRelay((_, path) =>
            path.EndsWith("/notes/inbox", StringComparison.Ordinal)
                ? (200, $"{{\"items\":[{items}]}}")
                : (200, "{}"));
        using var publisher = new RelayPublisher(Relay(relay.Root));
        publisher.TonightsNotes = () => notes;

        Call(publisher, "CollectClubNotes", CancellationToken.None);
        Assert.Equal(ClubNotes.MostFromTheRoom, notes.All.Count);
        Assert.True(notes.NoRoomFromTheRoom);

        // Offered again with the list full: still held, still on the relay.
        Call(publisher, "CollectClubNotes", CancellationToken.None);
        Call(publisher, "CollectClubNotes", CancellationToken.None);

        Assert.Equal(ClubNotes.MostFromTheRoom, notes.All.Count);
        Assert.DoesNotContain(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal));

        // And the store says so directly.
        Assert.Null(notes.AddFromTheRoom("Shout-out number 1", "Jess", out var notOnDisk));
        Assert.True(notOnDisk);
    }

    /// <summary>
    /// 6 Nov 2026, Bug hunt 11b review: a note held after a failed write,
    /// then on disk after a later save and confirmed, stayed in the held set;
    /// removing it later swallowed every new shout-out with the same words.
    /// </summary>
    [Fact]
    public void After_a_write_recovers_a_removed_shout_out_does_not_swallow_a_new_one_with_the_same_words()
    {
        var notes = ClubNotes.Load();
        Assert.Null(notes.Add("Raffle closes at eight"));

        var inbox = "{\"items\":[{\"id\":\"n1\",\"m\":\"Go Sam!\",\"n\":\"Jess\"}]}";

        using var relay = new FakeRelay((_, path) =>
            path.EndsWith("/notes/inbox", StringComparison.Ordinal) ? (200, Volatile.Read(ref inbox)) : (200, "{}"));
        using var publisher = new RelayPublisher(Relay(relay.Root));
        publisher.TonightsNotes = () => notes;

        // The write fails: held in memory, left on the relay.
        using (new FileStream(_data.PathOf("club-notes.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Call(publisher, "CollectClubNotes", CancellationToken.None);
        }

        var held = Assert.Single(notes.All, n => n.Text == "Go Sam!");
        Assert.DoesNotContain(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal));

        // A later save works, and the next poll confirms the relay's copy.
        Assert.Null(notes.Add("Track closes at ten"));
        Call(publisher, "CollectClubNotes", CancellationToken.None);
        Assert.Contains(relay.Requests, r => r.Contains("/notes/collected", StringComparison.Ordinal) && r.Contains("n1", StringComparison.Ordinal));

        // Read out and removed; the relay's inbox is now empty.
        notes.Remove(held.Id);
        Volatile.Write(ref inbox, "{\"items\":[]}");
        Call(publisher, "CollectClubNotes", CancellationToken.None);
        Assert.DoesNotContain(notes.All, n => n.Text == "Go Sam!");

        // Later somebody else sends the same words: a new shout-out, filed.
        Volatile.Write(ref inbox, "{\"items\":[{\"id\":\"n2\",\"m\":\"Go Sam!\",\"n\":\"Kim\"}]}");
        Call(publisher, "CollectClubNotes", CancellationToken.None);

        Assert.Contains(notes.All, n => n.Text == "Go Sam!" && n.From == "Kim");
    }

    /// <summary>
    /// 6 Nov 2026, Bug hunt 11b review: a note removed while held swallows
    /// its one leftover relay copy, and only that one.
    /// </summary>
    [Fact]
    public void A_removed_held_note_swallows_only_its_one_leftover_relay_copy()
    {
        var notes = LoadUnreadableNotes();

        Assert.Null(notes.AddFromTheRoom("Go Sam!", "Jess", out var notOnDisk));
        Assert.True(notOnDisk);

        notes.Remove(Assert.Single(notes.All).Id);

        // The leftover copy: handled.
        Assert.Null(notes.AddFromTheRoom("Go Sam!", "Jess", out notOnDisk));
        Assert.False(notOnDisk);
        Assert.Empty(notes.All);

        // A new one with the same words: filed (held, the file being unreadable).
        Assert.Null(notes.AddFromTheRoom("Go Sam!", "Kim", out notOnDisk));
        Assert.True(notOnDisk);
        Assert.Single(notes.All);
    }

    /// <summary>
    /// A relay on the loopback that answers each request from a rule and
    /// records "METHOD path body" for every one it was sent. (A copy of
    /// BugHunt11_RelayPublisherTests' private one.)
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
