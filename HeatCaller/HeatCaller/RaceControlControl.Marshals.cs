using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Marshals in place (12 Nov 2026, setup plan D4).
///
/// WHY: at most clubs, missing turn marshals hold the next race up more
/// often than missing drivers do. Auto start (Phase 1) went green on time
/// whether or not anyone was on the posts. For a club that ticks "Wait for
/// the marshals before starting" (the Auto start switch's right-click menu),
/// a minute before the next race this banner names its marshals and their
/// posts, with Marshals in place to confirm them. At zero an unconfirmed
/// start waits, and the announcer calls the posts once. Someone's missing...
/// logs a no-show on the round, for the club's marshal rule.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly TableLayoutPanel _marshalsBanner = new();
    private readonly Label _lblMarshalsBanner = new();
    private readonly Button _btnMarshalsMissing = new() { Text = "Someone's missing..." };

    /// <summary>The round the marshals were last called for at zero, so the call is made once.</summary>
    private string? _marshalsCalledFor;

    /// <summary>
    /// A marshal didn't turn up for a round: the round, "Name (Post)" and
    /// their transponder. MainForm saves it on the round and applies the
    /// club's marshal rule.
    /// </summary>
    public event Action<RoundDefinition, string, string>? MarshalNoShow;

    /// <summary>A no-show taken back off (setup plan step 8): the round, and "Name (Post)". MainForm undoes the rule.</summary>
    public event Action<RoundDefinition, string>? MarshalNoShowTakenBack;

    /// <summary>The marshals of the round the banner is showing, for Someone's missing.</summary>
    private IReadOnlyList<(string Name, string Post)> _marshalsShown = Array.Empty<(string, string)>();

    private RoundDefinition? _marshalsRound;

    /// <summary>The marshal banner, in the cars banner's warning frame. Called from the layout.</summary>
    private void BuildMarshalsAsk()
    {
        _marshalsBanner.AutoSize = true;
        _marshalsBanner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _marshalsBanner.ColumnCount = 1;
        _marshalsBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _marshalsBanner.Padding = new Padding(14, 10, 14, 10);
        _marshalsBanner.Visible = false;
        _marshalsBanner.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Warning, 2);
            e.Graphics.DrawRectangle(pen, 1, 1, _marshalsBanner.Width - 2, _marshalsBanner.Height - 2);
        };

        _lblMarshalsBanner.AutoSize = true;
        _lblMarshalsBanner.Font = AppTheme.BoldFont;
        _lblMarshalsBanner.Margin = new Padding(0, 6, 12, 0);
        _lblMarshalsBanner.MaximumSize = new Size(720, 0);

        var inPlace = new Button { Text = "Marshals in place" };
        inPlace.Click += (_, _) => ConfirmMarshals();

        _btnMarshalsMissing.Click += (_, _) => ShowMarshalsMissingMenu();

        _marshalsBanner.Controls.Add(new ButtonBar(tight: true).With(_lblMarshalsBanner, inPlace, _btnMarshalsMissing));
    }

    /// <summary>
    /// Once a second from the gap: shows the ask from a minute before the
    /// start until somebody confirms, and at zero calls the posts once.
    /// </summary>
    private void ShowMarshalsAsk(RoundDefinition next, IReadOnlyList<(string Name, string Post)> marshals, bool waitForMarshals, DateTime nowUtc)
    {
        var remaining = _autoStart.Remaining(nowUtc, Gap.PushedBackMinutes);
        var show = waitForMarshals
                   && !_autoStart.MarshalsConfirmed
                   && (_autoStartState == AutoStartState.WaitingForMarshals
                       || remaining is { } left && left <= AutoStartClock.AskForMarshalsFrom);

        if (!show)
        {
            HideMarshalsAsk();
            return;
        }

        _marshalsShown = marshals;
        _marshalsRound = next;

        var text = MarshalsInPlace.Ask(next.RoundLabel, marshals, waiting: _autoStartState == AutoStartState.WaitingForMarshals);

        if (_lblMarshalsBanner.Text != text)
        {
            _lblMarshalsBanner.Text = text;
        }

        // Always assigned, never compared: see UnknownTransponderBanner.RefreshVisibility.
        _marshalsBanner.Visible = true;

        if (_autoStartState == AutoStartState.WaitingForMarshals && _marshalsCalledFor != next.Id)
        {
            _marshalsCalledFor = next.Id;
            SayGapLine(MarshalsInPlace.Call(next.RoundLabel, marshals), "Marshals");
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} is waiting for its marshals. Marshals in place starts it.");
        }
    }

    private void HideMarshalsAsk()
    {
        _marshalsBanner.Visible = false;
        _marshalsShown = Array.Empty<(string, string)>();
        _marshalsRound = null;
    }

    /// <summary>The banner's Marshals in place.</summary>
    private void ConfirmMarshals()
    {
        _autoStart.ConfirmMarshals();
        HideMarshalsAsk();
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshals in place - confirmed at the desk.");
    }

    /// <summary>Someone's missing...: each marshal, to log as a no-show.</summary>
    private void ShowMarshalsMissingMenu()
    {
        if (_marshalsRound is not { } round || _marshalsShown.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        // Each marshal's transponder too (12 Nov 2026, setup plan step 8), so
        // the club's marshal rule can find their results.
        var posts = MarshalPostsFor(round);

        foreach (var (name, post) in _marshalsShown)
        {
            var who = MarshalsInPlace.Who(name, post);
            var transponder = posts.FirstOrDefault(p => p.Name == name && p.Post == post).Transponder ?? string.Empty;
            var logged = round.MarshalNoShows?.Contains(who, StringComparer.OrdinalIgnoreCase) == true;

            // Logged already: the same line takes it back off (setup plan
            // step 8) - a no-show logged by mistake costs a driver a result.
            var item = new ToolStripMenuItem(logged ? $"{who} did turn up - take the no-show off" : $"{who} didn't turn up") { Checked = logged };
            item.Click += (_, _) =>
            {
                if (logged)
                {
                    MarshalNoShowTakenBack?.Invoke(round, who);
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshal no-show taken off for {round.RoundLabel}: {who}.");
                    return;
                }

                MarshalNoShow?.Invoke(round, who, transponder);
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshal no-show logged for {round.RoundLabel}: {who}. "
                                      + "Get someone onto the post, then press Marshals in place.");
            };
            menu.Items.Add(item);
        }

        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(_btnMarshalsMissing, new Point(0, _btnMarshalsMissing.Height));
    }

    /// <summary>The Auto start switch menu's "Wait for the marshals before starting".</summary>
    private void ToggleWaitForMarshals()
    {
        _settings.AutoStartWaitForMarshals = !_settings.AutoStartWaitForMarshals;
        _settings.Save();

        if (!_settings.AutoStartWaitForMarshals)
        {
            HideMarshalsAsk();
        }

        StatusMessage?.Invoke(_settings.AutoStartWaitForMarshals
            ? $"{AppTime.LogStampNow()}  [auto] Auto start now waits for Marshals in place before each race with marshals."
            : $"{AppTime.LogStampNow()}  [auto] Auto start no longer waits for the marshals.");
    }
}

/// <summary>The marshals-in-place wording (setup plan D4), pure so it is tested.</summary>
public static class MarshalsInPlace
{
    /// <summary>One marshal as the banner and the no-show log name them: "Hugh Turn (Turn 3)".</summary>
    public static string Who(string name, string post) =>
        string.IsNullOrWhiteSpace(post) ? name : $"{name} ({post})";

    /// <summary>
    /// The banner: "Marshals for Q2 Heat 3: Hugh Turn (Turn 1), Rhoda Rage
    /// (Turn 2). In place?" - or, once the start is waiting, "Q2 Heat 3 is
    /// waiting for its marshals: ...".
    /// </summary>
    public static string Ask(string roundLabel, IReadOnlyList<(string Name, string Post)> marshals, bool waiting)
    {
        var who = string.Join(", ", marshals.Select(m => Who(m.Name, m.Post)));

        return waiting
            ? $"{roundLabel} is waiting for its marshals: {who}."
            : $"Marshals for {roundLabel}: {who}. In place?";
    }

    /// <summary>What the announcer says once, at zero: "Marshals for Q2 Heat 3, to your posts please: Hugh Turn on Turn 1 and Rhoda Rage on Turn 2. We start when you're in place."</summary>
    public static string Call(string roundLabel, IReadOnlyList<(string Name, string Post)> marshals)
    {
        var each = marshals
            .Select(m => string.IsNullOrWhiteSpace(m.Post) ? m.Name : $"{m.Name} on {m.Post}")
            .ToList();

        var list = each.Count switch
        {
            0 => string.Empty,
            1 => each[0],
            _ => $"{string.Join(", ", each.Take(each.Count - 1))} and {each[^1]}",
        };

        return $"Marshals for {roundLabel}, to your posts please: {list}. We start when you're in place.";
    }
}

/// <summary>The deputy's phone (12 Nov 2026, setup plan D8): Start now and Marshals in place from the remote.</summary>
public sealed partial class RaceControlControl
{
    /// <summary>The remote's "autonow": start the next race now. Null when it can't, with the reason in <paramref name="refusal"/>.</summary>
    private string? RemoteAutoNow(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "Auto start isn't counting down to a race right now.";
            return null;
        }

        refusal = null;
        AutoStartNow();
        return "Starting now.";
    }

    /// <summary>The remote's "marshalsin": the next race's marshals are in place.</summary>
    private string? RemoteMarshalsIn(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "No race is waiting for its marshals right now.";
            return null;
        }

        refusal = null;
        ConfirmMarshals();
        return "Marshals in place.";
    }
}
