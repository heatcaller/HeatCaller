using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The standby PC's side of the warm standby (11 Nov 2026, race-night build
/// plan, Phase 6.2 - see WarmStandby for the rules): every fifteen seconds,
/// asks the timing PC for a copy of its race data and keeps the newest good
/// one as a zip in WarmStandby.MirrorFolder, ready for Take Over.
///
/// WHY it never writes into the data folder: this PC's HeatCaller is running
/// while it mirrors, with its own stores loaded in memory. Swapping files
/// under it would have those stores save their old copy straight back over
/// the new one, or worse, mix the two. So the copy waits beside the data
/// as a zip, and only Take Over - which restarts the app - puts it in place.
///
/// A copy is checked whole (WarmStandby.Validate) before it replaces the
/// last good one, and written to a temp file and moved, so a dropped
/// connection or a damaged answer never leaves Take Over with nothing - or
/// half a night - to restore. The copy before is kept too.
///
/// Never runs once this PC has taken over (WarmStandby.ReadTakenOver):
/// checked before every ask, not just at start.
/// </summary>
internal sealed class StandbyMirror : IDisposable
{
    /// <summary>What to copy from: the copy's address and the PIN, or null with why not. Read on each ask, so a changed setting takes effect at once.</summary>
    internal sealed record Target(Uri? Address, string Pin, string? Problem);

    private readonly Func<Target> _target;
    private readonly string _mirrorFolder;
    private readonly string _dataRoot;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private int _asking;
    private volatile WarmStandby.MirrorStatus _status = WarmStandby.MirrorStatus.Off;

    /// <summary>Said once something changes worth logging (a copy failing after working, or working again).</summary>
    public event Action<string>? Logged;

    public StandbyMirror(Func<Target> target, string? mirrorFolder = null, string? dataRoot = null, HttpMessageHandler? handler = null)
    {
        _target = target;
        _mirrorFolder = mirrorFolder ?? WarmStandby.MirrorFolder;
        _dataRoot = dataRoot ?? AppPaths.Root;
        _ownsHttp = true;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(30);

        var taken = WarmStandby.ReadTakenOver(_dataRoot);
        var latest = LatestPath;

        // What is already on disk from before a restart, so Take Over is
        // offered straight away rather than after the first ask.
        _status = new WarmStandby.MirrorStatus(
            false,
            File.Exists(latest) ? File.GetLastWriteTimeUtc(latest) : null,
            null,
            0,
            null,
            null,
            taken?.AtUtc);
    }

    public WarmStandby.MirrorStatus Status => _status;

    /// <summary>The newest good copy.</summary>
    public string LatestPath => Path.Combine(_mirrorFolder, WarmStandby.LatestFileName);

    /// <summary>The copy before it.</summary>
    public string PreviousPath => Path.Combine(_mirrorFolder, WarmStandby.PreviousFileName);

    /// <summary>Whether a copy is there to take over from.</summary>
    public bool HasCopy => File.Exists(LatestPath);

    /// <summary>Starts asking every WarmStandby.PollEvery. Does nothing once this PC has taken over.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (WarmStandby.ReadTakenOver(_dataRoot) is { } taken)
            {
                _status = _status with { Enabled = false, TakenOverUtc = taken.AtUtc };
                return;
            }

            _status = _status with { Enabled = true, TakenOverUtc = null };
            _timer ??= new System.Threading.Timer(_ => _ = AskOnceAsync(), null, TimeSpan.FromSeconds(1), WarmStandby.PollEvery);
        }
    }

    /// <summary>Stops asking. The copies already kept stay.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _status = _status with { Enabled = false };
        }
    }

    /// <summary>Marks the status taken over, after Take Over left its marker.</summary>
    public void MarkTakenOver(DateTime atUtc)
    {
        Stop();
        _status = _status with { TakenOverUtc = atUtc };
    }

    /// <summary>
    /// Asks the timing PC once and keeps the copy if it is good. Never
    /// throws; the outcome is in Status. Only one ask at a time - a slow
    /// answer is not stacked on by the next tick. Returns whether a new
    /// copy was kept.
    /// </summary>
    public async Task<bool> AskOnceAsync(CancellationToken cancel = default)
    {
        if (Interlocked.Exchange(ref _asking, 1) == 1)
        {
            return false;
        }

        var hadError = _status.LastError is not null;

        try
        {
            // Checked before every ask: the marker can arrive while this is
            // running (Take Over) and from then on nothing is copied.
            if (WarmStandby.ReadTakenOver(_dataRoot) is { } taken)
            {
                Stop();
                _status = _status with { TakenOverUtc = taken.AtUtc };
                return false;
            }

            var target = _target();
            if (target.Address is null)
            {
                Fail(target.Problem ?? "Type the timing PC's address.");
                return false;
            }

            if (!WarmStandby.IsUsablePin(target.Pin))
            {
                Fail("Type the standby PIN (eight letters or digits or more) - the same one as on the timing PC.");
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, target.Address);
            request.Headers.Add("X-Pin", target.Pin);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Fail((int)response.StatusCode switch
                {
                    404 => "The timing PC isn't sharing a copy. Tick \"Let a standby PC copy this one\" on its Backup & Restore page.",
                    403 => "The timing PC refused the PIN. Check it is the same on both PCs.",
                    429 => "The timing PC has had too many wrong PINs and is refusing for a few minutes.",
                    421 => "The timing PC doesn't answer to that address. Use the address its Local Screens page shows.",
                    _ => $"The timing PC answered {(int)response.StatusCode}. The standby will try again.",
                });
                return false;
            }

            if (response.Content.Headers.ContentLength is > WarmStandby.MaxSnapshotBytes)
            {
                Fail("The copy is far bigger than race data could be. It was not kept.");
                return false;
            }

            Directory.CreateDirectory(_mirrorFolder);
            var partial = Path.Combine(_mirrorFolder, WarmStandby.LatestFileName + ".partial");

            await using (var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;

                while ((read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > WarmStandby.MaxSnapshotBytes)
                    {
                        file.Close();
                        TryDelete(partial);
                        Fail("The copy is far bigger than race data could be. It was not kept.");
                        return false;
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                }
            }

            WarmStandby.Check check;
            using (var written = File.OpenRead(partial))
            {
                check = WarmStandby.Validate(written);
            }

            if (!check.Ok)
            {
                TryDelete(partial);
                Fail(check.Problem ?? "The copy was not usable.");
                return false;
            }

            // Taken over while the copy was coming in: dropped, not kept.
            if (WarmStandby.ReadTakenOver(_dataRoot) is not null)
            {
                TryDelete(partial);
                return false;
            }

            if (File.Exists(LatestPath))
            {
                File.Copy(LatestPath, PreviousPath, overwrite: true);
            }

            File.Move(partial, LatestPath, overwrite: true);

            var takenAt = response.Headers.TryGetValues(WarmStandby.TakenAtHeader, out var stamps)
                          && DateTime.TryParse(stamps.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
                ? stamp.ToUniversalTime()
                : DateTime.UtcNow;

            var primary = response.Headers.TryGetValues(WarmStandby.PrimaryNameHeader, out var names)
                ? names.FirstOrDefault()
                : null;

            // The timing PC's clock may be off from this one's; a copy can't
            // be from the future as far as the age shown here goes.
            if (takenAt > DateTime.UtcNow)
            {
                takenAt = DateTime.UtcNow;
            }

            _status = _status with
            {
                LastCopyUtc = takenAt,
                LastTriedUtc = DateTime.UtcNow,
                Files = check.Files,
                Primary = string.IsNullOrWhiteSpace(primary) ? target.Address.Host : primary,
                LastError = null,
            };

            if (hadError)
            {
                Logged?.Invoke($"{AppTime.LogStampNow()}  [standby] Copying from {_status.Primary} again.");
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Fail(ex is TaskCanceledException
                ? "The timing PC didn't answer in time."
                : "Can't reach the timing PC. Check both are on the same network and its Local Screens server is running.");
            return false;
        }
        catch (Exception ex)
        {
            Fail($"Copying stopped: {ex.Message}");
            return false;
        }
        finally
        {
            Volatile.Write(ref _asking, 0);
        }
    }

    private void Fail(string why)
    {
        var first = _status.LastError is null;
        _status = _status with { LastError = why, LastTriedUtc = DateTime.UtcNow };

        if (first)
        {
            Logged?.Invoke($"{AppTime.LogStampNow()}  [standby] {why}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A leftover .partial is replaced by the next ask.
        }
    }

    public void Dispose()
    {
        Stop();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
