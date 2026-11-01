namespace SnagitBrowser.Core;

/// <summary>Eine Galerie-Zeile: Elemente [Start, Start+Count) mit gemeinsamer Höhe und je eigener Breite.</summary>
public sealed record LayoutRow(int Start, int Count, double Height, double[] Widths);

/// <summary>
/// Zeilen-Layout wie in Foto-Galerien: alle Bilder einer Zeile gleich hoch, Breite nach
/// Seitenverhältnis, jede Zeile (außer der letzten) füllt genau die verfügbare Breite.
/// </summary>
public static class JustifiedLayout
{
    public const double DefaultAspect = 1.6;
    public const double MinAspect = 0.25;
    public const double MaxAspect = 8;

    public static double Aspect(int width, int height) =>
        width > 0 && height > 0 ? Math.Clamp((double)width / height, MinAspect, MaxAspect) : DefaultAspect;

    public static List<LayoutRow> Build(IReadOnlyList<double> aspects, double availableWidth, double targetHeight, double gap)
    {
        var rows = new List<LayoutRow>();
        if (aspects.Count == 0 || availableWidth <= 1 || targetHeight <= 1) return rows;

        int i = 0, n = aspects.Count;
        while (i < n)
        {
            int start = i;
            double sum = 0; // Summe der Seitenverhältnisse in der Zeile
            double prevSum = 0;
            // So lange Bilder hinzufügen, bis die Zeile bei Zielhöhe mindestens voll ist
            while (i < n)
            {
                prevSum = sum;
                sum += Safe(aspects[i]);
                i++;
                if (sum * targetHeight + gap * (i - start - 1) >= availableWidth) break;
            }

            int count = i - start;
            bool full = sum * targetHeight + gap * (count - 1) >= availableWidth;

            if (!full)
            {
                // Letzte Zeile: nicht strecken
                rows.Add(MakeRow(aspects, start, count, targetHeight, null));
                break;
            }

            // Prüfen, ob die Zeile ohne das letzte Bild näher an der Zielhöhe läge
            if (count > 1)
            {
                double hWith = (availableWidth - gap * (count - 1)) / sum;
                double hWithout = (availableWidth - gap * (count - 2)) / prevSum;
                if (Math.Abs(Math.Log(hWithout / targetHeight)) < Math.Abs(Math.Log(hWith / targetHeight)))
                {
                    count--; i--; sum = prevSum;
                }
            }

            double height = (availableWidth - gap * (count - 1)) / sum;
            rows.Add(MakeRow(aspects, start, count, height, availableWidth - gap * (count - 1)));
        }
        return rows;
    }

    private static double Safe(double a) =>
        double.IsFinite(a) && a > 0 ? Math.Clamp(a, MinAspect, MaxAspect) : DefaultAspect;

    private static LayoutRow MakeRow(IReadOnlyList<double> aspects, int start, int count, double height, double? exactWidth)
    {
        var widths = new double[count];
        double used = 0;
        for (int k = 0; k < count; k++)
        {
            widths[k] = Math.Round(Safe(aspects[start + k]) * height);
            used += widths[k];
        }
        // Rundungsrest dem letzten Bild geben, damit die Zeile exakt bündig ist
        if (exactWidth is { } w) widths[^1] += Math.Round(w) - used;
        return new LayoutRow(start, count, Math.Round(height), widths);
    }
}
