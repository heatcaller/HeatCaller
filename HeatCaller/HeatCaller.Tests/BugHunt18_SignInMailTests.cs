using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18, "Email me a link to my page":
///
/// The check-in document offered the link on the club's switch and the mail
/// alone, but the laptop only collects the asks while driver nights are
/// published too - so with nights off the phone was told a link was on its
/// way and none ever came.
///
/// And the email itself was sent on the relay worker, the thread that sends
/// the live frames: the board froze for as long as the mail took.
/// </summary>
public sealed class BugHunt18_SignInMailTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AppSettings Mailing() => new()
    {
        RelayClub = "acme-rc",
        RelayBaseUrl = "http://127.0.0.1:9",
        RelayToken = "hg_test",
        RelayCheckInEnabled = true,
        RelayEmailSignInLink = true,
        PublishDriverNightsToTheRelay = true,
        RelayCheckInSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        SmtpHost = "smtp.club.invalid",
        SmtpPort = 587,
        SmtpUser = "club@club.invalid",
        SmtpPassword = "app-password",
    };

    private static bool EmailLinkOffered(AppSettings settings)
    {
        using var doc = JsonDocument.Parse(RelayDocuments.CheckIn(
            Array.Empty<WebServerService.CheckInEntry>(), ProfileStore.Load(), settings, "Tonight", new Dictionary<string, string?>()));

        return doc.RootElement.GetProperty("emailLink").GetBoolean();
    }

    [Fact]
    public void The_check_in_list_offers_the_link_only_when_the_laptop_will_collect_the_asks()
    {
        var on = Mailing();
        Assert.True(PinMailer.IsConfigured(on));
        Assert.True(EmailLinkOffered(on));

        var noNights = Mailing();
        noNights.PublishDriverNightsToTheRelay = false;
        Assert.False(RelayPublisher.SignInMailWanted(noNights));
        Assert.False(EmailLinkOffered(noNights));
    }

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    [Fact]
    public async Task The_collect_pass_acknowledges_and_returns_without_waiting_for_the_email()
    {
        var settings = Mailing();
        settings.RelayEnabled = false;
        using var publisher = new RelayPublisher(settings);

        // No pass of the worker's own, so only the one below runs.
        await publisher.QuiesceAsync();
        settings.RelayEnabled = true;

        var asked = new List<string>();
        var http = FieldOf(publisher, "_http");
        ((HttpClient)http.GetValue(publisher)!).Dispose();
        http.SetValue(publisher, new HttpClient(new Answers(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            lock (asked)
            {
                asked.Add(path);
            }

            var json = path.EndsWith("/signin/email/inbox", StringComparison.Ordinal)
                ? """{"ok":true,"items":[{"n":7,"email":"sam@example.com"}]}"""
                : """{"ok":true}""";

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        })));

        publisher.TransponderForEmail = email => email == "sam@example.com" ? "T100" : null;

        // A mailbox that doesn't answer until told to.
        var release = new TaskCompletionSource<MailResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.SendSignInMail = (to, subject, body) =>
        {
            started.TrySetResult(to + "\n" + body);
            return release.Task;
        };

        var collect = typeof(RelayPublisher).GetMethod("CollectSignInMailPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pass = Task.Run(() => collect.Invoke(publisher, new object[] { CancellationToken.None }));

        try
        {
            // The pass is over - and the ask acknowledged - while the email is still going.
            Assert.Same(pass, await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(10))));

            lock (asked)
            {
                Assert.Contains(asked, p => p.EndsWith("/c/acme-rc/signin/email/ack", StringComparison.Ordinal));
            }

            var mail = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.StartsWith("sam@example.com\n", mail);
            Assert.Contains("/c/acme-rc/night#d=", mail);
        }
        finally
        {
            release.TrySetResult(new MailResult { Ok = true });
        }
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
