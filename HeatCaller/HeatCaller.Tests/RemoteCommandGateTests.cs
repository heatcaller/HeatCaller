namespace HeatCaller.Tests;

/// <summary>
/// RemoteCommandGate - the brake /api/command never had (25 Sep audit): a
/// right PIN bought unlimited race-control commands, each queuing work onto
/// the UI thread that still ran after the phone stopped waiting.
/// </summary>
public sealed class RemoteCommandGateTests
{
    private readonly RemoteCommandGate _gate = new();

    [Fact]
    public void A_second_command_while_one_is_being_carried_out_is_turned_away_and_told_which()
    {
        Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("marshal-1", "RED", out _));

        var answer = _gate.TryBegin("marshal-2", "RED", out var running);

        Assert.Equal(RemoteCommandGate.Answer.Busy, answer);
        Assert.Equal("RED", running);
    }

    [Fact]
    public void Once_the_first_is_answered_the_next_goes()
    {
        _gate.TryBegin("marshal-1", "YELLOW", out _);
        _gate.End();

        Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("marshal-2", "GREEN", out _));
    }

    [Fact]
    public void One_device_is_stopped_after_its_share_for_the_minute()
    {
        for (var i = 0; i < RemoteCommandGate.PerDevicePerMinute; i++)
        {
            Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("script", "RESET", out _));
            _gate.End();
        }

        Assert.Equal(RemoteCommandGate.Answer.TooMany, _gate.TryBegin("script", "RESET", out _));

        // ...and nobody else is: the marshal's RED still goes.
        Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("marshal", "RED", out _));
    }

    [Fact]
    public void A_device_that_keeps_changing_its_address_meets_the_all_devices_ceiling()
    {
        for (var i = 0; i < RemoteCommandGate.AllDevicesPerMinute; i++)
        {
            Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin($"address-{i}", "RESET", out _));
            _gate.End();
        }

        Assert.Equal(RemoteCommandGate.Answer.TooMany, _gate.TryBegin("address-new", "RESET", out _));
    }

    [Fact]
    public void Being_turned_away_as_busy_costs_nothing_from_the_minute_s_share()
    {
        _gate.TryBegin("marshal-1", "RED", out _);

        // A phone retrying against a busy laptop - these must not use up its minute.
        for (var i = 0; i < RemoteCommandGate.PerDevicePerMinute * 3; i++)
        {
            Assert.Equal(RemoteCommandGate.Answer.Busy, _gate.TryBegin("marshal-2", "RED", out _));
        }

        _gate.End();

        Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("marshal-2", "GREEN", out _));
    }

    [Fact]
    public void Commands_from_many_threads_at_once_let_exactly_one_through()
    {
        // The flood from the audit: a few hundred at once. Nothing calls End,
        // as if the laptop were still busy with the first.
        var answers = new System.Collections.Concurrent.ConcurrentBag<RemoteCommandGate.Answer>();

        Parallel.For(0, 300, i => answers.Add(_gate.TryBegin($"phone-{i % 7}", "RESET", out _)));

        Assert.Equal(1, answers.Count(a => a == RemoteCommandGate.Answer.Go));
        Assert.Equal(299, answers.Count(a => a == RemoteCommandGate.Answer.Busy));
    }

    [Fact]
    public void A_fresh_start_of_the_web_server_forgets_the_counts()
    {
        for (var i = 0; i < RemoteCommandGate.PerDevicePerMinute; i++)
        {
            _gate.TryBegin("marshal", "YELLOW", out _);
            _gate.End();
        }

        _gate.Clear();

        Assert.Equal(RemoteCommandGate.Answer.Go, _gate.TryBegin("marshal", "YELLOW", out _));
    }
}
