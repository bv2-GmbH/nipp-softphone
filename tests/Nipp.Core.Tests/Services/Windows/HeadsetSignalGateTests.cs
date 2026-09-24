using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Ob ein Zustand an das Headset gemeldet werden darf (§22.5).
///
/// <para><b>Warum diese Regel Tests hat.</b> Der Befund vom 10.09.2026 war
/// „ich fliege aus dem Teams-Meeting, sobald es auf nipp klingelt". Die
/// Ursache war ein Bericht an ein Gerät, das nipp mit Teams teilt — und
/// Berichte gingen bisher auch dann hinaus, wenn nipp gar keinen Anruf hatte:
/// beim Start und bei jedem Audiogerätewechsel, am 10.09.2026 dreizehnmal.
/// Diese Regel entscheidet darüber, und sie ist die einzige Stelle der
/// Headset-Anbindung, die ohne Gerät prüfbar ist.</para>
/// </summary>
public class HeadsetSignalGateTests
{
    private static readonly HeadsetState Leer = new(false, false, false);
    private static readonly HeadsetState Klingelt = new(false, true, false);
    private static readonly HeadsetState ImGespraech = new(true, false, false);

    [Fact]
    public void Ohne_je_gemeldeten_Zustand_geht_kein_leerer_Bericht_hinaus()
    {
        // Der Startbericht und der bei jedem Geraetewechsel: kein Anruf, kein
        // Anlass — und beim Empfaenger auf der anderen Seite des geteilten
        // Handles ein Tastendruck.
        var urteil = HeadsetSignalGate.Erlaubt(Leer, jeGemeldet: false, fremdbelegt: false, eigenesGespraech: false);

        Assert.False(urteil.Schreiben);
        Assert.Equal("ohne Anlass", urteil.Grund);
    }

    [Fact]
    public void Nach_einem_gemeldeten_Klingeln_geht_alles_aus_hinaus()
    {
        // T84: die Lampe muss auch wieder ausgehen. Hier ist "alles aus" eine
        // Mitteilung, weil nipp vorher etwas anderes gesagt hat.
        var urteil = HeadsetSignalGate.Erlaubt(Leer, jeGemeldet: true, fremdbelegt: false, eigenesGespraech: false);

        Assert.True(urteil.Schreiben);
        Assert.Equal("Ende", urteil.Grund);
    }

    [Fact]
    public void Ein_eigener_Anruf_wird_gemeldet()
    {
        var urteil = HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: false, fremdbelegt: false, eigenesGespraech: false);

        Assert.True(urteil.Schreiben);
        Assert.Equal("Anruf", urteil.Grund);
    }

    [Fact]
    public void Bei_Fremdbelegung_wird_das_Klingeln_nicht_gemeldet()
    {
        // Der gemeldete Fall: ein Teams-Meeting laeuft, und es klingelt auf
        // nipp. Der Ring-Bericht ist die eine Meldung, die das fremde Gespraech
        // trifft, bevor der Benutzer entschieden hat.
        var urteil = HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: true, fremdbelegt: true, eigenesGespraech: false);

        Assert.False(urteil.Schreiben);
        Assert.Equal("Fremdbelegung", urteil.Grund);
    }

    [Fact]
    public void Bei_Fremdbelegung_wird_ein_angenommenes_Gespraech_doch_gemeldet()
    {
        // Nimmt der Benutzer an, hat er entschieden. Dass das andere Programm
        // dann das Geraet verliert, ist die Folge seiner Wahl und kein Fehler.
        var urteil = HeadsetSignalGate.Erlaubt(
            ImGespraech, jeGemeldet: true, fremdbelegt: true, eigenesGespraech: true);

        Assert.True(urteil.Schreiben);
        Assert.Equal("Anruf", urteil.Grund);
    }

    [Fact]
    public void Nach_dem_Ende_einer_Fremdbelegung_wird_wieder_gemeldet()
    {
        var waehrend = HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: true, fremdbelegt: true, eigenesGespraech: false);
        var danach = HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: true, fremdbelegt: false, eigenesGespraech: false);

        Assert.False(waehrend.Schreiben);
        Assert.True(danach.Schreiben);
    }

    /// <summary>
    /// <b>Was nipp gemeldet hat, nimmt es zurück — auch bei Fremdbelegung.</b>
    ///
    /// <para>Sonst bliebe die Lampe des eigenen, gerade beendeten Gesprächs
    /// stehen. <b>Am 14.09.2026 gemessen (M1):</b> ohne Abschlussbericht
    /// klingelt das Jabra weiter, auch nachdem der Anrufer aufgelegt hat, und
    /// hört nicht von selbst auf.</para>
    /// </summary>
    [Fact]
    public void Ein_leerer_Bericht_bleibt_auch_bei_Fremdbelegung_erlaubt()
    {
        var urteil = HeadsetSignalGate.Erlaubt(
            Leer, jeGemeldet: true, fremdbelegt: true, eigenesGespraech: false);

        Assert.True(urteil.Schreiben);
        Assert.Equal("Ende", urteil.Grund);
    }

    /// <summary>
    /// <b>Der gemeldete Fall vom 14.09.2026, ganz durchgespielt</b> (ADR-068).
    ///
    /// <para>Ein Meeting läuft, es klingelt, der Benutzer <b>lehnt ab</b>. Der
    /// Ring wird verschwiegen — und weil er verschwiegen wurde, bleibt auch
    /// der Abschluss liegen. Das fremde Gespräch bleibt unberührt.</para>
    ///
    /// <para><b>Das ist der Punkt, an dem die alte Fassung scheiterte:</b> sie
    /// prüfte die Fremdbelegung nur beim Klingeln, liess den Abschluss also
    /// durch — und der beendete das Meeting.</para>
    /// </summary>
    [Fact]
    public void Waehrend_einer_Fremdbelegung_klingelt_und_endet_nipp_stumm()
    {
        var beimKlingeln = HeadsetSignalGate.Erlaubt(
            Klingelt, jeGemeldet: false, fremdbelegt: true, eigenesGespraech: false);

        Assert.False(beimKlingeln.Schreiben);

        // Verschwiegen heisst: jeGemeldet bleibt false. Genau daran haengt der
        // naechste Schritt.
        var beimAblehnen = HeadsetSignalGate.Erlaubt(
            Leer, jeGemeldet: false, fremdbelegt: true, eigenesGespraech: false);

        Assert.False(beimAblehnen.Schreiben);
        Assert.Equal("ohne Anlass", beimAblehnen.Grund);
    }

    /// <summary>
    /// <b>Ein klingelnder Anruf ist noch keine Entscheidung, ein angenommener
    /// ist eine.</b>
    ///
    /// <para>Bis zum 14.09.2026 wurde die Fremdbelegung mit
    /// <c>calls.Count > 0</c> ermittelt — und weil beim Klingeln immer ein
    /// Anruf da ist, war sie genau im einzigen Fall, für den sie gebaut war,
    /// immer falsch.</para>
    /// </summary>
    [Fact]
    public void Ein_eigenes_Gespraech_schlaegt_die_Fremdbelegung()
    {
        var beimKlingeln = HeadsetSignalGate.Erlaubt(
            Klingelt, jeGemeldet: false, fremdbelegt: true, eigenesGespraech: false);

        var nachDemAnnehmen = HeadsetSignalGate.Erlaubt(
            ImGespraech, jeGemeldet: false, fremdbelegt: true, eigenesGespraech: true);

        Assert.False(beimKlingeln.Schreiben);
        Assert.True(nachDemAnnehmen.Schreiben);
    }

    /// <summary>
    /// Jeder Zweig nennt einen eigenen Grund.
    ///
    /// <para>Ein Gate, dessen Gründe im Protokoll gleich aussehen, macht „ich
    /// fliege trotzdem raus" wieder ununterscheidbar von „die Erkennung hat
    /// nicht angeschlagen" — genau die Lücke, die diese Änderung
    /// schliesst.</para>
    /// </summary>
    [Fact]
    public void Jeder_Zweig_nennt_einen_eigenen_Grund()
    {
        var gruende = new[]
        {
            HeadsetSignalGate.Erlaubt(Leer, jeGemeldet: false, fremdbelegt: false, eigenesGespraech: false).Grund,
            HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: true, fremdbelegt: true, eigenesGespraech: false).Grund,
            HeadsetSignalGate.Erlaubt(Leer, jeGemeldet: true, fremdbelegt: false, eigenesGespraech: false).Grund,
            HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: true, fremdbelegt: false, eigenesGespraech: false).Grund,
            HeadsetSignalGate.Erlaubt(Klingelt, jeGemeldet: false, fremdbelegt: false, eigenesGespraech: false, signaleErlaubt: false).Grund,
        };

        Assert.Equal(gruende.Length, gruende.Distinct().Count());
        Assert.DoesNotContain(gruende, string.IsNullOrWhiteSpace);
    }

    // --- H5, der Notausgang (ADR-068) -------------------------------------

    /// <summary>
    /// <b>Aus heisst aus.</b> Die Einstellung ist die Antwort auf ein Gerät,
    /// das sich anders verhält als die beiden gemessenen — eine Antwort mit
    /// Ausnahmen wäre keine.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Abgeschaltet_geht_gar_nichts_hinaus(bool jeGemeldet, bool fremdbelegt, bool eigenesGespraech)
    {
        foreach (var zustand in new[] { Leer, Klingelt, ImGespraech })
        {
            var urteil = HeadsetSignalGate.Erlaubt(
                zustand,
                jeGemeldet,
                fremdbelegt,
                eigenesGespraech,
                signaleErlaubt: false);

            Assert.False(urteil.Schreiben);
            Assert.Equal("abgeschaltet", urteil.Grund);
        }
    }

    /// <summary>
    /// <b>Auch der Abschlussbericht bleibt liegen</b>, und das ist die
    /// unbequeme Hälfte: wer mitten im Klingeln abschaltet, löscht die Lampe
    /// am Gerät selbst. Die bequemere Lesart — «einmal noch aufräumen» —
    /// hiesse, dass ein Notausgang ein letztes Mal genau das tut, wovor er
    /// schützen soll.
    /// </summary>
    [Fact]
    public void Abgeschaltet_bleibt_auch_der_Abschluss_liegen()
    {
        var urteil = HeadsetSignalGate.Erlaubt(
            Leer,
            jeGemeldet: true,
            fremdbelegt: false,
            eigenesGespraech: false,
            signaleErlaubt: false);

        Assert.False(urteil.Schreiben);
        Assert.Equal("abgeschaltet", urteil.Grund);
    }

    /// <summary>
    /// <b>Die Gegenprobe, und sie ist hier die wichtigere:</b> eingeschaltet
    /// ändert sich nichts. Der Standard ist ein — ein Notausgang, der im
    /// Normalbetrieb etwas verschiebt, ist ein Fehler und keine Einstellung.
    /// </summary>
    [Fact]
    public void Eingeschaltet_gilt_jede_bisherige_Regel_unveraendert()
    {
        foreach (var zustand in new[] { Leer, Klingelt, ImGespraech })
        {
            foreach (var jeGemeldet in new[] { false, true })
            {
                foreach (var fremd in new[] { false, true })
                {
                    foreach (var eigenes in new[] { false, true })
                    {
                        var ohneAngabe = HeadsetSignalGate.Erlaubt(zustand, jeGemeldet, fremd, eigenes);
                        var mitEin = HeadsetSignalGate.Erlaubt(zustand, jeGemeldet, fremd, eigenes, signaleErlaubt: true);

                        Assert.Equal(ohneAngabe.Schreiben, mitEin.Schreiben);
                        Assert.Equal(ohneAngabe.Grund, mitEin.Grund);
                    }
                }
            }
        }
    }
}
