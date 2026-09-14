using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Welcher Klang gilt — und was passiert, wenn er fehlt.
///
/// <para>Der Basispfad ist hier ein Wegwerfverzeichnis unter <c>%TEMP%</c>,
/// nie <c>%APPDATA%</c>: ein Testlauf hat in diesem Projekt schon einmal das
/// SIP-Konto des Benutzers samt Passwort gelöscht
/// (<c>TestIsolationTests</c>).</para>
/// </summary>
public class NippSoundsTests : IDisposable
{
    private readonly string _basis = Path.Combine(
        Path.GetTempPath(), $"nipp-sounds-{Guid.NewGuid():N}");

    public NippSoundsTests() => Directory.CreateDirectory(_basis);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_basis, recursive: true);
        }
        catch (IOException)
        {
            // Ein liegen gebliebenes Wegwerfverzeichnis ist kein Testfehler.
        }

        GC.SuppressFinalize(this);
    }

    private string Anlegen(string relativerPfad)
    {
        var vollstaendig = Path.Combine(_basis, relativerPfad.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(vollstaendig)!);
        File.WriteAllBytes(vollstaendig, [0x52, 0x49, 0x46, 0x46]);

        return vollstaendig;
    }

    // ---- Pfade ----

    [Fact]
    public void Ein_absoluter_Pfad_bleibt_wie_er_ist()
    {
        var eigene = Path.Combine(_basis, "eigener-klang.wav");

        Assert.Equal(eigene, NippSounds.Resolve(eigene, _basis));
    }

    /// <summary>
    /// Warum mitgelieferte Klänge relativ gespeichert werden: ein absoluter
    /// Pfad ins Ausgabeverzeichnis überlebt kein Update und keinen Umzug.
    /// </summary>
    [Fact]
    public void Ein_relativer_Pfad_gilt_gegen_das_Ausgabeverzeichnis()
    {
        var aufgeloest = NippSounds.Resolve(NippSounds.OwnRing, _basis);

        Assert.StartsWith(_basis, aufgeloest, StringComparison.Ordinal);
        Assert.EndsWith("nipp-ring.wav", aufgeloest, StringComparison.Ordinal);
    }

    // ---- Klingelton ----

    [Fact]
    public void Ohne_Wahl_gilt_der_eigene_Klingelton()
    {
        var eigen = Anlegen(NippSounds.OwnRing);
        Anlegen(NippSounds.SdkOldPhone);

        Assert.Equal(eigen, NippSounds.Ringtone(chosen: null, _basis));
    }

    [Fact]
    public void Die_Wahl_des_Benutzers_geht_vor()
    {
        Anlegen(NippSounds.OwnRing);
        var glocke = Anlegen(NippSounds.SdkOldPhone);

        Assert.Equal(glocke, NippSounds.Ringtone(NippSounds.SdkOldPhone, _basis));
    }

    /// <summary>
    /// Eine Wahl, deren Datei verschwunden ist, darf nicht in Stille enden —
    /// dann klingelt es am Gerät nicht, und §9.4 verlangt einen Ton.
    /// </summary>
    [Fact]
    public void Eine_verschwundene_Wahl_faellt_auf_den_eigenen_zurueck()
    {
        var eigen = Anlegen(NippSounds.OwnRing);

        Assert.Equal(eigen, NippSounds.Ringtone(@"D:\gibt-es-nicht\klang.wav", _basis));
    }

    [Fact]
    public void Fehlt_der_eigene_gilt_die_Telefonglocke_des_SDK()
    {
        var glocke = Anlegen(NippSounds.SdkOldPhone);

        Assert.Equal(glocke, NippSounds.Ringtone(chosen: null, _basis));
    }

    [Fact]
    public void Ohne_jeden_Klang_gibt_es_keinen_Klingelton()
    {
        Assert.Null(NippSounds.Ringtone(chosen: null, _basis));
    }

    // ---- Rufton ----

    [Fact]
    public void Der_eigene_Rufton_geht_dem_des_SDK_vor()
    {
        var eigen = Anlegen(NippSounds.OwnRingback);
        Anlegen(NippSounds.SdkRingback);

        Assert.Equal(eigen, NippSounds.Ringback(_basis));
    }

    [Fact]
    public void Fehlt_der_eigene_Rufton_gilt_der_des_SDK()
    {
        var sdk = Anlegen(NippSounds.SdkRingback);

        Assert.Equal(sdk, NippSounds.Ringback(_basis));
    }

    [Fact]
    public void Ohne_Rufton_gibt_es_keinen()
    {
        Assert.Null(NippSounds.Ringback(_basis));
    }

    // ---- Auswahlliste ----

    /// <summary>
    /// Die sechs sanften Klingeltöne des SDK sind <c>.mkv</c>, und die dafür
    /// nötige <c>bcmatroska2.dll</c> fehlt im win64-Prebuilt. Jeder von ihnen
    /// wäre in der Auswahl ein Eintrag, der zu Stille führt.
    /// </summary>
    [Fact]
    public void Die_Auswahl_enthaelt_nur_abspielbare_Dateien()
    {
        Assert.All(
            NippSounds.BundledRingtones,
            klang => Assert.EndsWith(".wav", klang.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Die_Auswahl_beginnt_mit_dem_eigenen_Klang()
    {
        Assert.Equal(NippSounds.OwnRing, NippSounds.BundledRingtones[0].RelativePath);
    }

    [Fact]
    public void Kein_Eintrag_der_Auswahl_ist_absolut()
    {
        Assert.All(
            NippSounds.BundledRingtones,
            klang => Assert.False(Path.IsPathRooted(klang.RelativePath)));
    }
}
