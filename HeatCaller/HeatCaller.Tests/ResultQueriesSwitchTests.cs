using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Result queries on a switch of their own (5 Nov 2026, Asher: "can you turn
/// off the something wrong with a result from the app? if not add it to the
/// club website tab"). On by default, as they always were with driver
/// requests on; off, driver/open carries "queries": false and the relay hides
/// "Something wrong?" and refuses a query.
/// </summary>
public sealed class ResultQueriesSwitchTests
{
    [Fact]
    public void Result_queries_are_on_by_default_and_for_an_old_settings_file()
    {
        Assert.True(new AppSettings().RelayResultQueries);

        var old = JsonSerializer.Deserialize<AppSettings>("{\"RelayDriverRequests\":true}");
        Assert.NotNull(old);
        Assert.True(old!.RelayResultQueries);
    }

    [Fact]
    public void Driver_open_says_queries_false_only_when_open_with_queries_off()
    {
        Assert.Equal("{\"open\":true}", RelayPublisher.AsksOpenBody(open: true, resultQueries: true));
        Assert.Equal("{\"open\":true,\"queries\":false}", RelayPublisher.AsksOpenBody(open: true, resultQueries: false));

        // Closing is closing: nothing about queries.
        Assert.Equal("{\"open\":false}", RelayPublisher.AsksOpenBody(open: false, resultQueries: false));
        Assert.Equal("{\"open\":false}", RelayPublisher.AsksOpenBody(open: false, resultQueries: true));
    }
}
