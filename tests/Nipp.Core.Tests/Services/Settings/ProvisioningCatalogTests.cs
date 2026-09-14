using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// ADR-054: die Pfade eines Profils stehen an einer Stelle.
///
/// <para><b>Der Befund.</b> Sie standen an zweien — als <c>switch</c> im
/// Dienst mit 29 Pfaden und als Zeichenkettenliste in <c>nippprov</c> mit 25.
/// Der Kommentar dort gab die Gefahr zu; eingetreten war sie längst.
/// <c>audio.ringtone</c>, <c>update.channel</c>, <c>update.check-on-start</c>
/// und <c>update.token</c> fehlten dem Generator: ein gültiges Update-Profil
/// meldete <c>pruefen</c> als unbekannt.</para>
/// </summary>
public sealed class ProvisioningCatalogTests
{
    [Fact]
    public void Jeder_Eintrag_uebersteht_die_Rundreise()
    {
        // Read → Apply → Read muss dasselbe ergeben. Ein Eintrag, der das
        // nicht tut, liest ein anderes Feld als er schreibt — und damit wäre
        // die Markierung «vom Benutzer geändert» systematisch falsch.
        var settings = new NippSettings();

        foreach (var eintrag in ProvisioningCatalog.Entries)
        {
            if (eintrag.Read(settings) is not { } wert)
            {
                // Nicht gesetzt (etwa der Klingelton) oder absichtlich ohne
                // Leseweg (das Update-Token). Beides ist in Ordnung.
                continue;
            }

            var angewendet = eintrag.Apply(settings, wert);

            Assert.True(
                angewendet is not null,
                $"«{eintrag.Path}» lehnt seinen eigenen gelesenen Wert «{wert}» ab.");

            Assert.Equal(wert, eintrag.Read(angewendet!));
        }
    }

    [Fact]
    public void Ein_unbrauchbarer_Wert_wird_abgelehnt_und_nicht_geraten()
    {
        // «null heisst unbrauchbar» ist der Vertrag, auf den sich der Dienst
        // verlässt. Ein Eintrag, der stattdessen einen Standardwert setzt,
        // machte aus einem Tippfehler im Profil eine stille Änderung.
        var eintrag = ProvisioningCatalog.Find("network.sip-port");

        Assert.NotNull(eintrag);
        Assert.Null(eintrag!.Apply(new NippSettings(), "kein Port"));
    }

    [Fact]
    public void Die_Pfade_sind_eindeutig()
    {
        var doppelt = ProvisioningCatalog.AllPaths
            .GroupBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToList();

        Assert.Empty(doppelt);
    }

    [Theory]
    [InlineData("audio.ringtone")]
    [InlineData("update.channel")]
    [InlineData("update.check-on-start")]
    [InlineData("update.token")]
    public void Die_vier_vergessenen_Pfade_sind_bekannt(string pfad)
    {
        // Namentlich, weil genau diese vier auseinandergelaufen waren. Ein
        // Test über die blosse Anzahl hätte den nächsten Fall wieder
        // durchgelassen.
        Assert.True(ProvisioningCatalog.Knows(pfad), $"«{pfad}» fehlt im Katalog.");
    }

    [Fact]
    public void Die_Listenpfade_sind_bekannt_aber_keine_Eintraege()
    {
        // Konten, Nebenstellen und Gruppen behandelt ApplyProfile eigens. Sie
        // brauchen einen Pfad — eine Sperre und eine Benutzeränderung gelten
        // für sie genauso —, aber kein Read: eine Kontenliste als Zeichenfolge
        // wäre eine zweite Wahrheit über ihren Inhalt.
        foreach (var pfad in ProvisioningCatalog.ListPaths)
        {
            Assert.True(ProvisioningCatalog.Knows(pfad));
            Assert.Null(ProvisioningCatalog.Find(pfad));
        }
    }

    [Fact]
    public void Nur_die_Bezugsquelle_und_das_Token_sind_der_Auslieferung_vorbehalten()
    {
        // ADR-012: wer aus der Ferne bestimmen kann, woher nipp seine
        // Konfiguration nimmt, braucht danach niemanden mehr zu fragen. Die
        // Liste gehört klein und namentlich geprüft.
        var nurFactory = ProvisioningCatalog.Entries
            .Where(static e => e.FactoryOnly)
            .Select(static e => e.Path)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["advanced.allow-insecure-provisioning", "advanced.provisioning-uri", "update.token"],
            nurFactory);
    }
}
