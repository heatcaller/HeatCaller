namespace HeatCaller;

/// <summary>
/// Whether a layout fits the room it is meant to go in, and by how much.
///
/// The question every plan eventually runs into, and the one the editor
/// could not answer: the grid is unbounded on purpose, so a circuit can be
/// drawn forty feet across without a word of complaint and only turn out
/// to be unbuildable when somebody carries the tiles into the hall.
///
/// Kept apart from the drawing so it can be checked by arithmetic rather
/// than by eye - the numbers below are the whole feature, and a plan that
/// says "fits" when it does not is worse than one that says nothing.
/// </summary>
public static class RoomFit
{
    /// <summary>
    /// How much clearance is wanted all round before a fit is called
    /// comfortable, in millimetres.
    ///
    /// A track wedged wall to wall is not raceable: a marshal has to stand
    /// somewhere, a car that leaves the circuit has to be reachable, and
    /// somebody has to get round the outside to the rostrum. Half a metre
    /// is the width of a person sideways.
    /// </summary>
    public const double ComfortableMarginMm = 500;

    public enum Verdict
    {
        /// <summary>No room has been given.</summary>
        Unknown,

        /// <summary>It fits with room to walk round it.</summary>
        Comfortable,

        /// <summary>It fits, but only just - see ComfortableMarginMm.</summary>
        Tight,

        /// <summary>It fits only if the layout is turned a quarter turn.</summary>
        TurnedOnly,

        /// <summary>It does not fit either way round.</summary>
        TooBig,
    }

    public sealed record Result(Verdict Verdict, string Says, double SpareWidthMm, double SpareDepthMm)
    {
        public bool Fits => Verdict is Verdict.Comfortable or Verdict.Tight or Verdict.TurnedOnly;
    }

    /// <summary>
    /// Checks a layout's footprint against a room. All in millimetres;
    /// which way round the room's two numbers are given does not matter,
    /// because a room can be walked into from either end.
    /// </summary>
    public static Result Check(double layoutWidthMm, double layoutDepthMm, double roomWidthMm, double roomDepthMm)
    {
        if (roomWidthMm <= 0 || roomDepthMm <= 0)
        {
            return new Result(Verdict.Unknown, string.Empty, 0, 0);
        }

        if (layoutWidthMm <= 0 || layoutDepthMm <= 0)
        {
            return new Result(Verdict.Unknown, "Nothing laid out yet.", 0, 0);
        }

        var spareW = roomWidthMm - layoutWidthMm;
        var spareD = roomDepthMm - layoutDepthMm;

        // The same layout turned a quarter turn, which is a real answer:
        // plenty of halls are long and narrow and the circuit simply wants
        // to be the other way round.
        var turnedW = roomWidthMm - layoutDepthMm;
        var turnedD = roomDepthMm - layoutWidthMm;

        var straightOn = spareW >= 0 && spareD >= 0;
        var turned = turnedW >= 0 && turnedD >= 0;

        if (!straightOn && !turned)
        {
            // Reported against the better of the two orientations, so the
            // number is how much too big it actually is rather than how
            // much too big it is the wrong way round.
            var over = Math.Min(Short(spareW, spareD), Short(turnedW, turnedD));

            return new Result(
                Verdict.TooBig,
                $"Too big for the room by {Feet(over)} - it won't fit either way round.",
                spareW,
                spareD);
        }

        if (!straightOn)
        {
            return new Result(
                Verdict.TurnedOnly,
                $"Fits only if you turn the layout a quarter turn - {Feet(turnedW)} and {Feet(turnedD)} to spare that way.",
                turnedW,
                turnedD);
        }

        var tight = spareW < ComfortableMarginMm || spareD < ComfortableMarginMm;

        return new Result(
            tight ? Verdict.Tight : Verdict.Comfortable,
            tight
                ? $"Fits, but only just - {Feet(spareW)} across and {Feet(spareD)} front to back. "
                    + "Leave room to stand and to pick a car up."
                : $"Fits with {Feet(spareW)} to spare across and {Feet(spareD)} front to back.",
            spareW,
            spareD);
    }

    /// <summary>How far short the tighter of the two directions is, as a positive number.</summary>
    private static double Short(double spareWidth, double spareDepth) =>
        Math.Max(0, -Math.Min(spareWidth, spareDepth));

    /// <summary>
    /// A length in millimetres as feet and inches - how a club measures a
    /// room, whatever the tiles are sold in.
    /// </summary>
    public static string Feet(double mm)
    {
        var inches = Math.Abs(mm) / 25.4;
        var feet = (int)(inches / 12);
        var rest = (int)Math.Round(inches - feet * 12);

        // 11.6 inches rounding to 12 is a foot, not "3ft 12in".
        if (rest == 12)
        {
            feet++;
            rest = 0;
        }

        if (feet == 0)
        {
            return $"{rest}in";
        }

        return rest == 0 ? $"{feet}ft" : $"{feet}ft {rest}in";
    }

    /// <summary>Feet and inches to millimetres, for the boxes the room is typed into.</summary>
    public static double FromFeet(double feet, double inches) => (feet * 12 + inches) * 25.4;
}
