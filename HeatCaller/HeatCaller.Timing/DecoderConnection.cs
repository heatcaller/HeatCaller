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
/// Adding a decoder: give it the next unused number (8) and add it to
/// <see cref="Offered"/>. One that talks over a serial port, the network or
/// ZeroMQ then needs only a protocol in DecoderProtocols: StreamDecoderClient
/// runs every decoder from 3 up. Anything else is handled wherever EasyLap
/// and I-Lap are.
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

    /// <summary>
    /// MyLaps / AMB decoders speaking P3 (AMBrc, RC4, TranX3, ChipX), over
    /// the network or a serial port. Added on 1 Nov 2026 with the four below,
    /// all written from published formats and none yet run on a real decoder
    /// - all five are <see cref="Beta"/>.
    /// </summary>
    internal const int MyLapsP3 = 3;

    /// <summary>The ZRound open protocol: RiCino, and anything else ZRound lists as using it.</summary>
    internal const int ZRound = 4;

    /// <summary>Howard Cano's DIY decoder, and RCHourglass in its Cano or RCHourglass mode.</summary>
    internal const int Cano = 5;

    /// <summary>OpenStint, the open-source decoder for RC3 and OpenStint transponders, over ZeroMQ.</summary>
    internal const int OpenStint = 6;

    /// <summary>
    /// An EasyLap or Robitronic decoder on a serial port rather than USB. The
    /// USB box carries a 38400 8N1 UART stream inside HID (see EasyLapFramer),
    /// so the serial models very likely send the same bytes. No owner has
    /// confirmed it yet.
    /// </summary>
    internal const int EasyLapSerial = 7;

    /// <summary>What the pickers list, in order, and the saved number each one stands for.</summary>
    private static readonly (int Value, string Label)[] Offered =
    {
        (EasyLap, "Direct EasyLap Decoder (USB)"),
        (ILap, "I-Lap Decoder (Serial)"),
        (MyLapsP3, "MyLaps / AMB P3 (Beta)"),
        (ZRound, "ZRound open protocol / RiCino (Beta)"),
        (Cano, "Cano / RCHourglass (Beta)"),
        (OpenStint, "OpenStint (Beta)"),
        (EasyLapSerial, "EasyLap / Robitronic Serial (Beta)"),
    };

    /// <summary>
    /// Decoders written from published formats and never yet run against a
    /// real one at a track. Labelled "(Beta)" in every picker, and
    /// the console says so each time one connects. Take a decoder out of
    /// here - and "(Beta)" off its label - only once it has timed a
    /// real session on the real hardware and its raw capture has been read.
    /// </summary>
    private static readonly HashSet<int> Beta = new() { MyLapsP3, ZRound, Cano, OpenStint, EasyLapSerial };

    /// <summary>True for a decoder not yet proven on real hardware - see <see cref="Beta"/>.</summary>
    internal static bool IsBeta(int value) => Beta.Contains(value);

    /// <summary>
    /// True for the decoders StreamDecoderClient runs (3 and up) rather than
    /// a client of their own. They can be the line, but not a split loop.
    /// </summary>
    internal static bool IsStream(int value) => value is MyLapsP3 or ZRound or Cano or OpenStint or EasyLapSerial;

    /// <summary>A short name for the console and the support bundle.</summary>
    internal static string ShortName(int value) => value switch
    {
        EasyLap => "EasyLap",
        ILap => "I-Lap",
        MyLapsP3 => "MyLaps P3",
        ZRound => "ZRound",
        Cano => "Cano",
        OpenStint => "OpenStint",
        EasyLapSerial => "EasyLap serial",
        _ => "(" + value + ")",
    };

    /// <summary>
    /// The picker entries, in picker order.
    ///
    /// Strings, not object[]. A ComboBox takes a string[] just as happily -
    /// array covariance - so nothing at the pickers changes, and the decoder
    /// list stops being shaped like a control's item collection. The timing
    /// code is being moved out into a layer of its own, and anything in it
    /// that is secretly a UI type has to come back out before it can go.
    /// </summary>
    internal static string[] Items() => Offered.Select(o => o.Label).ToArray();

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
