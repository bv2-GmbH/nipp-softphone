using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Phone;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Merkt sich, was eine Quelle zu einer Nummer gesagt hat (§21.4).
///
/// <b>Nur im Arbeitsspeicher, und nur kurz.</b> Hier liegen personenbezogene
/// Daten aus fremden Systemen — Namen, Firmen, Umsätze —, und sie gehören
/// nicht auf die Platte und nicht länger als nötig in den Speicher. Fünf
/// Minuten decken den Fall ab, der zählt: derselbe Anrufer ruft gleich noch
/// einmal an, weil das Gespräch abgebrochen ist.
///
/// <b>Der Zwischenspeicher wird geleert, wenn sich die Konfiguration
/// ändert.</b> Wer ein Mapping korrigiert, will das Ergebnis sehen und nicht
/// die Antwort von vorhin.
/// </summary>
internal sealed class CallerContextCache(TimeProvider time)
{
    private readonly record struct Key(string SourceId, string Digits);

    private readonly record struct Entry(ContextFragment Fragment, DateTimeOffset ExpiresAt);

    private readonly Dictionary<Key, Entry> _entries = [];

    /// <summary>
    /// Der Eintrag zu dieser Nummer, wenn er noch gilt.
    /// </summary>
    public ContextFragment? TryGet(string sourceId, PhoneNumberKey number)
    {
        if (number.Digits.Length == 0)
        {
            return null;
        }

        var key = new Key(sourceId, number.Digits);

        if (!_entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= time.GetUtcNow())
        {
            _entries.Remove(key);
            return null;
        }

        return entry.Fragment;
    }

    public void Set(
        string sourceId,
        PhoneNumberKey number,
        ContextFragment fragment,
        CallerLookupSettings settings)
    {
        if (number.Digits.Length == 0 || settings.CacheSeconds <= 0)
        {
            return;
        }

        Prune(settings);

        _entries[new Key(sourceId, number.Digits)] = new Entry(
            fragment,
            time.GetUtcNow().AddSeconds(settings.CacheSeconds));
    }

    /// <summary>Leert alles — bei einer Änderung der Konfiguration.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Hält die Grösse in Grenzen.
    ///
    /// <b>Abgelaufene zuerst</b>, und erst wenn das nicht reicht, die
    /// ältesten. Ein Zwischenspeicher, der unbegrenzt wächst, ist bei
    /// personenbezogenen Daten kein Leistungsproblem, sondern ein
    /// Datenschutzproblem.
    /// </summary>
    private void Prune(CallerLookupSettings settings)
    {
        var limit = Math.Max(1, settings.CacheMaxEntries);

        if (_entries.Count < limit)
        {
            return;
        }

        var now = time.GetUtcNow();

        foreach (var expired in _entries.Where(e => e.Value.ExpiresAt <= now).Select(static e => e.Key).ToList())
        {
            _entries.Remove(expired);
        }

        while (_entries.Count >= limit)
        {
            var oldest = _entries.MinBy(static e => e.Value.ExpiresAt).Key;

            if (!_entries.Remove(oldest))
            {
                break;
            }
        }
    }
}
