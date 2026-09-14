using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Der Anruferkontext an einem Eintrag der Anrufliste (§22.3, ADR-027,
/// ADR-036).
///
/// <para><b>Abgerufen wird beim Aufklappen, gespeichert wird nichts.</b> §21.2
/// erlaubt der Anrufliste einen extern aufgelösten Namen, keinen
/// Gesprächsinhalt, und dem das API-Team des Journals wurde schriftlich zugesagt, dass
/// seine Antworten nie auf die Platte gehen. <c>HistoryStoresNoContextTests</c>
/// hält das fest — der Test stand vor diesem Code.</para>
///
/// <para><b>Was das kostet, ist bekannt:</b> ein Eintrag von vor drei Monaten
/// zeigt den heutigen Stand, nicht den von damals. Ohne Netz bleibt der Bereich
/// leer, aber nicht wortlos.</para>
///
/// <para><b>Seit dem 07.09.2026 zeichnet hier eine Karte</b> (ADR-036).
/// Vorher baute diese Datei ihre Zeilen selbst und beschriftete sie maschinell
/// aus dem Feldnamen — „Letzte arbeit zeile" war hier der Normalfall und auf
/// der Gesprächskarte ein Fehler. Jetzt gilt <see cref="CardKind.History"/>,
/// und sie lässt sich im Designer bearbeiten wie die drei anderen.</para>
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// Bricht die vorige Abfrage ab, sobald eine neue beginnt.
    ///
    /// <para>Wer durch die Liste klickt, löste sonst je Eintrag einen Abruf aus,
    /// und die Antworten kämen in beliebiger Reihenfolge zurück — die Karte
    /// zeigte dann den Kontext eines Eintrags, den niemand mehr offen hat.
    /// Dasselbe Muster wie bei der Kontaktsuche, dort mit einem
    /// Generationszähler.</para>
    /// </summary>
    private CancellationTokenSource? _historyContextRequest;

    /// <summary>
    /// Der zuletzt geholte Schnappschuss — um die Karte nach einer Änderung im
    /// Designer neu zeichnen zu können, ohne die Quellen erneut zu fragen.
    /// </summary>
    private ContextSnapshot? _historySnapshot;

    /// <summary>Die Zeile, deren Bereich gerade offen ist.</summary>
    [ObservableProperty]
    private HistoryRow? _expandedHistory;

    /// <summary>
    /// Die aufgelöste Karte — was der Renderer zeichnet.
    ///
    /// Dieselbe Engine wie in der Gesprächsansicht und im Designer: eine
    /// Anzeige, die ihre Werte anders gewinnt als die anderen, ist die Stelle,
    /// an der sie auseinanderlaufen.
    /// </summary>
    [ObservableProperty]
    private CardModel _historyCard = CardModel.Empty;

    /// <summary>Quellen, zu denen es etwas zu sagen gibt — Fehler, Zeitgrenze, übersprungen.</summary>
    public ObservableCollection<CallerCardSource> HistoryContextSources { get; } = [];

    /// <summary>Ob gerade gefragt wird.</summary>
    [ObservableProperty]
    private bool _isLoadingHistoryContext;

    /// <summary>
    /// Was statt der Felder steht, wenn es keine gibt — nie ein leerer Kasten
    /// ohne Erklärung (§15).
    /// </summary>
    [ObservableProperty]
    private string _historyContextPlaceholder = string.Empty;

    /// <summary>Ob überhaupt etwas anzuzeigen ist.</summary>
    public bool HasHistoryContext =>
        HistoryCard.HasContent
        || HistoryContextSources.Count > 0
        || HistoryContextPlaceholder.Length > 0;

    /// <summary>
    /// Die Kopfzeile des Bereichs: Uhrzeit und Ergebnis zum offenen Eintrag.
    ///
    /// <para>Bewusst <b>nicht</b> Teil der Karte (ADR-036). Sie sagt, welcher
    /// Eintrag offen ist — der Bereich steht unter der Liste und nicht in der
    /// Zeile, und ohne diese Angabe stünden dort Werte ohne Bezug. Was sich
    /// wegkonfigurieren lässt, darf nicht die Frage beantworten, wozu das
    /// Übrige gehört.</para>
    /// </summary>
    public string ExpandedHistoryDetail
    {
        get
        {
            if (ExpandedHistory?.Entry is not { } entry)
            {
                return string.Empty;
            }

            var fakten = FactsOf(entry);

            return fakten.DurationText.Length > 0
                ? $"{fakten.TimeText} · {fakten.Outcome} · {fakten.DurationText}"
                : $"{fakten.TimeText} · {fakten.Outcome}";
        }
    }

    /// <summary>
    /// Die Anrufangaben eines Eintrags — für die Kopfzeile und für den
    /// Namensraum <c>call</c> auf der Karte.
    ///
    /// Einmal gebaut und an beiden Stellen benutzt: die Kopfzeile und die Karte
    /// sollen dieselbe Uhrzeit zeigen.
    /// </summary>
    private static CallFacts FactsOf(CallHistoryEntry entry) => new(
        entry.StartedAt,
        entry.Duration,
        CallOutcomeText.For(entry.Outcome, entry.Direction),
        CallOutcomeText.For(entry.Direction));

    /// <summary>
    /// Klappt einen Eintrag auf oder zu.
    ///
    /// <para>Ein zweiter Klick auf denselben Eintrag schliesst ihn — so wie ein
    /// Aufklappbereich sich überall sonst verhält.</para>
    ///
    /// <para><b>Und er gilt damit als gesehen</b> (ADR-035): das Abzeichen wird
    /// um eins kleiner, die Zeile verliert ihre Fettschrift. Vermerkt wird auch
    /// beim Schliessen — wer einen Eintrag angeklickt hat, hat ihn gesehen,
    /// unabhängig davon, was er danach tut.</para>
    /// </summary>
    public void ToggleHistoryDetails(HistoryRow? row)
    {
        if (row is not null)
        {
            MarkSeen(row);
        }

        if (row is null || ReferenceEquals(row, ExpandedHistory))
        {
            CollapseHistoryDetails();
            return;
        }

        ExpandedHistory = row;
        OnPropertyChanged(nameof(ExpandedHistoryDetail));

        _ = LoadHistoryContextAsync(row.Entry);
    }

    /// <summary>
    /// Vermerkt einen Eintrag als gesehen — in der Datenbank und in der Zeile.
    ///
    /// <para>Die Zeile wird <b>nicht</b> ausgetauscht: sie steckt gerade in der
    /// Auswahl der Liste, und ein Austausch mitten im Auswahlereignis nähme ihr
    /// die Markierung. Die Begründung steht vollständig an
    /// <see cref="HistoryRow"/>.</para>
    /// </summary>
    private void MarkSeen(HistoryRow row)
    {
        if (!row.IsNew)
        {
            return;
        }

        if (_history.MarkSeen(row.Id))
        {
            MissedCount = _history.CountMissed();
        }

        row.IsNew = false;
    }

    /// <summary>
    /// Vermerkt alles Offene als gesehen (§8.3).
    ///
    /// Ohne diesen Weg bliebe ein Abzeichen mit dreissig alten Einträgen nur
    /// über „Anrufliste löschen" wegzubekommen — also über den Verlust der
    /// Liste selbst.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MarkAllHistorySeen()
    {
        if (_history.MarkAllSeen() == 0)
        {
            return;
        }

        foreach (var zeile in History)
        {
            zeile.IsNew = false;
        }

        MissedCount = _history.CountMissed();
    }

    /// <summary>Schliesst den Bereich und bricht ab, was noch läuft.</summary>
    public void CollapseHistoryDetails()
    {
        CancelHistoryContextRequest();

        ExpandedHistory = null;
        _historySnapshot = null;
        HistoryCard = CardModel.Empty;
        HistoryContextSources.Clear();
        HistoryContextPlaceholder = string.Empty;
        IsLoadingHistoryContext = false;

        OnPropertyChanged(nameof(ExpandedHistoryDetail));
        OnPropertyChanged(nameof(HasHistoryContext));
    }

    private void CancelHistoryContextRequest()
    {
        var laufend = _historyContextRequest;
        _historyContextRequest = null;

        if (laufend is null)
        {
            return;
        }

        laufend.Cancel();
        laufend.Dispose();
    }

    private async Task LoadHistoryContextAsync(CallHistoryEntry entry)
    {
        CancelHistoryContextRequest();

        _historySnapshot = null;
        HistoryCard = CardModel.Empty;
        HistoryContextSources.Clear();
        HistoryContextPlaceholder = string.Empty;
        IsLoadingHistoryContext = true;
        OnPropertyChanged(nameof(HasHistoryContext));

        if (_callerContext is null)
        {
            IsLoadingHistoryContext = false;
            HistoryContextPlaceholder = "Es sind keine externen Quellen eingerichtet.";
            OnPropertyChanged(nameof(HasHistoryContext));
            return;
        }

        var request = new CancellationTokenSource();
        _historyContextRequest = request;

        ContextSnapshot schnappschuss;

        try
        {
            schnappschuss = await _callerContext
                .LookupNumberAsync(entry.Number, request.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Überholt — der Benutzer ist weitergeklickt. Der neue Aufruf hat
            // die Anzeige längst übernommen.
            return;
        }
        catch (Exception)
        {
            // ADR-045: Der Ausnahmetyp stand hier im Benutzertext —
            // «Die Quellen liessen sich nicht fragen (HttpRequestException).»
            // Ein Klassenname gibt niemandem etwas in die Hand; im Protokoll
            // steht er ohnehin.
            IsLoadingHistoryContext = false;
            HistoryContextPlaceholder =
                "Die Quellen liessen sich nicht fragen. Netzwerkverbindung prüfen; "
                + "Einzelheiten stehen im Protokoll.";
            OnPropertyChanged(nameof(HasHistoryContext));
            return;
        }

        // Zwischen Start und Antwort kann der Bereich geschlossen oder ein
        // anderer Eintrag geöffnet worden sein.
        if (!ReferenceEquals(request, _historyContextRequest))
        {
            return;
        }

        // Der Anruf selbst gehört zum Schnappschuss: eine Karte in der
        // Anrufliste darf `call.duration` und `call.outcome` ansprechen, und
        // die weiss nur diese Stelle.
        ApplyHistoryContext(schnappschuss with { Facts = FactsOf(entry) });
    }

    private void ApplyHistoryContext(ContextSnapshot snapshot)
    {
        IsLoadingHistoryContext = false;
        _historySnapshot = snapshot;

        foreach (var (id, fragment) in snapshot.Sources)
        {
            // Wie in der Gesprächsansicht: dass die lokalen Kontakte nichts
            // wussten, ist keine Nachricht. Der Name steht in der Zeile darüber.
            var stillLokal = string.Equals(id, "contacts", StringComparison.Ordinal)
                && fragment.State == SourceState.Empty;

            if (fragment.IsNotable() && !stillLokal)
            {
                HistoryContextSources.Add(
                    new CallerCardSource(fragment.DisplayName, fragment.State, fragment.Message));
            }
        }

        BuildHistoryCard();

        if (!HistoryCard.HasContent && HistoryContextSources.Count == 0)
        {
            HistoryContextPlaceholder = snapshot.Sources.Count == 0
                ? "Zu dieser Nummer wird nichts nachgeschlagen."
                : "Die Quellen wissen nichts zu dieser Nummer.";
        }

        OnPropertyChanged(nameof(HasHistoryContext));
    }

    /// <summary>
    /// Zeichnet die Karte aus dem letzten Schnappschuss.
    ///
    /// <para>Getrennt vom Abruf, weil sie zweimal gebraucht wird: nach der
    /// Antwort einer Quelle und nachdem im Designer eine neue Karte gespeichert
    /// wurde. Ohne den zweiten Weg zeigte ein offener Eintrag die Karte von
    /// vorher, und niemand verstünde, warum die Änderung nicht ankommt — genau
    /// der Grund, aus dem der <c>CardResolver</c> sein <c>Changed</c>
    /// hat.</para>
    /// </summary>
    private void BuildHistoryCard()
    {
        if (_cards is null || _historySnapshot is not { } schnappschuss)
        {
            HistoryCard = CardModel.Empty;
            return;
        }

        HistoryCard = CardLayoutEngine.Build(_cards.For(CardKind.History), schnappschuss);
    }

    private void OnHistoryCardChanged(object? sender, EventArgs e)
    {
        BuildHistoryCard();
        OnPropertyChanged(nameof(HasHistoryContext));
    }
}
