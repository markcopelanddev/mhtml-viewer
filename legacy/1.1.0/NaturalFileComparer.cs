using System.Globalization;

namespace MhtmlViewer;

internal sealed class NaturalFileComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return CompareNatural(Path.GetFileName(x), Path.GetFileName(y));
    }

    private static int CompareNatural(string x, string y)
    {
        var xIndex = 0;
        var yIndex = 0;

        while (xIndex < x.Length && yIndex < y.Length)
        {
            if (char.IsDigit(x[xIndex]) && char.IsDigit(y[yIndex]))
            {
                var numberCompare = CompareNumberRun(x, ref xIndex, y, ref yIndex);
                if (numberCompare != 0)
                {
                    return numberCompare;
                }

                continue;
            }

            var charCompare = char.ToUpperInvariant(x[xIndex]).CompareTo(char.ToUpperInvariant(y[yIndex]));
            if (charCompare != 0)
            {
                return charCompare;
            }

            xIndex += 1;
            yIndex += 1;
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int CompareNumberRun(string x, ref int xIndex, string y, ref int yIndex)
    {
        var xStart = xIndex;
        var yStart = yIndex;

        while (xIndex < x.Length && char.IsDigit(x[xIndex]))
        {
            xIndex += 1;
        }

        while (yIndex < y.Length && char.IsDigit(y[yIndex]))
        {
            yIndex += 1;
        }

        var xDigits = x[xStart..xIndex].TrimStart('0');
        var yDigits = y[yStart..yIndex].TrimStart('0');

        if (xDigits.Length == 0)
        {
            xDigits = "0";
        }

        if (yDigits.Length == 0)
        {
            yDigits = "0";
        }

        var lengthCompare = xDigits.Length.CompareTo(yDigits.Length);
        if (lengthCompare != 0)
        {
            return lengthCompare;
        }

        var valueCompare = string.Compare(xDigits, yDigits, StringComparison.Ordinal);
        if (valueCompare != 0)
        {
            return valueCompare;
        }

        return string.Compare(x[xStart..xIndex], y[yStart..yIndex], CultureInfo.InvariantCulture, CompareOptions.Ordinal);
    }
}
