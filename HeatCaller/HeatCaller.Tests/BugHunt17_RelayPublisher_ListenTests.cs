using System.Net;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, #194: a listening-along close in flight to the OLD club when
/// the operator saves a new one. Bug hunt 16 kept the close owed; but the old
/// club's answer still marked listening as told, so the next pass took the
/// new club as already closed and never sent it anything.
/// </summary>
public sealed class BugHunt17_RelayPublisher_ListenTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    [Fact]
    public void A_listening_close_the_old_club_took_is_then_sent_to_the_new_one()
    {
        var settings = new AppSettings
        {
            ListenAlongOnTheRelay = false,
            RelayEnabled = false,
            RelayBaseUrl = "http://127.0.0.1:9",
            RelayClub = "old-club",
            RelayToken = "hg_test",
        };

        using var publisher = new RelayPublisher(settings);

        var asked = new List<string>();
        var saved = false;

        // Listening's own client since 19 Nov 2026.
        var http = FieldOf(publisher, "_listenHttp");
        ((HttpClient)http.GetValue(publisher)!).Dispose();
        http.SetValue(publisher, new HttpClient(new Answers(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            asked.Add(path);

            // The operator saves the new club before the old one's answer is back.
            if (!saved && path.Contains("/c/old-club/", StringComparison.Ordinal))
            {
                saved = true;
                settings.RelayClub = "new-club";
                publisher.SettingsChanged();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        })));

        var close = typeof(RelayPublisher).GetMethod("SendListenClose", BindingFlags.Instance | BindingFlags.NonPublic)!;

        close.Invoke(publisher, new object[] { CancellationToken.None });

        Assert.Contains(asked, p => p.Contains($"/c/old-club/{ListenAlong.Channel}/open", StringComparison.Ordinal));
        Assert.Equal(true, FieldOf(publisher, "_listenClosePending").GetValue(publisher));
        Assert.Equal(false, FieldOf(publisher, "_listenOpenTold").GetValue(publisher));

        // The next pass really tells the new club, and only then is it settled.
        close.Invoke(publisher, new object[] { CancellationToken.None });

        Assert.Contains(asked, p => p.Contains($"/c/new-club/{ListenAlong.Channel}/open", StringComparison.Ordinal));
        Assert.Equal(false, FieldOf(publisher, "_listenClosePending").GetValue(publisher));
    }

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }
}
