using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Die Zustände der Präsenz-Abonnements (§14.8, W2.1 Etappe B4).
///
/// <para><b>Warum die Änderungserkennung hier geprüft wird.</b> Die Prüfung
/// läuft alle fünf Sekunden aus dem Pump. Ohne sie stünde dieselbe Zeile
/// zwölfmal je Minute und Nebenstelle im Protokoll — bei elf Nebenstellen
/// sind das rund 7 900 Zeilen pro Stunde, und ein Protokoll, in dem man
/// nichts mehr findet, ist keines.</para>
/// </summary>
public class PresenceWatchTests
{
    private const string Anna = "sip:201@example.test";
    private const string Beat = "sip:202@example.test";

    /// <summary>Der erste gemeldete Zustand ist immer eine Meldung.</summary>
    [Fact]
    public void Der_erste_Zustand_wird_gemeldet()
    {
        var watch = new PresenceWatch();

        var meldung = watch.Observe(Anna, "Active");

        Assert.NotNull(meldung);
        Assert.Equal("Active", meldung.State);
        Assert.False(meldung.IsError);
        Assert.NotEmpty(meldung.Hint);
    }

    /// <summary>
    /// <b>Derselbe Zustand noch einmal ist keine Meldung</b> — und das ist
    /// der Normalfall: ein Abonnement steht stundenlang unverändert.
    /// </summary>
    [Fact]
    public void Derselbe_Zustand_wird_nicht_wiederholt()
    {
        var watch = new PresenceWatch();

        watch.Observe(Anna, "Active");

        Assert.Null(watch.Observe(Anna, "Active"));
        Assert.Null(watch.Observe(Anna, "Active"));
    }

    /// <summary>Ein Wechsel dagegen schon.</summary>
    [Fact]
    public void Ein_Wechsel_wird_gemeldet()
    {
        var watch = new PresenceWatch();

        watch.Observe(Anna, "Active");
        var meldung = watch.Observe(Anna, "Terminated");

        Assert.NotNull(meldung);
        Assert.Equal("Terminated", meldung.State);
    }

    /// <summary>
    /// <b>Die Nebenstellen werden getrennt geführt.</b> Sonst verschluckt
    /// die eine den Wechsel der anderen, und im Protokoll fehlt genau die
    /// Zeile, die den Fehler erklärt.
    /// </summary>
    [Fact]
    public void Jede_Nebenstelle_wird_fuer_sich_gefuehrt()
    {
        var watch = new PresenceWatch();

        Assert.NotNull(watch.Observe(Anna, "Active"));
        Assert.NotNull(watch.Observe(Beat, "Active"));
        Assert.Null(watch.Observe(Anna, "Active"));
        Assert.NotNull(watch.Observe(Beat, "Error"));
    }

    /// <summary>
    /// <b>Ein abgelehntes Abonnement ist ein Fehler</b>, und daran hängt die
    /// Protokollstufe — nicht am Namen des Zustands.
    /// </summary>
    [Fact]
    public void Ein_abgelehntes_Abonnement_ist_ein_Fehler()
    {
        var watch = new PresenceWatch();

        var meldung = watch.Observe(Anna, "Error");

        Assert.NotNull(meldung);
        Assert.True(meldung.IsError);
        Assert.Contains("489", meldung.Hint);
    }

    /// <summary>
    /// <b>Eine Anmeldung, die nie bestätigt wird</b>, bleibt bei «Antwort
    /// steht aus» — und sagt genau das, statt nur «Pending» zu melden.
    /// </summary>
    [Theory]
    [InlineData("OutgoingProgress")]
    [InlineData("Pending")]
    public void Eine_unbestaetigte_Anmeldung_sagt_dass_die_Antwort_aussteht(string state)
    {
        var watch = new PresenceWatch();

        var meldung = watch.Observe(Anna, state);

        Assert.NotNull(meldung);
        Assert.Contains("Antwort steht aus", meldung.Hint);
        Assert.False(meldung.IsError);
    }

    /// <summary>
    /// <b>Ein unbekannter Zustand ist kein Fehler, sondern einer ohne
    /// Erklärung.</b> Das SDK darf Zustände hinzufügen; die Zeile im
    /// Protokoll soll dann den Namen nennen und nicht schweigen.
    /// </summary>
    [Fact]
    public void Ein_unbekannter_Zustand_wird_ohne_Erklaerung_gemeldet()
    {
        var watch = new PresenceWatch();

        var meldung = watch.Observe(Anna, "IrgendwasNeues");

        Assert.NotNull(meldung);
        Assert.Equal("IrgendwasNeues", meldung.State);
        Assert.Empty(meldung.Hint);
        Assert.False(meldung.IsError);
    }

    /// <summary>
    /// <b>Eine Nebenstelle, die aus der Liste fällt und zurückkommt</b>,
    /// meldet ihren Zustand wieder. Ohne das bliebe der erste Wechsel stumm
    /// — der Fall: eine Nebenstelle aus der Gruppe ziehen und wieder hinein.
    /// </summary>
    [Fact]
    public void Eine_vergessene_Nebenstelle_meldet_wieder()
    {
        var watch = new PresenceWatch();

        watch.Observe(Anna, "Active");
        watch.Forget(Anna);

        Assert.NotNull(watch.Observe(Anna, "Active"));
    }

    /// <summary>
    /// Die Gegenprobe: <b>ohne</b> das Vergessen bleibt sie stumm. Sonst
    /// wäre der Test darüber auch grün, wenn <c>Forget</c> nichts täte.
    /// </summary>
    [Fact]
    public void Ohne_Vergessen_bleibt_sie_stumm()
    {
        var watch = new PresenceWatch();

        watch.Observe(Anna, "Active");

        Assert.Null(watch.Observe(Anna, "Active"));
        Assert.Equal(1, watch.Count);
    }

    /// <summary>Leere Angaben sind keine Meldung — und kein Absturz.</summary>
    [Theory]
    [InlineData("", "Active")]
    [InlineData("sip:201@example.test", "")]
    [InlineData(" ", " ")]
    public void Leere_Angaben_melden_nichts(string address, string state)
    {
        var watch = new PresenceWatch();

        Assert.Null(watch.Observe(address, state));
        Assert.Equal(0, watch.Count);
    }
}
