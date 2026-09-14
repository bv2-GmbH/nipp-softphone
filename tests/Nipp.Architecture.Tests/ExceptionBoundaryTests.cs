using System.Text.RegularExpressions;

namespace Nipp.Architecture.Tests;

/// <summary>
/// Erzwingt die Ausnahmegrenze aus ADR-053.
///
/// <para><b>Der Anlass.</b> Am 13.09.2026 gab es in nipp drei Wege, auf denen
/// eine Ausnahme den Prozess beendete, ohne dass irgendwo ein Fänger stand:
/// ein Abonnent im SDK-Callback, ein <c>LinphoneException</c> aus dem Wrapper,
/// und ein <c>async void</c>-Ereignisbehandler. Alle drei sind geschlossen —
/// und alle drei sind mit einer einzigen unachtsamen Zeile wieder offen.</para>
///
/// <para><b>Warum ein Quelltext-Scan.</b> Diese Regeln lassen sich zur Laufzeit
/// nicht prüfen: ein Fänger, den es nicht gibt, fällt erst auf, wenn er
/// gebraucht würde — beim Kunden, mitten im Gespräch. §13 lässt Quelltext-Scans
/// für genau solche Zusagen ausdrücklich zu.</para>
/// </summary>
public sealed partial class ExceptionBoundaryTests
{
    /// <summary>
    /// Jeder <c>async void</c> in der Fensterschicht fängt selbst.
    ///
    /// <para>Ein solcher Behandler hat keinen Aufrufer mehr, der fängt: die
    /// Ausnahme geht an <c>App.OnUnhandledException</c>, und das protokolliert
    /// nur — es setzt bewusst kein <c>Handled</c> (ADR-053). Wer dort ankommt,
    /// hat einen Fehler, den niemand vorhergesehen hat, und der soll nicht
    /// still weiterlaufen; also muss davor gefangen werden.</para>
    ///
    /// <para><b>Erlaubt sind zwei Formen:</b> ein eigener <c>try</c> im Rumpf,
    /// oder der Weg über <c>GuardAsync</c> beziehungsweise
    /// <c>CallbackGuard</c>. Beides endet an derselben Stelle — bei einer
    /// Protokollzeile und einer Meldung, die der Benutzer lesen kann.</para>
    /// </summary>
    [Fact]
    public void Kein_async_void_ohne_Faenger()
    {
        var verstoesse = new List<string>();

        foreach (var datei in RepositoryFiles.EnumerateSources(
            Path.Combine(RepositoryFiles.SourceRoot, "Nipp.App")))
        {
            var text = File.ReadAllText(datei);

            foreach (Match treffer in AsyncVoidSignatur().Matches(text))
            {
                var rumpf = RumpfAb(text, treffer.Index + treffer.Length);

                if (rumpf.Contains("catch", StringComparison.Ordinal)
                    || rumpf.Contains("GuardAsync", StringComparison.Ordinal)
                    || rumpf.Contains("CallbackGuard", StringComparison.Ordinal))
                {
                    continue;
                }

                verstoesse.Add($"{Path.GetFileName(datei)}: {treffer.Groups[1].Value}");
            }
        }

        Assert.True(
            verstoesse.Count == 0,
            "Diese async-void-Behandler fangen nicht und nehmen bei einer Ausnahme nipp mit "
                + "(ADR-053). Entweder ein eigener try/catch, oder der Aufruf ueber GuardAsync:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, verstoesse));
    }

    /// <summary>
    /// Jeder Callback, den die Bridge beim SDK anmeldet, läuft durch den
    /// Wächter.
    ///
    /// <para>Das ist die Stelle, an der eine Ausnahme in nativen Code
    /// zurückliefe — dort greift kein <c>try</c> dieses Prozesses mehr, auch
    /// der in <c>SipPumpHost.OnTick</c> nicht: er liegt ausserhalb der nativen
    /// Rahmen. Wer hier einen Callback direkt zuweist, nimmt den Schutz weg,
    /// und es fällt erst am Gerät auf.</para>
    /// </summary>
    [Fact]
    public void Jeder_SDK_Callback_laeuft_durch_den_Waechter()
    {
        var datei = Path.Combine(
            RepositoryFiles.SourceRoot, "Nipp.Core", "Services", "Telephony", "SipEventBridge.cs");

        var text = File.ReadAllText(datei);
        var ohneWaechter = new List<string>();

        foreach (Match treffer in ListenerZuweisung().Matches(text))
        {
            // Die Zuweisung und die nächsten Zeilen: der Wächter steht
            // entweder in derselben Zeile oder direkt darunter.
            var ab = treffer.Index;
            var bis = Math.Min(text.Length, ab + 260);
            var fenster = text[ab..bis];

            // Das Lösen in Dispose setzt auf null — dort gibt es nichts zu
            // schützen, und ohne diese Ausnahme meldete der Test acht
            // Verstösse, die keine waren.
            if (fenster.TrimStart()[..Math.Min(120, fenster.TrimStart().Length)]
                .Contains("= null", StringComparison.Ordinal))
            {
                continue;
            }

            if (!fenster.Contains("CallbackGuard.Run", StringComparison.Ordinal))
            {
                ohneWaechter.Add(treffer.Groups[1].Value);
            }
        }

        Assert.True(
            ohneWaechter.Count == 0,
            "Diese SDK-Callbacks sind ohne CallbackGuard.Run angemeldet (ADR-053). Eine "
                + "Ausnahme daraus laeuft in nativen Code zurueck und beendet den Prozess:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, ohneWaechter));
    }

    /// <summary>
    /// <c>LinphoneException</c> verlässt die Telefonieschicht nicht.
    ///
    /// <para>Sie ist ein SDK-Typ, und ViewModels kennen keine SDK-Typen — das
    /// erzwingt bereits <see cref="SdkBoundaryTests"/> über <c>using
    /// Linphone</c>. Diese Zusage ist die andere Hälfte: <c>SipService</c>
    /// übersetzt sie in eine <c>InvalidOperationException</c> mit einem Satz,
    /// den der Benutzer lesen kann, <b>ohne</b> sie als innere Ausnahme
    /// mitzugeben. Die Kette wäre sonst ein Weg, auf dem der Typ doch
    /// hinauswandert.</para>
    /// </summary>
    [Fact]
    public void LinphoneException_wird_in_der_Telefonieschicht_gefangen()
    {
        var telefonie = Path.Combine(
            RepositoryFiles.SourceRoot, "Nipp.Core", "Services", "Telephony");

        var gefangen = RepositoryFiles.EnumerateSources(telefonie)
            .Any(d => File.ReadAllText(d).Contains("catch (LinphoneException", StringComparison.Ordinal));

        Assert.True(
            gefangen,
            "Kein einziges 'catch (LinphoneException' in Services/Telephony. Der Wrapper wirft "
                + "bei Pause, Resume, SendDtmf und Terminate; bis zum 13.09.2026 fing das "
                + "niemand, und ein Klick auf «Halten» im falschen Moment nahm nipp mit "
                + "(ADR-053).");
    }

    /// <summary>
    /// <c>private async void Name(</c> — die Signatur eines Ereignisbehandlers.
    /// </summary>
    [GeneratedRegex(@"private async void (\w+)\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex AsyncVoidSignatur();

    /// <summary>
    /// <c>listener.OnXxx =</c> — eine Anmeldung beim SDK.
    /// </summary>
    [GeneratedRegex(@"listener\.(On\w+)\s*=", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ListenerZuweisung();

    /// <summary>
    /// Der Rumpf einer Methode ab einer Stelle — bis zur schliessenden
    /// Klammer, oder bis zum Semikolon bei einem Ausdrucksrumpf.
    ///
    /// <para>Bewusst einfach gehalten: gezählt werden geschweifte Klammern.
    /// Das genügt für Ereignisbehandler und kostet keinen Parser.</para>
    /// </summary>
    private static string RumpfAb(string text, int start)
    {
        var tiefe = 0;
        var begonnen = false;

        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    tiefe++;
                    begonnen = true;
                    break;

                case '}':
                    tiefe--;
                    if (begonnen && tiefe == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;

                case ';' when !begonnen:
                    // Ausdrucksrumpf: private async void X(...) => Y;
                    return text[start..(i + 1)];

                default:
                    break;
            }
        }

        return text[start..];
    }
}
