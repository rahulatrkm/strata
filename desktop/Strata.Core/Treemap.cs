namespace Strata.Core;

public readonly record struct Tile<T>(T Item, double X, double Y, double W, double H);

/// <summary>Squarified treemap (Bruls, Huizing &amp; van Wijk).</summary>
public static class Treemap
{
    public static List<Tile<T>> Squarify<T>(IReadOnlyList<T> items, Func<T, long> sizeOf,
        double x, double y, double width, double height)
    {
        var tiles = new List<Tile<T>>();
        if (width <= 0 || height <= 0) return tiles;

        var nodes = items.Where(i => sizeOf(i) > 0).OrderByDescending(sizeOf).ToList();
        if (nodes.Count == 0) return tiles;

        double total = nodes.Sum(n => (double)sizeOf(n));
        if (total <= 0) return tiles;
        double scale = width * height / total;

        double rx = x, ry = y, rw = width, rh = height;
        int i = 0;
        while (i < nodes.Count && rw > 0 && rh > 0)
        {
            bool vertical = rw >= rh;
            double side = vertical ? rh : rw;

            var row = new List<T> { nodes[i] };
            double sum = sizeOf(nodes[i]);
            double best = WorstRatio(row, sizeOf, sum, side, scale);
            int j = i + 1;
            while (j < nodes.Count)
            {
                double nextSum = sum + sizeOf(nodes[j]);
                row.Add(nodes[j]);
                double candidate = WorstRatio(row, sizeOf, nextSum, side, scale);
                if (candidate > best) { row.RemoveAt(row.Count - 1); break; }
                sum = nextSum;
                best = candidate;
                j++;
            }

            double thickness = Math.Min(sum * scale / side, vertical ? rw : rh);
            double offset = 0;
            foreach (var n in row)
            {
                double length = thickness > 0 ? sizeOf(n) * scale / thickness : 0;
                tiles.Add(vertical
                    ? new Tile<T>(n, rx, ry + offset, thickness, length)
                    : new Tile<T>(n, rx + offset, ry, length, thickness));
                offset += length;
            }

            if (vertical) { rx += thickness; rw -= thickness; }
            else { ry += thickness; rh -= thickness; }
            i = j;
        }
        return tiles;
    }

    private static double WorstRatio<T>(List<T> row, Func<T, long> sizeOf, double sum, double side, double scale)
    {
        if (row.Count == 0 || side <= 0 || sum <= 0) return double.PositiveInfinity;
        double thickness = sum * scale / side;
        if (thickness <= 0) return double.PositiveInfinity;

        double worst = 0;
        foreach (var n in row)
        {
            double length = sizeOf(n) * scale / thickness;
            if (length <= 0) return double.PositiveInfinity;
            worst = Math.Max(worst, Math.Max(thickness / length, length / thickness));
        }
        return worst;
    }
}
