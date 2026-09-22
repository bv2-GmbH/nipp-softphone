using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Eine Zeile auf der Anruferkarte: Beschriftung und Wert.
/// </summary>
/// <param name="Label">Was links steht.</param>
/// <param name="Value">Was rechts steht.</param>
/// <param name="SourceId">Aus welcher Quelle — für die Fehlersuche.</param>
public sealed record CallerCardField(string Label, string Value, string SourceId)
{
    /// <summary>
    /// Was eine Sprachausgabe vorliest (Befund A1-16). Beschriftung und Wert
    /// stehen nebeneinander in zwei Spalten; ohne diese Zeile liest sie sie
    /// als zwei zusammenhanglose Bruchstücke oder, schlimmer, die ganze
    /// Aufstellung des Records samt <see cref="SourceId"/>.
    /// </summary>
    public string AccessibleName => $"{Label}, {Value}";
}

/// <summary>
/// Was eine Quelle gerade beiträgt — die Zeile unter der Karte.
/// </summary>
/// <param name="DisplayName">Wie die Quelle heisst.</param>
/// <param name="State">Ihr Zustand.</param>
/// <param name="Message">Was zu sagen ist, oder <c>null</c>.</param>
public sealed record CallerCardSource(string DisplayName, SourceState State, string? Message)
{
    /// <summary>
    /// Der Satz, der in der Oberfläche steht.
    ///
    /// <b>Fertig formuliert hier und nicht im XAML</b>: §15 verlangt, dass
    /// eine Meldung Ursache und Abhilfe nennt, und eine Zustandsangabe, die
    /// erst die Ansicht in Text übersetzt, verteilt dasselbe Wissen auf zwei
    /// Orte.
    /// </summary>
    public string Text => State switch
    {
        SourceState.Loading => $"{DisplayName} wird gefragt …",
        SourceState.Timeout => Message ?? $"{DisplayName} antwortet nicht",
        SourceState.Error => Message ?? $"{DisplayName}: Fehler",
        SourceState.Skipped => Message ?? $"{DisplayName} übersprungen",
        SourceState.Empty => $"{DisplayName}: nichts gefunden",
        _ => DisplayName,
    };

    /// <summary>
    /// Ob diese Quelle überhaupt erwähnt wird.
    ///
    /// Eine erfolgreiche Quelle sagt nichts — ihre Felder stehen auf der
    /// Karte, und eine Zeile „CRM: gefunden" darunter wäre Rauschen. Genannt
    /// wird, was fehlt oder noch aussteht.
    /// </summary>
    public bool IsNotable => State is not SourceState.Success;
}

/// <summary>
/// Die Anruferkarte in der Gesprächsansicht (§21.1).
///
/// <b>Zwei Darstellungen nebeneinander, und das ist Absicht.</b>
/// <see cref="Layout"/> ist die konfigurierbare Karte — Abschnitte, Zeilen,
/// Spalten, wie der Administrator sie beschreibt. <see cref="Title"/> und
/// <see cref="Subtitle"/> gehören dagegen in den Kopf der Gesprächsansicht,
/// zusammen mit Dauer und Zustand: wer im Gespräch ist, soll den Namen dort
/// lesen und nicht in einer Karte darunter suchen.
///
/// <see cref="Fields"/> und <see cref="Sources"/> sind die einfache
/// Darstellung für den Fall, dass keine Karte greift.
///
/// <b>Was hier über die Anzeige entschieden wird.</b> Die Karte folgt dem
/// <b>ausgewählten</b> Gespräch, nicht dem zuletzt gemeldeten: bei zwei
/// Gesprächen soll sie zeigen, mit wem man gerade spricht, und nicht
/// umspringen, weil im Hintergrund ein zweiter Anruf seinen Zustand ändert.
/// </summary>
public sealed partial class CallerCardViewModel : ObservableObject, IDisposable
{
    private readonly CallerContextService _context;
    private readonly ActiveCallViewModel _calls;
    private readonly CardResolver _cards;
    private readonly CallPartyResolver _party;

    /// <summary>
    /// Die übersetzte Karte. Neu übersetzt nur, wenn sich die Konfiguration
    /// ändert — nicht bei jeder Antwort einer Quelle (§6: die Auflösung läuft
    /// auf dem Thread, der das SDK bedient).
    /// </summary>
    private CompiledCard _card = new();

    private bool _disposed;

    /// <summary>Die Zeilen der Karte, in der Reihenfolge, in der sie stehen.</summary>
    public ObservableCollection<CallerCardField> Fields { get; } = [];

    /// <summary>Was die Quellen tun — nur, was erwähnenswert ist.</summary>
    public ObservableCollection<CallerCardSource> Sources { get; } = [];

    /// <summary>
    /// Der <b>Name</b> des Gesprächspartners — oder leer, wenn keiner bekannt
    /// ist. <b>Nie eine Nummer</b> (ADR-043).
    ///
    /// <para><b>Hier zählt jede Millisekunde.</b> Die lokalen Kontakte
    /// antworten in derselben Iteration, in der der Anruf gemeldet wird — der
    /// Name steht also, bevor irgendein fremdes System gefragt wurde.</para>
    ///
    /// <para>Bis zum 12.09.2026 stand hier das Feld <c>displayName</c> der
    /// lokalen Quelle und bei fehlendem Kontext <c>call.DisplayLabel</c> — also
    /// die blosse Nummer, dreizehn Zeilen unter dem Kommentar, der das
    /// verbietet. Beides beantwortet jetzt <see cref="CallPartyResolver"/>.</para>
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Was in der Kopfzeile der Gesprächsansicht steht: der Name, sonst die
    /// formatierte Nummer — <b>nie leer</b>.
    ///
    /// <b>Nicht dasselbe wie <see cref="Title"/></b>, und genau darum gibt es
    /// beides: ein leerer Kopf sieht aus wie ein Fehler, eine Nummer als Name
    /// ist einer.
    /// </summary>
    [ObservableProperty]
    private string _headline = string.Empty;

    /// <summary>Die zweite Zeile: die Firma, wenn eine bekannt ist.</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Ob überhaupt etwas anzuzeigen ist.</summary>
    [ObservableProperty]
    private bool _hasContext;

    /// <summary>
    /// Die aufgelöste Karte, wie der Renderer sie bekommt (§21).
    ///
    /// <b>Hier stehen keine Ausdrücke mehr</b>, nur Werte und Sichtbarkeiten.
    /// Die Oberfläche zeichnet, was dasteht.
    /// </summary>
    [ObservableProperty]
    private CardModel _layout = CardModel.Empty;

    public CallerCardViewModel(
        CallerContextService context,
        ActiveCallViewModel calls,
        CardResolver cards,
        CallPartyResolver party)
    {
        _context = context;
        _calls = calls;
        _cards = cards;
        _party = party;

        CompileCard();

        _context.ContextChanged += OnContextChanged;
        _calls.PropertyChanged += OnSelectedCallChanged;
        _cards.Changed += OnCardsChanged;
    }

    private void OnCardsChanged(object? sender, EventArgs e)
    {
        CompileCard();
        Refresh();
    }

    /// <summary>
    /// Holt die Karte, die gerade gilt.
    ///
    /// <b>Seit ADR-032 kann das eine eigene sein.</b> Welche es ist, entscheidet
    /// der <see cref="CardResolver"/> — die Karte aus der Konfiguration, sonst
    /// die mitgelieferte, und bei einem Fehler in der eigenen ebenfalls die
    /// mitgelieferte samt Befund in den Einstellungen. Übersetzt wird dort,
    /// nicht hier: dieser Code läuft auf dem Thread, der alle 20 ms
    /// <c>Core.Iterate()</c> bedient (§6).
    /// </summary>
    private void CompileCard() => _card = _cards.For(CardKind.ActiveExpanded);

    private void OnSelectedCallChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ActiveCallViewModel.SelectedCall))
        {
            Refresh();
        }
    }

    private void OnContextChanged(object? sender, ContextSnapshot snapshot)
    {
        // Nur der Anruf, den der Benutzer gerade ansieht. Ein zweiter Anruf im
        // Hintergrund darf die Karte nicht überschreiben.
        if (_calls.SelectedCall is not { } call || call.Handle != snapshot.Call)
        {
            return;
        }

        Apply(snapshot);
        Namen(call);
    }

    /// <summary>Baut die Karte zum gerade ausgewählten Gespräch neu auf.</summary>
    public void Refresh()
    {
        if (_calls.SelectedCall is not { } call)
        {
            Clear();
            return;
        }

        if (_context.SnapshotFor(call.Handle) is { } snapshot)
        {
            Apply(snapshot);
        }
        else
        {
            // Kein Kontext zu diesem Anruf — etwa weil keine Quelle
            // eingerichtet ist. Dann bleibt die Karte leer; der Name kommt
            // trotzdem, denn die lokalen Kontakte hängen an keiner Quelle.
            Clear();
            Subtitle = string.Empty;
        }

        Namen(call);
    }

    /// <summary>
    /// Setzt Kopfzeile und Name — <b>an einer Stelle, nach beiden Zweigen</b>.
    ///
    /// Steht die Zuweisung in jedem Zweig einzeln, ist der Zweig ohne Kontext
    /// der, den beim nächsten Mal jemand vergisst; genau so entstand die
    /// Nummer im Kopf.
    /// </summary>
    private void Namen(CallInfo call)
    {
        Title = _party.NameOf(call) ?? string.Empty;
        Headline = _party.Describe(call);
    }

    private void Apply(ContextSnapshot snapshot)
    {
        // Die konfigurierbare Karte (§21). Sie steht neben den einfachen
        // Listen, nicht an ihrer Stelle: Titel und Untertitel gehören in den
        // Kopf der Gesprächsansicht und nicht in die Karte darunter.
        Layout = CardLayoutEngine.Build(_card, snapshot);

        Fields.Clear();
        Sources.Clear();

        var lokal = snapshot.Sources.GetValueOrDefault("contacts");

        // <b>Der Name steht hier nicht mehr.</b> Er kam aus dem Feld
        // «displayName» der lokalen Quelle — das gibt es aber nur, wenn zu
        // diesem Anruf überhaupt eine Kontextsitzung läuft. Ohne eine stand
        // im Kopf die blosse Nummer, und bei ausgehenden Anrufen war das der
        // Normalfall. Den Namen beantwortet jetzt der Auflöser (ADR-043),
        // dieselbe Stelle wie für Toast, Leiste und Infobereich.
        Subtitle = Text(lokal, "company") ?? string.Empty;

        // Die Felder der externen Quellen, in der Reihenfolge der Quellen und
        // innerhalb einer Quelle in der Reihenfolge des Mappings. Der lokale
        // Beitrag steht schon oben als Titel.
        foreach (var (id, fragment) in snapshot.Sources)
        {
            // Die lokale Quelle meldet sich nicht, wenn sie bloss nichts fand.
            //
            // „Kontakte: nichts gefunden" stand dadurch in jedem Gespräch mit
            // einer unbekannten Nummer — eine Zeile, die nichts sagt: ist ein
            // Name bekannt, steht er oben, und ist keiner bekannt, sieht man die
            // Nummer und weiss es. Ein Fehler oder eine Zeitgrenze der lokalen
            // Quelle bleibt dagegen eine Nachricht.
            var stillLokal = string.Equals(id, "contacts", StringComparison.Ordinal)
                && fragment.State == SourceState.Empty;

            if (fragment.IsNotable() && !stillLokal)
            {
                Sources.Add(new CallerCardSource(fragment.DisplayName, fragment.State, fragment.Message));
            }

            if (string.Equals(id, "contacts", StringComparison.Ordinal) || !fragment.HasData)
            {
                continue;
            }

            foreach (var (name, value) in fragment.Fields)
            {
                if (value.IsEmpty)
                {
                    continue;
                }

                Fields.Add(new CallerCardField(Humanize(name), value.AsDisplayText(), id));
            }
        }

        HasContext = Fields.Count > 0 || Sources.Count > 0;
    }

    private void Clear()
    {
        Fields.Clear();
        Sources.Clear();

        Layout = CardModel.Empty;
        Title = string.Empty;
        Headline = string.Empty;
        Subtitle = string.Empty;
        HasContext = false;
    }

    private static string? Text(ContextFragment? fragment, string field) =>
        fragment is not null
        && fragment.Fields.TryGetValue(field, out var value)
        && !value.IsEmpty
            ? value.AsText()
            : null;

    /// <summary>
    /// Macht aus einem Feldnamen eine Beschriftung: <c>customerNumber</c> wird
    /// „Customer number".
    ///
    /// <b>Eine Notlösung, und zwar bewusst.</b> Richtige Beschriftungen kommen
    /// mit der konfigurierbaren Karte (I4) aus der Konfiguration — dort
    /// bestimmt der Administrator, was neben einem Wert steht. Bis dahin ist
    /// ein zerlegter Feldname besser lesbar als <c>customerNumber</c> und
    /// ehrlicher als eine erfundene Übersetzung.
    /// </summary>
    /// <summary>
    /// Aus einem Feldnamen des Mappings eine Beschriftung machen.
    ///
    /// <c>internal</c> und nicht privat: die Anrufliste zeigt seit §22.3
    /// dieselben Felder und soll sie gleich beschriften — zwei Kopien wären
    /// zwei Gelegenheiten, sie auseinanderlaufen zu lassen.
    /// </summary>
    internal static string Humanize(string field)
    {
        if (field.Length == 0)
        {
            return field;
        }

        var builder = new System.Text.StringBuilder(field.Length + 8);

        builder.Append(char.ToUpperInvariant(field[0]));

        foreach (var c in field.AsSpan(1))
        {
            if (char.IsUpper(c))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _context.ContextChanged -= OnContextChanged;
        _calls.PropertyChanged -= OnSelectedCallChanged;
        _cards.Changed -= OnCardsChanged;
    }
}

/// <summary>Kleine Hilfe, damit die Bedingung nur einmal geschrieben steht.</summary>
internal static class ContextFragmentExtensions
{
    public static bool IsNotable(this ContextFragment fragment) =>
        fragment.State is not SourceState.Success;
}
