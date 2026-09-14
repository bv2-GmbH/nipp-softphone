using Nipp.Core.Diagnostics;

namespace Nipp.Core.Tests.Diagnostics;

/// <summary>
/// ADR-022, Nachtrag vom 13.09.2026: der SDK-Trace unterliegt derselben Zusage
/// wie jede eigene Protokollzeile.
///
/// <para><b>Der Befund.</b> <c>SdkLogBridge</c> reichte jede SDK-Zeile
/// ungefiltert weiter. Auf Stufe Debug standen im Protokoll dieser Maschine am
/// 12.09.2026 <b>714 Zeilen mit Digest-Kopfzeilen</b> und <b>4 282 mit
/// Rufnummern und Anzeigenamen</b>. Das Diagnosepaket nimmt die Protokolle mit
/// zum Support — die Zusage aus §21.2 war damit genau dann nicht gehalten,
/// wenn der Support Debug einschalten liess.</para>
///
/// <para><b>Die Vorlagen sind echt.</b> Jede Zeile hier ist aus dem Protokoll
/// vom 12.09.2026 abgeschrieben; nur Domäne und Nummern sind durch Muster
/// ersetzt. Gegen die vereinbarte Form zu testen statt gegen die echte ist der
/// Fehler, den dieses Projekt bei den Integrationen schon einmal gemacht
/// hat.</para>
/// </summary>
public sealed class SipLineMaskingTests
{
    [Fact]
    public void Die_Antwort_einer_Digest_Anmeldung_verschwindet()
    {
        // Das Gefährlichste in der Datei: nonce und response zusammen sind
        // eine gebrauchsfertige Anmeldung.
        const string zeile =
            """
            Authorization: Digest realm="pbx.example.test", nonce="1789194193/248c6595d2560237bcbbe4249e14ce56", algorithm=MD5, opaque="0ea81f134f4147f0", username="151bv2",  uri="sip:pbx.example.test", response="b7d2c5198c8d6d156992d86e0aa41b33"
            """;

        var maskiert = LogMasking.SipLine(zeile);

        Assert.DoesNotContain("248c6595d2560237bcbbe4249e14ce56", maskiert, StringComparison.Ordinal);
        Assert.DoesNotContain("b7d2c5198c8d6d156992d86e0aa41b33", maskiert, StringComparison.Ordinal);

        // Was der Support braucht, bleibt: dass es überhaupt eine Challenge
        // gab, und von wem.
        Assert.Contains("Digest", maskiert, StringComparison.Ordinal);
        Assert.Contains("realm=\"pbx.example.test\"", maskiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Auch_die_Challenge_der_Anlage_verliert_ihre_Nonce()
    {
        const string zeile =
            """
            WWW-Authenticate: Digest realm="pbx.example.test",nonce="1789194193/248c6595d2560237bcbbe4249e14ce56",opaque="0ea81f134f4147f0",algorithm=MD5,qop="auth"
            """;

        var maskiert = LogMasking.SipLine(zeile);

        Assert.DoesNotContain("248c6595d2560237bcbbe4249e14ce56", maskiert, StringComparison.Ordinal);
        Assert.Contains("qop=\"auth\"", maskiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Anzeigename_verschwindet()
    {
        // Wer anruft, ist so aussagekräftig wie die Nummer — dieselbe Regel.
        const string zeile = """From: "Anna Muster" <sip:0791234567@pbx.example.test>;tag=bo3KcqanB""";

        var maskiert = LogMasking.SipLine(zeile);

        Assert.DoesNotContain("Anna Muster", maskiert, StringComparison.Ordinal);
        Assert.DoesNotContain("0791234567", maskiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Rufnummer_im_Benutzerteil_verschwindet()
    {
        const string zeile = "To: <sip:0791234567@pbx.example.test>;tag=38e14e70";

        var maskiert = LogMasking.SipLine(zeile);

        Assert.DoesNotContain("0791234567", maskiert, StringComparison.Ordinal);

        // Die Domäne bleibt: ohne sie liesse sich nicht mehr sagen, an welche
        // Anlage die Nachricht ging.
        Assert.Contains("pbx.example.test", maskiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Auch_eine_dreistellige_Nebenstelle_verschwindet()
    {
        // Die Lücke, die Line() offenliess: sie maskiert erst ab sieben
        // Stellen. In einer Anlage mit dreistelligen Nebenstellen ist «907»
        // aber genau die Aussage «wer hat mit wem» — und sie stand vierzigmal
        // am Tag im Protokoll.
        const string zeile =
            """[liblinphone] Looking up for an account matching Address("907" <sip:907@pbx.example.test>)""";

        var maskiert = LogMasking.SipLine(zeile);

        Assert.DoesNotContain("sip:907@", maskiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_eigene_Kennung_bleibt_lesbar()
    {
        // Die Gegenprobe, und sie ist wichtig: das eigene Konto ist keine
        // fremde Person, sondern die Nebenstelle dieses Arbeitsplatzes. Ohne
        // sie liesse sich bei mehreren Konten kein Anmeldeproblem mehr
        // zuordnen — dieselbe Abwägung wie in PrivacyLogTests, wo Identity
        // und Account bewusst nicht auf der Liste stehen.
        const string zeile = """From: "151bv2" <sip:151bv2@pbx.example.test>;tag=bo3KcqanB""";

        var maskiert = LogMasking.SipLine(zeile);

        Assert.Contains("151bv2@pbx.example.test", maskiert, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[belle-sip] No listening point matching for [udp://pbx.example.test:5060]")]
    [InlineData("[liblinphone] Account is moving from state [Progress] to [Ok]")]
    [InlineData("SIP/2.0 401 Unauthorized")]
    [InlineData("[mediastreamer] No playback card with id WASAPI: Default Capture")]
    public void Was_der_Support_braucht_bleibt_unveraendert(string zeile)
    {
        // Der eigentliche Prüfstein. Eine Maskierung, die den Zustandsverlauf,
        // die Antwortcodes und die Filterketten unlesbar macht, nimmt dem
        // Support genau das, wofür er Debug einschalten lässt — und dann
        // schaltet er es eben nicht mehr ein.
        Assert.Equal(zeile, LogMasking.SipLine(zeile));
    }

    [Fact]
    public void Eine_leere_Zeile_bleibt_leer()
    {
        Assert.Equal(string.Empty, LogMasking.SipLine(string.Empty));
    }
}
