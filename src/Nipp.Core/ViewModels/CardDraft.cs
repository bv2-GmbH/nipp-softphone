using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.Integrations.Cards;

namespace Nipp.Core.ViewModels;

/// <summary>Welche Art Baustein — die geschlossene Menge aus §21.2.</summary>
public enum DraftElementKind
{
    Text,
    Field,
    Badge,
    Divider,
    Spacer,
    Button,
    Link,
    SourceStatus,
}

/// <summary>
/// Wie der Wert eines Bausteins zustande kommt.
///
/// <para><b>Das ist der Kern des Designers.</b> Ein Ausdruck ist mächtig und
/// als einziger Weg unbedienbar: wer eine Zeile hinzufügen will, soll ein
/// Feld auswählen und nicht <c>coalesce(role('name'), contacts.displayName,
/// formatPhone(number.e164))</c> tippen. Also wird die Absicht benannt und der
/// Ausdruck daraus erzeugt.</para>
///
/// <para><b>Und der Rückweg muss stimmen.</b> Eine bestehende Karte wird
/// gelesen, nicht neu geschrieben — wer sie öffnet und ohne Änderung
/// speichert, muss dieselbe Karte zurückbekommen. Deshalb wird eine
/// strukturierte Form nur dann angenommen, wenn das Erzeugte
/// <b>zeichengleich</b> wieder das Original ergibt; sonst bleibt es ein
/// Ausdruck. Der Designer kann damit jede Karte öffnen, auch eine von Hand
/// geschriebene, und keine verliert etwas.</para>
/// </summary>
public enum DraftValueMode
{
    /// <summary>Ein Feld, etwa <c>crm.contactName</c>.</summary>
    Field,

    /// <summary>Eine Bedeutung, etwa <c>role('name')</c> — quellenunabhängig.</summary>
    Role,

    /// <summary>Der erste Treffer aus mehreren — <c>coalesce(a, b, c)</c>.</summary>
    FirstOf,

    /// <summary>Ein Text in Anführungszeichen, etwa <c>'VIP'</c>.</summary>
    Literal,

    /// <summary>Ein Ausdruck, wie er dasteht. Für alles Übrige.</summary>
    Expression,
}

/// <summary>Wann ein Baustein sichtbar ist.</summary>
public enum DraftVisibility
{
    /// <summary>Immer.</summary>
    Always,

    /// <summary>Nur wenn der eigene Wert etwas enthält.</summary>
    WhenValuePresent,

    /// <summary>Nach einer eigenen Bedingung.</summary>
    Expression,
}

/// <summary>
/// Ein Wert im Designer — die Absicht, nicht der Ausdruck.
///
/// Die Umwandlung in beide Richtungen steht hier, damit sie ohne Fenster
/// prüfbar ist: <c>Nipp.App</c> hat kein Testprojekt, und der Rückweg ist die
/// Stelle, an der eine Karte etwas verlieren könnte.
/// </summary>
public sealed partial class DraftValue : ObservableObject
{
    /// <summary>Was zusammengesetzt wird, wenn der Modus <c>FirstOf</c> ist.</summary>
    public ObservableCollection<string> Parts { get; } = [];

    [ObservableProperty]
    private DraftValueMode _mode = DraftValueMode.Field;

    /// <summary>
    /// Der einzelne Wert: der Feldpfad, der Rollenname, der Text oder der
    /// ganze Ausdruck — je nach <see cref="Mode"/>.
    /// </summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>Baut den Ausdruck, der in der Karte steht.</summary>
    public string ToExpression() => Mode switch
    {
        DraftValueMode.Field => Text.Trim(),
        DraftValueMode.Role => $"role('{Text.Trim()}')",
        DraftValueMode.Literal => $"'{Text.Replace("'", string.Empty, StringComparison.Ordinal)}'",
        DraftValueMode.FirstOf => Parts.Count switch
        {
            0 => string.Empty,
            1 => Parts[0],
            _ => $"coalesce({string.Join(", ", Parts)})",
        },
        _ => Text,
    };

    /// <summary>
    /// Liest einen Ausdruck in die strukturierte Form — <b>nur wenn der
    /// Rückweg zeichengleich stimmt</b>.
    ///
    /// <para>Der Vergleich ist die ganze Sicherheit dieser Methode. Ohne ihn
    /// würde eine Karte beim Öffnen und Speichern still umgeschrieben: aus
    /// <c>coalesce(a,b)</c> würde <c>coalesce(a, b)</c>, aus
    /// <c>role( 'name' )</c> etwas anderes, und aus einem Ausdruck, der nur
    /// so <b>aussieht</b> wie ein Feldpfad, ein Feldpfad. Was sich nicht
    /// zeichengleich erzeugen lässt, bleibt <see cref="DraftValueMode.Expression"/>
    /// und wird unverändert weitergegeben.</para>
    /// </summary>
    public static DraftValue FromExpression(string? expression)
    {
        var wert = new DraftValue();
        var text = (expression ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            wert.Mode = DraftValueMode.Field;
            wert.Text = string.Empty;

            return wert;
        }

        foreach (var kandidat in Kandidaten(text))
        {
            if (string.Equals(kandidat.ToExpression(), text, StringComparison.Ordinal))
            {
                return kandidat;
            }
        }

        wert.Mode = DraftValueMode.Expression;
        wert.Text = text;

        return wert;
    }

    /// <summary>
    /// Die Formen, die zu prüfen sind — in der Reihenfolge, in der sie
    /// gewinnen sollen. Geprüft wird danach über den Rückweg, hier wird nur
    /// geraten.
    /// </summary>
    private static IEnumerable<DraftValue> Kandidaten(string text)
    {
        // role('name')
        if (text.StartsWith("role('", StringComparison.Ordinal) && text.EndsWith("')", StringComparison.Ordinal))
        {
            yield return new DraftValue
            {
                Mode = DraftValueMode.Role,
                Text = text[6..^2],
            };
        }

        // 'Ein Text'
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
        {
            yield return new DraftValue
            {
                Mode = DraftValueMode.Literal,
                Text = text[1..^1],
            };
        }

        // coalesce(a, b, c)
        if (text.StartsWith("coalesce(", StringComparison.Ordinal) && text[^1] == ')')
        {
            var inhalt = text[9..^1];
            var teile = Zerlegen(inhalt);

            if (teile is not null)
            {
                var wert = new DraftValue { Mode = DraftValueMode.FirstOf };

                foreach (var teil in teile)
                {
                    wert.Parts.Add(teil);
                }

                yield return wert;
            }
        }

        // Ein einfacher Pfad: quelle.feld
        //
        // <b>Und nur ein solcher.</b> Der Rückweg von `Field` gibt den Text
        // unverändert aus — ohne diese Prüfung passt also *jeder* Ausdruck als
        // Feldpfad, und `Expression` wäre nie erreichbar. Zeichengleich wäre
        // das trotzdem, der Fehler also unsichtbar: erst der
        // Eigenschaftenbereich hätte `concat(a, ' ', b)` als „Feld" mit diesem
        // Namen angeboten, mit einer Auswahlliste daneben, in der er nicht
        // vorkommt. Gefunden hat das der Test über die Modi, nicht der über
        // den Rundlauf.
        if (IstFeldpfad(text))
        {
            yield return new DraftValue
            {
                Mode = DraftValueMode.Field,
                Text = text,
            };
        }
    }

    /// <summary>
    /// Ob der Text als Feldpfad taugt: <c>quelle.feld</c>, <c>number.e164</c>
    /// oder ein einzelner Name wie <c>status</c>.
    ///
    /// Buchstabe am Anfang jedes Abschnitts, danach Buchstaben, Ziffern und
    /// Unterstrich, höchstens drei Abschnitte. Alles mit Klammer, Komma,
    /// Anführungszeichen, Leerzeichen oder Rechenzeichen ist ein Ausdruck.
    /// </summary>
    private static bool IstFeldpfad(string text)
    {
        var abschnitte = text.Split('.');

        if (abschnitte.Length is 0 or > 3)
        {
            return false;
        }

        foreach (var abschnitt in abschnitte)
        {
            if (abschnitt.Length == 0 || !char.IsLetter(abschnitt[0]))
            {
                return false;
            }

            if (!abschnitt.All(static c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Zerlegt die Argumente von <c>coalesce</c> an den Kommas der obersten
    /// Ebene.
    ///
    /// <b>Klammertiefe zählen und nicht <c>Split(',')</c>:</b> ein Argument
    /// kann selbst ein Funktionsaufruf sein —
    /// <c>coalesce(role('name'), formatPhone(number.e164))</c>. Ein blindes
    /// Trennen ergäbe dort vier Teile, und der Rückweg wäre nicht mehr
    /// derselbe Ausdruck.
    /// </summary>
    private static List<string>? Zerlegen(string inhalt)
    {
        var teile = new List<string>();
        var tiefe = 0;
        var inText = false;
        var start = 0;

        for (var i = 0; i < inhalt.Length; i++)
        {
            var zeichen = inhalt[i];

            if (zeichen == '\'')
            {
                inText = !inText;
                continue;
            }

            if (inText)
            {
                continue;
            }

            switch (zeichen)
            {
                case '(':
                    tiefe++;
                    break;

                case ')':
                    tiefe--;

                    if (tiefe < 0)
                    {
                        return null;
                    }

                    break;

                case ',' when tiefe == 0:
                    teile.Add(inhalt[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        if (tiefe != 0 || inText)
        {
            return null;
        }

        teile.Add(inhalt[start..].Trim());

        return teile.Any(static t => t.Length == 0) ? null : teile;
    }
}

/// <summary>
/// Ein Baustein im Designer.
///
/// <para><b>Eine Klasse für alle Arten, nicht sieben.</b> Der
/// Eigenschaftenbereich zeigt je Art andere Felder — mit einer Vererbungskette
/// müsste er den Typ wechseln, und ein Wechsel der Art („aus dem Feld soll
/// eine Überschrift werden") wäre ein Neuanlegen mit Verlust der Bedingung.
/// So bleibt alles erhalten, und was nicht zur Art gehört, wird beim
/// Umwandeln weggelassen.</para>
/// </summary>
public sealed partial class DraftElement : ObservableObject
{
    [ObservableProperty]
    private DraftElementKind _kind = DraftElementKind.Field;

    /// <summary>Beschriftung — bei <see cref="DraftElementKind.Field"/>, Knopf und Verweis.</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    public DraftValue Value { get; } = new();

    /// <summary>Der Stil bei einem Text.</summary>
    [ObservableProperty]
    private CardTextStyle _style = CardTextStyle.Body;

    [ObservableProperty]
    private int _maxLines = 1;

    /// <summary>
    /// Was bei einem leeren Wert steht. <c>null</c> heisst: die Zeile
    /// verschwindet — und genau das ist die häufigste Wahl auf einer Karte,
    /// deren Quellen nicht immer alles liefern.
    /// </summary>
    [ObservableProperty]
    private string? _emptyText;

    /// <summary>
    /// Ob die Beschriftung eines Feldes dasteht (ADR-037).
    ///
    /// Abgeschaltet bleibt sie trotzdem gefüllt: der Renderer trägt sie an den
    /// Wert, damit eine Sprachausgabe sie nennt.
    /// </summary>
    [ObservableProperty]
    private bool _showLabel = true;

    /// <summary>Wie viel Luft ein Abstand lässt.</summary>
    [ObservableProperty]
    private CardSpacerSize _spacerSize = CardSpacerSize.Medium;

    /// <summary>Der Ton eines Abzeichens, als Ausdruck.</summary>
    [ObservableProperty]
    private string _tone = "'neutral'";

    /// <summary>Die Adresse eines Verweises oder eines Knopfes.</summary>
    [ObservableProperty]
    private string _url = string.Empty;

    /// <summary>Welche Quelle ein Zustandsbaustein zeigt.</summary>
    [ObservableProperty]
    private string _sourceId = string.Empty;

    [ObservableProperty]
    private DraftVisibility _visibility = DraftVisibility.Always;

    /// <summary>Die eigene Bedingung, wenn <see cref="Visibility"/> das sagt.</summary>
    [ObservableProperty]
    private string _visibleWhen = string.Empty;

    /// <summary>Was in der Baumansicht des Designers dasteht.</summary>
    public string Headline => Kind switch
    {
        DraftElementKind.Divider => "Trennlinie",
        DraftElementKind.Spacer => SpacerSize switch
        {
            CardSpacerSize.Small => "Abstand, klein",
            CardSpacerSize.Large => "Abstand, gross",
            _ => "Abstand",
        },
        DraftElementKind.SourceStatus => $"Zustand von {SourceId}",
        DraftElementKind.Text => Style switch
        {
            CardTextStyle.Title => "Überschrift",
            CardTextStyle.Subtitle => "Unterzeile",
            CardTextStyle.Caption => "Kleingedrucktes",
            _ => "Text",
        },
        DraftElementKind.Badge => "Abzeichen",
        DraftElementKind.Button => $"Schaltfläche: {Label}",
        DraftElementKind.Link => $"Verweis: {Label}",
        _ => Label.Length > 0 ? Label : "Feld",
    };

    /// <summary>
    /// Was klein darunter steht — der Wert in seiner lesbaren Form.
    ///
    /// <para><b>Auch Linie und Abstand sagen etwas.</b> Beide waren hier leer,
    /// und ein Knopf ohne zweite Zeile wird im Aufbau-Baum so flach, dass er
    /// sich wie nicht auswählbar liest — genau daraus wurde die Meldung, Linien
    /// und Abstände liessen sich nicht löschen. Löschen konnte der Kern sie
    /// immer.</para>
    /// </summary>
    public string Detail => Kind switch
    {
        DraftElementKind.Divider => "waagrechte Linie",
        DraftElementKind.Spacer => SpacerSize switch
        {
            CardSpacerSize.Small => "kleine Lücke",
            CardSpacerSize.Large => "grosse Lücke",
            _ => "Lücke",
        },
        DraftElementKind.SourceStatus => string.Empty,
        DraftElementKind.Button or DraftElementKind.Link => Url,
        _ => Value.ToExpression(),
    };

    /// <summary>Baut den Baustein der Karte.</summary>
    public CardElement ToElement()
    {
        var bedingung = Visibility switch
        {
            DraftVisibility.WhenValuePresent => $"!isEmpty({Value.ToExpression()})",
            DraftVisibility.Expression => string.IsNullOrWhiteSpace(VisibleWhen) ? null : VisibleWhen,
            _ => null,
        };

        return Kind switch
        {
            DraftElementKind.Text => new CardText(Value.ToExpression())
            {
                Style = Style,
                MaxLines = Math.Max(1, MaxLines),
                VisibleWhen = bedingung,
            },

            DraftElementKind.Badge => new CardBadge(Value.ToExpression())
            {
                Tone = Tone,
                VisibleWhen = bedingung,
            },

            DraftElementKind.Divider => new CardDivider { VisibleWhen = bedingung },

            DraftElementKind.Spacer => new CardSpacer
            {
                Size = SpacerSize,
                VisibleWhen = bedingung,
            },

            DraftElementKind.Button => new CardButton(Label, new OpenUrlAction(Url))
            {
                VisibleWhen = bedingung,
            },

            DraftElementKind.Link => new CardLink(Label, Url) { VisibleWhen = bedingung },

            DraftElementKind.SourceStatus => new CardSourceStatus(SourceId)
            {
                VisibleWhen = bedingung,
            },

            _ => new CardField(Label, Value.ToExpression())
            {
                EmptyText = EmptyText,
                ShowLabel = ShowLabel,
                VisibleWhen = bedingung,
            },
        };
    }

    /// <summary>Liest einen Baustein der Karte ein.</summary>
    public static DraftElement FromElement(CardElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var entwurf = new DraftElement();

        switch (element)
        {
            case CardText text:
                entwurf.Kind = DraftElementKind.Text;
                entwurf.Style = text.Style;
                entwurf.MaxLines = text.MaxLines;
                entwurf.Uebernehmen(text.Value);
                break;

            case CardField field:
                entwurf.Kind = DraftElementKind.Field;
                entwurf.Label = field.Label;
                entwurf.EmptyText = field.EmptyText;
                entwurf.ShowLabel = field.ShowLabel;
                entwurf.Uebernehmen(field.Value);
                break;

            case CardBadge badge:
                entwurf.Kind = DraftElementKind.Badge;
                entwurf.Tone = badge.Tone;
                entwurf.Uebernehmen(badge.Text);
                break;

            case CardDivider:
                entwurf.Kind = DraftElementKind.Divider;
                break;

            case CardSpacer spacer:
                entwurf.Kind = DraftElementKind.Spacer;
                entwurf.SpacerSize = spacer.Size;
                break;

            case CardButton button:
                entwurf.Kind = DraftElementKind.Button;
                entwurf.Label = button.Label;
                entwurf.Url = button.Action is OpenUrlAction open ? open.Url : string.Empty;
                break;

            case CardLink link:
                entwurf.Kind = DraftElementKind.Link;
                entwurf.Label = link.Label;
                entwurf.Url = link.Url;
                break;

            case CardSourceStatus status:
                entwurf.Kind = DraftElementKind.SourceStatus;
                entwurf.SourceId = status.Source;
                break;
        }

        entwurf.BedingungLesen(element.VisibleWhen);

        return entwurf;
    }

    private void Uebernehmen(string? ausdruck)
    {
        var wert = DraftValue.FromExpression(ausdruck);

        Value.Mode = wert.Mode;
        Value.Text = wert.Text;
        Value.Parts.Clear();

        foreach (var teil in wert.Parts)
        {
            Value.Parts.Add(teil);
        }
    }

    /// <summary>
    /// Liest die Sichtbarkeit — und erkennt die häufige Form
    /// <c>!isEmpty(&lt;eigener Wert&gt;)</c> als „nur wenn ein Wert da ist".
    ///
    /// Nur, wenn sie sich auf <b>den eigenen Wert</b> bezieht: eine Bedingung
    /// über ein anderes Feld ist eine eigene Bedingung und bleibt eine.
    /// </summary>
    private void BedingungLesen(string? bedingung)
    {
        if (string.IsNullOrWhiteSpace(bedingung))
        {
            Visibility = DraftVisibility.Always;
            VisibleWhen = string.Empty;

            return;
        }

        var text = bedingung.Trim();

        if (string.Equals(text, $"!isEmpty({Value.ToExpression()})", StringComparison.Ordinal))
        {
            Visibility = DraftVisibility.WhenValuePresent;
            VisibleWhen = string.Empty;

            return;
        }

        Visibility = DraftVisibility.Expression;
        VisibleWhen = text;
    }
}

/// <summary>Eine Spalte im Designer.</summary>
public sealed partial class DraftColumn : ObservableObject
{
    [ObservableProperty]
    private int _span = CardLayout.Columns;

    public ObservableCollection<DraftElement> Elements { get; } = [];
}

/// <summary>Eine Zeile im Designer.</summary>
public sealed class DraftRow
{
    public ObservableCollection<DraftColumn> Columns { get; } = [];

    /// <summary>Wie viele Rastereinheiten noch frei sind.</summary>
    public int FreeSpan => CardLayout.Columns - Columns.Sum(static c => Math.Max(1, c.Span));
}

/// <summary>Ein Abschnitt im Designer.</summary>
public sealed partial class DraftSection : ObservableObject
{
    [ObservableProperty]
    private string _id = "abschnitt";

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private string _visibleWhen = string.Empty;

    public ObservableCollection<DraftRow> Rows { get; } = [];
}

/// <summary>
/// Eine Karte, während sie bearbeitet wird (K4, ADR-032).
///
/// <para><b>Ein veränderlicher Spiegel von <see cref="CardDefinition"/>.</b>
/// Die Definition selbst ist ein <c>record</c> aus unveränderlichen Listen —
/// richtig für alles, was sie liest, und unbrauchbar für einen Editor: jedes
/// Verschieben eines Bausteins wäre ein Neubau des ganzen Baums, und die
/// Oberfläche müsste jedes Mal alles neu zeichnen.</para>
///
/// <para><b>Der Rundlauf ist die Abnahme dieses Typs.</b>
/// <c>FromDefinition(ToDefinition(x)) == x</c> muss für jede Karte gelten,
/// auch für eine von Hand geschriebene mit Ausdrücken, die der Designer nicht
/// in Formulare zerlegen kann. Was er nicht versteht, gibt er unverändert
/// weiter — er darf nichts wegwerfen, was er nicht anzeigen kann.</para>
/// </summary>
public sealed partial class CardDraft : ObservableObject
{
    [ObservableProperty]
    private string _id = "eigene";

    [ObservableProperty]
    private string _name = "Eigene Karte";

    [ObservableProperty]
    private CardKind _kind = CardKind.ActiveExpanded;

    [ObservableProperty]
    private int _schemaVersion = 1;

    public ObservableCollection<DraftSection> Sections { get; } = [];

    /// <summary>Alle Bausteine, von oben nach unten — für die Liste im Designer.</summary>
    public IEnumerable<DraftElement> AllElements =>
        Sections.SelectMany(static s => s.Rows)
            .SelectMany(static r => r.Columns)
            .SelectMany(static c => c.Elements);

    public CardDefinition ToDefinition() => new(
        Id,
        Name,
        Kind,
        [
            .. Sections.Select(static s => new CardSection(
                s.Id,
                s.Title,
                [
                    .. s.Rows.Select(static r => new CardRow(
                    [
                        .. r.Columns.Select(static c => new CardColumn(
                            Math.Clamp(c.Span, 1, CardLayout.Columns),
                            [.. c.Elements.Select(static e => e.ToElement())])),
                    ])),
                ],
                string.IsNullOrWhiteSpace(s.VisibleWhen) ? null : s.VisibleWhen)),
        ],
        SchemaVersion);

    public static CardDraft FromDefinition(CardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var entwurf = new CardDraft
        {
            Id = definition.Id,
            Name = definition.Name,
            Kind = definition.Kind,
            SchemaVersion = definition.SchemaVersion,
        };

        foreach (var abschnitt in definition.Sections)
        {
            var s = new DraftSection
            {
                Id = abschnitt.Id,
                Title = abschnitt.Title,
                VisibleWhen = abschnitt.VisibleWhen ?? string.Empty,
            };

            foreach (var zeile in abschnitt.Rows)
            {
                var r = new DraftRow();

                foreach (var spalte in zeile.Columns)
                {
                    var c = new DraftColumn { Span = spalte.Span };

                    foreach (var baustein in spalte.Elements)
                    {
                        c.Elements.Add(DraftElement.FromElement(baustein));
                    }

                    r.Columns.Add(c);
                }

                s.Rows.Add(r);
            }

            entwurf.Sections.Add(s);
        }

        return entwurf;
    }

    /// <summary>
    /// Eine leere Karte einer Art — für „von vorn anfangen".
    ///
    /// Mit einem Abschnitt und einer Zeile, nicht ganz leer: eine Karte ohne
    /// Zeile hat keine Stelle, auf die sich etwas ziehen liesse, und der
    /// Designer wäre eine Fläche, auf der nichts geht.
    /// </summary>
    public static CardDraft Empty(CardKind kind)
    {
        var entwurf = new CardDraft
        {
            Id = kind switch
            {
                CardKind.ActiveExpanded => "active-custom",
                CardKind.IncomingCompact => "incoming-custom",
                CardKind.Toast => "toast-custom",
                _ => "history-custom",
            },
            Name = kind switch
            {
                CardKind.ActiveExpanded => "Gespräch",
                CardKind.IncomingCompact => "Eingehender Anruf",
                CardKind.Toast => "Benachrichtigung",
                _ => "Anrufliste",
            },
            Kind = kind,
        };

        var abschnitt = new DraftSection { Id = "kopf" };

        if (kind == CardKind.Toast)
        {
            // Drei Zeilen zum Anfangen, nicht eine leere.
            //
            // <b>Und ausdrücklich nicht die mitgelieferte Zusammensetzung.</b>
            // Die setzt Zeile 1 aus Name, Firma und Art zusammen und lässt
            // Teile weg — als Ausdruck wäre das ein Ungetüm, das im Designer
            // nur als „Ausdruck" erscheint (die Begründung steht am
            // <c>ToastComposer</c>). Wer den Toast selbst zusammenstellt,
            // fängt mit drei einfachen Zeilen an und sieht in der Vorschau,
            // was er dafür aufgibt.
            foreach (var rolle in new[] { "name", "work", "summary" })
            {
                var zeile = NewRow();
                var baustein = new DraftElement { Kind = DraftElementKind.Text };

                baustein.Value.Mode = DraftValueMode.Role;
                baustein.Value.Text = rolle;

                zeile.Columns[0].Elements.Add(baustein);
                abschnitt.Rows.Add(zeile);
            }
        }
        else
        {
            abschnitt.Rows.Add(NewRow());
        }

        entwurf.Sections.Add(abschnitt);

        return entwurf;
    }

    /// <summary>Eine neue Zeile mit einer Spalte über die ganze Breite.</summary>
    public static DraftRow NewRow()
    {
        var zeile = new DraftRow();
        zeile.Columns.Add(new DraftColumn { Span = CardLayout.Columns });

        return zeile;
    }

    /// <summary>
    /// Ob die Kennung als Kennung taugt — sie steht in Befunden und im
    /// Protokoll.
    /// </summary>
    public bool HasUsableId =>
        !string.IsNullOrWhiteSpace(Id)
        && Id.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_')
        && Id.Length <= 64;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name} ({Kind})");
}
