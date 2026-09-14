using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.History;

/// <summary>
/// Der „gesehen"-Zustand der Anrufliste (ADR-035).
///
/// <para><b>Der wichtigste Test hier ist die Wanderung</b>
/// (<see cref="Eine_Datei_aus_Fassung_1_bekommt_die_Spalte_und_behaelt_ihre_Zeilen"/>).
/// Die Anrufliste eines Jahres ist die einzige Nutzerdatei, die nipp nicht
/// wiederherstellen kann; <c>CREATE TABLE IF NOT EXISTS</c> lässt eine
/// bestehende Tabelle unberührt, auch wenn ihr eine Spalte fehlt, und der
/// Fehler zeigte sich sonst erst im Betrieb beim ersten Zugriff.</para>
/// </summary>
public sealed class CallHistorySeenTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"nipp-seen-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private CallHistoryStore NewStore() =>
        new(NullLogger<CallHistoryStore>.Instance, _databasePath);

    private static CallHistoryEntry Missed(string number = "+41791234567") => new(
        Id: 0,
        Number: number,
        DisplayName: null,
        Direction: CallDirection.Incoming,
        Outcome: CallOutcome.Missed,
        StartedAt: DateTimeOffset.UtcNow,
        Duration: null,
        Codec: null,
        AccountIdentity: null,
        RecordingPath: null);

    [Fact]
    public void Ein_neuer_verpasster_Anruf_zaehlt_und_ist_neu()
    {
        var store = NewStore();

        store.Add(Missed());

        Assert.Equal(1, store.CountMissed());
        Assert.True(store.Query().Single().IsNew);
    }

    [Fact]
    public void Angesehen_senkt_die_Zahl_um_genau_eins()
    {
        var store = NewStore();

        store.Add(Missed("+41791111111"));
        store.Add(Missed("+41792222222"));

        Assert.Equal(2, store.CountMissed());

        var erster = store.Query()[0];

        Assert.True(store.MarkSeen(erster.Id));
        Assert.Equal(1, store.CountMissed());

        // Und die andere Zeile bleibt neu — das ist der Unterschied zu einem
        // Abzeichen, das beim Öffnen der Liste einfach auf null springt.
        Assert.Single(store.Query(), static e => e.IsNew);
    }

    [Fact]
    public void Zweimal_ansehen_aendert_nichts()
    {
        var store = NewStore();
        store.Add(Missed());

        var eintrag = store.Query().Single();

        Assert.True(store.MarkSeen(eintrag.Id));

        var zeitpunkt = store.Query().Single().SeenAt;

        Assert.False(store.MarkSeen(eintrag.Id));
        Assert.Equal(zeitpunkt, store.Query().Single().SeenAt);
        Assert.Equal(0, store.CountMissed());
    }

    [Fact]
    public void Nur_verpasste_Anrufe_sind_je_neu()
    {
        var store = NewStore();

        store.Add(Missed() with { Outcome = CallOutcome.Answered, Direction = CallDirection.Outgoing });
        store.Add(Missed() with { Outcome = CallOutcome.Busy, Direction = CallDirection.Outgoing });

        Assert.Equal(0, store.CountMissed());
        Assert.DoesNotContain(store.Query(), static e => e.IsNew);
    }

    [Fact]
    public void Alle_als_gesehen_leert_das_Abzeichen()
    {
        var store = NewStore();

        store.Add(Missed("+41791111111"));
        store.Add(Missed("+41792222222"));
        store.Add(Missed("+41793333333"));

        Assert.Equal(3, store.MarkAllSeen());
        Assert.Equal(0, store.CountMissed());

        // Ein zweiter Aufruf hat nichts mehr zu tun.
        Assert.Equal(0, store.MarkAllSeen());
    }

    [Fact]
    public void Der_Zustand_ueberlebt_einen_Neustart()
    {
        var store = NewStore();
        store.Add(Missed("+41791111111"));
        store.Add(Missed("+41792222222"));

        store.MarkSeen(store.Query()[0].Id);

        // Ein zweiter Store auf derselben Datei ist der Neustart von nipp.
        // Ohne die Spalte in der Datenbank stünde hier wieder 2 — genau der
        // Fall, für den ADR-035 den Arbeitsspeicher ausschliesst.
        Assert.Equal(1, NewStore().CountMissed());
    }

    [Fact]
    public void Eine_Datei_aus_Fassung_1_bekommt_die_Spalte_und_behaelt_ihre_Zeilen()
    {
        // Die Tabelle, wie sie vor dem 07.09.2026 aussah — ohne seen_at, mit
        // user_version 1.
        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString()))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE calls (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    number           TEXT    NOT NULL,
                    display_name     TEXT,
                    direction        INTEGER NOT NULL,
                    outcome          INTEGER NOT NULL,
                    started_at       TEXT    NOT NULL,
                    duration_seconds INTEGER,
                    codec            TEXT,
                    account_identity TEXT,
                    recording_path   TEXT
                );

                INSERT INTO calls (number, direction, outcome, started_at)
                VALUES ('+41791234567', 0, 1, '2026-09-01T10:00:00.0000000+00:00');

                PRAGMA user_version = 1;
                """;

            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var store = NewStore();
        var eintraege = store.Query();

        // Die alte Zeile ist noch da, gilt als ungesehen und zählt.
        Assert.Single(eintraege);
        Assert.Equal("+41791234567", eintraege[0].Number);
        Assert.Null(eintraege[0].SeenAt);
        Assert.True(eintraege[0].IsNew);
        Assert.Equal(1, store.CountMissed());

        // Und sie lässt sich ansehen — die Spalte ist wirklich da und nicht
        // nur die Fassungsnummer hochgesetzt.
        Assert.True(store.MarkSeen(eintraege[0].Id));
        Assert.Equal(0, store.CountMissed());
    }

    [Fact]
    public void Eine_frische_Datei_ueberlebt_die_Wanderung_ebenfalls()
    {
        // Eine neu angelegte Datei bringt die Spalte schon aus CREATE TABLE
        // mit, steht aber auf user_version 0. Ein blindes ALTER TABLE wäre
        // hier ein „duplicate column name" — also ausgerechnet beim ersten
        // Start auf einem neuen Gerät, wo nichts zu wandern war.
        var store = NewStore();

        store.Add(Missed());

        Assert.Equal(1, store.CountMissed());
    }
}
