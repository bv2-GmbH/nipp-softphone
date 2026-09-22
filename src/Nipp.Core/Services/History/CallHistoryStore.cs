using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.Diagnostics;

namespace Nipp.Core.Services.History;

/// <summary>
/// Die lokale Anrufliste in SQLite (§8.3, AP6.1).
///
/// §10: Daten unter <c>%LOCALAPPDATA%\nipp</c>. Die Aufbewahrung ist
/// konfigurierbar (Standard 365 Tage) und wird beim Start aufgeräumt.
/// </summary>
public sealed class CallHistoryStore
{
    private readonly ILogger<CallHistoryStore> _logger;
    private readonly string _connectionString;
    private readonly string _databasePath;

    public CallHistoryStore(ILogger<CallHistoryStore> logger)
        : this(logger, DefaultDatabasePath)
    {
    }

    /// <summary>Konstruktor mit eigenem Pfad — für Tests.</summary>
    public CallHistoryStore(ILogger<CallHistoryStore> logger, string databasePath)
    {
        _logger = logger;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _databasePath = databasePath;

        EnsureSchemaOrPreserveBroken();
    }

    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "history.db");

    /// <summary>
    /// Legt das Schema an — und wenn die Datei dabei nicht mitspielt, legt sie
    /// beiseite und fängt neu an (ADR-053).
    ///
    /// <para><b>Warum das sein muss.</b> Dieser Konstruktor läuft in
    /// <c>App.OnLaunched</c>, und dort fängt niemand. Eine beschädigte oder von
    /// einem Sicherungsdienst gehaltene <c>history.db</c> hiess bis zum
    /// 13.09.2026: <b>nipp startet nicht</b> — ohne Fenster, ohne Meldung, nur
    /// mit einem Eintrag in <c>crash.txt</c>. Ein Telefon, das wegen seiner
    /// Anrufliste nicht mehr klingelt, hat seine Aufgabe verfehlt.</para>
    ///
    /// <para><b>Und warum beiseitelegen statt löschen.</b> Die Anrufliste ist
    /// die einzige Nutzerdatei, die nipp nicht wiederherstellen kann. Dasselbe
    /// Vorgehen wie bei einer unlesbaren <c>settings.json</c>: die Datei
    /// bekommt einen Namen mit Zeitstempel und bleibt liegen. Geht auch das
    /// nicht — etwa weil ein anderer Prozess sie hält —, läuft nipp ohne
    /// Anrufliste weiter, und jeder Zugriff darauf scheitert einzeln und
    /// protokolliert.</para>
    /// </summary>
    private void EnsureSchemaOrPreserveBroken()
    {
        try
        {
            EnsureSchema();
            return;
        }
        catch (SqliteException ex)
        {
            HistoryLog.DatabaseUnusable(_logger, LogMasking.Path(_databasePath), ex.Message);
        }

        if (!TryPreserveBroken())
        {
            return;
        }

        try
        {
            EnsureSchema();
        }
        catch (SqliteException ex)
        {
            // Zweiter Fehlschlag auf einer frisch angelegten Datei: dann liegt
            // es nicht an ihrem Inhalt, sondern am Ort — kein Schreibrecht,
            // volle Platte, ein Laufwerk, das weg ist.
            HistoryLog.DatabaseUnusable(_logger, LogMasking.Path(_databasePath), ex.Message);
        }
    }

    /// <summary>
    /// Hebt eine unbrauchbare Datei auf, statt sie zu überschreiben — wie
    /// <c>SettingsService</c> es mit einer kaputten <c>settings.json</c> tut.
    /// </summary>
    private bool TryPreserveBroken()
    {
        try
        {
            if (!File.Exists(_databasePath))
            {
                return false;
            }

            // Erst den Verbindungspool leeren, sonst haelt er die Datei noch
            // offen und File.Move scheitert.
            //
            // <b>Das ist kein theoretischer Fall.</b> Der gescheiterte
            // EnsureSchema-Versuch gibt seine Verbindung ueber `using` frei —
            // und `Dispose` gibt sie an den Pool zurueck statt sie zu
            // schliessen, samt Dateihandle. Ob der Zug gelingt, haengt dann
            // davon ab, wann der Pool aufraeumt: der Test dazu lief einzeln
            // gruen und im vollen Lauf rot, was genau danach aussieht.
            SqliteConnection.ClearPool(new SqliteConnection(_connectionString));

            var target = $"{_databasePath}.kaputt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(_databasePath, target, overwrite: false);
            HistoryLog.DatabasePreserved(_logger, LogMasking.Path(target));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryLog.DatabaseNotPreserved(_logger, ex.Message);
            return false;
        }
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // Gespeichert wird alles (§8.3), angezeigt nur das Wichtigste (§20.3).
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS calls (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                number           TEXT    NOT NULL,
                display_name     TEXT,
                direction        INTEGER NOT NULL,
                outcome          INTEGER NOT NULL,
                started_at       TEXT    NOT NULL,
                duration_seconds INTEGER,
                codec            TEXT,
                account_identity TEXT,
                recording_path   TEXT,
                seen_at          TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_calls_started_at ON calls (started_at DESC);
            CREATE INDEX IF NOT EXISTS ix_calls_number     ON calls (number);
            """;

        command.ExecuteNonQuery();

        StampSchemaVersion(connection);
    }

    /// <summary>
    /// Die Fassung des Schemas, die dieser Code erwartet.
    ///
    /// <para>Erhoehen, wenn sich die Tabelle aendert, und in
    /// <see cref="StampSchemaVersion"/> den Schritt dorthin ergaenzen.</para>
    ///
    /// <para><b>2 seit dem 07.09.2026:</b> <c>seen_at</c> fuer den
    /// „gesehen"-Zustand (ADR-035). Das ist die erste Wanderung dieser
    /// Datei — die Stelle war seit September dafuer da.</para>
    /// </summary>
    private const int SchemaVersion = 2;

    /// <summary>
    /// Haelt die Schemafassung in der Datei fest und wandert, falls noetig,
    /// von einer aelteren dorthin.
    ///
    /// <para><b>Warum jetzt und nicht erst bei der ersten Aenderung.</b>
    /// <c>CREATE TABLE IF NOT EXISTS</c> legt eine Tabelle an, die es nicht
    /// gibt, und laesst eine vorhandene unberuehrt — auch dann, wenn ihr eine
    /// Spalte fehlt. Eine spaetere Erweiterung haette also stillschweigend auf
    /// einer alten Tabelle gearbeitet und beim ersten Zugriff auf die neue
    /// Spalte geworfen, mitten im Betrieb. Wer die Stelle erst dann anlegt,
    /// hat keine Ahnung mehr, welche Fassungen im Feld stehen.</para>
    /// </summary>
    private static void StampSchemaVersion(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";

        var current = Convert.ToInt32(read.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);

        if (current == SchemaVersion)
        {
            return;
        }

        // Von 0 nach 1 ist nichts zu tun: die Tabelle sieht schon so aus.
        //
        // Von 1 nach 2 kommt „seen_at" dazu (ADR-035). Gefragt wird dabei die
        // Tabelle selbst und nicht die Fassungsnummer — bei einer frisch
        // angelegten Datei hat CREATE TABLE die Spalte schon mitgebracht,
        // steht aber noch auf user_version 0. Ein ALTER TABLE allein waere
        // dort ein „duplicate column name" beim ersten Start auf einem neuen
        // Geraet, also ausgerechnet dort, wo nichts zu wandern war.
        EnsureColumn(connection, "calls", "seen_at", "TEXT");

        using var write = connection.CreateCommand();
        write.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// Ergaenzt eine Spalte, falls sie fehlt.
    ///
    /// <para>SQLite kennt kein <c>ADD COLUMN IF NOT EXISTS</c>; gefragt wird
    /// deshalb <c>PRAGMA table_info</c>. Der Weg ist unabhaengig davon, wie
    /// die Datei in ihren Zustand gekommen ist — und das ist der Punkt: eine
    /// Wanderung, die nur bei der erwarteten Vorfassung funktioniert, faellt
    /// aus, sobald jemand eine Datei aus einer anderen Fassung mitbringt.</para>
    /// </summary>
    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string type)
    {
        using var read = connection.CreateCommand();
        read.CommandText = $"PRAGMA table_info({table});";

        using (var reader = read.ExecuteReader())
        {
            while (reader.Read())
            {
                // Spalte 1 ist der Name.
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var write = connection.CreateCommand();
        write.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
        write.ExecuteNonQuery();
    }

    /// <summary>Trägt einen abgeschlossenen Anruf ein.</summary>
    public long Add(CallHistoryEntry entry) =>
        Guarded(nameof(Add), () => AddCore(entry), 0L);

    private long AddCore(CallHistoryEntry entry)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO calls
                (number, display_name, direction, outcome, started_at,
                 duration_seconds, codec, account_identity, recording_path)
            VALUES
                ($number, $displayName, $direction, $outcome, $startedAt,
                 $duration, $codec, $account, $recording);
            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$number", entry.Number);
        command.Parameters.AddWithValue("$displayName", (object?)entry.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$direction", (int)entry.Direction);
        command.Parameters.AddWithValue("$outcome", (int)entry.Outcome);
        command.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$duration", entry.Duration.HasValue
            ? (object)(long)entry.Duration.Value.TotalSeconds
            : DBNull.Value);
        command.Parameters.AddWithValue("$codec", (object?)entry.Codec ?? DBNull.Value);
        command.Parameters.AddWithValue("$account", (object?)entry.AccountIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue("$recording", (object?)entry.RecordingPath ?? DBNull.Value);

        var id = (long)(command.ExecuteScalar() ?? 0L);
        HistoryLog.Added(_logger, LogMasking.Number(entry.Number), entry.Outcome.ToString());
        return id;
    }

    /// <summary>
    /// Liest die Liste, neueste zuerst. <paramref name="search"/> greift auf
    /// Nummer und Name (§8.3).
    /// </summary>
    public IReadOnlyList<CallHistoryEntry> Query(
        CallHistoryFilter filter = CallHistoryFilter.All,
        string? search = null,
        int limit = 200) =>
        Guarded(nameof(Query), () => QueryCore(filter, search, limit), []);

    private List<CallHistoryEntry> QueryCore(
        CallHistoryFilter filter,
        string? search,
        int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        var where = new List<string>();

        switch (filter)
        {
            case CallHistoryFilter.Missed:
                where.Add($"outcome = {(int)CallOutcome.Missed}");
                break;
            case CallHistoryFilter.Incoming:
                where.Add($"direction = {(int)CallDirection.Incoming}");
                break;
            case CallHistoryFilter.Outgoing:
                where.Add($"direction = {(int)CallDirection.Outgoing}");
                break;
            case CallHistoryFilter.Recorded:
                where.Add("recording_path IS NOT NULL");
                break;
            case CallHistoryFilter.All:
            default:
                break;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ESCAPE: ohne das wirken „%" und „_" im Suchtext als Platzhalter.
            // Wer nach «50%» sucht, bekaeme jeden Eintrag; wer nach «_» sucht,
            // ebenfalls. Der Backslash steht im SQL-Text, nicht in einer
            // Protokollvorlage — dort waere er unzulaessig (CS1009).
            where.Add(
                @"(number LIKE $search ESCAPE '\' OR display_name LIKE $search ESCAPE '\')");

            // Verbatim-Zeichenfolgen: ein Backslash ist darin ein gewoehnliches
            // Zeichen, und genau darum geht es hier.
            var escaped = search.Trim()
                .Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal);

            command.Parameters.AddWithValue("$search", $"%{escaped}%");
        }

        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        command.CommandText = $"""
            SELECT id, number, display_name, direction, outcome, started_at,
                   duration_seconds, codec, account_identity, recording_path,
                   seen_at
            FROM calls
            {clause}
            ORDER BY started_at DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CallHistoryEntry>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(Read(reader));
        }

        return results;
    }

    /// <summary>
    /// Anzahl <b>ungesehener</b> verpasster Anrufe — die Zahl im Abzeichen der
    /// Umschaltleiste (§20.1, ADR-035).
    ///
    /// <para><b>Bis zum 07.09.2026 zaehlte das hier alle verpassten Anrufe der
    /// Aufbewahrungsfrist</b>, also bis zu einem Jahr. Die Zahl wurde nie
    /// kleiner, ausser jemand loeschte die Liste; sie mass „jemals verpasst"
    /// und nicht „da ist noch etwas offen". Ein Abzeichen, das man nicht
    /// wegbekommt, wird nach zwei Wochen nicht mehr gelesen.</para>
    /// </summary>
    public int CountMissed() => Guarded(nameof(CountMissed), CountMissedCore, 0);

    private int CountMissedCore()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM calls WHERE outcome = {(int)CallOutcome.Missed} "
                + "AND seen_at IS NULL;";

        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Vermerkt, dass ein Eintrag angesehen wurde. Ein zweiter Aufruf aendert
    /// nichts — der erste Zeitpunkt bleibt stehen.
    /// </summary>
    /// <returns>Ob dieser Aufruf etwas geaendert hat.</returns>
    public bool MarkSeen(long id) =>
        Guarded(nameof(MarkSeen), () => MarkSeenCore(id), false);

    private bool MarkSeenCore(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "UPDATE calls SET seen_at = $now WHERE id = $id AND seen_at IS NULL;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Vermerkt alle offenen Eintraege als angesehen.
    ///
    /// <para>Der einzige Weg, ein Abzeichen mit dreissig alten Eintraegen
    /// loszuwerden, ohne die Anrufliste zu loeschen — und wer die Wahl nicht
    /// hat, loescht irgendwann die Liste.</para>
    /// </summary>
    /// <returns>Wie viele Eintraege betroffen waren.</returns>
    public int MarkAllSeen() => Guarded(nameof(MarkAllSeen), MarkAllSeenCore, 0);

    private int MarkAllSeenCore()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = "UPDATE calls SET seen_at = $now WHERE seen_at IS NULL;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        var betroffen = command.ExecuteNonQuery();

        if (betroffen > 0)
        {
            HistoryLog.AllSeen(_logger, betroffen);
        }

        return betroffen;
    }

    /// <summary>
    /// Löscht Einträge, die älter sind als die Aufbewahrungsfrist (§8.3,
    /// Standard 365 Tage). Beim Start zu rufen.
    /// </summary>
    public int Purge(int retentionDays) =>
        Guarded(nameof(Purge), () => PurgeCore(retentionDays), 0);

    private int PurgeCore(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        using var connection = Open();
        using var command = connection.CreateCommand();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        command.CommandText = "DELETE FROM calls WHERE started_at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));

        var removed = command.ExecuteNonQuery();

        if (removed > 0)
        {
            HistoryLog.Purged(_logger, removed, retentionDays);
        }

        return removed;
    }

    /// <summary>Leert die Liste vollständig.</summary>
    public void Clear() => Guarded(nameof(Clear), () => { ClearCore(); return true; }, false);

    private void ClearCore()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM calls;";
        command.ExecuteNonQuery();

        HistoryLog.Cleared(_logger);
    }

    private static CallHistoryEntry Read(SqliteDataReader reader) => new(
        Id: reader.GetInt64(0),
        Number: reader.GetString(1),
        DisplayName: reader.IsDBNull(2) ? null : reader.GetString(2),
        Direction: (CallDirection)reader.GetInt32(3),
        Outcome: (CallOutcome)reader.GetInt32(4),
        StartedAt: DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        Duration: reader.IsDBNull(6) ? null : TimeSpan.FromSeconds(reader.GetInt64(6)),
        Codec: reader.IsDBNull(7) ? null : reader.GetString(7),
        AccountIdentity: reader.IsDBNull(8) ? null : reader.GetString(8),
        RecordingPath: reader.IsDBNull(9) ? null : reader.GetString(9),
        SeenAt: reader.IsDBNull(10)
            ? null
            : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture));

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Jeder Zugriff auf die Datei läuft hier durch (ADR-053).
    ///
    /// <para><b>Warum eine leere Liste besser ist als eine Ausnahme.</b>
    /// <c>Add</c> wird aus der <c>CallStateChanged</c>-Kette gerufen, also aus
    /// dem SDK-Callback heraus. Eine Ausnahme von dort nimmt den Prozess mit —
    /// mitten im Verbindungsaufbau. Eine Anrufliste, die einen Eintrag
    /// verliert, ist ärgerlich; ein Telefon, das deswegen abstürzt, ist
    /// kaputt.</para>
    ///
    /// <para><b>Nie still.</b> Der Benutzer merkt nur, dass ein Eintrag fehlt;
    /// die Protokollzeile ist die einzige Spur, die sagt, warum.</para>
    ///
    /// <para><b>Die Reihenfolge der beiden Methoden ist die ganze Sache, und
    /// sie war vom 13.09. bis zum 22.09.2026 falsch.</b> Oeffentlich ist die
    /// Methode mit dem <c>Guarded</c>-Aufruf, privat die mit dem Koerper:
    /// <c>public Add(…) => Guarded(nameof(Add), () =&gt; AddCore(…), 0L);</c>
    /// und darunter <c>private AddCore(…) { … }</c>. Andersherum ruft die
    /// private Methode sich selbst — sie ist dann tot und endlos rekursiv —,
    /// und der oeffentliche Weg, den alle Aufrufer nehmen, traegt den
    /// Datenbankzugriff ohne <c>try</c>. Sechs von sieben Paaren standen so
    /// da, und der Compiler sagt dazu nichts: eine ungenutzte private Methode
    /// ist keine Warnung.</para>
    ///
    /// <para><b>Gemessen, nicht vermutet (T318, Befund A1-13):</b>
    /// <c>history.db</c> im laufenden Betrieb schreibgeschuetzt, dann einen
    /// ungelesenen verpassten Anruf angeklickt — nipp war weg. Die
    /// <c>SqliteException</c> aus <c>MarkSeen</c> geht ueber
    /// <c>OnHistorySelectionChanged</c> in <c>Do_Abi_Invoke</c>, den
    /// WinRT-Rahmen, und endet als <c>0xc000027b</c> in
    /// <c>combase.dll</c> — dieselbe Signatur wie ADR-067. Genau der Weg, den
    /// ADR-053 beschreibt.</para>
    /// </summary>
    private T Guarded<T>(string operation, Func<T> work, T fallback)
    {
        try
        {
            return work();
        }
        catch (SqliteException ex)
        {
            HistoryLog.AccessFailed(_logger, operation, ex.Message);
            return fallback;
        }
    }
}

internal static partial class HistoryLog
{
    [LoggerMessage(EventId = 2700, Level = LogLevel.Debug,
        Message = "Anruf in der Liste vermerkt: {Number} ({Outcome})")]
    public static partial void Added(ILogger logger, string number, string outcome);

    [LoggerMessage(EventId = 2701, Level = LogLevel.Information,
        Message = "{Count} Eintraege aelter als {Days} Tage aus der Anrufliste entfernt")]
    public static partial void Purged(ILogger logger, int count, int days);

    [LoggerMessage(EventId = 2702, Level = LogLevel.Information,
        Message = "Anrufliste geleert")]
    public static partial void Cleared(ILogger logger);

    [LoggerMessage(EventId = 2703, Level = LogLevel.Information,
        Message = "{Count} Eintraege der Anrufliste als gesehen vermerkt")]
    public static partial void AllSeen(ILogger logger, int count);

    // ADR-053: die Anrufliste darf nipp nicht am Start hindern und keinen
    // Anruf mitnehmen. Was hier steht, ist die einzige Spur — der Benutzer
    // merkt nur, dass seine Liste leer ist.
    [LoggerMessage(EventId = 2704, Level = LogLevel.Error,
        Message = "Die Anrufliste unter {Path} ist nicht benutzbar: {Reason}. nipp laeuft "
            + "weiter, die Liste bleibt leer.")]
    public static partial void DatabaseUnusable(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 2705, Level = LogLevel.Warning,
        Message = "Die unbrauchbare Anrufliste liegt jetzt unter {Path} — sie wurde nicht "
            + "geloescht, sondern beiseitegelegt.")]
    public static partial void DatabasePreserved(ILogger logger, string path);

    [LoggerMessage(EventId = 2706, Level = LogLevel.Warning,
        Message = "Die unbrauchbare Anrufliste liess sich nicht beiseitelegen: {Reason}. "
            + "Vermutlich haelt sie ein anderer Prozess.")]
    public static partial void DatabaseNotPreserved(ILogger logger, string reason);

    [LoggerMessage(EventId = 2707, Level = LogLevel.Warning,
        Message = "Zugriff auf die Anrufliste fehlgeschlagen ({Operation}): {Reason}")]
    public static partial void AccessFailed(ILogger logger, string operation, string reason);
}
