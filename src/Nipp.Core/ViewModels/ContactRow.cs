using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Zeile in der Kontaktliste (§8.4, §20.1).
///
/// Warum ein eigener Typ und nicht <see cref="Contact"/> direkt: der Kontakt
/// ist unveränderlich, seine Präsenz ändert sich aber im Sekundentakt. Würde
/// die Liste bei jeder Präsenzmeldung neue Kontakte bekommen, verlöre sie
/// Auswahl und Bildlaufposition — dieselbe Falle, die in der Abnahme schon bei
/// den Gesprächen zugeschlagen hat.
/// </summary>
public sealed partial class ContactRow : ObservableObject
{
    [ObservableProperty]
    private PresenceStatus _presence;

    /// <summary>
    /// Ob die Zeile ihre Angaben <b>unter sich</b> aufgeklappt zeigt
    /// (ADR-048).
    ///
    /// <para><b>Gesetzt wird das ausschliesslich von
    /// <see cref="ShellViewModel.ToggleContactDetails"/>.</b> Eine Zeile, die
    /// sich selbst aufklappte, wüsste nichts von der vorher offenen — und die
    /// Regel «es ist höchstens eine offen» stünde an so vielen Stellen, wie es
    /// Listen gibt. Sie steht an einer.</para>
    ///
    /// <para><b>Der Bereich stand von ADR-046 bis zum 13.09.2026 unter der
    /// Liste</b>, für alle drei Listen gemeinsam. Der Ort war nicht der
    /// Fehler, den ADR-046 behoben hat — der Fehler war, dass Team-Zeilen ihn
    /// in der Zeile aufklappten und Outlook-Zeilen darunter. Jetzt klappt er
    /// in <b>jeder</b> Zeile auf, und die Gleichheit bleibt.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isDetailExpanded;

    /// <summary>
    /// Ob genau diese Zeile gerade gezogen wird (ADR-066).
    ///
    /// <para><b>An der Zeile und nicht am Container.</b> Die Listen
    /// virtualisieren: ein Container wird beim Scrollen einer anderen Zeile
    /// zugeteilt. Wer die Deckkraft dort setzt, hat sie irgendwann an der
    /// falschen — genau der Fehler, der in der Fassung vor ADR-066 als
    /// <c>Einfassen</c> im Code-behind stand.</para>
    /// </summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>
    /// Die Deckkraft der Zeile — <b>gedämpft, solange sie gezogen wird</b>.
    ///
    /// <para>Als fertiger Wert und nicht als Wandler: eine Bindung mit
    /// Konverter bräuchte für diesen einen Fall einen eigenen Typ in den
    /// Ressourcen beider Vorlagen.</para>
    /// </summary>
    public double DragOpacity => IsDragging ? 0.45 : 1.0;

    partial void OnIsDraggingChanged(bool value) => OnPropertyChanged(nameof(DragOpacity));

    public ContactRow(Contact contact, PresenceStatus presence)
    {
        Contact = contact;
        _presence = presence;

        Choices =
        [
            .. contact.Numbers
                .Where(static n => !string.IsNullOrWhiteSpace(n.Number))
                .Select(static n => new ContactNumberChoice(
                    n.Number,
                    PhoneNumberFormat.ForDisplay(n.Number),
                    KindLabel(n.Kind))),
        ];
    }

    public Contact Contact { get; }

    public string DisplayName => Contact.DisplayName;

    public string Initials => Contact.Initials;

    /// <summary>
    /// Was ein Bildschirmleser vorliest.
    ///
    /// <para>Ohne diese Angabe liest er den Klassennamen —
    /// «Nipp.Core.ViewModels.ContactRow» stand im UI-Automation-Baum an jeder
    /// Zeile, weil eine ListView ohne <c>AutomationProperties.Name</c> auf
    /// <c>ToString()</c> des gebundenen Objekts zurueckfaellt. Eine Kontaktliste,
    /// die zehnmal ihren eigenen Typnamen sagt, ist unbenutzbar.</para>
    ///
    /// <para>Name und zweite Zeile zusammen, mit Komma: der Vorleser macht
    /// daran eine Pause, und die Nummer ist bei einem Telefon der Grund, warum
    /// jemand die Zeile hoert.</para>
    /// </summary>
    /// <para><b>Und die Präsenz gehört dazu</b> (ADR-046). Sie steht sichtbar
    /// in derselben Zeile, aber der Name auf dem umschliessenden Raster gilt
    /// für die ganze Zeile — was sonst noch darin steht, wird beim Durchgehen
    /// der Liste nicht mehr vorgelesen. §8.4 verlangt Farbe <b>und</b> Text;
    /// sichtbar war beides da, für die Sprachausgabe galt es nicht.</para>
    public string AccessibleName
    {
        get
        {
            var teile = SubLabel.Length > 0
                ? $"{DisplayName}, {SubLabel}"
                : DisplayName;

            return HasPresence ? $"{teile}, {PresenceText}" : teile;
        }
    }

    /// <summary>
    /// Die zweite Zeile: <b>immer die Nummer</b>, formatiert, dahinter die
    /// Firma, wenn es eine gibt.
    ///
    /// Vorher stand dort mal die Firma und mal die Nummer, je nachdem, was
    /// Outlook lieferte — in einer Liste sah das aus wie zwei verschiedene
    /// Arten von Eintrag. Für ein Telefon ist die Nummer die Information; die
    /// Firma hilft nur, wenn zwei Leute gleich heissen.
    /// </summary>
    public string SubLabel
    {
        get
        {
            var number = PhoneNumberFormat.ForDisplay(Contact.PrimaryNumber);

            var text = Contact.Company is { Length: > 0 } company
                ? number.Length > 0 ? $"{number} · {company}" : company
                : number;

            // Der Hinweis steht hinten, nicht zwischen Nummer und Firma: er
            // ist die unwichtigste Angabe der Zeile und darf als erstes
            // abgeschnitten werden, wenn der Platz nicht reicht.
            return MoreNumbersHint.Length > 0 && text.Length > 0
                ? $"{text} · {MoreNumbersHint}"
                : text;
        }
    }

    public string? Number => Contact.PrimaryNumber;

    /// <summary>
    /// Alle wählbaren Nummern des Kontakts, mit deutscher Benennung ihrer Art.
    ///
    /// <b>Warum es das gibt.</b> Bis zum 06.09.2026 zeigte und wählte die
    /// Zeile nur <see cref="Contact.PrimaryNumber"/> — also
    /// <c>Numbers[0]</c>. Eins CRM-Kontakt mit Festnetz <b>und</b> Mobil
    /// hatte beide im Modell, und die zweite war in der ganzen Oberfläche
    /// nicht erreichbar.
    /// </summary>
    public IReadOnlyList<ContactNumberChoice> Choices { get; }

    /// <summary>
    /// Ob die Zeile mehr als eine Nummer hat. Nur dann wird gefragt, welche
    /// gewählt werden soll — der häufige Fall darf keinen zusätzlichen Klick
    /// bekommen.
    /// </summary>
    public bool HasMultipleNumbers => Choices.Count > 1;

    /// <summary>
    /// Der Zusatz an der Zeile, wenn es mehr als eine Nummer gibt — etwa
    /// <c>+1 Nummer</c>. Sonst leer, damit nichts danebensteht, wo nichts zu
    /// holen ist.
    ///
    /// Ausgeschrieben statt nur <c>+1</c>: allein hinter einer Rufnummer läse
    /// sich das wie eine Vorwahl.
    /// </summary>
    public string MoreNumbersHint => Choices.Count switch
    {
        < 2 => string.Empty,
        2 => "+1 Nummer",
        var n => $"+{n - 1} Nummern",
    };

    /// <summary>
    /// Die Nummern, die auf einer Kachel Platz haben (ADR-047).
    ///
    /// <para><b>Zwei, und die Zahl ist erzwungen.</b> Ein
    /// <c>ItemsWrapGrid</c> misst die erste Kachel und gibt allen anderen
    /// dasselbe Mass; ungleich hohe Kacheln ergäben ein Raster mit Löchern.
    /// Festgelegt ist die Höhe deshalb auf zwei Nummernzeilen — Nebenstelle
    /// und Handy, der Regelfall seit ADR-041.</para>
    /// </summary>
    public IReadOnlyList<ContactNumberChoice> TileChoices => [.. Choices.Take(2)];

    /// <summary>Die übrigen Nummern — sie stehen hinter dem Zusatz auf der Kachel.</summary>
    public IReadOnlyList<ContactNumberChoice> HiddenChoices => [.. Choices.Skip(2)];

    /// <summary>Ob es überhaupt welche gibt.</summary>
    public bool HasHiddenChoices => Choices.Count > 2;

    /// <summary>
    /// Der Zusatz in der letzten Kachelzeile — «+1 Nummer». Ausgeschrieben aus
    /// demselben Grund wie <see cref="MoreNumbersHint"/>.
    /// </summary>
    public string HiddenChoicesHint => Choices.Count switch
    {
        < 3 => string.Empty,
        3 => "+1 Nummer",
        var n => $"+{n - 2} Nummern",
    };

    /// <summary>
    /// Die Zeile unter dem Namen auf der Kachel: die Firma, wo es eine gibt.
    ///
    /// <b>Nicht die Gruppe.</b> Die steht im Kopf über der Kachel, und
    /// dasselbe Wort zweimal übereinander erklärt nichts — derselbe Grund, aus
    /// dem der Abschnitt «Nebenstellen» und nicht «Team» heisst (ADR-044).
    /// </summary>
    public string TileContext => Contact.Company ?? string.Empty;

    /// <summary>
    /// Was ein Bildschirmleser an einer Kachel vorliest.
    ///
    /// <para><b>Die Gruppe gehört dazu, anders als in der Liste.</b> Ein
    /// Raster hat keine Zeilenreihenfolge, an der man sich entlanghangelt;
    /// wer mit den Pfeiltasten hineinspringt, weiss sonst nicht, in welchem
    /// Abschnitt er gelandet ist.</para>
    /// </summary>
    public string TileAccessibleName
    {
        get
        {
            var teile = Contact.Group is { Length: > 0 } gruppe
                ? $"{DisplayName}, {gruppe}"
                : DisplayName;

            if (SubLabel.Length > 0)
            {
                teile = $"{teile}, {SubLabel}";
            }

            return HasPresence ? $"{teile}, {PresenceText}" : teile;
        }
    }

    private static string KindLabel(ContactNumberKind kind) => kind switch
    {
        ContactNumberKind.Business => "Geschäftlich",
        ContactNumberKind.Mobile => "Mobil",
        ContactNumberKind.Home => "Privat",
        _ => "Weitere",
    };

    /// <summary>Ob diese Zeile eine Team-Nebenstelle ist — nur die haben Präsenz (§14.8).</summary>
    public bool IsTeam => Contact.Source == ContactSourceKind.Team;

    /// <summary>
    /// Ob eine Präsenzlampe angezeigt wird. Ein Outlook-Kontakt bekommt keine,
    /// und eine Team-Nebenstelle ohne SIP-Adresse auch nicht — dort ist
    /// „unbekannt" die Wahrheit, aber eine graue Lampe suggeriert, es werde
    /// beobachtet.
    /// </summary>
    public bool HasPresence => IsTeam && Contact.SipAddress is { Length: > 0 };

    /// <summary>
    /// §8.4: der Zustand als <b>Text</b>, nicht nur als Farbe. Bindet neben die
    /// Lampe, damit die Liste auch ohne Farbwahrnehmung lesbar bleibt.
    /// </summary>
    public string PresenceText => BlfService.Describe(Presence);

    /// <summary>
    /// Die Herkunft als Überschrift für die Gruppierung (§8.4: getrennt
    /// sichtbar).
    ///
    /// <b>Der Anzeigename der Quelle wird mitgegeben</b>, wo es einen gibt:
    /// bei einem Kontakt aus einem Fremdsystem sagt „extern" nichts,
    /// „Muster-CRM" schon. Er wird beim Bauen der Zeile gesetzt, weil nur die
    /// Suche weiss, welche Quelle geantwortet hat.
    /// </summary>
    public string SourceLabel => Contact.Source switch
    {
        ContactSourceKind.Team => "Team",
        ContactSourceKind.Outlook => "Outlook",
        _ => SourceDisplayName ?? Contact.EffectiveSourceId,
    };

    /// <summary>
    /// Der Anzeigename der externen Quelle, wie er in den Einstellungen steht.
    /// <c>null</c> bei Team und Outlook — die heissen immer gleich.
    /// </summary>
    public string? SourceDisplayName { get; init; }

    /// <summary>
    /// Die Herkünfte einer zusammengeführten Zeile, für das Abzeichen in der
    /// Liste (§21.4: nachvollziehbar bleiben, woher ein Kontakt stammt).
    /// </summary>
    public IReadOnlyList<string> OriginLabels =>
        [.. Contact.AllOrigins.Select(static o => o.SourceId)];

    /// <summary>Ob sich dieser Kontakt im Fremdsystem öffnen lässt (§21.3).</summary>
    public bool CanOpen => Contact.OpenUri is not null;

    partial void OnPresenceChanged(PresenceStatus value) => OnPropertyChanged(nameof(PresenceText));
}

/// <summary>
/// Eine wählbare Nummer eines Kontakts, fertig für die Anzeige.
/// </summary>
/// <param name="Number">Die Nummer zum Wählen, unformatiert.</param>
/// <param name="Display">Dieselbe Nummer, lesbar gesetzt.</param>
/// <param name="KindLabel">Die Art auf Deutsch: Geschäftlich, Mobil, Privat.</param>
public sealed record ContactNumberChoice(string Number, string Display, string KindLabel)
{
    /// <summary>
    /// Was ein Bildschirmleser an einem Nummernknopf vorliest (C19).
    ///
    /// <para><b>Der Befund.</b> Die Knöpfe trugen als Namen nur
    /// <see cref="Display"/> — eine Sprachausgabe las «plus vier eins sieben
    /// neun …» und sagte nicht, dass ein Druck darauf anruft. Im Menü, das
    /// <c>CallOrAsk</c> baut, stand dieselbe Sache seit jeher richtig da
    /// («Mobil anrufen, +41 79 …»): <b>zwei Fassungen einer Angabe, und die
    /// schlechtere stand an dem Ort, den man öfter erreicht.</b></para>
    ///
    /// <para>Jetzt steht sie einmal hier und gilt damit für alle vier Orte,
    /// an denen die gemeinsame Vorlage benutzt wird — Zeile, Kachel, Flyout
    /// der weiteren Nummern und das Menü.</para>
    /// </summary>
    public string AccessibleName => $"{KindLabel} anrufen, {Display}";
}
