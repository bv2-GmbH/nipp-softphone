using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Die Nebenstellen des eigenen Teams (§8.4).
///
/// Sie kommen aus den Einstellungen oder aus der Provisionierung (§17) und
/// stehen damit sofort zur Verfügung — kein COM, kein Netz, kein Warten.
/// Nur diese Kontakte werden für das Besetztlampenfeld abonniert (§14.8).
/// </summary>
public sealed class TeamContactSource : IContactSource
{
    private readonly SettingsService _settings;

    public TeamContactSource(SettingsService settings) => _settings = settings;

    public ContactSourceKind Kind => ContactSourceKind.Team;

    public bool IsAvailable => true;

    /// <summary>
    /// Die Kennung einer Nebenstelle.
    ///
    /// <para>Steht als eigene Funktion da und nicht als Zeichenkette mitten im
    /// Laden, weil das Umsortieren in der Kontaktliste dieselbe Formel braucht:
    /// es bildet die Zeilen wieder auf die Einträge der Einstellungen ab. Zwei
    /// Stellen, und die Zuordnung bricht still.</para>
    ///
    /// <para><b>Die Zahl ist nicht der Platz in der Liste, sondern der Zähler
    /// gleicher Kurzwahlen</b> — und das ist der Unterschied zwischen einer
    /// Kennung und einer Positionsangabe (ADR-042).</para>
    ///
    /// <para><b>Was mit dem Index schiefging.</b> Bis zum 12.09.2026 stand hier
    /// die Position. Nach dem ersten Umsortieren hatte sich die gespeicherte
    /// Liste verschoben, die Zeilen im Speicher trugen aber weiter ihre alten
    /// Kennungen — beim <b>zweiten</b> Ziehvorgang schlugen in
    /// <see cref="TeamOrder.Apply"/> deshalb <i>alle</i> Zuordnungen fehl, das
    /// Ergebnis war unverändert, und die Prüfung auf „nichts geändert" traf zu:
    /// <b>kein Speichern, kein Protokolleintrag</b>, während die Liste die neue
    /// Ordnung zeigte. Beim nächsten Start sprang sie zurück.</para>
    ///
    /// <para><b>Warum der Zähler reicht.</b> Zwei Einträge ergeben dieselbe
    /// Zeichenkette nur bei gleicher Kurzwahl <i>und</i> gleichem Zähler — was
    /// der Zähler ausschliesst. Zwei Nebenstellen dürfen also weiterhin gleich
    /// heissen und dieselbe Nummer haben; unterscheidbar bleiben sie trotzdem,
    /// und zwar über ein Merkmal, das ein Umsortieren übersteht.</para>
    /// </summary>
    public static string IdOf(TeamExtension member, int gleicheKurzwahl)
    {
        ArgumentNullException.ThrowIfNull(member);

        return $"team:{gleicheKurzwahl}:{member.Extension}";
    }

    /// <summary>
    /// Die Kennungen einer ganzen Liste, in ihrer Reihenfolge.
    ///
    /// <b>Der einzige Ort, an dem der Zähler entsteht.</b> Wer ihn selbst
    /// nachrechnet, hat die zweite Stelle mit derselben Formel — genau davor
    /// warnt der Kommentar an <see cref="IdOf"/>, und genau das ist am
    /// 12.09.2026 passiert.
    /// </summary>
    public static IReadOnlyList<string> IdsOf(IReadOnlyList<TeamExtension> team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var gesehen = new Dictionary<string, int>(team.Count, StringComparer.OrdinalIgnoreCase);
        var kennungen = new List<string>(team.Count);

        foreach (var member in team)
        {
            var kurzwahl = member.Extension ?? string.Empty;

            gesehen.TryGetValue(kurzwahl, out var bisher);
            gesehen[kurzwahl] = bisher + 1;

            kennungen.Add(IdOf(member, bisher));
        }

        return kennungen;
    }

    /// <summary>
    /// Baut die Kontakte aus Nebenstellen und Gruppen.
    ///
    /// <b>Öffentlich, weil es zwei Aufrufer gibt:</b> das Laden hier und das
    /// Nachziehen des Zwischenspeichers nach einem Umsortieren
    /// (<c>ContactStore.ReplaceTeam</c>). Beide müssen dieselben Kennungen und
    /// dieselben Gruppen ergeben — sonst zeigt die Liste etwas anderes, als
    /// gespeichert ist.
    /// </summary>
    public static IReadOnlyList<Contact> Build(
        IReadOnlyList<TeamExtension> team,
        IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(team);

        var kennungen = IdsOf(team);
        var contacts = new List<Contact>(team.Count);

        for (var i = 0; i < team.Count; i++)
        {
            var member = team[i];

            contacts.Add(new Contact(
                Id: kennungen[i],
                DisplayName: member.DisplayName,
                Numbers: NumbersOf(member),
                Source: ContactSourceKind.Team,
                SipAddress: member.SipAddress,
                Company: null,
                Group: TeamGroups.NameOf(member, groups)));
        }

        return contacts;
    }

    public Task<IReadOnlyList<Contact>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var contacts = Build(
            _settings.Current.Contacts.Team,
            _settings.Current.Contacts.Groups);

        return Task.FromResult(contacts);
    }

    /// <summary>
    /// Die wählbaren Nummern einer Nebenstelle: die Kurzwahl, und die
    /// Handynummer <b>nur wenn sie gefüllt ist</b>.
    ///
    /// <para><c>ClipResolver</c> und <c>ContactStore.Search</c> laufen roh über
    /// <c>Numbers</c>; eine leere <see cref="ContactNumber"/> wäre dort toter
    /// Ballast — und beim Auflösen eines eingehenden Anrufs eine Nummer, die
    /// auf alles passt, was auch leer ist.</para>
    ///
    /// <para><b>Die Reihenfolge ist die Wichtigkeit.</b> Die Nebenstelle
    /// bleibt vorn und damit <c>PrimaryNumber</c>: ein Klick auf den Kollegen
    /// wählt weiterhin intern, nicht aufs Handy.</para>
    /// </summary>
    private static IReadOnlyList<ContactNumber> NumbersOf(TeamExtension member)
    {
        if (string.IsNullOrWhiteSpace(member.Mobile))
        {
            return [new ContactNumber(member.Extension, ContactNumberKind.Business)];
        }

        return
        [
            new ContactNumber(member.Extension, ContactNumberKind.Business),
            new ContactNumber(member.Mobile.Trim(), ContactNumberKind.Mobile),
        ];
    }
}
