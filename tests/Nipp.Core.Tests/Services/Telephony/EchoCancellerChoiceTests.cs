using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Welcher Echo-Canceller gilt und wann er wirkt (§9.4, ADR-006 Punkt 2,
/// ADR-076).
///
/// <para><b>Warum das geprüft wird.</b> Nicht, weil die Regel schwierig
/// wäre — sie ist eine Mengenabfrage. Sondern weil die Zahl, die sie
/// beantwortet, vorher als Literal mitten im Dienst stand (<c>clockRate &gt;
/// 8000</c>) und dort die Grenze eines Filters festhielt, den nipp gar nicht
/// mehr benutzt. Diese Tests halten fest, <b>dass es eine Stelle ist</b>: ein
/// Filterwechsel ändert die Liste, und die Anzeige folgt von selbst.</para>
///
/// <para><b>Was diese Tests ausdrücklich nicht beweisen</b> — und das ist die
/// wichtigere Hälfte: <b>dass <c>MSSpeexEC</c> bei 8 kHz tatsächlich
/// arbeitet.</b> Hier steht nur, was nipp darüber annimmt. Ein grüner Test
/// macht eine Annahme nicht wahr, er schützt sie (CLAUDE.md, Befund A8); den
/// Beweis trägt <b>T332</b> — eine Zeile <c>MSSpeexEC</c> mit einem Count
/// über null in der Filterstatistik eines echten Gesprächs.</para>
/// </summary>
public class EchoCancellerChoiceTests
{
    /// <summary>
    /// <b>8 kHz ist der Fall, um den es geht.</b> Alle 29 Codec-Verhandlungen
    /// vom 05. bis zum 08.10.2026 endeten bei PCMU oder PCMA — wäre diese
    /// Rate nicht dabei, bliebe die Echounterdrückung im Alltag genauso
    /// wirkungslos wie mit dem alten Filter.
    /// </summary>
    [Fact]
    public void Schmalband_ist_abgedeckt()
    {
        Assert.True(EchoCancellerChoice.IstWirksamBei(8000));
    }

    /// <summary>
    /// G.722 läuft bei 16 kHz und ist seit §9.5 eingeschaltet.
    /// </summary>
    [Fact]
    public void Breitband_bis_32_kHz_ist_abgedeckt()
    {
        Assert.True(EchoCancellerChoice.IstWirksamBei(16000));
        Assert.True(EchoCancellerChoice.IstWirksamBei(32000));
    }

    /// <summary>
    /// <b>Der Preis dieser Wahl, und er steht hier bewusst als Test.</b>
    /// Speex deckt 48 kHz nicht ab — ein Opus-Gespräch mit voller Bandbreite
    /// liefe also ohne Echounterdrückung, genau wie heute jedes
    /// 8-kHz-Gespräch. In den vier gemessenen Tagen kam dieser Fall kein
    /// einziges Mal vor; käme er häufiger vor, wäre die Wahl neu zu treffen
    /// und nicht nur die Liste zu erweitern.
    /// </summary>
    [Fact]
    public void Volle_Bandbreite_ist_nicht_abgedeckt()
    {
        Assert.False(EchoCancellerChoice.IstWirksamBei(48000));
    }

    /// <summary>
    /// <b>Unbekannt ist nicht wirksam.</b> Solange der Codec nicht ausgehandelt
    /// ist, meldet das SDK die Rate 0. Eine Zusage, die man nicht halten kann,
    /// wird nicht gegeben — dieselbe Haltung wie bei der Fremdbelegung in
    /// ADR-068, nur mit umgekehrtem Vorzeichen: dort schadet ein falsches
    /// „belegt", hier ein falsches „wirkt".
    /// </summary>
    [Fact]
    public void Unbekannte_Rate_gilt_als_unwirksam()
    {
        Assert.False(EchoCancellerChoice.IstWirksamBei(0));
    }

    /// <summary>
    /// Der Filtername darf nicht leer sein — leer hiesse, das SDK nimmt
    /// seinen eigenen Standard, und das ist genau der Filter, der in 128
    /// Statistiken bei Count 0 stand.
    /// </summary>
    [Fact]
    public void Es_ist_ein_Filter_gewaehlt()
    {
        Assert.False(string.IsNullOrWhiteSpace(EchoCancellerChoice.FilterName));
    }
}
