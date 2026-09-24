using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Die Konten und ihre Anmeldezustände (§20.2, W2.1 Etappe B2).
///
/// <para><b>Was diese Tests festhalten, hat je einen Tag gekostet:</b> eine
/// Lampe, die bei jeder Erneuerung kurz rot wurde; eine Fehlermeldung zu
/// einem Konto, das in der Oberfläche gar nicht stand; und ein Ereignis, an
/// dem vier Empfänger hängen und das bei jedem Lebenszeichen des Servers neu
/// feuerte.</para>
/// </summary>
public class AccountRegistryTests
{
    private static SipAccountSettings Konto(string username, string domain = "example.test") =>
        new()
        {
            Username = username,
            Domain = domain,
            Password = "geheim",
            DisplayName = $"Platz {username}",
        };

    private static string Erklaere(string text, SipAccountSettings settings) =>
        $"erklärt: {text} ({settings.Domain})";

    private static AccountRegistry MitKonten(params string[] namen)
    {
        var registry = new AccountRegistry();

        foreach (var name in namen)
        {
            registry.Set(Konto(name));
        }

        return registry;
    }

    // --- Eintragen, entfernen, Standard ------------------------------------

    /// <summary>
    /// Das erste eingetragene Konto ist Standard für ausgehende Anrufe (§9.1)
    /// — und das zweite ändert daran nichts.
    /// </summary>
    [Fact]
    public void Das_erste_Konto_wird_Standard()
    {
        var registry = MitKonten("101", "102");

        Assert.Equal("sip:101@example.test", registry.DefaultIdentity);
        Assert.Equal(2, registry.Count);
    }

    /// <summary>
    /// <b>Ein Standardkonto, das es nicht mehr gibt, ist kein Standard</b> —
    /// es ist eine leere Kontoauswahl. Beim Entfernen rückt das nächste nach.
    /// </summary>
    [Fact]
    public void Beim_Entfernen_des_Standardkontos_rueckt_eines_nach()
    {
        var registry = MitKonten("101", "102");

        registry.Remove("sip:101@example.test");

        Assert.Equal("sip:102@example.test", registry.DefaultIdentity);
        Assert.Equal(1, registry.Count);
    }

    /// <summary>
    /// Wird ein anderes entfernt, bleibt der Standard stehen — sonst wechselt
    /// die Anlage unter dem Benutzer, weil er ein Konto aufgeräumt hat.
    /// </summary>
    [Fact]
    public void Ein_anderes_Konto_zu_entfernen_laesst_den_Standard_stehen()
    {
        var registry = MitKonten("101", "102");

        registry.Remove("sip:102@example.test");

        Assert.Equal("sip:101@example.test", registry.DefaultIdentity);
    }

    /// <summary>
    /// Das letzte Konto zu entfernen lässt keinen Standard zurück, der ins
    /// Leere zeigt.
    /// </summary>
    [Fact]
    public void Ohne_Konten_gibt_es_keinen_Standard()
    {
        var registry = MitKonten("101");

        registry.Remove("sip:101@example.test");

        Assert.Null(registry.DefaultIdentity);
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// <b>Ein eingetragenes Konto meldet sich gerade an</b>, es hat nicht
    /// «nichts gehört». Die Oberfläche unterscheidet beides.
    /// </summary>
    [Fact]
    public void Ein_neues_Konto_beginnt_bei_InProgress()
    {
        var registry = MitKonten("101");

        Assert.Equal(RegistrationStatus.InProgress, registry.StatusOf("sip:101@example.test"));
    }

    /// <summary>§20.2 lässt zehn Konten zu — die Registry zählt sie, die Grenze zieht der Dienst.</summary>
    [Fact]
    public void Zehn_Konten_sind_zehn()
    {
        var registry = MitKonten([.. Enumerable.Range(101, 10).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

        Assert.Equal(10, registry.Count);
        Assert.Equal(10, registry.Snapshot().Count);
        Assert.Single(registry.Snapshot(), static a => a.IsDefault);
    }

    // --- Die Zuordnung des Kontos -----------------------------------------

    /// <summary>
    /// Das SDK meldet die Identität <b>mit Anzeigenamen</b>, die Schlüssel
    /// hier sind ohne.
    /// </summary>
    [Fact]
    public void Eine_Identitaet_mit_Anzeigenamen_findet_ihr_Konto()
    {
        var registry = MitKonten("101");

        Assert.Equal("sip:101@example.test", registry.Normalize("\"nipp Testgeraet\" <sip:101@example.test>"));
        Assert.Equal("sip:101@example.test", registry.Normalize("sip:101@example.test"));
    }

    /// <summary>
    /// <b>Und was nicht passt, passt nicht.</b> Ein Konto, das nipp nicht
    /// eingerichtet hat, darf nicht auf ein eingerichtetes abgebildet werden
    /// — sonst trägt das falsche Konto den Fehler.
    /// </summary>
    [Theory]
    [InlineData("sip:999@example.test")]
    [InlineData("\"Alt\" <sip:101@andere.test>")]
    [InlineData("")]
    public void Eine_fremde_Identitaet_findet_kein_Konto(string raw)
    {
        var registry = MitKonten("101");

        Assert.Null(registry.Normalize(raw));
    }

    // --- Was ein Ereignis auslöst -----------------------------------------

    /// <summary>
    /// <b>Ein Konto, das nipp nicht kennt, ist kein Alarm</b> — es ist das
    /// aus <c>linphonerc</c> wiederhergestellte, das gleich darauf ersetzt
    /// wird. Am Gerät sah man eine Lampe, die kurz rot wurde, und eine
    /// Fehlermeldung zu einem Konto, das in der Oberfläche gar nicht steht.
    /// </summary>
    [Fact]
    public void Ein_Fehlschlag_eines_unbekannten_Kontos_ist_veraltet()
    {
        var registry = MitKonten("101");

        var ergebnis = registry.Record(
            "sip:alt@fremd.test",
            RegistrationStatus.Failed,
            "io error",
            Erklaere);

        Assert.True(ergebnis.IsStale);
        Assert.Null(ergebnis.Identity);
        Assert.False(ergebnis.AccountsChanged);
    }

    /// <summary>
    /// Die Gegenprobe: ein <b>Erfolg</b> eines unbekannten Kontos ist kein
    /// veralteter Fehlschlag, sondern schlicht nichts, was uns betrifft.
    /// </summary>
    [Fact]
    public void Ein_Erfolg_eines_unbekannten_Kontos_ist_nicht_veraltet()
    {
        var registry = MitKonten("101");

        var ergebnis = registry.Record("sip:alt@fremd.test", RegistrationStatus.Registered, "ok", Erklaere);

        Assert.False(ergebnis.IsStale);
        Assert.False(ergebnis.AccountsChanged);
    }

    /// <summary>
    /// <b>Ein Fehlschlag wird erklärt</b> (§15) — und zwar mit der Domäne
    /// <b>des betroffenen Kontos</b>. Bei zwei Konten auf verschiedenen
    /// Anlagen nannte die Meldung vorher eine Anlage, die mit dem Fehler
    /// nichts zu tun hatte.
    /// </summary>
    [Fact]
    public void Ein_Fehlschlag_wird_mit_der_eigenen_Domaene_erklaert()
    {
        var registry = new AccountRegistry();
        registry.Set(Konto("101", "anlage-a.test"));
        registry.Set(Konto("201", "anlage-b.test"));

        var ergebnis = registry.Record("sip:201@anlage-b.test", RegistrationStatus.Failed, "403", Erklaere);

        Assert.Equal("erklärt: 403 (anlage-b.test)", ergebnis.Message);
        Assert.Equal("sip:201@anlage-b.test", ergebnis.Identity);
    }

    /// <summary>Ein Erfolg wird nicht erklärt — da gibt es nichts zu tun.</summary>
    [Fact]
    public void Ein_Erfolg_bleibt_unerklaert()
    {
        var registry = MitKonten("101");

        var ergebnis = registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);

        Assert.Equal("ok", ergebnis.Message);
    }

    // --- Die Gegenprobe aus ADR-060 ---------------------------------------

    /// <summary>
    /// <b>Die wichtigere Hälfte:</b> eine Erneuerung, die nichts ändert,
    /// meldet auch nichts.
    ///
    /// <para>Ein <c>AccountsChanged</c> ist kein Hinweis, sondern ein
    /// Auftrag — vier Empfänger hängen daran, und einer davon hat an einem
    /// Tag mit wackelnder Anmeldung 727-mal die Einstellungen geschrieben.
    /// Die Anlage erneuert alle zehn Minuten.</para>
    /// </summary>
    [Fact]
    public void Eine_Erneuerung_ohne_Aenderung_meldet_nichts()
    {
        var registry = MitKonten("101");

        var erste = registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);
        var zweite = registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);

        Assert.True(erste.AccountsChanged);
        Assert.False(zweite.AccountsChanged);
    }

    /// <summary>
    /// <b>Und die Gegenprobe dazu, die noch wichtiger ist:</b> eine echte
    /// Änderung darf nicht verschluckt werden. Eine zu grobe Bremse
    /// verschluckt den echten Wechsel (ADR-060).
    /// </summary>
    [Fact]
    public void Ein_echter_Wechsel_wird_gemeldet()
    {
        var registry = MitKonten("101");

        registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);
        var wechsel = registry.Record("sip:101@example.test", RegistrationStatus.Failed, "403", Erklaere);

        Assert.True(wechsel.AccountsChanged);
        Assert.Equal(RegistrationStatus.Failed, registry.StatusOf("sip:101@example.test"));
    }

    /// <summary>
    /// Auch eine geänderte <b>Meldung</b> bei gleichem Zustand ist eine
    /// Änderung: in der Kontoliste steht sie unter dem Namen.
    /// </summary>
    [Fact]
    public void Eine_geaenderte_Meldung_bei_gleichem_Zustand_zaehlt()
    {
        var registry = MitKonten("101");

        registry.Record("sip:101@example.test", RegistrationStatus.Failed, "403", Erklaere);
        var zweite = registry.Record("sip:101@example.test", RegistrationStatus.Failed, "404", Erklaere);

        Assert.True(zweite.AccountsChanged);
    }

    // --- Der Kontowechsel im laufenden Gespräch ---------------------------

    /// <summary>
    /// <b>Ein Kontowechsel lässt die anderen Konten in Ruhe</b> — auch ihre
    /// Zustände. Das ist der Fall «zwei Konten, im Gespräch auf dem einen,
    /// das andere meldet sich neu an».
    /// </summary>
    [Fact]
    public void Ein_Ereignis_eines_Kontos_laesst_die_anderen_stehen()
    {
        var registry = MitKonten("101", "102");

        registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);
        registry.Record("sip:102@example.test", RegistrationStatus.Failed, "403", Erklaere);

        Assert.Equal(RegistrationStatus.Registered, registry.StatusOf("sip:101@example.test"));
        Assert.Equal(RegistrationStatus.Failed, registry.StatusOf("sip:102@example.test"));
    }

    /// <summary>
    /// <b>Ein Zustand für ein Kürzel, das nicht mehr eingetragen ist</b>,
    /// verändert nichts — auch nicht die Liste.
    /// </summary>
    [Fact]
    public void Ein_Ereignis_nach_dem_Entfernen_veraendert_nichts()
    {
        var registry = MitKonten("101", "102");
        registry.Remove("sip:102@example.test");

        var ergebnis = registry.Record("sip:102@example.test", RegistrationStatus.Failed, "io error", Erklaere);

        Assert.True(ergebnis.IsStale);
        Assert.Single(registry.Snapshot());
    }

    /// <summary>
    /// Ein Konto erneut einzutragen setzt seinen Zustand zurück — es meldet
    /// sich ja wirklich neu an. Sonst stünde die Lampe auf «angemeldet»,
    /// während das REGISTER noch läuft.
    /// </summary>
    [Fact]
    public void Ein_erneut_eingetragenes_Konto_beginnt_wieder_bei_InProgress()
    {
        var registry = MitKonten("101");
        registry.Record("sip:101@example.test", RegistrationStatus.Registered, "ok", Erklaere);

        registry.Set(Konto("101"));

        Assert.Equal(RegistrationStatus.InProgress, registry.StatusOf("sip:101@example.test"));
        Assert.Equal(1, registry.Count);
    }
}
