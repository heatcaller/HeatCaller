namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), marshal swaps from phones: a yes to covering a
/// post is checked again when it is made (the ask may be old by then), and
/// the desk is told why a swap it approved could not be made.
/// </summary>
public sealed class BugHunt17_DriverRequests_MarshalsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Max = "0000000000000000000a";
    private const string Hugh = "0000000000000000000b";
    private const string Rhoda = "0000000000000000000c";
    private const string Round = "0123456789abcdef0123456789abcdef";

    /// <summary>A desk over a program where Max (100) and Rhoda (300) marshal the round and Hugh (200) is free.</summary>
    private sealed class Desk
    {
        private int _next;

        public AppSettings Settings { get; } = new();
        public ProfileStore Profiles { get; } = ProfileStore.Load();
        public DriverRequestStore Store { get; } = DriverRequestStore.Load();
        public DriverRequestDesk Requests { get; }
        public HashSet<string> Marshalling { get; } = new() { "100", "300" };
        public List<(string Marshal, string Cover)> Swaps { get; } = new();
        public List<string> Failed { get; } = new();

        /// <summary>Why the program refuses the next swap, or null to make it.</summary>
        public string? Refusal { get; set; }

        public Desk(bool needDesk)
        {
            Settings.MarshalSwapsNeedDesk = needDesk;
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Max Power" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "200", DriverName = "Hugh Turn" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "300", DriverName = "Rhoda Rage" }, out _));
            var badges = new Dictionary<string, string> { [Max] = "100", [Hugh] = "200", [Rhoda] = "300" };

            Requests = new DriverRequestDesk(Store, Settings, Profiles, null, SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), new UndoService(), _ => { },
                badge => badges.TryGetValue(badge, out var t) ? t : null)
            {
                DutyOf = (round, main) => round == Round && Marshalling.Contains(main) ? new MarshalSwaps.Duty("Q2 Heat 3 (Stock)", "Turn 3") : null,
                CoverCandidates = (round, main) => badges
                    .Where(b => !Marshalling.Contains(b.Value) && b.Value != main)
                    .Select(b => (b.Value, b.Key))
                    .ToList(),
                ApplyMarshalCover = (round, marshal, cover) =>
                {
                    if (Refusal is not null)
                    {
                        return Refusal;
                    }

                    Swaps.Add((marshal, cover));
                    Marshalling.Remove(marshal);
                    Marshalling.Add(cover);
                    return null;
                },
            };
            Requests.MarshalSwapFailed = Failed.Add;
        }

        public DriverRequest Ask(string kind, Dictionary<string, string> texts, string badge)
        {
            var ask = new DriverRequest
            {
                Id = (++_next).ToString("x24"),
                Badge = badge,
                Kind = kind,
                AskedUtc = DateTime.UtcNow.AddSeconds(_next),
                Proof = DriverAsk.ByBadge,
            };

            foreach (var (k, v) in texts)
            {
                ask.Texts[k] = v;
            }

            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            Requests.FileWaitingWrites();
            return Store.Get(ask.Id)!;
        }

        public DriverRequest Swap(string from, string to) =>
            Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(to) }, from);

        public DriverRequest Yes(DriverRequest swap, string from) =>
            Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, from);
    }

    [Fact]
    public void A_swap_the_desk_approves_too_late_says_why_it_was_not_made()
    {
        var desk = new Desk(needDesk: true);
        var yes = desk.Yes(desk.Swap(Max, Hugh), Hugh);
        Assert.Equal(DriverAsk.Waiting, yes.State);

        desk.Refusal = "Q2 Heat 3 has already run.";

        Assert.False(desk.Requests.ApproveCover(yes, out var refusal));
        Assert.Equal("Q2 Heat 3 has already run.", refusal);
        Assert.Empty(desk.Swaps);
    }

    [Fact]
    public void One_driver_cannot_say_yes_to_two_posts_of_the_same_race()
    {
        var desk = new Desk(needDesk: false);

        // Both asks are good when sent: Hugh is free.
        var fromMax = desk.Swap(Max, Hugh);
        var fromRhoda = desk.Swap(Rhoda, Hugh);
        Assert.Equal(DriverAsk.Approved, fromMax.State);
        Assert.Equal(DriverAsk.Approved, fromRhoda.State);

        Assert.Equal(DriverAsk.Approved, desk.Yes(fromRhoda, Hugh).State);

        var second = desk.Yes(fromMax, Hugh);

        Assert.Equal(DriverAsk.Declined, second.State);
        Assert.Contains("marshalling it already", second.Said, StringComparison.Ordinal);
        Assert.Equal(new[] { ("300", "200") }, desk.Swaps);

        // Max thinks it's sorted, so Race Control is told he is still on.
        Assert.Contains("Max P. is still on the post", Assert.Single(desk.Failed), StringComparison.Ordinal);
    }

    [Fact]
    public void A_yes_to_an_ask_the_marshal_has_since_put_to_somebody_else_is_refused()
    {
        var desk = new Desk(needDesk: false);
        desk.Marshalling.Remove("300");

        var old = desk.Swap(Max, Hugh);
        var newer = desk.Swap(Max, Rhoda);
        Assert.Equal(DriverAsk.Approved, newer.State);

        var stale = desk.Yes(old, Hugh);

        Assert.Equal(DriverAsk.Declined, stale.State);
        Assert.Equal("That ask to cover isn't open any more.", stale.Said);
        Assert.Empty(desk.Swaps);

        // The one asked instead can still take it.
        Assert.Equal(DriverAsk.Approved, desk.Yes(newer, Rhoda).State);
        Assert.Equal(new[] { ("100", "300") }, desk.Swaps);
    }

    [Fact]
    public void A_yes_for_a_marshal_who_no_longer_has_the_post_is_refused()
    {
        var desk = new Desk(needDesk: true);
        var yes = desk.Yes(desk.Swap(Max, Hugh), Hugh);

        // The program changed while it waited for the desk.
        desk.Marshalling.Remove("100");

        Assert.False(desk.Requests.ApproveCover(yes, out var refusal));
        Assert.Equal("The swap can't be made - Max P. isn't marshalling that race any more.", refusal);
        Assert.Empty(desk.Swaps);
    }
}
