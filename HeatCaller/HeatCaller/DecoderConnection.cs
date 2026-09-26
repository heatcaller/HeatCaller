namespace HeatCaller;

/// <summary>
/// The decoder choices, and the numbers they are saved as.
///
/// The chosen decoder is persisted as a NUMBER - Venue.ConnectionType in
/// venues.json and AppSettings.LastConnectionType in settings.json - so
/// those numbers are a storage format, not a presentation detail. They
/// never change and are never reused: a venue saved as I-Lap (2) must come
/// back as I-Lap, whatever the picker happens to list.
///
/// The picker's POSITION is a different thing, and since 0.50.0 it no
/// longer lines up with the number. Next Level Timing (the Neon Timing
/// API, saved as 0) was retired, so position 0 is now EasyLap (1) and
/// position 1 is I-Lap (2). Never write SelectedIndex to settings or
/// compare it with a number below: go through <see cref="ValueAt"/> and
/// <see cref="IndexOf"/>, which are the only two places that know how the
/// two relate. Reading a raw index is exactly the bug the old comment here
/// warned about - "no laps come in" rather than anything that looks like
/// an error.
///
/// Every picker fills from <see cref="Items"/> so none can drift from the
/// others. They had: the Connection page offered three and the Venues page
/// two, while both read and wrote the same integer, and a venue set up for
/// I-Lap came back as EasyLap the next time anybody opened it.
///
/// Adding a decoder: give it the next unused number (3), add it to
/// <see cref="Offered"/>, and handle it wherever EasyLap and I-Lap are
/// handled.
/// </summary>
internal static class DecoderConnection
{
    /// <summary>
    /// Next Level Timing, over its Neon Timing API. Retired in 0.50.0 and no
    /// longer offered. Kept only so a 0 read back from an older settings or
    /// venue file is recognised - it is treated as <see cref="EasyLap"/>.
    /// </summary>
    internal const int RetiredNeonApi = 0;

    internal const int EasyLap = 1;
    internal const int ILap = 2;

    /// <summary>What the pickers list, in order, and the saved number each one stands for.</summary>
    private static readonly (int Value, string Label)[] Offered =
    {
        (EasyLap, "Direct EasyLap Decoder (USB)"),
        (ILap, "I-Lap Decoder (Serial)"),
    };

    /// <summary>The picker entries, in picker order.</summary>
    internal static object[] Items() => Offered.Select(o => (object)o.Label).ToArray();

    /// <summary>
    /// The saved number a picker position stands for. A position that lists
    /// nothing (-1, before anything is selected) reads as EasyLap, the first
    /// entry, rather than as a number no decoder answers to.
    /// </summary>
    internal static int ValueAt(int index) =>
        index >= 0 && index < Offered.Length ? Offered[index].Value : Offered[0].Value;

    /// <summary>
    /// The picker position for a saved number. Anything no longer offered -
    /// the retired Neon API above all, and a number from a newer version
    /// this one does not know - lands on the first entry, EasyLap.
    /// </summary>
    internal static int IndexOf(int value)
    {
        for (var i = 0; i < Offered.Length; i++)
        {
            if (Offered[i].Value == value)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>True when a saved number is one the pickers still offer.</summary>
    internal static bool IsOffered(int value) => Offered.Any(o => o.Value == value);
}
