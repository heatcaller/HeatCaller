namespace HeatCaller;

/// <summary>
/// How old a car's newest lap is as the relay's live frame is built - the
/// "lapAge" on each row, in milliseconds.
///
/// WHY (13 Nov 2026, bug hunt 17, #173): the relay notes when each car's
/// newest lap reached it, and a phone reading lap times out drops a lap that
/// is more than a few seconds old by that note. But the frame said nothing
/// of when the lap was DONE, so the relay could only go by when the frame
/// arrived - and a frame that waited on a bad connection made an old lap
/// look new, and the phone said it late. For a driver, a late time said as
/// if fresh is worse than no time. So each row now says how old its lap was
/// when the frame was built, the publisher says how long the frame then
/// waited (RelayPublisher.HeldMs), and the relay takes both off the arrival.
///
/// Measured on the decoder's clock, where the lap itself was timed: what
/// that clock reads now (the last time it was heard from, carried forward
/// on the app's monotonic clock - RaceControlSession.EstimatedDeviceTimeNowMs)
/// less the car's last counted crossing. No wall clock comes into it, here
/// or on the relay, so a laptop whose clock is minutes out says the same.
/// </summary>
internal static class RelayLapAge
{
    /// <summary>The oldest age that is sent as it is. The relay believes no more than this either (LAP_AGE_MAX_MS in relay/src/server.ts).</summary>
    public const long MaxMs = 10 * 60 * 1000;

    /// <summary>
    /// The age to send for this row, or null to send none - the relay then
    /// goes by when the frame arrived, as it always did. None for:
    ///  - a ghost, whose crossings are on its own clock (time since the
    ///    green), not the decoder's;
    ///  - a car with no counted crossing to measure from;
    ///  - a start lap credited at the green to a car past the line: its
    ///    crossing was before the green, but the lap is news as of the green;
    ///  - an age that comes out negative - a decoder reconnected since, or
    ///    laps fed in by something that is not the decoder. Never sent: a
    ///    lap cannot be newer than now.
    /// </summary>
    public static long? Of(RacerStanding car, long deviceNowMs)
    {
        if (Ghosts.IsGhost(car.TransponderId)
            || car.LapTimesMs.Count == 0
            || car.LastLapDeviceTimeMs <= 0
            || (car.StartLapCredited && car.LapTimesMs.Count == 1))
        {
            return null;
        }

        var age = deviceNowMs - car.LastLapDeviceTimeMs;

        return age < 0 ? null : Math.Min(age, MaxMs);
    }

    /// <summary>
    /// The relay's live frame (WebServerService.BuildStateJson, forRelay)
    /// with "lapAge" put on each row that has one to give.
    ///
    /// A row is found by its "tag" (WebServerService.RelayRowTag), which is
    /// on every real car's row and never on a ghost's. The race is read a
    /// moment after the frame was built, so the age goes on only while the
    /// row's "lap" is still the car's count of crossings: a lap that landed
    /// in between is not in this frame, and its age is not this frame's.
    ///
    /// Anything unexpected and the frame is handed back as it came - a frame
    /// with no age is the frame of every version before this one, and the
    /// live board must never be lost for the sake of it.
    /// </summary>
    public static string Stamp(string stateJson, IReadOnlyList<RacerStanding> ordered, long deviceNowMs, Func<string, string?> tagOf)
    {
        try
        {
            Dictionary<string, (int Lap, long Age)>? ages = null;

            foreach (var car in ordered)
            {
                if (Of(car, deviceNowMs) is { } age && tagOf(car.TransponderId) is { } tag)
                {
                    (ages ??= new Dictionary<string, (int Lap, long Age)>(StringComparer.Ordinal))[tag] = (car.LapTimesMs.Count, age);
                }
            }

            if (ages is null
                || System.Text.Json.Nodes.JsonNode.Parse(stateJson) is not System.Text.Json.Nodes.JsonObject document
                || document["standings"] is not System.Text.Json.Nodes.JsonArray rows)
            {
                return stateJson;
            }

            var any = false;

            foreach (var item in rows)
            {
                if (item is System.Text.Json.Nodes.JsonObject row
                    && row["tag"] is System.Text.Json.Nodes.JsonValue tagValue && tagValue.TryGetValue<string>(out var tag)
                    && ages.TryGetValue(tag, out var known)
                    && row["lap"] is System.Text.Json.Nodes.JsonValue lapValue && lapValue.TryGetValue<int>(out var lap)
                    && lap == known.Lap)
                {
                    row["lapAge"] = known.Age;
                    any = true;
                }
            }

            return any ? document.ToJsonString() : stateJson;
        }
        catch
        {
            return stateJson;
        }
    }
}
