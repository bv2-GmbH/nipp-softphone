using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Die Grenze zwischen dem SDK und allem, was daran hängt (ADR-053).
///
/// <para><b>Warum es diese Klasse gibt.</b> Die Callbacks des SDK kommen aus
/// <c>linphone_core_iterate</c> — also aus nativem Code, über einen
/// Reverse-P/Invoke-Rahmen. Eine Ausnahme, die aus einem solchen Delegaten
/// austritt, unterläuft die .NET-Ausnahmebehandlung: der <c>try</c> in
/// <c>SipPumpHost.OnTick</c> liegt <b>ausserhalb</b> der nativen Rahmen und
/// sieht sie nie. Was dabei herauskommt, ist im besten Fall ein beendeter
/// Prozess und im schlechteren ein Core in einem Zustand, den niemand
/// beschreiben kann.</para>
///
/// <para><b>Und es ist kein hypothetischer Fall.</b> An
/// <c>CallStateChanged</c> hängen acht Abonnenten, darunter die
/// Seitennavigation des Hauptfensters und der Schreibzugriff auf die
/// Anrufliste. Eine <c>XamlParseException</c> aus dem Konstruktor der
/// Gesprächsansicht hat nipp schon einmal beim Klingeln mitgenommen — die
/// Lehre steht in CLAUDE.md unter „Ein Ressourcenwörterbuch gilt nur für den
/// Teilbaum darunter".</para>
///
/// <para><b>Die Regel.</b> Ein Fehler in einem Abonnenten ist der Fehler
/// dieses Abonnenten, nicht des Telefons. Er wird hier gefangen,
/// protokolliert und geht nicht weiter. <b>Nie still</b> — eine verschluckte
/// Ausnahme ohne Protokollzeile ist genau die Lücke, die dieses Projekt
/// zweimal teuer bezahlt hat („die Taste tut nichts" war nicht von „hier kommt
/// gar nichts an" zu unterscheiden). Deshalb steht jede gefangene Ausnahme auf
/// Stufe <c>Warning</c> im Protokoll, mit Callback-Namen und Anrufkennung.</para>
///
/// <para><b>Was hier nicht hingehört.</b> Der Wächter fängt für die Dauer
/// eines Callbacks. Er ist kein Ersatz dafür, dass ein Abonnent seine eigenen
/// erwartbaren Fehler behandelt — wer weiss, dass eine Datei fehlen kann, fängt
/// das selbst und sagt es dem Benutzer. Hier landet, was niemand vorhergesehen
/// hat.</para>
/// </summary>
/// <remarks>
/// <b>Öffentlich, nicht intern.</b> Die Zusage dieser Klasse ist die
/// wichtigste des ganzen Schrittes, und sie gehört geprüft; ohne
/// <c>InternalsVisibleTo</c> geht das nur so. Ein SDK-Typ steht in keiner
/// Signatur, die Schichtgrenze bleibt also unberührt.
/// </remarks>
public static class CallbackGuard
{
    /// <summary>
    /// Führt <paramref name="action"/> aus und lässt keine Ausnahme in den
    /// nativen Rahmen zurück.
    /// </summary>
    /// <param name="logger">Für die Protokollzeile im Fehlerfall.</param>
    /// <param name="callback">
    /// Name des SDK-Callbacks, etwa <c>OnCallStateChanged</c>. Steht in der
    /// Protokollzeile und ist das Einzige, was später sagt, wo es geknallt hat.
    /// </param>
    /// <param name="action">Was zu tun ist.</param>
    /// <returns>
    /// <c>true</c>, wenn es durchlief. Der Rückgabewert ist für Tests und für
    /// Aufrufer, die nach einem Fehlschlag etwas anderes tun wollen; die
    /// Callbacks selbst ignorieren ihn.
    /// </returns>
    public static bool Run(ILogger logger, string callback, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            TelephonyLog.CallbackFailed(logger, callback, ex.GetType().Name, Describe(ex));
            return false;
        }
    }

    /// <summary>
    /// Der Text einer Ausnahme, wie er ins Protokoll darf — maskiert (§21.2,
    /// ADR-022).
    ///
    /// <para><b>Warum maskiert.</b> Eine Ausnahmemeldung trägt oft genau das,
    /// was nicht ins Protokoll gehört: ein fehlgeschlagener Abruf nennt die
    /// angefragte Adresse samt Rufnummer, ein Datenbankfehler die Zeile, die
    /// er nicht schreiben konnte. Wer eine fremde Meldung ungeprüft weitergibt,
    /// gibt weiter, was darin steht.</para>
    ///
    /// <para>Reine Funktion, damit sie ohne Protokoll prüfbar ist.</para>
    /// </summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception.Message;

        return string.IsNullOrWhiteSpace(message)
            ? "(ohne Meldung)"
            : LogMasking.Line(message);
    }
}
