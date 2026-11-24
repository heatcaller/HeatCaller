using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Ready to race and the drivers' meeting (12 Nov 2026, setup plan B3 and
/// D10). Here because MainForm is what can see the decoder, the phone
/// server, the relay, the announcer and the backups at once - the facts
/// behind ReadyChecklist - and it opens the closing checklist's window
/// (CloseNightForm) with Ready to race's lines.
/// </summary>
public partial class MainForm
{
    private CloseNightForm? _readyToRace;

    /// <summary>What Ready to race checks tonight.</summary>
    private ReadyFacts ReadyToRaceFacts()
    {
        var program = _raceProgram;
        var day = program?.TonightsDayRounds() ?? Array.Empty<RoundDefinition>();
        var built = day.Any(r => r.Kind != RoundKind.Practice);
        var raced = day.Any(r => r.IsRecorded && r.Kind != RoundKind.Practice);

        bool? posted = _settings.PostHeatsBeforeFirstRace
            ? HeatPosting.Posted(_raceEvents.Active) || raced
            : null;

        var heard = _race.IsDemoMode
                    // WHY (23 Nov 2026, bug hunt 24): a demo race saved tonight
                    // used to count as the decoder having heard a car.
                    || TonightsSightings.InSessions(RacesThatProveALoopCrossing(_sessionHistory.All), TonightsSightings.NightStartUtc(DateTime.Now)).Count > 0
                    || _transponderWatch.Recent().Count > 0;

        return new ReadyFacts(
            _race.IsDemoMode ? null : LineDecoderConnected(),
            heard,
            _webServer.IsRunning || _settings.WebServerAutoStart ? _webServer.IsRunning : null,
            _relayPublisher.Status is { Enabled: true, Configured: true } status ? status.Online : null,
            _announcer.DescribeVoiceBlocker(),
            built,
            posted,
            program?.NotHeardTonight(day) ?? Array.Empty<string>(),
            program?.PapersNotInOrderTonight(),
            _settings.LastAutoBackupUtc);
    }

    /// <summary>Tonight's Ready to race list, with the club's own lines and tonight's hand ticks.</summary>
    private List<CloseNightChecklist.Item>? ReadyToRaceList(string? dayId)
    {
        if (dayId is null)
        {
            return null;
        }

        return ReadyChecklist.Build(
            ReadyToRaceFacts(),
            DateTime.UtcNow,
            _settings.ReadyOwnItems,
            CloseNightChecklist.TicksFor(_settings.ReadyTicks, dayId));
    }

    /// <summary>Opens Ready to race, or brings it forward when it's open already.</summary>
    private void OpenReadyToRace()
    {
        if (_raceProgram is not { } program || program.ActiveDayId is not { } dayId)
        {
            return;
        }

        if (_readyToRace is { IsDisposed: false } open)
        {
            if (open.DayId == dayId)
            {
                open.Reload();
                open.Activate();
                return;
            }

            open.Close();
        }

        var form = new CloseNightForm(
            () => ReadyToRaceList(dayId),
            (key, done) =>
            {
                _settings.ReadyTicks = CloseNightChecklist.Tick(_settings.ReadyTicks, dayId, key, done);
                _settings.Save();
            },
            kind => ReadyToRaceGo(kind, dayId),
            () => _settings.ReadyOwnItems,
            lines =>
            {
                _settings.ReadyOwnItems = lines;
                _settings.Save();
            },
            ready: true)
        {
            DayId = dayId,
        };

        form.FormClosed += (_, _) => _readyToRace = null;
        _readyToRace = form;
        form.Show(this);
    }

    /// <summary>The button beside a Ready to race line: the screen that sorts it.</summary>
    private async void ReadyToRaceGo(CloseNightKind kind, string dayId)
    {
        switch (kind)
        {
            case CloseNightKind.Decoder:
                GoToPage("Connection");
                break;

            case CloseNightKind.PhoneServer:
                GoToPage("Local Screens");
                break;

            case CloseNightKind.Website:
                GoToPage("Club Website");
                break;

            case CloseNightKind.Voice:
                GoToPage("Sounds");
                break;

            case CloseNightKind.Heats:
                GoToPage("Race Day");
                break;

            case CloseNightKind.Cars:
                GoToPage("Race Control");
                break;

            case CloseNightKind.Papers:
                GoToPage("Race Day");
                _raceProgram?.ShowEntriesTab();
                break;

            case CloseNightKind.Meeting:
                OpenDriversMeeting(dayId);
                break;

            case CloseNightKind.Backup:
                try
                {
                    var folder = BackupService.SafeBackupFolder(_settings.AutoBackupFolder);
                    var secrets = BackupService.CaptureSecrets();
                    var path = await Task.Run(() => BackupService.CreateBackup(folder, automatic: false, secrets));
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Ready to race: backed up to {path}");
                    _settings.ReadyTicks = CloseNightChecklist.Tick(_settings.ReadyTicks, dayId, "backup", true);
                    _settings.Save();
                    _readyToRace?.Reload();
                }
                catch (Exception ex)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Ready to race: backup failed: {ex.Message}");
                    AppDialog.Show(this, "The backup couldn't be taken: " + ex.Message, "Ready to Race", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                break;
        }
    }

    /// <summary>The drivers' meeting window (setup plan D10): the points, editable, to print or have read out.</summary>
    private void OpenDriversMeeting(string? dayId)
    {
        if (_raceProgram?.MeetingFacts() is not { } facts)
        {
            return;
        }

        using var form = new DriversMeetingForm(DriversMeeting.Text(facts));

        form.ReadOut += text =>
        {
            // WHY (15 Nov 2026): Read It Out said nothing back, and a long
            // meeting takes seconds to render, so it was pressed again and
            // the meeting was read twice. Now the window says where it is,
            // and the button stays off until the reading is over.
            if (_announcer.DescribeVoiceBlocker() is { } blocked)
            {
                form.ShowReading($"Not read out - {blocked}.", reading: false);
                return;
            }

            form.Used = true;
            form.ShowReading("Waiting for the announcer...", reading: true);

            void OnUi(Action act)
            {
                if (!form.IsDisposed && form.IsHandleCreated)
                {
                    form.BeginInvoke(() =>
                    {
                        if (!form.IsDisposed)
                        {
                            act();
                        }
                    });
                }
            }

            _announcer.SpeakReminder(
                DriversMeeting.Spoken(text),
                showOnScreens: true,
                onAir: "Drivers' meeting",
                dequeued: () => OnUi(() => form.ShowReading("Reading it out now...", reading: true)),
                finished: () => OnUi(() => form.ShowReading($"Read out at {AppTime.TimeOfDay(DateTime.Now)}.", reading: false)));

            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] The drivers' meeting was read out.");
        };

        form.Print += text =>
        {
            var pages = TextSheetRenderer.RenderPages(text);

            try
            {
                PagePrinting.PrintWithPreview(this, "Drivers' meeting", pages);
            }
            finally
            {
                foreach (var page in pages)
                {
                    page.Dispose();
                }
            }
        };

        form.ShowDialog(this);

        // Read out or printed: the meeting's line ticks itself off.
        if (form.Used && (dayId ?? _raceProgram?.ActiveDayId) is { } id)
        {
            _settings.ReadyTicks = CloseNightChecklist.Tick(_settings.ReadyTicks, id, "meeting", true);
            _settings.Save();
            _readyToRace?.Reload();
        }
    }
}

/// <summary>The drivers' meeting: the points in an editable box, with Print and Read Out (setup plan D10).</summary>
internal sealed class DriversMeetingForm : Form
{
    private readonly TextBox _txt = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };

    /// <summary>Read Out was pressed, with the box's text.</summary>
    public event Action<string>? ReadOut;

    /// <summary>Print was pressed, with the box's text.</summary>
    public event Action<string>? Print;

    /// <summary>Printed or read out at least once.</summary>
    public bool Used { get; set; }

    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = AppTheme.TextMuted, Text = string.Empty };
    private readonly Button _btnRead = new() { Text = "Read It Out", AutoSize = true };

    /// <summary>
    /// Lets Read It Out go again if the announcer never says it's done -
    /// the line can be dropped unheard (Stop, a cleared queue), and a button
    /// left off for good would be worse than a second reading.
    /// </summary>
    private readonly System.Windows.Forms.Timer _giveUp = new() { Interval = 180_000 };

    /// <summary>Where the read-out is, under the box; the button is off while it is going.</summary>
    public void ShowReading(string status, bool reading)
    {
        _lblStatus.Text = status;
        _btnRead.Enabled = !reading;
        _btnRead.Text = !reading && Used ? "Read It Again" : "Read It Out";
        _giveUp.Stop();

        if (reading)
        {
            _giveUp.Start();
        }
    }

    public DriversMeetingForm(string text)
    {
        Text = "Drivers' Meeting";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = "Two minutes before the first race, said the same way every week. Built from tonight's day - change anything "
                   + "before you print it or have the announcer read it out. Changes here are for tonight only.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _txt.Text = text;

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnPrint = new Button { Text = "Print...", AutoSize = true };
        var btnRead = _btnRead;

        btnPrint.Click += (_, _) =>
        {
            Used = true;
            Print?.Invoke(_txt.Text);
        };
        btnRead.Click += (_, _) => ReadOut?.Invoke(_txt.Text);

        _giveUp.Tick += (_, _) =>
        {
            _giveUp.Stop();
            _btnRead.Enabled = true;
            _btnRead.Text = "Read It Again";
            _lblStatus.Text = "The announcer never said it finished - Read It Again if it wasn't heard.";
        };

        // 18 Nov 2026, the dialog version of the Venues look: the points in
        // one card, its hint line under the caption, the box taking the
        // height; where the read-out is under the card; Read It Out, the
        // button used most on the night, in the primary colour. The window
        // is 40px taller than it was so the box keeps its height in the card.
        var card = new Section("Tonight's meeting");
        card.Add(lblHow, gapBelow: UiLayout.RowGap);
        card.AddFill(_txt, () => 260, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_lblStatus, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose, btnRead, btnPrint), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        // WHY (15 Nov 2026): the box is the first thing focused, and a text
        // box focused that way selects everything - one stray key and the
        // meeting was gone. Opens with the caret at the top instead.
        Shown += (_, _) =>
        {
            _txt.SelectionStart = 0;
            _txt.SelectionLength = 0;
        };

        FormClosed += (_, _) => _giveUp.Dispose();

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnRead, AppTheme.ButtonKind.Primary);
        DialogFit.Keep(this, page, width: 640, height: 560);
    }
}
