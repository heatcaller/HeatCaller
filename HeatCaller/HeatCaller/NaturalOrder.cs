namespace HeatCaller;

/// <summary>
/// Orders names the way people number things: "Pack 2" before "Pack 10",
/// where an ordinary comparison - character by character - puts "Pack 10",
/// "Pack 11" and the rest between "Pack 1" and "Pack 2" (Which pack?,
/// layout pictures, 30 Sep).
///
/// A run of digits is compared as the number it is; the words between are
/// compared as the person's own settings compare them, ignoring case. Two
/// names that come out the same that way - "Pack 01" and "Pack 1", or two
/// spelled alike in different cases - are put in one fixed order rather
/// than left as they happened to be.
/// </summary>
internal sealed class NaturalOrder : IComparer<string?>
{
    public static readonly NaturalOrder Instance = new();

    private NaturalOrder()
    {
    }

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;

        while (i < a.Length && j < b.Length)
        {
            var aDigits = IsDigit(a[i]);
            var bDigits = IsDigit(b[j]);

            if (aDigits && bDigits)
            {
                var aStart = i;
                var bStart = j;

                while (i < a.Length && IsDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && IsDigit(b[j]))
                {
                    j++;
                }

                // As numbers: without their leading noughts, the longer is
                // the larger, and the same length goes digit by digit. No
                // run is too long for this, as it would be to parse.
                var aNumber = a.AsSpan(aStart, i - aStart).TrimStart('0');
                var bNumber = b.AsSpan(bStart, j - bStart).TrimStart('0');

                if (aNumber.Length != bNumber.Length)
                {
                    return aNumber.Length.CompareTo(bNumber.Length);
                }

                var digits = aNumber.SequenceCompareTo(bNumber);

                if (digits != 0)
                {
                    return Math.Sign(digits);
                }

                continue;
            }

            if (aDigits != bDigits)
            {
                // A number before words, as an ordinary comparison has it.
                return aDigits ? -1 : 1;
            }

            var aWordsStart = i;
            var bWordsStart = j;

            while (i < a.Length && !IsDigit(a[i]))
            {
                i++;
            }

            while (j < b.Length && !IsDigit(b[j]))
            {
                j++;
            }

            var words = string.Compare(
                a.Substring(aWordsStart, i - aWordsStart),
                b.Substring(bWordsStart, j - bWordsStart),
                StringComparison.CurrentCultureIgnoreCase);

            if (words != 0)
            {
                return Math.Sign(words);
            }
        }

        // The one that ran out first is the shorter: first.
        var rest = (a.Length - i).CompareTo(b.Length - j);

        return rest != 0 ? rest : Math.Sign(string.CompareOrdinal(a, b));
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
