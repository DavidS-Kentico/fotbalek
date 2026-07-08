namespace Fotbalek.Web.Game.Core;

/// <summary>Immutable summary of one measurement window: count + distribution (§12). Percentiles are
/// computed at the edge because Azure Monitor flattens OpenTelemetry histograms to avg/min/max/sum/count
/// only (no percentiles yet), and p95/max is exactly what matters for latency.</summary>
public readonly record struct SampleStats(int Count, double Min, double Mean, double P50, double P95, double Max);

/// <summary>Collects double samples over a window and summarizes them (§12). Not thread-safe — each
/// instance is owned by a single loop (the server tick loop). Sorts in place on <see cref="Summarize"/>,
/// so callers <see cref="Clear"/> right after.</summary>
internal sealed class SampleWindow(int capacity)
{
    private readonly List<double> _samples = new(capacity);

    public int Count => _samples.Count;

    public void Add(double value) => _samples.Add(value);

    public void Clear() => _samples.Clear();

    public SampleStats Summarize()
    {
        var n = _samples.Count;
        if (n == 0)
            return default;
        _samples.Sort();
        var sum = 0.0;
        foreach (var v in _samples)
            sum += v;
        return new SampleStats(n, _samples[0], sum / n, Percentile(0.50), Percentile(0.95), _samples[n - 1]);
    }

    /// <summary>Nearest-rank percentile over the already-sorted backing list.</summary>
    private double Percentile(double p)
    {
        var n = _samples.Count;
        var index = (int)Math.Ceiling(p * n) - 1;
        return _samples[Math.Clamp(index, 0, n - 1)];
    }
}
