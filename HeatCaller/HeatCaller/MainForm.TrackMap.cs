namespace HeatCaller;

/// <summary>
/// MainForm's side of the live track map (TrackLine, TrackPositions,
/// TrackMapView): following the race's crossings, working out the line of
/// the layout Race Control is racing on, and handing the map to the Big
/// Screen (through Race Control) and to the Live Results page.
///
/// The layout is Race Control's - the one its Track box is on, which is
/// also where the sector lengths come from (SectorLengthsFeetProvider). Its
/// line is worked out again only when the plan changes.
/// </summary>
public partial class MainForm
{
    /// <summary>The line last worked out, and what it was worked out from - so it is not measured again ten times a second.</summary>
    private (int Shape, TrackLine? Line)? _trackLineMade;

    /// <summary>Follows the race and says when a car seems to have stopped. Called once, from the constructor, after the race and the web server exist.</summary>
    private void WireTrackMap()
    {
        _trackPositions.Attach(_race);

        // Said once a stop, in the console. Not spoken: anything new the
        // announcer says starts switched off until a rehearsal has heard it.
        _trackPositions.CarStopped += (standing, where) =>
            OnClientRawMessage($"{AppTime.LogStampNow()}  [track] {DriverNameOf(standing.TransponderId)} may have stopped {where} - "
                               + "nothing heard at the next loop. It's shown red on the track map.");

        _webServer.TrackMap = TrackMapNow;
    }

    /// <summary>
    /// The line of the layout Race Control is racing on, and every car placed
    /// on it now - or no line when that layout has no plan with a closed
    /// racing line through a finish line. UI thread.
    /// </summary>
    internal (TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars) TrackMapNow()
    {
        var line = CurrentTrackLine();

        if (line is null)
        {
            return (null, Array.Empty<TrackPositions.Car>());
        }

        var racing = _race.State == RaceState.Racing && _race.Flag != FlagState.Red && !_race.IsPracticeMode && !_race.IsDemoMode;

        return (line, _trackPositions.Estimate(_race.GetOrderedStandings(), line, TimingClock.NowMs, racing));
    }

    private TrackLine? CurrentTrackLine()
    {
        var name = _settings.LastRaceControlTrackName;
        var design = string.IsNullOrWhiteSpace(name)
            ? null
            : _trackLayouts.All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))?.Design;

        var shape = ShapeOf(design);

        if (_trackLineMade is { } made && made.Shape == shape)
        {
            return made.Line;
        }

        TrackLine? line;

        try
        {
            line = TrackLine.From(design);
        }
        catch (Exception ex)
        {
            line = null;
            OnClientRawMessage($"{AppTime.LogStampNow()}  [track] Couldn't draw the track map from the plan: {ex.Message}");
        }

        _trackLineMade = (shape, line);
        return line;
    }

    /// <summary>
    /// A number that changes whenever anything the line is made from does -
    /// the racing line's points, whether it closes, the markings and where
    /// they are. Cheap: a plan is a few hundred points at most.
    /// </summary>
    private static int ShapeOf(RcpTrackDesign? design)
    {
        if (design is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(design.RouteClosed);

        foreach (var point in design.Route)
        {
            hash.Add(point?.X);
            hash.Add(point?.Y);
        }

        foreach (var decal in design.Decals)
        {
            hash.Add(decal?.Kind);
            hash.Add(decal?.Col);
            hash.Add(decal?.Row);
        }

        return hash.ToHashCode();
    }
}
