namespace HeatCaller;

/// <summary>
/// "Tell the drivers" on the Setup tab (12 Nov 2026, setup plan D13): the
/// next race night as the phones show it, with Announce and Call It Off.
/// MainForm does the announcing, because it needs the relay and the mailbox.
/// </summary>
public sealed partial class RaceDayEventControl
{
    /// <summary>What the phones are told, on a pill: green while taking entries, amber once called off (17 Nov 2026).</summary>
    private readonly InboxPill _lblNextNight = new() { Anchor = AnchorStyles.Left };
    private readonly Button _btnAnnounce = new() { Text = "Announce...", AutoSize = true };
    private readonly Button _btnCallOff = new() { Text = "Call It Off...", AutoSize = true };

    /// <summary>"Announce..." - announce or change the next race night on the phones.</summary>
    public event Action<IWin32Window>? AnnounceNightRequested;

    /// <summary>"Call It Off..." - the next race night is off.</summary>
    public event Action<IWin32Window>? CallOffRequested;

    /// <summary>The row, on Tonight's banner after its caption.</summary>
    private Control BuildNextNightRow(Label caption)
    {
        _btnAnnounce.Click += (_, _) =>
        {
            AnnounceNightRequested?.Invoke(this);
            ShowNextNight();
        };
        _btnCallOff.Click += (_, _) =>
        {
            CallOffRequested?.Invoke(this);
            ShowNextNight();
        };

        ShowNextNight();
        return FieldGrid.Inline(caption, _lblNextNight, _btnAnnounce, _btnCallOff);
    }

    /// <summary>What the phones are told about the next night, in a few words.</summary>
    internal void ShowNextNight()
    {
        _lblNextNight.Text = _settings?.NextNight is not { } night
            ? "Not announced"
            : night.Off
                ? $"{NextNight.When(night.Day)} - called off"
                : $"{NextNight.When(night.Day)}" + (night.TakingEntries ? " - taking entries" : string.Empty);
        _lblNextNight.Tone = _settings?.NextNight is not { } shown
            ? PillTone.Plain
            : shown.Off ? PillTone.Warning : PillTone.Good;
        _btnCallOff.Enabled = _settings?.NextNight is { Off: false };
        _btnAnnounce.Enabled = _settings is not null;
    }
}
