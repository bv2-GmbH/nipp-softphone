using System.Text.RegularExpressions;

namespace Nipp.Core.Diagnostics;

/// <summary>
/// Maskiert Rufnummern für das Protokoll.
///
/// Der Anlass: das Protokoll geht als Diagnosepaket an den Support (§9.6), und
/// §21.2 verbietet Rufnummern dort ausdrücklich — bisher galt die Regel nur für
/// die Integrationen und wurde dort von einem Architekturtest erzwungen. Der
/// Telefonie-Kern schrieb in dieselbe Datei „Anruf an +41791234567". Beides
/// zusammen hätte die Zusage nicht gehalten.
///
/// <para>Der Massstab ist Diagnose gegen Datenschutz: die letzten drei Ziffern
/// bleiben stehen. Damit lässt sich in einem Protokoll noch erkennen, ob zwei
/// Meldungen denselben Anruf betreffen, ohne dass die Nummer wählbar wäre. Wer
/// einen Anruf vollständig verfolgen will, nimmt das Handle — es steht überall
/// daneben und ist eindeutig.</para>
///
/// <para><b>Interne Nebenstellen bleiben lesbar.</b> Bis vier Ziffern ist eine
/// Nummer nach derselben Regel intern, nach der <c>PhoneNumberKey</c> sie nicht
/// an fremde Systeme gibt. „151" ist keine Person, sondern ein Apparat im
/// eigenen Haus, und ohne diese Ausnahme wäre jedes Protokoll über die eigene
/// Anlage unbrauchbar.</para>
/// </summary>
public static partial class LogMasking
{
    /// <summary>Wie viele Ziffern am Ende stehen bleiben.</summary>
    private const int VisibleDigits = 3;

    /// <summary>
    /// Bis hierher gilt eine Nummer als interne Nebenstelle und bleibt lesbar —
    /// dieselbe Grenze wie in <c>PhoneNumberKey</c>.
    /// </summary>
    private const int InternalMaxDigits = 4;

    /// <summary>
    /// Maskiert eine einzelne Rufnummer: <c>+41791234567</c> wird zu
    /// <c>…567</c>, <c>151</c> bleibt <c>151</c>.
    /// </summary>
    public static string Number(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(leer)";
        }

        var digits = 0;

        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                digits++;
            }
        }

        if (digits == 0)
        {
            // Eine SIP-Adresse ohne Ziffern („sip:info@example.ch") ist keine
            // Rufnummer, aber trotzdem eine Person. Nur das Schema bleibt.
            var at = value.IndexOf('@', StringComparison.Ordinal);
            return at > 0 ? "…@" + value[(at + 1)..] : "…";
        }

        if (digits <= InternalMaxDigits)
        {
            return value;
        }

        var tail = new char[VisibleDigits];
        var found = 0;

        for (var i = value.Length - 1; i >= 0 && found < VisibleDigits; i--)
        {
            if (char.IsDigit(value[i]))
            {
                tail[VisibleDigits - 1 - found] = value[i];
                found++;
            }
        }

        return "…" + new string(tail, VisibleDigits - found, found);
    }

    /// <summary>
    /// Maskiert die Rufnummer in einem Aufnahmepfad. Das Namensschema ist
    /// <c>yyyy-MM-dd_HHmmss_&lt;Nummer&gt;.wav</c> (§8.2) — der Ordner und die
    /// Zeit bleiben, damit die Datei auffindbar bleibt.
    /// </summary>
    public static string Path(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(leer)";
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(value);
        var underscore = name.LastIndexOf('_');

        if (underscore <= 0 || underscore == name.Length - 1)
        {
            return value;
        }

        var number = name[(underscore + 1)..];

        if (!number.Any(char.IsDigit))
        {
            return value;
        }

        var directory = System.IO.Path.GetDirectoryName(value) ?? string.Empty;
        var masked = name[..(underscore + 1)] + Number(number) + System.IO.Path.GetExtension(value);

        return directory.Length > 0 ? System.IO.Path.Combine(directory, masked) : masked;
    }

    /// <summary>
    /// Maskiert eine Zeile aus dem SDK-Trace (ADR-022, Nachtrag vom
    /// 13.09.2026).
    ///
    /// <para><b>Der Befund dahinter.</b> Auf Stufe Debug reicht
    /// <c>SdkLogBridge</c> die SIP-Nachrichten im Klartext weiter. Im Protokoll
    /// dieser Entwicklungsmaschine standen am 12.09.2026 <b>714 Zeilen mit
    /// Digest-Kopfzeilen</b> und <b>4 282 mit Rufnummern und Anzeigenamen</b>.
    /// Das Diagnosepaket nimmt die Protokolle mit zum Support — die Zusage aus
    /// §21.2 war also genau dann nicht gehalten, wenn sie am meisten
    /// bedeutete.</para>
    ///
    /// <para><b>Drei Schritte, in dieser Reihenfolge:</b></para>
    /// <list type="number">
    ///   <item><b>Zugangsdaten.</b> Bei <c>Authorization</c>,
    ///   <c>WWW-Authenticate</c> und ihren Proxy-Geschwistern verschwinden
    ///   <c>nonce</c>, <c>cnonce</c> und <c>response</c>. <c>realm</c> und das
    ///   Schema bleiben: «hat die Anlage überhaupt eine Challenge geschickt»
    ///   ist oft die ganze Frage, und mit dem, was bleibt, kann sich
    ///   niemand anmelden.</item>
    ///   <item><b>Anzeigenamen.</b> <c>"Anna Muster" &lt;sip:…</c> wird zu
    ///   <c>"…" &lt;sip:…</c>. Wer anruft, ist so aussagekräftig wie die
    ///   Nummer.</item>
    ///   <item><b>Der Benutzerteil von <c>sip:</c>, <c>sips:</c> und
    ///   <c>tel:</c></b> — sobald er Ziffern trägt, läuft er durch
    ///   <see cref="Number"/>. <b>Auch dreistellig</b>, anders als in
    ///   <see cref="Line"/>: in einer Anlage mit dreistelligen Nebenstellen
    ///   ist «907» genau die Aussage «wer hat mit wem». Die Domäne bleibt —
    ///   ohne sie liesse sich nicht mehr sagen, an welche Anlage die Nachricht
    ///   ging.</item>
    /// </list>
    ///
    /// <para><b>Was bewusst bleibt:</b> Zustandsverläufe, Antwortcodes,
    /// Filterketten, die eigene Kontokennung. Eine Maskierung, die dem Support
    /// nimmt, wofür er Debug einschalten lässt, wird abgeschaltet und schützt
    /// dann gar nichts.</para>
    /// </summary>
    public static string SipLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        try
        {
            var text = AuthSecrets().Replace(line, @"$1=""…""");
            text = DisplayName().Replace(text, @"""…"" <$1:");
            text = SipUser().Replace(text, m => m.Groups[1].Value + ":"
                + MaskSipUser(m.Groups[2].Value) + "@");

            return Line(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // Lieber eine unbrauchbare Zeile als eine ungefilterte.
            return "(Zeile beim Maskieren verworfen)";
        }
    }

    /// <summary><c>nonce</c>, <c>cnonce</c> und <c>response</c> einer Digest-Anmeldung.</summary>
    [GeneratedRegex(
        @"\b(nonce|cnonce|response)\s*=\s*""[^""]*""",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex AuthSecrets();

    /// <summary>Der Anzeigename vor einer SIP-Adresse.</summary>
    [GeneratedRegex(
        @"""[^""]*""\s*<(sips?|tel):",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DisplayName();

    /// <summary>
    /// Der Benutzerteil einer Adresse im Trace.
    ///
    /// <para><b>Strenger als <see cref="Number"/>, und das ist Absicht.</b>
    /// Dort bleibt alles bis vier Ziffern lesbar, weil eine interne
    /// Nebenstelle in einer eigenen Protokollzeile die Zuordnung erst möglich
    /// macht. Im SIP-Trace ist eine <b>nackte Ziffernfolge im Benutzerteil</b>
    /// aber immer eine Gesprächspartei — «907» in einem <c>From:</c> ist die
    /// Aussage «wer hat mit wem», und genau die soll nicht mitlaufen
    /// (§21.2).</para>
    ///
    /// <para><b>Ein Name bleibt.</b> <c>151bv2</c> ist eine Kontokennung und
    /// keine Rufnummer: ohne sie liesse sich bei mehreren Konten kein
    /// Anmeldeproblem mehr zuordnen. Dieselbe Abwägung wie in
    /// <c>PrivacyLogTests</c>, wo <c>Identity</c> und <c>Account</c> bewusst
    /// nicht auf der Liste stehen.</para>
    /// </summary>
    private static string MaskSipUser(string user)
    {
        var nurZiffern = true;

        foreach (var c in user)
        {
            if (!char.IsAsciiDigit(c) && c != '+')
            {
                nurZiffern = false;
                break;
            }
        }

        if (!nurZiffern)
        {
            return user;
        }

        var ziffern = user.Where(char.IsAsciiDigit).ToArray();

        return ziffern.Length <= VisibleDigits
            ? "…"
            : "…" + new string(ziffern[^VisibleDigits..]);
    }

    /// <summary>
    /// Der Benutzerteil einer SIP- oder tel-Adresse, sofern er mit einer Ziffer
    /// beginnt. Ein rein alphabetischer Benutzerteil ist ein Name und wird
    /// nicht angefasst — <see cref="MaskSipUser"/> erklärt, warum.
    /// </summary>
    [GeneratedRegex(
        @"\b(sips?|tel):(\+?[0-9][0-9A-Za-z._-]*)@",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SipUser();

    /// <summary>
    /// Maskiert jede Ziffernfolge in einer Protokollzeile, die lang genug für
    /// eine Rufnummer ist — für das Diagnosepaket, das fremde Zeilen mitnimmt
    /// (SDK-Meldungen, SIP-Trace) und sie nicht einzeln kennt.
    ///
    /// <para>Bewusst grob: eine RTP-Zeitmarke wird mitmaskiert. Eine Zeitmarke
    /// im Support-Ticket ist verzichtbar, eine Kundennummer nicht. Zeitstempel
    /// und Adressen bleiben erhalten, weil ihre Ziffern durch Trennzeichen
    /// unterbrochen sind.</para>
    /// </summary>
    public static string Line(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        try
        {
            // Aufnahmedateien zuerst: ihr Name traegt die Rufnummer (§8.2,
            // Schema yyyy-MM-dd_HHmmss_<Nummer>.wav), und die Ziffern stehen
            // unmittelbar vor einem Punkt — genau dort greift LongDigitRuns
            // absichtlich nicht, damit Zeitstempel und Adressen lesbar
            // bleiben. Diese eine Form ist bekannt, also wird sie benannt
            // behandelt (W1.3).
            var text = RecordingFileNames().Replace(line, m => "_" + Number(m.Groups[1].Value) + ".wav");

            return LongDigitRuns().Replace(text, m => Number(m.Value));
        }
        catch (RegexMatchTimeoutException)
        {
            // Lieber eine unbrauchbare Zeile als eine ungefilterte.
            return "(Zeile beim Maskieren verworfen)";
        }
    }

    /// <summary>
    /// Sieben bis fünfzehn Ziffern am Stück, mit optionalem Plus davor und
    /// ohne angrenzende Ziffer, Punkt, Doppelpunkt oder Bindestrich — damit
    /// bleiben „2026-09-06", „18:38:46.020" und „192.168.1.20" unberührt.
    /// </summary>
    /// <summary>
    /// Der Rufnummernteil eines Aufnahmedateinamens (§8.2).
    /// </summary>
    [GeneratedRegex(@"_(\+?\d{5,15})\.wav", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordingFileNames();

    [GeneratedRegex(@"(?<![\d.:\-])\+?\d{7,15}(?![\d.:\-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LongDigitRuns();
}
