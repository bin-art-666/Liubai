namespace Liubai;

internal readonly record struct BandColorSample(int Column, uint Color);

// A whole bar needs evidence distributed across the window's top edge. Never
// renormalize a tiny uncovered fragment into a supposedly dominant background.
internal static class ExpandedColorPolicy
{
    internal const int Columns = 16, Rows = 5;
    internal static uint? Representative(IReadOnlyList<BandColorSample> samples)
    {
        if (samples.Count < Columns * Rows * .70 || samples.Select(s => s.Column).Distinct().Count() < 12) return null;
        for (int region = 0; region < 3; region++)
        {
            int start = region * Columns / 3, end = (region + 1) * Columns / 3;
            if (samples.Count(s => s.Column >= start && s.Column < end) < (end - start) * Rows * .5) return null;
        }
        var seeds = samples.GroupBy(s => s.Color & 0xF0F0F0).Select(g => g.First().Color);
        var dominant = seeds.Select(color => samples.Where(s => WindowColorAnalysis.Similar(s.Color, color, 18)).ToArray())
            .MaxBy(group => group.Length)!;
        if (dominant.Length < samples.Count * .65 || dominant.Select(s => s.Column).Distinct().Count() < 11) return null;
        for (int region = 0; region < 3; region++)
        {
            int start = region * Columns / 3, end = (region + 1) * Columns / 3;
            if (!dominant.Any(s => s.Column >= start && s.Column < end)) return null;
        }
        uint r = (uint)dominant.Average(s => (s.Color >> 16) & 255);
        uint g = (uint)dominant.Average(s => (s.Color >> 8) & 255);
        uint b = (uint)dominant.Average(s => s.Color & 255);
        return (r << 16) | (g << 8) | b;
    }
}
