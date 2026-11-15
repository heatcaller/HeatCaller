using System.Net;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 #30, "Email me a link to my page": the emails went out before
/// the ack to the relay, and the ack's answer was never checked. An ack that
/// failed left the asks on the relay, so the next pass, 30 seconds on,
/// emailed the same driver again - and again on every pass until one landed.
/// </summary>
public sealed class BugHunt19_SignInMailTests : IDisposable
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

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    [Fact]
    public async Task A_failed_ack_does_not_email_the_driver_again_on_the_next_pass()
    {
        var settings = Mailing();
        settings.RelayEnabled = false;
        using var publisher = new RelayPublisher(settings);

        // No pass of the worker's own, so only the ones below run.
        await publisher.QuiesceAsync();
        settings.RelayEnabled = true;

        var acks = 0;
        var inbox = """{"ok":true,"items":[{"n":7,"email":"sam@example.com"}]}""";
        var http = FieldOf(publisher, "_http");
        ((HttpClient)http.GetValue(publisher)!).Dispose();
        http.SetValue(publisher, new HttpClient(new Answers(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/signin/email/inbox", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Volatile.Read(ref inbox), Encoding.UTF8, "application/json") };
            }

            Interlocked.Increment(ref acks);

            // The relay is having a bad moment: the asks stay where they are.
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"ok":false}""") };
        })));

        publisher.TransponderForEmail = email => email is "sam@example.com" or "ann@example.com" ? "T100" : null;

        var mailed = new List<string>();
        publisher.SendSignInMail = (to, subject, body) =>
        {
            lock (mailed)
            {
                mailed.Add(to);
            }

            return Task.FromResult(new MailResult { Ok = true });
        };

        var collect = typeof(RelayPublisher).GetMethod("CollectSignInMailPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tick = FieldOf(publisher, "_nextSignInMailTick");

        void Pass()
        {
            tick.SetValue(publisher, 0L);
            collect.Invoke(publisher, new object[] { CancellationToken.None });
        }

        Pass();
        Pass();
        Pass();

        // A new ask arrives alongside the one still waiting: only it goes.
        Volatile.Write(ref inbox, """{"ok":true,"items":[{"n":7,"email":"sam@example.com"},{"n":8,"email":"ann@example.com"}]}""");
        Pass();

        await Task.Delay(500);

        Assert.Equal(4, Volatile.Read(ref acks));

        lock (mailed)
        {
            Assert.Equal(new[] { "ann@example.com", "sam@example.com" }, mailed.Order(StringComparer.Ordinal).ToArray());
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
