using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.History;

/// <summary>
/// §13 nennt die Verlaufs-Aufbewahrung als verbindlich zu testen.
///
/// Jeder Test bekommt eine eigene Datenbankdatei — so laufen sie parallel und
/// beeinflussen sich nicht.
/// </summary>
public sealed class CallHistoryStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"nipp-history-test-{Guid.NewGuid():N}.db");

    private readonly CallHistoryStore _store;

    public CallHistoryStoreTests()
    {
        _store = new CallHistoryStore(NullLogger<CallHistoryStore>.Instance, _databasePath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static CallHistoryEntry Entry(
        string number = "+41445128430",
        CallDirection direction = CallDirection.Outgoing,
        CallOutcome outcome = CallOutcome.Answered,
        DateTimeOffset? startedAt = null,
        TimeSpan? duration = null,
        string? recordingPath = null,
        string? displayName = null) => new(
            Id: 0,
            Number: number,
            DisplayName: displayName,
            Direction: direction,
            Outcome: outcome,
            StartedAt: startedAt ?? DateTimeOffset.UtcNow,
            Duration: duration ?? TimeSpan.FromSeconds(42),
            Codec: "PCMU",
            AccountIdentity: "sip:151@pbx.example.ch",
            RecordingPath: recordingPath);

    [Fact]
    public void Ein_Eintrag_kommt_vollstaendig_zurueck()
    {
        var started = new DateTimeOffset(2026, 9, 4, 14, 30, 0, TimeSpan.FromHours(2));

        _store.Add(Entry(startedAt: started, duration: TimeSpan.FromSeconds(95), displayName: "Muster AG"));

        var entry = Assert.Single(_store.Query());

        // Die vier Felder aus §20.3
        Assert.Equal("+41445128430", entry.Number);
        Assert.Equal(TimeSpan.FromSeconds(95), entry.Duration);
        Assert.Equal(started, entry.StartedAt);
        Assert.Equal(CallOutcome.Answered, entry.Outcome);

        // Und die, die gespeichert aber nicht angezeigt werden
        Assert.Equal("PCMU", entry.Codec);
        Assert.Equal("sip:151@pbx.example.ch", entry.AccountIdentity);
        Assert.Equal("Muster AG", entry.DisplayName);
    }

    [Fact]
    public void Der_Name_hat_Vorrang_vor_der_Nummer()
    {
        // §20.3 zeigt „Nummer" — gemeint ist, was man erkennt. Ein aufgelöster
        // Name ist nützlicher als die Ziffern.
        _store.Add(Entry(displayName: "Muster AG"));

        Assert.Equal("Muster AG", _store.Query()[0].DisplayLabel);
    }

    [Fact]
    public void Ohne_Namen_steht_die_Nummer_da()
    {
        _store.Add(Entry(displayName: null));

        // Lesbar gruppiert, nicht als Ziffernkette: die Liste zeigt dieselbe
        // Schreibweise wie die Kontakte, sonst wirken derselbe Anrufer hier
        // und dort wie zwei verschiedene.
        Assert.Equal("+41 44 512 84 30", _store.Query()[0].DisplayLabel);
    }

    [Fact]
    public void Neueste_zuerst()
    {
        var now = DateTimeOffset.UtcNow;
        _store.Add(Entry(number: "111", startedAt: now.AddHours(-2)));
        _store.Add(Entry(number: "222", startedAt: now));
        _store.Add(Entry(number: "333", startedAt: now.AddHours(-1)));

        var numbers = _store.Query().Select(e => e.Number).ToList();

        Assert.Equal(["222", "333", "111"], numbers);
    }

    [Theory]
    [InlineData(CallHistoryFilter.Missed, 1)]
    [InlineData(CallHistoryFilter.Incoming, 2)]
    [InlineData(CallHistoryFilter.Outgoing, 2)]
    [InlineData(CallHistoryFilter.All, 4)]
    public void Die_Filter_aus_Paragraph_8_3_greifen(CallHistoryFilter filter, int expected)
    {
        _store.Add(Entry(direction: CallDirection.Incoming, outcome: CallOutcome.Missed));
        _store.Add(Entry(direction: CallDirection.Incoming, outcome: CallOutcome.Answered));
        _store.Add(Entry(direction: CallDirection.Outgoing, outcome: CallOutcome.Answered));
        _store.Add(Entry(direction: CallDirection.Outgoing, outcome: CallOutcome.NoAnswer));

        Assert.Equal(expected, _store.Query(filter).Count);
    }

    [Fact]
    public void Der_Aufnahmefilter_findet_nur_Aufgenommenes()
    {
        _store.Add(Entry(number: "111"));
        _store.Add(Entry(number: "222", recordingPath: @"C:\irgendwo\aufnahme.wav"));

        var found = Assert.Single(_store.Query(CallHistoryFilter.Recorded));
        Assert.Equal("222", found.Number);
    }

    [Fact]
    public void Die_Suche_greift_auf_Nummer_und_Name()
    {
        _store.Add(Entry(number: "+41445128430", displayName: "Muster AG"));
        _store.Add(Entry(number: "+41791234567", displayName: "Beispiel GmbH"));

        Assert.Single(_store.Query(search: "5128"));
        Assert.Single(_store.Query(search: "Muster"));
        Assert.Single(_store.Query(search: "beispiel"));   // ohne Rücksicht auf Grossschreibung
        Assert.Equal(2, _store.Query(search: "+41").Count);
    }

    [Fact]
    public void Verpasste_werden_gezaehlt()
    {
        // Speist das Abzeichen in der Umschaltleiste (§20.1).
        _store.Add(Entry(direction: CallDirection.Incoming, outcome: CallOutcome.Missed));
        _store.Add(Entry(direction: CallDirection.Incoming, outcome: CallOutcome.Missed));
        _store.Add(Entry(direction: CallDirection.Incoming, outcome: CallOutcome.Answered));

        Assert.Equal(2, _store.CountMissed());
    }

    [Fact]
    public void Alte_Eintraege_werden_aufgeraeumt()
    {
        // §8.3: „Aufbewahrung konfigurierbar (Standard 365 Tage), Aufräumen
        // beim Start."
        var now = DateTimeOffset.UtcNow;
        _store.Add(Entry(number: "alt", startedAt: now.AddDays(-400)));
        _store.Add(Entry(number: "grenzwertig", startedAt: now.AddDays(-366)));
        _store.Add(Entry(number: "neu", startedAt: now.AddDays(-10)));

        var removed = _store.Purge(retentionDays: 365);

        Assert.Equal(2, removed);
        var rest = Assert.Single(_store.Query());
        Assert.Equal("neu", rest.Number);
    }

    [Fact]
    public void Aufbewahrung_null_raeumt_nichts_auf()
    {
        // Sonst würde eine unbedachte 0 in den Einstellungen die ganze Liste
        // löschen.
        _store.Add(Entry(startedAt: DateTimeOffset.UtcNow.AddDays(-1000)));

        Assert.Equal(0, _store.Purge(retentionDays: 0));
        Assert.Single(_store.Query());
    }

    [Fact]
    public void Die_Aufbewahrung_verschont_alles_Neuere()
    {
        _store.Add(Entry(startedAt: DateTimeOffset.UtcNow.AddDays(-364)));

        Assert.Equal(0, _store.Purge(retentionDays: 365));
        Assert.Single(_store.Query());
    }

    [Fact]
    public void Eine_zweite_Instanz_sieht_dieselben_Daten()
    {
        // Die Liste überlebt einen Neustart — das ist der ganze Sinn.
        _store.Add(Entry(number: "bleibt"));

        var second = new CallHistoryStore(NullLogger<CallHistoryStore>.Instance, _databasePath);

        Assert.Equal("bleibt", second.Query()[0].Number);
    }

    [Fact]
    public void Leeren_entfernt_alles()
    {
        _store.Add(Entry());
        _store.Add(Entry());

        _store.Clear();

        Assert.Empty(_store.Query());
    }
}
