namespace HeatCaller.Tests;

/// <summary>
/// P4 of the timing layer (23 Oct 2026): the lookups the race calls on the
/// timing thread. ProfileStore.PrimaryForAlternate is asked on every pass
/// from that thread while the UI thread edits the profiles, so it must give
/// a whole answer from any thread - and the same answers it always gave.
/// </summary>
public sealed class TimingLayer_P4_LookupTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DriverProfile Driver(string main, params string[] alternates)
    {
        var profile = new DriverProfile { Transponder = main, DriverName = "Driver " + main };

        foreach (var alternate in alternates)
        {
            profile.Transponders.Add(new OwnedTransponder { Id = alternate });
        }

        return profile;
    }

    [Fact]
    public void The_alternate_lookup_answers_as_before()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(Driver("100", "200"), out _));
        Assert.True(store.TryAdd(Driver("400", "500"), out _));

        Assert.Equal("100", store.PrimaryForAlternate("200"));
        Assert.Equal("100", store.PrimaryForAlternate(" 200 "));
        Assert.Null(store.PrimaryForAlternate("100"));
        Assert.Null(store.PrimaryForAlternate("999"));
        Assert.Null(store.PrimaryForAlternate(""));

        // Gone with its driver.
        store.Delete("400");
        Assert.Null(store.PrimaryForAlternate("500"));
    }

    [Fact]
    public void The_alternate_lookup_is_safe_from_another_thread_while_the_profiles_change()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(Driver("100", "200"), out _));

        var stop = false;
        Exception? fault = null;
        var wrong = 0;
        var asked = 0;

        // The timing thread's side: asking on every pass.
        var timing = new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    // 100's spare is never anyone else's, whatever is going on.
                    if (store.PrimaryForAlternate("200") != "100")
                    {
                        Interlocked.Increment(ref wrong);
                    }

                    store.PrimaryForAlternate("7000");
                    Interlocked.Increment(ref asked);
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        }) { IsBackground = true };

        timing.Start();

        // The UI's side: drivers added, changed and deleted meanwhile.
        for (var i = 0; i < 300; i++)
        {
            var main = (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            store.Upsert(Driver(main, (7000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (i % 3 == 0)
            {
                store.Delete(main);
            }
        }

        Volatile.Write(ref stop, true);
        Assert.True(timing.Join(TimeSpan.FromSeconds(5)));

        Assert.Null(fault);
        Assert.Equal(0, wrong);
        Assert.True(asked > 0);
    }
}
