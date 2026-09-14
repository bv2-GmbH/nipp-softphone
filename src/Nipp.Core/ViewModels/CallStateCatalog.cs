using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Wie ein Anrufzustand heisst — genau eine Stelle (ADR-044).
///
/// <para><b>Der Befund.</b> Dieselbe Aufzählung stand dreimal im Code, jedes
/// Mal mit eigenem Wortlaut: die Hauptansicht sagte «eingehender Anruf», die
/// Gesprächsansicht «Anruf wartet»; die eine «von der Gegenstelle gehalten»,
/// die andere «von der Gegenseite gehalten». Kein Benutzer sieht die drei
/// nebeneinander, aber jeder sieht sie nacheinander — und drei Kopien einer
/// Aufzählung sind drei Gelegenheiten, sie falsch zu haben, und eine
/// Gelegenheit, nur eine davon zu korrigieren. Dasselbe Muster hat ADR-032
/// beim Feldkatalog und ADR-043 beim Namen des Gesprächspartners aufgelöst.</para>
///
/// <para><b>Zwei Fragen, nicht eine</b> — wie beim <c>CallPartyResolver</c>:
/// <see cref="Of"/> gibt den Zustand als Wort und antwortet immer.
/// <see cref="Caption"/> ist die Kopfzeile der Gesprächsansicht und darf leer
/// bleiben; nur dort trägt der Satz «Gespräch läuft weiter», der erklärt, dass
/// ein Gespräch den Weg zurück zur Wähltastatur überlebt.</para>
///
/// <para><b>«Gegenseite», nicht «Gegenstelle».</b> Die sichtbaren Texte in
/// XAML sagen bereits «die Gegenseite muss davon wissen»; das ist das
/// geläufigere Wort, und zwei Wörter für dieselbe Person sind eines zu viel.</para>
/// </summary>
public static class CallStateCatalog
{
    /// <summary>Der Zustand als Wort. Kleingeschrieben — er steht in Sätzen.</summary>
    public static string Of(CallStatus status) => status switch
    {
        CallStatus.Dialing => "wird aufgebaut",
        CallStatus.Ringing => "klingelt",
        CallStatus.Incoming => "eingehender Anruf",
        CallStatus.Connected => "verbunden",
        CallStatus.OnHold => "gehalten",
        CallStatus.RemoteOnHold => "von der Gegenseite gehalten",
        CallStatus.Ended => "beendet",
        CallStatus.Failed => "fehlgeschlagen",
        _ => "unbekannt",
    };

    /// <summary>
    /// Die Kopfzeile der Gesprächsansicht.
    ///
    /// <para>Leer, wenn es nichts zu sagen gibt: ohne Anruf und nach seinem
    /// Ende. Bei einem verbundenen Gespräch der Satz, der den Weg zurück
    /// erklärt — in jedem anderen Zustand wäre er falsch, und das war er dort
    /// eine Zeit lang auch.</para>
    /// </summary>
    public static string Caption(CallStatus? status) => status switch
    {
        null => string.Empty,
        CallStatus.Ended or CallStatus.Failed => string.Empty,
        CallStatus.Connected => "Gespräch läuft weiter",
        var s => Of(s.Value),
    };
}
