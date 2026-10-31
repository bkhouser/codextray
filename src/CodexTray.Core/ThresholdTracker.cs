namespace CodexTray.Core;

public sealed class ThresholdTracker
{
    private readonly Dictionary<string, int> _bands;

    public ThresholdTracker(IDictionary<string, int>? initialBands = null)
    {
        _bands = initialBands is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(initialBands, StringComparer.OrdinalIgnoreCase);
    }

    public int? Observe(string bucketId, decimal usedPercent)
    {
        var newBand = usedPercent >= 90m ? 90 : usedPercent >= 70m ? 70 : 0;
        if (!_bands.TryGetValue(bucketId, out var previousBand))
        {
            if (_bands.Count >= 64) _bands.Remove(_bands.Keys.First());
            _bands[bucketId] = newBand;
            return null;
        }

        _bands[bucketId] = newBand;
        return newBand > previousBand ? newBand : null;
    }

    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_bands, StringComparer.OrdinalIgnoreCase);
}
