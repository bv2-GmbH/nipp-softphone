using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.History;

/// <summary>
/// <b>Der Test, der ADR-027 von der anderen Möglichkeit unterscheidet.</b>
///
/// <para>Die Anrufliste darf nach §21.2 einen extern aufgelösten <b>Namen</b>
/// speichern — einen Namen, nicht einen Gesprächsinhalt. Dem das API-Team des Journals
/// wurde schriftlich zugesagt: „Die Antwort bleibt höchstens fünf Minuten im
/// Arbeitsspeicher und wird nie auf die Platte geschrieben."</para>
///
/// <para>Beim Anruferkontext in der Anrufliste (§22.3) gab es zwei Wege:
/// beim Anklicken neu abrufen, oder beim Anruf mitspeichern. Der zweite wäre
/// bequemer — sofort da, auch offline, historisch richtig — und hätte
/// Gesprächszusammenfassungen und Stimmungswerte aller Anrufe eines Jahres
/// dauerhaft auf den Arbeitsplatz gelegt. Entschieden wurde der erste
/// (ADR-027).</para>
///
/// <para><b>Dieser Test steht bewusst vor dem Code.</b> Er ist wertlos, wenn er
/// erst danach entsteht: dann prüft er, was ohnehin gebaut wurde, statt die
/// Grenze zu ziehen, an der gebaut werden soll. Wer die Tabelle später um eine
/// Spalte für Inhalte erweitert, muss hier vorbei — und dann ist der Anlass da,
/// ADR-027 neu zu entscheiden und dem das API-Team des Journals Bescheid zu sagen.</para>
/// </summary>
public sealed class HistoryStoresNoContextTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "history.db");

    private CallHistoryStore Store() =>
        new(NullLogger<CallHistoryStore>.Instance, DatabasePath);

    /// <summary>
    /// Die Spalten, die die Anrufliste führen darf. <b>Wer hier etwas ergänzt,
    /// ändert eine Zusage</b> — siehe Klassenkommentar.
    /// </summary>
    private static readonly string[] AllowedColumns =
    [
        "id",
        "number",
        "display_name",
        "direction",
        "outcome",
        "started_at",
        "duration_seconds",
        "codec",
        "account_identity",
        "recording_path",

        // Dazugekommen am 07.09.2026 (ADR-035, Schemafassung 2). Der Test hat
        // richtig angeschlagen und ist hier bewusst erweitert worden:
        // `seen_at` ist ein Zeitstempel — wann jemand den Eintrag angesehen
        // hat —, kein Gesprächsinhalt und keine Angabe aus einem Fremdsystem.
        // Die Zusage an das das API-Team des Journals ist davon nicht berührt.
        "seen_at",
    ];

    [Fact]
    public void Die_Anrufliste_hat_keine_Spalte_fuer_Gespraechsinhalte()
    {
        var store = Store();

        store.Add(new CallHistoryEntry(
            0,
            "+41791234567",
            "Muster AG",
            CallDirection.Incoming,
            CallOutcome.Answered,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(42),
            "PCMU",
            "sip:151@pbx.example.ch",
            null));

        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('calls');";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        var unexpected = columns
            .Where(c => !AllowedColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "Die Anrufliste hat eine neue Spalte bekommen: "
                + string.Join(", ", unexpected)
                + Environment.NewLine
                + "Paragraph 21.2 erlaubt ihr einen extern aufgeloesten Namen, keinen "
                + "Gespraechsinhalt. Wenn die Spalte einen tragen soll, gehoert ADR-027 neu "
                + "entschieden und das das API-Team des Journals benachrichtigt — die Zusage lautet, dass "
                + "deren Antworten nie auf die Platte gehen.");
    }

    [Fact]
    public void Der_gespeicherte_Name_ist_das_einzige_was_von_aussen_kommt()
    {
        // Die Gegenprobe zum Test darüber: ein Name darf gespeichert werden,
        // und er wird auch zurückgelesen. Ohne diesen Fall liesse sich die
        // Regel dadurch „erfüllen", dass gar nichts mehr aufgelöst wird.
        var store = Store();

        store.Add(new CallHistoryEntry(
            0,
            "+41791234567",
            "Muster AG",
            CallDirection.Incoming,
            CallOutcome.Answered,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(42),
            "PCMU",
            null,
            null));

        var entry = Assert.Single(store.Query());

        Assert.Equal("Muster AG", entry.DisplayName);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
