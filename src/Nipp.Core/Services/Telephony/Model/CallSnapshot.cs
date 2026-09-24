namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Was das SDK über einen Anruf sagt — einmal gelesen, als eigener Wert
/// (W2.1, Etappe B1).
///
/// <para><b>Wofür es das gibt.</b> Die Zustandsmaschine der Anrufe mischte
/// vier Dinge in einer Methode: das SDK lesen, entscheiden, protokollieren
/// und melden. Prüfbar war davon nichts — jeder Zugriff auf
/// <c>call.CurrentParams</c> oder <c>call.Reason</c> braucht einen laufenden
/// Anruf an einer echten Anlage, und deshalb waren die Regeln, die dieses
/// Projekt am teuersten bezahlt hat, nur als Kommentar festgehalten.</para>
///
/// <para><b>Die Regel dahinter</b> (ADR-074): was das SDK <b>liest</b>, wird
/// an einer Stelle in eine eigene Momentaufnahme übersetzt — in
/// <c>SipEventBridge</c>, wo ohnehin <c>using Linphone</c> steht. Was daraus
/// <b>folgt</b>, entscheidet eine reine Klasse ohne SDK-Typ
/// (<see cref="CallFlow"/>). Was das SDK <b>tut</b>, bleibt in
/// <c>SipService</c> und wird weiterhin am Gerät geprüft.</para>
///
/// <para><b>Kein Typ aus dem SDK kommt hier vor</b>, auch nicht als Enum:
/// <see cref="RawState"/> trägt den Zustandsnamen als Zeichenkette, damit das
/// Protokoll ihn nennen kann, ohne dass die Grenze aus §6 aufweicht.</para>
/// </summary>
/// <param name="Number">
/// Die Gegenseite, wie sie im Signal steht — nie leer, notfalls «unbekannt».
/// </param>
/// <param name="DisplayName">
/// Was die Signalisierung an Namen hergab, sonst <c>null</c>. <b>Nicht</b> der
/// aufgelöste Name: den beantwortet <c>CallPartyResolver</c> (ADR-043).
/// </param>
/// <param name="Status">
/// Der übersetzte Zustand, oder <c>null</c> für einen Zwischenzustand ohne
/// eigene Aussage. <b>Null heisst «lass den bisherigen stehen»</b> und ist
/// kein Fehler — das SDK meldet mehr Zwischenschritte, als es Zustände gibt.
/// </param>
/// <param name="Message">Der Text des SDK zu diesem Zustand.</param>
/// <param name="IsIncomingNew">
/// Ob dieses Ereignis ein <b>eingehender</b> Anruf ist, wie ihn das SDK
/// ankündigt. Nur auf diesen Zustand hin darf ein unbekannter Anruf angelegt
/// werden — jedes andere Ereignis zu einem unbekannten Anruf gehört einem, den
/// nipp nie geführt hat.
/// </param>
/// <param name="EarlyMedia">
/// Ob die Gegenseite schon einen Audiostrom schickt, während es läutet (§9.4).
/// Daran hängt, wer den Rufton spielt: mit Early Media die Anlage, ohne das
/// SDK selbst.
/// </param>
/// <param name="Codec">Der ausgehandelte Codec, sonst <c>null</c>.</param>
/// <param name="Encryption">Die Medienverschlüsselung.</param>
/// <param name="EndReason">
/// Warum das Gespräch endete — nur gesetzt, wenn dieses Ereignis das Ende ist.
/// <b>Beim nächsten Ereignis ist der Anruf aus der Verwaltung</b> (§20.3);
/// wer den Grund später sucht, findet nichts mehr.
/// </param>
/// <param name="SdkAccountIdentity">
/// Das Konto, das das SDK am Anruf führt — als Adresse, unzugeordnet.
/// </param>
/// <param name="ToAddress">
/// Die gerufene Adresse. Zweiter Weg zum Konto, wenn das SDK keines nennt.
/// </param>
/// <param name="RawState">Der SDK-Zustandsname, für das Protokoll.</param>
public sealed record CallSnapshot(
    string Number,
    string? DisplayName,
    CallStatus? Status,
    string Message,
    bool IsIncomingNew,
    bool EarlyMedia,
    string? Codec,
    MediaEncryptionMode Encryption,
    CallEndReason? EndReason,
    string? SdkAccountIdentity,
    string? ToAddress,
    string RawState)
{
    /// <summary>
    /// Ob dieses Ereignis das Gespräch beendet. <b>Dass beides denselben Weg
    /// nimmt, ist Absicht:</b> für die Verwaltung ist ein gescheiterter Anruf
    /// derselbe Fall wie ein beendeter — er geht raus.
    /// </summary>
    public bool IsTerminal => Status is CallStatus.Ended or CallStatus.Failed;

    /// <summary>
    /// Ob dieses Ereignis ein Läuten ist — in beiden Richtungen, und damit
    /// die Frage, ob ein Rufton laufen darf.
    /// </summary>
    public bool IsRinging => Status is CallStatus.Ringing or CallStatus.Dialing;
}
