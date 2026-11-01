using SnagitBrowser.Core;

namespace SnagitBrowser.Tests;

public class JustifiedLayoutTests
{
    private const double Gap = 6;

    [Fact]
    public void FullRowsFillWidthExactly()
    {
        var rnd = new Random(42);
        var aspects = Enumerable.Range(0, 500).Select(_ => 0.5 + rnd.NextDouble() * 2.5).ToList();
        var rows = JustifiedLayout.Build(aspects, 1500, 300, Gap);

        Assert.Equal(aspects.Count, rows.Sum(r => r.Count));
        foreach (var r in rows.Take(rows.Count - 1))
        {
            double width = r.Widths.Sum() + Gap * (r.Count - 1);
            Assert.InRange(width, 1499, 1501);
            Assert.InRange(r.Height, 150, 600); // nicht extrem verzerrt
        }
    }

    [Fact]
    public void LastRowIsNotStretched()
    {
        var rows = JustifiedLayout.Build([1.5, 1.5, 1.5, 1.5, 1.5], 2000, 300, Gap);
        var last = rows[^1];
        Assert.Equal(300, last.Height);
        Assert.True(last.Widths.Sum() < 2000);
    }

    [Fact]
    public void AspectRatioIsPreserved()
    {
        var rows = JustifiedLayout.Build([2.0, 1.0, 0.5, 1.6, 1.33, 3.0], 1200, 250, Gap);
        int idx = 0;
        double[] aspects = [2.0, 1.0, 0.5, 1.6, 1.33, 3.0];
        foreach (var r in rows)
            for (int k = 0; k < r.Count; k++, idx++)
                if (k < r.Count - 1) // letztes Element trägt den Rundungsrest
                    Assert.InRange(r.Widths[k] / r.Height, aspects[idx] * 0.97, aspects[idx] * 1.03);
    }

    [Fact]
    public void PanoramaGetsOwnRow()
    {
        var rows = JustifiedLayout.Build([8.0, 1.0], 1000, 400, Gap);
        Assert.Equal(1, rows[0].Count);
        Assert.Equal(1000, rows[0].Widths[0]);
        Assert.True(rows[0].Height < 400);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidWidthGivesNoRows(double width) =>
        Assert.Empty(JustifiedLayout.Build([1.0, 2.0], width, 300, Gap));

    [Fact]
    public void InvalidAspectsDoNotCrash()
    {
        var rows = JustifiedLayout.Build([0, double.NaN, -1, double.PositiveInfinity, 1.5], 800, 200, Gap);
        Assert.Equal(5, rows.Sum(r => r.Count));
        Assert.All(rows.SelectMany(r => r.Widths), w => Assert.True(w > 0));
        Assert.Equal(JustifiedLayout.DefaultAspect, JustifiedLayout.Aspect(0, 100));
    }
}
