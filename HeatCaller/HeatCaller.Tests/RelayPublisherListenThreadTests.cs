using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// LISTENING ALONG ON A THREAD OF ITS OWN (19 Nov 2026, the music "skips or
/// drops"). A stretch of the room's sound used to go out on the publisher's
/// worker, so it waited behind whatever document upload or poll was on the
/// wire - up to a minute - and a stretch older than fifteen seconds is
/// thrown away. These say it no longer waits, that the two threads share
/// one backoff so a relay that is down is not hammered by either, that it
/// sends nothing while publishing is off, and that it stops cleanly.
/// </summary>
public sealed class RelayPublisherListenThreadTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AppSettings Settings() => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = "http://127.0.0.1:9",
        RelayClub = "test-club",
        RelayToken = "hg_test",
        ListenAlongOnTheRelay = true,
    };

    private static ListenSegment Segment(long number) =>
        new(new byte[2048], 4, 115, 1152, ListenStream.SampleRate, 638_000_000_000_000_000, number, Array.Empty<ListenCaption>(), DateTime.UtcNow);

    private static FieldInfo FieldOf(string name) =>
        typeof(RelayPublisher).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(RelayPublisher), name);

    private static void Use(RelayPublisher publisher, string client, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        var field = FieldOf(client);
        ((HttpClient)field.GetValue(publisher)!).Dispose();
        field.SetValue(publisher, new HttpClient(new Answers(answer)));
    }

    private static Task<HttpResponseMessage> Ok() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

    /// <summary>The worker's client, stuck on its first request until let go or cancelled - a document on a slow uplink.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Stuck(ManualResetEventSlim entered, ManualResetEventSlim release) =>
        async (request, token) =>
        {
            entered.Set();
            await Task.Run(() => release.Wait(token), token);
            return await Ok();
        };

    private static bool IsStretch(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.Contains($"/publish/{ListenAlong.Channel}/", StringComparison.Ordinal);

    private static TimeSpan BackoffRemaining(RelayPublisher publisher) =>
        (TimeSpan)typeof(RelayPublisher).GetMethod("BackoffRemaining", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(publisher, null)!;

    [Fact]
    public void A_stretch_is_delivered_while_a_slow_document_upload_is_in_flight()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var delivered = new ManualResetEventSlim();

        Use(publisher, "_http", Stuck(entered, release));
        Use(publisher, "_listenHttp", (request, _) =>
        {
            if (IsStretch(request))
            {
                delivered.Set();
            }

            return Ok();
        });

        settings.RelayEnabled = true;
        publisher.SettingsChanged();

        // The worker is part-way through a request that will not finish.
        Assert.True(entered.Wait(Patience), "the worker never sent anything");

        Assert.Equal(ListenAlong.Verdict.Held, publisher.OfferStreamToListeners("2026-11-19", Segment(1)));
        Assert.True(delivered.Wait(Patience), "the stretch waited behind the worker");

        release.Set();
    }

    [Fact]
    public void Stretches_go_out_in_the_order_they_were_cut()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var bodies = new ConcurrentQueue<string>();

        Use(publisher, "_http", Stuck(entered, release));
        Use(publisher, "_listenHttp", async (request, _) =>
        {
            if (IsStretch(request))
            {
                bodies.Enqueue(await request.Content!.ReadAsStringAsync());
            }

            return await Ok();
        });

        settings.RelayEnabled = true;
        publisher.SettingsChanged();

        for (var n = 1; n <= 4; n++)
        {
            publisher.OfferStreamToListeners("2026-11-19", Segment(n));
        }

        var until = DateTime.UtcNow + Patience;

        while (bodies.Count < 4 && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
        }

        Assert.Equal(new[] { 1, 2, 3, 4 }, bodies.Select(b => System.Text.Json.JsonDocument.Parse(b).RootElement.GetProperty("n").GetInt32()));
        release.Set();
    }

    [Fact]
    public void A_relay_listening_cannot_reach_backs_the_worker_off_too()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var tried = new ManualResetEventSlim();

        // The worker never answers (so never resets the backoff); listening
        // cannot connect at all.
        Use(publisher, "_http", Stuck(entered, release));
        Use(publisher, "_listenHttp", (_, _) =>
        {
            tried.Set();
            throw new HttpRequestException("No connection could be made");
        });

        settings.RelayEnabled = true;
        publisher.SettingsChanged();
        publisher.OfferStreamToListeners("2026-11-19", Segment(1));

        Assert.True(tried.Wait(Patience));

        var until = DateTime.UtcNow + Patience;

        while (BackoffRemaining(publisher) <= TimeSpan.Zero && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
        }

        Assert.True(BackoffRemaining(publisher) > TimeSpan.Zero, "the shared backoff was not started");
        release.Set();
    }

    [Fact]
    public void While_the_worker_is_backing_off_listening_waits_too()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        var asked = 0;

        Use(publisher, "_http", (_, _) => Ok());
        Use(publisher, "_listenHttp", (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Ok();
        });

        // The worker has failed enough to wait thirty seconds.
        var failed = typeof(RelayPublisher).GetMethod("Failed", BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (var i = 0; i < 6; i++)
        {
            failed.Invoke(publisher, null);
        }

        // On without SettingsChanged, which would forget the backoff.
        settings.RelayEnabled = true;
        publisher.OfferStreamToListeners("2026-11-19", Segment(1));
        Thread.Sleep(1500);

        Assert.Equal(0, Volatile.Read(ref asked));
        Assert.True(BackoffRemaining(publisher) > TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Nothing_is_sent_to_listeners_while_publishing_is_off()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        var asked = 0;

        Use(publisher, "_listenHttp", (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Ok();
        });

        publisher.OfferStreamToListeners("2026-11-19", Segment(1));
        Thread.Sleep(1200);

        Assert.Equal(0, Volatile.Read(ref asked));
    }

    [Fact]
    public void Disposing_stops_the_listen_thread_even_mid_send()
    {
        var settings = Settings();
        var publisher = new RelayPublisher(settings);
        using var entered = new ManualResetEventSlim();
        using var never = new ManualResetEventSlim();
        using var sending = new ManualResetEventSlim();

        Use(publisher, "_http", Stuck(entered, never));
        Use(publisher, "_listenHttp", async (_, token) =>
        {
            sending.Set();
            await Task.Delay(Timeout.Infinite, token);
            return await Ok();
        });

        settings.RelayEnabled = true;
        publisher.SettingsChanged();
        publisher.OfferStreamToListeners("2026-11-19", Segment(1));

        Assert.True(sending.Wait(Patience));

        var listener = (Thread)FieldOf("_listener").GetValue(publisher)!;
        Assert.True(listener.IsAlive);

        publisher.Dispose();

        Assert.False(listener.IsAlive);
    }

    [Fact]
    public async Task Quiescing_stops_a_listen_send_on_its_way_and_sends_nothing_more()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(settings);
        using var sending = new ManualResetEventSlim();
        var asked = 0;

        Use(publisher, "_http", (_, _) => Ok());
        Use(publisher, "_listenHttp", async (_, token) =>
        {
            Interlocked.Increment(ref asked);
            sending.Set();
            await Task.Delay(Timeout.Infinite, token);
            return await Ok();
        });

        settings.RelayEnabled = true;
        publisher.SettingsChanged();
        publisher.OfferStreamToListeners("2026-11-19", Segment(1));
        Assert.True(sending.Wait(Patience));

        var quiet = publisher.QuiesceAsync();
        Assert.Same(quiet, await Task.WhenAny(quiet, Task.Delay(Patience)));

        var before = Volatile.Read(ref asked);
        publisher.OfferStreamToListeners("2026-11-19", Segment(2));
        await Task.Delay(1200);

        Assert.Equal(before, Volatile.Read(ref asked));
        Assert.True(((ManualResetEventSlim)FieldOf("_listenIdle").GetValue(publisher)!).IsSet);
    }

    private sealed class Answers(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return answer(request, cancellationToken);
        }
    }
}
