using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Settings;

/// <summary>
/// Der gemeinsame Prüfer für Einstellungen (§9).
///
/// <b>Warum er ein eigener Typ ist.</b> Dieselben Prüfungen standen bisher nur
/// im ViewModel und galten damit für genau einen der drei Wege, auf denen
/// Einstellungen in nipp gelangen. Ein Provisioning-Profil oder eine
/// eingelesene Sicherungsdatei brachte einen SIP-Port 0 oder eine negative
/// Aufbewahrung ungeprüft mit — gespeichert wurde beides.
/// </summary>
public sealed class SettingsValidatorTests
{
    [Fact]
    public void Die_Standardwerte_sind_brauchbar()
    {
        // Wenn das je fehlschlägt, startet nipp nicht mehr: Load() liefert bei
        // fehlender Datei genau diese Werte, und Write prüft sie.
        Assert.Empty(SettingsValidator.Validate(new NippSettings()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Ein_unmoeglicher_SIP_Port_wird_beanstandet(int port)
    {
        var settings = new NippSettings
        {
            Network = new NetworkSettings { SipPort = port },
        };

        var issue = SettingsValidator.FirstIssue(settings);

        Assert.NotNull(issue);
        Assert.Contains("Port", issue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ein_verkehrter_RTP_Portbereich_wird_beanstandet()
    {
        // Ohne diese Prüfung übernimmt das SDK den Bereich verkehrt herum, und
        // der Fehler zeigt sich erst als Gespräch ohne Ton — an einer Stelle
        // also, an der niemand nach einer Einstellung sucht.
        var settings = new NippSettings
        {
            Network = new NetworkSettings { RtpPortMin = 8000, RtpPortMax = 7000 },
        };

        var issue = SettingsValidator.FirstIssue(settings);

        Assert.NotNull(issue);
        Assert.Contains("verkehrt", issue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eine_negative_Aufbewahrung_wird_beanstandet()
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { HistoryRetentionDays = -5 },
        };

        Assert.NotNull(SettingsValidator.FirstIssue(settings));
    }

    [Fact]
    public void Null_Tage_Aufbewahrung_sind_erlaubt_und_heissen_nie_aufraeumen()
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { HistoryRetentionDays = 0 },
        };

        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("--")]
    [InlineData("keine")]
    public void Ein_Laenderpraefix_ganz_ohne_Ziffern_wird_beanstandet(string prefix)
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { CountryPrefix = prefix },
        };

        Assert.NotNull(SettingsValidator.FirstIssue(settings));
    }

    [Theory]
    [InlineData("++41")]
    [InlineData("+4a1")]
    [InlineData("41-")]
    [InlineData("+41 ")]
    public void Schreibfehler_im_Laenderpraefix_werden_geduldet(string prefix)
    {
        // Festgehalten, weil es überrascht: alle vier ergeben „+41". Der
        // NumberNormalizer wirft heraus, was keine Wählziffer ist — Buchstaben,
        // Bindestriche, Leerzeichen —, und nimmt, was übrig bleibt. Das ist
        // vertretbar: ein Präfix, dessen Absicht eindeutig ist, soll niemanden
        // aufhalten, und die Schreibweisen aus Visitenkarten sind so gemischt,
        // dass Strenge hier nur ärgern würde.
        //
        // Der Test hält fest, dass das Absicht ist und nicht Zufall — wer die
        // Prüfung eines Tages verschärft, sieht hier, was er dabei aufgibt.
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { CountryPrefix = prefix },
        };

        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData("+41")]
    [InlineData("41")]
    [InlineData("0041")]
    public void Die_drei_Schreibweisen_des_Laenderpraefix_gehen_alle(string prefix)
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { CountryPrefix = prefix },
        };

        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Fact]
    public void Eine_Provisioning_Adresse_ohne_Schema_wird_beanstandet()
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { ProvisioningUri = "crm.example.ch/profil.xml" },
        };

        Assert.NotNull(SettingsValidator.FirstIssue(settings));
    }

    [Fact]
    public void Keine_Provisioning_Adresse_ist_kein_Fehler()
    {
        var settings = new NippSettings
        {
            Advanced = new AdvancedSettings { ProvisioningUri = null },
        };

        Assert.Empty(SettingsValidator.Validate(settings));
    }

    [Fact]
    public void Ohne_PCMA_und_PCMU_wird_beanstandet()
    {
        // §9.5: mit dieser Auswahl scheitert die Verhandlung mit vielen
        // Anlagen. Die Regel gab es schon, aber nur als geworfene Ausnahme
        // beim Speichern — und die traf beim Provisionieren mitten in ein halb
        // angewendetes Profil.
        var settings = new NippSettings
        {
            Codecs = new CodecSettings { Enabled = ["opus"] },
        };

        var issue = SettingsValidator.FirstIssue(settings);

        Assert.NotNull(issue);
        Assert.Contains("PCMA", issue, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehrere_Fehler_werden_alle_gemeldet()
    {
        // Wer eine Datei von Hand schreibt, will nicht nach jedem Speichern
        // einen weiteren Fehler entdecken — dieselbe Begründung wie beim
        // Validator der Integrationen.
        var settings = new NippSettings
        {
            Network = new NetworkSettings { SipPort = 0, KeepAliveSeconds = -1 },
            Advanced = new AdvancedSettings { HistoryRetentionDays = -5, CountryPrefix = "abc" },
        };

        Assert.True(SettingsValidator.Validate(settings).Count >= 4);
    }
}
