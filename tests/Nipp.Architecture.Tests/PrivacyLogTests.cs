using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt, dass keine Rufnummer unmaskiert ins Protokoll geht — überall, nicht
/// nur in der Integrationsplattform.
///
/// <para><b>Der Anlass.</b> §21.2 verbietet Rufnummern im Protokoll, und
/// <see cref="IntegrationBoundaryTests"/> erzwang das für
/// <c>Services/Integrations/</c>. Der Telefonie-Kern schrieb in dieselbe Datei
/// „Anruf {Handle} an +41791234567 aufgebaut" — auf Stufe Information, also
/// immer. Das Diagnosepaket nimmt die Protokolle mit zum Support (§9.6), und
/// damit war die Zusage nicht gehalten. Die Grenze eines Verzeichnisses ist
/// keine Grenze für eine Datenschutzregel.</para>
///
/// <para><b>Was geprüft wird.</b> Nicht die Vorlage — dort <i>soll</i> ein
/// Platzhalter wie <c>{Destination}</c> stehen, sonst könnte man einen Anruf
/// gar nicht verfolgen. Geprüft wird die <b>Aufrufstelle</b>: wer einen solchen
/// Platzhalter füllt, muss <c>LogMasking</c> benutzen.</para>
///
/// <para>Quelltext-Scan, in §13 ausdrücklich zugelassen.</para>
/// </summary>
public sealed partial class PrivacyLogTests
{
    /// <summary>
    /// Argumentnamen, die eine Rufnummer tragen.
    ///
    /// <para><c>Identity</c> und <c>Account</c> stehen nicht dabei: eine
    /// SIP-Identität ist die eigene Nebenstelle, keine fremde Person, und ohne
    /// sie liesse sich bei mehreren Konten kein Registrierungsproblem mehr
    /// zuordnen.</para>
    ///
    /// <para><c>Path</c> steht ebenfalls nicht dabei, obwohl der Name eines
    /// Aufnahmepfads die Rufnummer enthält (§8.2). Der Grund ist praktisch:
    /// fast jeder Protokollaufruf in den Einstellungen und der Provisionierung
    /// gibt einen Dateipfad weiter, und keiner davon trägt eine Nummer. Ein
    /// Test, der zwanzig Fehlalarme liefert, wird abgeschaltet und schützt dann
    /// nichts. Die Aufnahmepfade sind einzeln maskiert; wer dort eine neue
    /// Meldung ergänzt, findet die Nachbarn als Vorbild.</para>
    /// </summary>
    private static readonly string[] SensitiveParameters =
        ["number", "destination", "remotenumber", "caller"];

    /// <summary>
    /// Methoden, deren Name schon sagt, dass sie maskieren — der Aufruf gilt
    /// damit als erledigt.
    /// </summary>
    private const string MaskingCall = "LogMasking.";

    [Fact]
    public void Keine_Rufnummer_geht_unmaskiert_in_eine_Protokollmeldung()
    {
        var violations = new List<string>();

        foreach (var file in RepositoryFiles.EnumerateSources(RepositoryFiles.SourceRoot))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (!RepositoryFiles.IsCode(line) || !LogCall().IsMatch(line))
                {
                    continue;
                }

                // Die Argumente stehen manchmal auf der Folgezeile.
                var statement = line;

                for (var j = i + 1; j < lines.Length && j <= i + 3; j++)
                {
                    if (statement.Contains(");", StringComparison.Ordinal))
                    {
                        break;
                    }

                    statement += " " + lines[j].Trim();
                }

                if (statement.Contains(MaskingCall, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var parameter in SensitiveParameters)
                {
                    // Ein Argument, das nach einer Nummer benannt ist, ohne dass
                    // maskiert wird.
                    if (Argument(statement, parameter))
                    {
                        violations.Add(
                            $"{RepositoryFiles.Relative(file)}:{i + 1} gibt '{parameter}' "
                                + "unmaskiert an eine Protokollmeldung weiter");
                        break;
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Rufnummern gehoeren nicht ins Protokoll (§21.2), und das Diagnosepaket nimmt die "
                + "Protokolle mit zum Support (§9.6). LogMasking.Number bzw. LogMasking.Path "
                + "an der Aufrufstelle benutzen — die Vorlage bleibt unveraendert."
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Ob ein Argument dieses Namens übergeben wird. Bewusst grob: der Test
    /// soll bei einem neuen Protokollaufruf anschlagen, nicht jede Schreibweise
    /// kennen.
    /// </summary>
    private static bool Argument(string statement, string parameter)
    {
        var index = statement.IndexOf('(', StringComparison.Ordinal);

        if (index < 0)
        {
            return false;
        }

        var arguments = statement[index..];

        return Regex.IsMatch(
            arguments,
            @"[\s,(\.]" + Regex.Escape(parameter) + @"\s*[,)]",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Ein Aufruf einer Protokollklasse dieses Projekts. Alle heissen
    /// <c>…Log</c>.
    /// </summary>
    [GeneratedRegex(@"\b[A-Za-z]*Log\.[A-Z][A-Za-z]*\(")]
    private static partial Regex LogCall();

    /// <summary>
    /// Der SDK-Trace läuft durch die Maskierung (ADR-022, Nachtrag vom
    /// 13.09.2026).
    ///
    /// <para><b>Der Befund.</b> <c>SdkLogBridge</c> reichte auf Stufe Debug
    /// jede SIP-Nachricht im Klartext weiter. Im Protokoll der
    /// Entwicklungsmaschine standen am 12.09.2026 <b>714 Zeilen mit
    /// Digest-Kopfzeilen</b> und <b>4 282 mit Rufnummern und
    /// Anzeigenamen</b> — und das Diagnosepaket nimmt die Protokolle mit
    /// zum Support.</para>
    ///
    /// <para>Diese Zeile ist die ganze Verdrahtung. Wer sie entfernt, hebt die
    /// Zusage auf, ohne dass irgendetwas anderes auffällt: der Build bleibt
    /// grün, die Oberfläche unverändert, und im Protokoll steht wieder
    /// alles.</para>
    /// </summary>
    [Fact]
    public void Der_SDK_Trace_wird_maskiert()
    {
        var datei = Path.Combine(
            RepositoryFiles.SourceRoot, "Nipp.Core", "Services", "Telephony", "SdkLogBridge.cs");

        var text = File.ReadAllText(datei);

        Assert.Contains("LogMasking.SipLine", text, StringComparison.Ordinal);

        // Und die Weitergabe geht durch sie hindurch, nicht daran vorbei.
        Assert.DoesNotContain(
            "SdkMessage(_logger, Map(level), domain ?? \"sdk\", text)",
            text,
            StringComparison.Ordinal);
    }
}
