using System.Reflection;
using Nipp.Core.Services.Settings;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Das Speichermodell der Einstellungen (ADR-045).
///
/// <para><b>Warum diese Tests so aussehen.</b> <c>SettingsViewModel</c> hängt
/// an sieben Diensten, darunter <c>ISipService</c> — es gibt in diesem Projekt
/// keinen Ersatz dafür, und einen zu bauen wäre teurer als der Nutzen. Geprüft
/// wird deshalb, was sich ohne Instanz prüfen lässt: dass die beiden Listen,
/// die entscheiden, wann geschrieben wird, auf <b>wirklich vorhandene</b>
/// Eigenschaften zeigen.</para>
///
/// <para>Das ist kein Ersatz für den Test am Gerät (T194 bis T196), aber es
/// fängt den Fehler, der hier am wahrscheinlichsten ist: eine Eigenschaft wird
/// umbenannt, der Eintrag in der Liste bleibt stehen, und ein Feld schreibt ab
/// da bei jedem Tastendruck — oder gar nicht mehr.</para>
/// </summary>
public sealed class SettingsSaveModelTests
{
    private static IReadOnlyCollection<string> NamesIn(string feld)
    {
        var info = typeof(SettingsViewModel).GetField(
            feld,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(info);

        return (HashSet<string>)info!.GetValue(null)!;
    }

    private static bool HatEigenschaft(string name) =>
        typeof(SettingsViewModel).GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null;

    [Theory]
    [InlineData("NurAnzeige")]
    [InlineData("ErstBeimVerlassen")]
    public void Jeder_Eintrag_zeigt_auf_eine_vorhandene_Eigenschaft(string feld)
    {
        foreach (var name in NamesIn(feld))
        {
            Assert.True(HatEigenschaft(name), $"{feld} nennt «{name}» — die Eigenschaft gibt es nicht.");
        }
    }

    [Fact]
    public void Die_beiden_Listen_ueberschneiden_sich_nicht()
    {
        // Eine Eigenschaft ist entweder Anzeige oder Einstellung. Stünde sie in
        // beiden, entschiede die Reihenfolge der Abfrage — und die steht an
        // einer Stelle, die niemand liest, wenn er die Listen pflegt.
        var beide = NamesIn("NurAnzeige").Intersect(NamesIn("ErstBeimVerlassen")).ToList();

        Assert.Empty(beide);
    }

    [Fact]
    public void Die_Freitextfelder_sind_vollstaendig()
    {
        // Wer hier eines vergisst, prueft mit jedem Tastendruck eine halb
        // getippte Eingabe und beanstandet das Tippen selbst.
        string[] erwartet =
        [
            "SipPort", "KeepAliveSeconds", "StunServer", "CountryPrefix",
            "RecordingDirectory", "HistoryRetentionDays", "GlobalHotkey", "ProvisioningUri",
        ];

        var vorhanden = NamesIn("ErstBeimVerlassen");

        foreach (var name in erwartet)
        {
            Assert.Contains(name, vorhanden);
        }
    }

    [Theory]
    [InlineData("sip:pbx.example.ch")]
    [InlineData("SIPS:pbx.example.ch")]
    [InlineData("151@pbx.example.ch")]
    [InlineData("pbx example ch")]
    [InlineData("pbx.example.ch/sip")]
    public void Eine_SIP_Adresse_im_Domainfeld_faellt_sofort_auf(string eingabe)
    {
        // Ein Tippfehler in der Domain kostete zwoelf Sekunden Wartezeit: das
        // Konto wurde angelegt, registriert, und erst die Zeitgrenze meldete,
        // dass niemand geantwortet hat.
        Assert.NotNull(SettingsValidator.DescribeDomainIssue(eingabe));
    }

    [Theory]
    [InlineData("pbx.example.ch")]
    [InlineData("  pbx.example.ch  ")]
    [InlineData("pbx")]
    [InlineData("192.168.1.10")]
    public void Was_eine_Domain_sein_kann_wird_durchgelassen(string eingabe)
    {
        // Bewusst nachsichtig: eine Anlage kann im Intranet stehen und «pbx»
        // heissen. Ein fehlender Punkt ist deshalb kein Grund — geprueft wird
        // nur, was sicher falsch ist.
        Assert.Null(SettingsValidator.DescribeDomainIssue(eingabe));
    }
}
