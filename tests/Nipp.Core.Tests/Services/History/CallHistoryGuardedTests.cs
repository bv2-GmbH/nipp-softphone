using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.History;

/// <summary>
/// Dass eine unbrauchbare Datei nipp nicht mitnimmt (ADR-053, Befund A1-13).
///
/// <para><b>Warum es diese Datei gibt.</b> Vom 13.09. bis zum 22.09.2026 lief
/// der Schutz aus <c>CallHistoryStore.Guarded</c> bei sechs von sieben
/// Zugriffen ins Leere: die private <c>…Core</c>-Methode trug den
/// <c>Guarded</c>-Aufruf und rief sich selbst, der öffentliche Weg trug den
/// Datenbankzugriff ohne <c>try</c>. Alle bestehenden Tests blieben grün — sie
/// arbeiten auf einer funktionierenden Datei, und dort ist nicht zu
/// unterscheiden, ob ein <c>try</c> drumherum steht.</para>
///
/// <para><b>Gemessen hat es erst T318</b>, am laufenden Programm: eine
/// schreibgeschützte <c>history.db</c>, ein Klick auf einen ungelesenen
/// verpassten Anruf, und nipp war weg — <c>0xc000027b</c> in
/// <c>combase.dll</c>, weil die <c>SqliteException</c> über
/// <c>Do_Abi_Invoke</c> in den WinRT-Rahmen läuft.</para>
///
/// <para><b>Was diese Tests prüfen, ist deshalb nicht das Ergebnis, sondern
/// dass überhaupt eines zurückkommt:</b> kein Wurf, und der Ersatzwert. Ein
/// Reflexionstest über die Namen der Methoden täte es nicht — er hielte das
/// Muster fest und nicht seine Wirkung.</para>
/// </summary>
public sealed class CallHistoryGuardedTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"nipp-guarded-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            // Erst den Schreibschutz weg, sonst bleibt die Datei liegen.
            File.SetAttributes(_databasePath, FileAttributes.Normal);
            File.Delete(_databasePath);
        }
    }

    private static CallHistoryEntry Missed() => new(
        Id: 0,
        Number: "+41791234567",
        DisplayName: null,
        Direction: CallDirection.Incoming,
        Outcome: CallOutcome.Missed,
        StartedAt: DateTimeOffset.UtcNow,
        Duration: null,
        Codec: null,
        AccountIdentity: null,
        RecordingPath: null);

    /// <summary>
    /// Legt eine brauchbare Datei mit einem verpassten Anruf an und schützt
    /// sie danach gegen Schreiben — der Zustand aus T318.
    /// </summary>
    private CallHistoryStore SchreibgeschuetzterStore()
    {
        var store = new CallHistoryStore(NullLogger<CallHistoryStore>.Instance, _databasePath);
        var id = store.Add(Missed());

        Assert.NotEqual(0, id);

        // Die Verbindung muss zurück in den Pool, sonst hält sie ein Handle
        // auf eine Datei, deren Attribute gerade wechseln.
        SqliteConnection.ClearAllPools();
        File.SetAttributes(_databasePath, FileAttributes.ReadOnly);

        return store;
    }

    [Fact]
    public void Ein_Eintrag_auf_einer_schreibgeschuetzten_Datei_wirft_nicht()
    {
        var store = SchreibgeschuetzterStore();

        // 0 ist der Ersatzwert: kein Eintrag, aber auch kein Wurf aus dem
        // SDK-Callback heraus, aus dem Add gerufen wird.
        Assert.Equal(0L, store.Add(Missed()));
    }

    [Fact]
    public void Gesehen_vermerken_auf_einer_schreibgeschuetzten_Datei_wirft_nicht()
    {
        var store = SchreibgeschuetzterStore();
        var eintrag = store.Query().Single();

        // Das ist der Weg aus T318: die Zeile bleibt ungelesen, nipp bleibt
        // stehen.
        Assert.False(store.MarkSeen(eintrag.Id));
    }

    [Fact]
    public void Alles_gesehen_auf_einer_schreibgeschuetzten_Datei_wirft_nicht()
    {
        var store = SchreibgeschuetzterStore();

        Assert.Equal(0, store.MarkAllSeen());
    }

    [Fact]
    public void Aufraeumen_auf_einer_schreibgeschuetzten_Datei_wirft_nicht()
    {
        var store = SchreibgeschuetzterStore();

        // Purge laeuft beim Start. Ein Wurf hier heisst: nipp startet nicht.
        Assert.Equal(0, store.Purge(retentionDays: 1));
    }

    [Fact]
    public void Leeren_auf_einer_schreibgeschuetzten_Datei_wirft_nicht()
    {
        var store = SchreibgeschuetzterStore();

        store.Clear();

        Assert.Single(store.Query());
    }

    [Fact]
    public void Lesen_geht_auf_einer_schreibgeschuetzten_Datei_weiter()
    {
        var store = SchreibgeschuetzterStore();

        // Die Gegenprobe, und sie ist die wichtigere Haelfte: eine Datei, die
        // sich nicht beschreiben laesst, laesst sich immer noch lesen. Wer
        // hier zu grob schuetzt, macht aus einem fehlenden Eintrag eine leere
        // Anrufliste.
        Assert.Single(store.Query());
        Assert.Equal(1, store.CountMissed());
    }
}
