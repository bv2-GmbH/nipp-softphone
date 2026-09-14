using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Das laufende Gespräch (§8.2, AP4.4–4.8).
///
/// Verwaltet bis zu zwei Gespräche: eines aktiv, eines gehalten, mit
/// sichtbarem Umschalten. Mehr als zwei lehnt der Dienst ab.
///
/// §6: kennt keine SDK-Typen. Der Architekturtest erzwingt das.
/// </summary>
public sealed partial class ActiveCallViewModel : ObservableObject, IDisposable
{
    private readonly ISipService _sip;

    /// <summary>
    /// Die Kontakte für die Vorschlagsliste beim Weiterleiten (§22.1).
    /// Dieselbe Quelle wie im Kontakte-Tab; nichts wird zusätzlich geladen.
    /// </summary>
    private readonly ContactStore _contacts;

    /// <summary>
    /// Die Präsenz der Team-Nebenstellen. <b>Sie ist der Grund, warum Team
    /// zuoberst steht:</b> beim Weiterverbinden ist „ist die Person überhaupt
    /// frei" die eigentliche Frage, und das Besetztlampenfeld hat die Antwort
    /// bereits abonniert (§8.4).
    /// </summary>
    private readonly BlfService _blf;
    private readonly CallPartyResolver _party;
    private readonly SettingsService _settings;
    private readonly ILogger<ActiveCallViewModel> _logger;
    private readonly Dictionary<CallHandle, CallQuality> _quality = [];
    private bool _disposed;

    /// <summary>
    /// Alle offenen Gespräche, höchstens zwei. Reihenfolge nach Aufbauzeit,
    /// damit das Umschalten nicht springt.
    ///
    /// <b>Zeilen, keine Anrufe</b> (ADR-043): eine <see cref="CallRow"/> bleibt
    /// dieselbe Instanz, solange das Gespräch läuft. Vorher wurde bei jedem
    /// Zustandswechsel die <c>CallInfo</c> in der Sammlung ausgetauscht, und
    /// die ListView verlor ihre Auswahl.
    /// </summary>
    public ObservableCollection<CallRow> Calls { get; } = [];

    /// <summary>
    /// Das gewählte Gespräch — geführt über die <b>Kennung</b>, nicht über die
    /// Objektreferenz.
    ///
    /// Der Grund ist ein Fehler, der in der Abnahme aufgetreten ist:
    /// <see cref="CallInfo"/> ist unveränderlich, jeder Zustandswechsel
    /// erzeugt eine neue Instanz. Eine ListView, die two-way an das Objekt
    /// gebunden ist, findet ihre Auswahl nach dem Austausch nicht wieder und
    /// schreibt <c>null</c> zurück — die Gesprächsansicht wurde leer, sobald
    /// man „Halten" drückte, während das Gespräch weiterlief.
    ///
    /// Über die Kennung kann das nicht passieren: sie überlebt jeden
    /// Zustandswechsel.
    /// </summary>
    private CallHandle? _selectedHandle;

    [ObservableProperty]
    private CallQuality? _selectedQuality;

    /// <summary>Die gewählte Zeile, aus der Kennung aufgelöst.</summary>
    public CallRow? SelectedRow =>
        _selectedHandle is { } handle
            ? Calls.FirstOrDefault(c => c.Handle == handle)
            : null;

    /// <summary>Das gewählte Gespräch, aus der Kennung aufgelöst.</summary>
    public CallInfo? SelectedCall => SelectedRow?.Call;

    /// <summary>
    /// Wählt ein Gespräch aus. Von der Oberfläche zu rufen; ein <c>null</c>
    /// wird ignoriert, solange noch Gespräche offen sind — sonst könnte ein
    /// Listenwechsel die Ansicht leeren.
    /// </summary>
    public void SelectCall(CallInfo? call)
    {
        if (call is null && Calls.Count > 0)
        {
            return;
        }

        SetSelected(call?.Handle);
    }

    /// <summary>Dasselbe, wenn die Ansicht eine Zeile in der Hand hat.</summary>
    public void SelectRow(CallRow? row) => SelectCall(row?.Call);

    private void SetSelected(CallHandle? handle)
    {
        if (_selectedHandle == handle)
        {
            return;
        }

        _selectedHandle = handle;

        SelectedQuality = handle is { } h && _quality.TryGetValue(h, out var quality)
            ? quality
            : null;

        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedCall));
        NotifyState();
    }

    [ObservableProperty]
    private string _dtmfInput = string.Empty;

    [ObservableProperty]
    private string _transferTarget = string.Empty;

    [ObservableProperty]
    private string? _lastError;

    public ActiveCallViewModel(
        ISipService sip,
        ContactStore contacts,
        BlfService blf,
        CallPartyResolver party,
        SettingsService settings,
        ILogger<ActiveCallViewModel> logger)
    {
        _sip = sip;
        _settings = settings;
        _contacts = contacts;
        _blf = blf;
        _party = party;
        _logger = logger;

        _sip.CallStateChanged += OnCallStateChanged;
        _sip.QualityUpdated += OnQualityUpdated;
        _sip.TransferCompleted += OnTransferCompleted;

        // Der Name aus einem fremden System trifft nach dem letzten
        // Zustandswechsel ein — ohne dieses Abonnement bliebe in der Zeile die
        // Nummer stehen.
        _party.PartyChanged += OnPartyChanged;

        // Den bereits laufenden Zustand übernehmen, nicht nur auf künftige
        // Ereignisse warten.
        //
        // Ohne das bleibt die Gesprächsansicht leer, wenn das ViewModel erst
        // beim ersten Öffnen der Seite entsteht — die Ereignisse des laufenden
        // Anrufs sind dann längst durch. Genau so ist es aufgetreten: Anruf
        // aufgebaut, auf „Gespräch" gewechselt, keine Schaltflächen.
        //
        // Die App erzeugt dieses ViewModel inzwischen beim Start, damit es von
        // Anfang an mithört. Diese Zeilen bleiben trotzdem: sie machen das
        // ViewModel unabhängig davon, wann es entsteht.
        foreach (var call in _sip.ActiveCalls)
        {
            Calls.Add(new CallRow(call, _party.Describe(call)));
        }

        SetSelected(Calls.FirstOrDefault()?.Handle);
    }

    public bool HasCall => Calls.Count > 0;

    /// <summary>Ob es ein zweites Gespräch gibt, zwischen dem gemakelt werden kann (§8.2).</summary>
    public bool CanSwap => Calls.Count > 1;

    public bool IsMuted => SelectedCall?.IsMuted ?? false;

    public bool IsOnHold => SelectedCall?.Status == CallStatus.OnHold;

    /// <summary>
    /// §8.2: ein sichtbarer Aufnahmeindikator ist Pflicht — in der Schweiz ist
    /// das Mitschneiden ohne Kenntnis der Gegenseite strafbar.
    /// </summary>
    public bool IsRecording => SelectedCall?.IsRecording ?? false;

    /// <summary>
    /// Ob begleitet weitergeleitet werden kann: dafür braucht es ein zweites
    /// Gespräch, bei dem angekündigt wurde (§8.2).
    /// </summary>
    public bool CanTransferAttended => Calls.Count > 1;

    /// <summary>
    /// Was in der Kopfzeile neben „Auflegen" steht.
    ///
    /// <para>Dort stand fest verdrahtet „Gespräch läuft weiter" — auch während
    /// ein Anruf klingelte, beim Wählen und im Leerzustand. Der Satz sollte
    /// erklären, dass das Gespräch beim Zurückgehen zur Wähltastatur nicht
    /// endet; in jedem anderen Zustand war er schlicht falsch, und einem
    /// Benutzer, der nipp zum ersten Mal sieht, fällt genau das auf.</para>
    /// </summary>
    public string StateCaption => CallStateCatalog.Caption(SelectedCall?.Status);

    /// <summary>
    /// Annehmen über den Knopf in der Gesprächsansicht.
    ///
    /// <para><b>Die Protokollzeile ist nicht Beiwerk.</b> Am 09.09.2026 legte
    /// nipp jedes angenommene Gespräch sofort wieder auf, und im Protokoll
    /// stand nur „Anruf … angenommen" — ohne Urheber. Ob der Benutzer die
    /// Taste am Headset gedrückt, im Toast geklickt oder hier gedrückt hatte,
    /// war nicht zu entscheiden; der Toast protokolliert seit immer, dieser
    /// Weg tat es nicht. Jede Benutzerhandlung, die einen Anrufzustand
    /// ändert, gehört ins Protokoll — mit Kennung, ohne Nummer (§21.2).</para>
    /// </summary>
    /// <summary>
    /// Führt eine Gesprächshandlung aus und macht aus ihrem Scheitern einen
    /// Satz statt eines Absturzes (ADR-053).
    ///
    /// <para><b>Was hier gefangen wird, ist kein Programmfehler, sondern ein
    /// Rennen.</b> <c>SipService</c> vergisst ein Gespräch bei <c>End</c> und
    /// <c>Released</c> — also bevor die Oberfläche nachgezogen hat. Die Knöpfe
    /// bleiben einen Wimpernschlag klickbar, und wer «Stumm» drückt, während
    /// die Gegenseite auflegt, trifft genau dieses Fenster. Bis zum 13.09.2026
    /// nahm das den Prozess mit: die Befehle liefen aus <c>async void</c>, und
    /// <c>App.OnUnhandledException</c> protokolliert nur.</para>
    ///
    /// <para>Weiterleiten und Aufnahme machten das schon einzeln; die Regel
    /// steht jetzt einmal, und alle sechs Befehle benutzen sie.</para>
    /// </summary>
    private async Task GuardedAsync(Func<Task> action) =>
        await GuardedTrueAsync(action).ConfigureAwait(true);

    /// <summary>
    /// Wie <see cref="GuardedAsync"/>, sagt aber, ob es geklappt hat — für
    /// Handlungen, die aus mehreren Schritten bestehen (Makeln) oder deren
    /// Anzeige davon abhängt (Tastentöne).
    /// </summary>
    private async Task<bool> GuardedTrueAsync(Func<Task> action)
    {
        LastError = null;

        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            // Die Meldung kommt aus SipService und sagt bereits, was gilt und
            // was zu tun ist — sie wird hier nicht noch einmal verpackt.
            LastError = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (SelectedCall is { Status: CallStatus.Incoming } call)
        {
            ActiveCallLog.AcceptPressed(_logger, call.Handle.ToString());
            await GuardedAsync(() => _sip.AcceptAsync(call.Handle)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task HangUpAsync()
    {
        if (SelectedCall is { } call)
        {
            ActiveCallLog.HangUpPressed(_logger, call.Handle.ToString());
            await GuardedAsync(() => _sip.HangUpAsync(call.Handle)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        if (SelectedCall is { } call)
        {
            await GuardedAsync(() => _sip.SetMutedAsync(call.Handle, !call.IsMuted))
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleHoldAsync()
    {
        if (SelectedCall is { } call)
        {
            await GuardedAsync(
                () => _sip.SetHoldAsync(call.Handle, call.Status != CallStatus.OnHold))
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Makeln: das gewählte Gespräch aktiv setzen, das andere halten. §8.2
    /// verlangt, dass das Umschalten sichtbar ist — die Oberfläche zeigt beide
    /// Gespräche mit ihrem Zustand.
    /// </summary>
    [RelayCommand]
    private async Task SwapAsync()
    {
        if (Calls.Count < 2 || SelectedCall is not { } current)
        {
            return;
        }

        var other = Calls.First(c => c.Handle != current.Handle).Call;

        // Erst das laufende halten, dann das andere holen. Umgekehrt hätte man
        // kurz zwei aktive Gespräche und damit zwei offene Mikrofone.
        //
        // Beide Schritte unter einem Wächter: scheitert der erste, wird der
        // zweite gar nicht erst versucht — sonst stünden am Ende zwei aktive
        // Gespräche, und das ist der Zustand, den die Reihenfolge gerade
        // vermeiden soll.
        var erfolg = true;

        if (current.Status != CallStatus.OnHold)
        {
            erfolg = await GuardedTrueAsync(() => _sip.SetHoldAsync(current.Handle, true))
                .ConfigureAwait(true);
        }

        if (erfolg && other.Status == CallStatus.OnHold)
        {
            erfolg = await GuardedTrueAsync(() => _sip.SetHoldAsync(other.Handle, false))
                .ConfigureAwait(true);
        }

        if (erfolg)
        {
            SetSelected(other.Handle);
        }
    }

    /// <summary>
    /// Vorschläge für das Übergabeziel (§22.1).
    ///
    /// <para><b>Team-Nebenstellen zuoberst, mit ihrer Präsenzlampe.</b> Beim
    /// Weiterverbinden ist „ist die Person überhaupt frei" die eigentliche
    /// Frage; das Besetztlampenfeld hat die Antwort schon abonniert, sie kostet
    /// also nichts extra. Danach die übrigen Kontakte.</para>
    ///
    /// <para><b>Die freie Eingabe bleibt.</b> Eine externe Nummer muss ohne
    /// Umweg eingetippt werden können — die Liste ergänzt das Feld, sie ersetzt
    /// es nicht (ADR-023).</para>
    /// </summary>
    public ObservableCollection<ContactRow> TransferSuggestions { get; } = [];

    /// <summary>Ob es gerade Vorschläge gibt.</summary>
    public bool HasTransferSuggestions => TransferSuggestions.Count > 0;

    /// <summary>Höchstens so viele — mehr passen unter das Feld nicht.</summary>
    private const int MaxTransferSuggestions = 5;

    /// <summary>
    /// Baut die Vorschlagsliste neu.
    ///
    /// <para>Bei leerem Feld stehen die Team-Nebenstellen da: das ist der
    /// häufige Fall beim Weiterverbinden, und wer sie sucht, soll nicht erst
    /// tippen müssen. Ab dem ersten Zeichen wird gesucht, Team weiterhin
    /// zuerst.</para>
    /// </summary>
    private void UpdateTransferSuggestions()
    {
        TransferSuggestions.Clear();

        var query = TransferTarget.Trim();
        var eigene = SelectedCall?.RemoteNumber;

        var treffer = query.Length == 0
            ? _contacts.Contacts.Where(static c => c.Source == ContactSourceKind.Team)
            : _contacts.Search(query);

        // Team zuerst, danach der Rest — innerhalb der Gruppen bleibt die
        // Reihenfolge, die der Store liefert (das Team ist von Hand sortiert).
        foreach (var contact in treffer.OrderBy(static c => c.Source == ContactSourceKind.Team ? 0 : 1))
        {
            if (contact.PrimaryNumber is not { Length: > 0 } nummer)
            {
                continue;
            }

            // Nicht an den Anrufer selbst zurückgeben: das Gespräch, das gerade
            // läuft, ist kein sinnvolles Übergabeziel.
            if (eigene is { Length: > 0 } && ClipResolver.IsSameNumber(nummer, eigene))
            {
                continue;
            }

            TransferSuggestions.Add(new ContactRow(contact, _blf.StatusOf(contact.SipAddress)));

            if (TransferSuggestions.Count == MaxTransferSuggestions)
            {
                break;
            }
        }

        OnPropertyChanged(nameof(HasTransferSuggestions));
    }

    /// <summary>
    /// Ob blind weitergeleitet werden kann: es braucht ein Gespräch und ein
    /// Ziel.
    ///
    /// <para>Fehlte bisher als Regel — der Knopf war auch bei leerem Zielfeld
    /// aktiv und tat dann nichts. Ein Knopf, der ohne Erklärung nicht reagiert,
    /// ist schlimmer als ein grauer.</para>
    /// </summary>
    public bool CanTransferBlind =>
        SelectedCall is not null && !string.IsNullOrWhiteSpace(TransferTarget);

    /// <summary>Blindes Weiterleiten: sofort abgeben, ohne Rückfrage (§8.2).</summary>
    [RelayCommand(CanExecute = nameof(CanTransferBlind))]
    private async Task TransferBlindAsync()
    {
        if (SelectedCall is not { } call || string.IsNullOrWhiteSpace(TransferTarget))
        {
            return;
        }

        LastError = null;

        try
        {
            await _sip.TransferAsync(call.Handle, TransferTarget.Trim(), TransferMode.Blind)
                .ConfigureAwait(true);
            TransferTarget = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>
    /// Begleitetes Weiterleiten: die beiden bestehenden Gespräche
    /// zusammenschalten. Setzt voraus, dass beim Ziel schon angekündigt wurde
    /// (§8.2) — deshalb braucht es zwei Gespräche.
    /// </summary>
    [RelayCommand]
    private async Task TransferAttendedAsync()
    {
        if (SelectedCall is not { } call)
        {
            return;
        }

        LastError = null;

        try
        {
            await _sip.TransferAsync(call.Handle, string.Empty, TransferMode.Attended)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            // Die Meldung erklärt, dass zuerst das Ziel angerufen werden muss.
            LastError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendDtmfAsync(string digit)
    {
        if (SelectedCall is not { } call || string.IsNullOrEmpty(digit))
        {
            return;
        }

        if (await GuardedTrueAsync(() => _sip.SendDtmfAsync(call.Handle, digit[0]))
            .ConfigureAwait(true))
        {
            // Die Eingabe nur mitschreiben, wenn der Ton auch hinausging —
            // sonst zeigt das Feld eine Ziffernfolge, die die Gegenseite nie
            // gehört hat.
            DtmfInput += digit;
        }
    }

    /// <summary>
    /// Aufnahme starten oder beenden. Namensschema nach §8.2:
    /// <c>JJJJ-MM-TT_HHMMSS_&lt;Nummer&gt;.wav</c>.
    /// </summary>
    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (SelectedCall is not { } call)
        {
            return;
        }

        LastError = null;

        try
        {
            if (call.IsRecording)
            {
                await _sip.StopRecordingAsync(call.Handle).ConfigureAwait(true);
                return;
            }

            // Der Pfad wird nicht hier gebildet: er muss beim Aufbau des
            // Anrufs feststehen, weil Call.Params read-only sind. Der Dienst
            // legt ihn aus RecordingDirectory und der Gegenstelle an.
            var started = await _sip.StartRecordingAsync(call.Handle).ConfigureAwait(true);

            if (!started)
            {
                LastError = "Die Aufnahme liess sich für dieses Gespräch nicht starten — es "
                    + "wurde ohne Aufnahmemöglichkeit aufgebaut. Wer von Anfang an aufzeichnen "
                    + "will, prüft vorher den Aufnahmeordner in den Einstellungen.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Die Aufnahme liess sich nicht starten: {ex.Message} Aufnahmeordner prüfen.";
        }
    }

    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        var index = IndexOf(e.Call.Handle);

        if (!e.Call.IsActive)
        {
            if (index >= 0)
            {
                Calls.RemoveAt(index);
            }

            _quality.Remove(e.Call.Handle);

            if (_selectedHandle == e.Call.Handle)
            {
                SetSelected(Calls.FirstOrDefault()?.Handle);
            }

            _party.Forget(e.Call.Handle);

            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(SelectedCall));
            NotifyState();
            return;
        }

        if (index >= 0)
        {
            // Die Zeile bleibt, ihr Inhalt wechselt. Vorher wurde hier die
            // Instanz in der Sammlung ausgetauscht — genau das, was der
            // Ansicht die Auswahl nahm.
            Calls[index].Call = e.Call;
            Calls[index].Label = _party.Describe(e.Call);
        }
        else
        {
            Calls.Add(new CallRow(e.Call, _party.Describe(e.Call)));

            // Ein neues Gespräch wird gewählt, wenn keines läuft. Sonst bleibt
            // die Wahl beim laufenden — ein zweiter eingehender Anruf soll die
            // Ansicht nicht mitten im Gespräch wegreissen.
            if (_selectedHandle is null)
            {
                SetSelected(e.Call.Handle);
            }
        }

        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedCall));
        NotifyState();
    }

    /// <summary>
    /// Eine fremde Quelle hat einen Namen nachgeliefert — die betroffene Zeile
    /// bekommt ihn.
    /// </summary>
    private void OnPartyChanged(object? sender, CallHandle handle)
    {
        var index = IndexOf(handle);

        if (index >= 0)
        {
            Calls[index].Label = _party.Describe(Calls[index].Call);
        }
    }

    /// <summary>
    /// Was aus der Weiterleitung geworden ist (W1.2, Befund B5).
    ///
    /// <para>Bis zum 13.09.2026 gab es diese Antwort nicht: das Feld leerte
    /// sich, die Ansicht sah aus wie Erfolg, und ein abgelehnter REFER liess
    /// das Gespräch je nach Anlage gehalten stehen — ohne ein Wort.</para>
    ///
    /// <para><b>Nur der Fehlschlag wird gemeldet.</b> Gelingt die Übergabe,
    /// endet das eigene Gespräch, und die Ansicht schliesst sich ohnehin; eine
    /// Erfolgsmeldung wäre ein Hinweis auf etwas, das man gerade sieht.</para>
    /// </summary>
    private void OnTransferCompleted(object? sender, TransferResultEventArgs e)
    {
        if (e.Outcome != TransferOutcome.Failed)
        {
            return;
        }

        LastError = "Die Weiterleitung wurde nicht angenommen. Das Gespräch läuft weiter — "
            + "Nummer prüfen und noch einmal versuchen.";
    }

    private void OnQualityUpdated(object? sender, CallQualityEventArgs e)
    {
        _quality[e.Handle] = e.Quality;

        if (_selectedHandle == e.Handle)
        {
            SelectedQuality = e.Quality;
        }
    }

    private int IndexOf(CallHandle handle)
    {
        for (var i = 0; i < Calls.Count; i++)
        {
            if (Calls[i].Handle == handle)
            {
                return i;
            }
        }

        return -1;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasCall));
        OnPropertyChanged(nameof(CanSwap));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsOnHold));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanTransferAttended));
        OnPropertyChanged(nameof(StateCaption));
        OnPropertyChanged(nameof(CanTransferBlind));
        TransferBlindCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Ein geändertes Übergabeziel entscheidet mit, ob blind weitergeleitet
    /// werden kann.
    /// </summary>
    partial void OnTransferTargetChanged(string value)
    {
        OnPropertyChanged(nameof(CanTransferBlind));
        TransferBlindCommand.NotifyCanExecuteChanged();
        UpdateTransferSuggestions();
    }

    /// <summary>
    /// Übernimmt einen Vorschlag ins Zielfeld — gewählt wird damit noch nicht.
    ///
    /// <para>Der zweite Schritt bleibt bewusst: „Sofort abgeben" und „Erst
    /// ankündigen" sind zwei verschiedene Dinge, und ein Klick auf einen Namen
    /// darf nicht raten, welches gemeint war.</para>
    /// </summary>
    public void PickTransferTarget(ContactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Contact.PrimaryNumber is { Length: > 0 } nummer)
        {
            TransferTarget = nummer;
        }
    }

    /// <summary>Baut die Vorschläge neu — beim Öffnen des Übergabebereichs.</summary>
    public void RefreshTransferSuggestions() => UpdateTransferSuggestions();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sip.CallStateChanged -= OnCallStateChanged;
        _sip.QualityUpdated -= OnQualityUpdated;
        _sip.TransferCompleted -= OnTransferCompleted;
        _party.PartyChanged -= OnPartyChanged;
    }

    // --- Wiedergabegerät im Gespräch (ADR-046) ---

    /// <summary>
    /// Die Geräte, über die das Gespräch laufen kann.
    ///
    /// <para><b>Warum das hier steht und nicht nur in den Einstellungen.</b>
    /// Das Wechseln des Audiogeräts ist der häufigste Einstellungswechsel eines
    /// Softphone-Benutzers — Headset angesteckt, Headset abgezogen,
    /// Dockingstation verlassen — und es war fünf Klicks entfernt, hinter einer
    /// Gruppe auf einer Seite, die man im Gespräch gar nicht sehen will. nipp
    /// meldet den Gerätewechsel längst von selbst in der Hinweisleiste; die
    /// Handlung darauf lag nur woanders.</para>
    ///
    /// <para>Jedes Mal frisch gefragt und nicht zwischengespeichert: Geräte
    /// kommen und gehen, und genau in dem Moment, in dem jemand diese Liste
    /// öffnet, hat sich meistens gerade etwas geändert.</para>
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> PlaybackDevices =>
        [.. _sip.GetAudioDevices().Where(static d => d.CanPlay)];

    /// <summary>Welches Gerät gerade eingestellt ist — für den Haken im Menü.</summary>
    public string? SelectedPlaybackDeviceId => _settings.Current.Audio.OutputDeviceId;

    /// <summary>
    /// Stellt das Wiedergabegerät um.
    ///
    /// <para>Über die Einstellungen und nicht am SDK vorbei: <c>Save</c> löst
    /// <c>Changed</c> aus, und daran hängt das Anwenden. Ein zweiter Weg zum
    /// selben Ziel wäre die nächste Doppelwahrheit — und der hier überlebt
    /// ausserdem den Neustart, was ein Benutzer nach einem Gerätewechsel
    /// erwartet.</para>
    /// </summary>
    public void UsePlaybackDevice(string? deviceId)
    {
        var jetzt = _settings.Current;

        if (string.Equals(jetzt.Audio.OutputDeviceId, deviceId, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Save(jetzt with { Audio = jetzt.Audio with { OutputDeviceId = deviceId } });

        OnPropertyChanged(nameof(SelectedPlaybackDeviceId));
    }

    /// <summary>
    /// Die Mikrofone — dieselbe Frage wie bei der Wiedergabe (W2.5, C5).
    ///
    /// <para><b>Der Befund.</b> Im Gespräch liess sich nur die Wiedergabe
    /// umstellen. Wer vom Notebook-Audio aufs Headset wechselte, hörte am
    /// Headset und sprach ins Notebook — und der Weg zum Mikrofon führte über
    /// die Einstellungen, also aus dem Gespräch heraus. Die halbe Antwort ist
    /// hier schlimmer als keine: sie sieht aus, als wäre das Gerät
    /// umgestellt.</para>
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> CaptureDevices =>
        [.. _sip.GetAudioDevices().Where(static d => d.CanRecord)];

    /// <summary>Welches Mikrofon eingestellt ist — für den Haken im Menü.</summary>
    public string? SelectedCaptureDeviceId => _settings.Current.Audio.InputDeviceId;

    /// <summary>Stellt das Mikrofon um. Derselbe Weg wie bei der Wiedergabe.</summary>
    public void UseCaptureDevice(string? deviceId)
    {
        var jetzt = _settings.Current;

        if (string.Equals(jetzt.Audio.InputDeviceId, deviceId, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Save(jetzt with { Audio = jetzt.Audio with { InputDeviceId = deviceId } });

        OnPropertyChanged(nameof(SelectedCaptureDeviceId));
    }
}

/// <summary>
/// Was der Benutzer in der Gesprächsansicht gedrückt hat.
///
/// <para>Nur die beiden Handlungen, die einen Anruf beginnen oder beenden.
/// Stumm, Halten und Makeln ändern nichts, was hinterher mit „warum ist das
/// Gespräch weg" zu verwechseln wäre.</para>
/// </summary>
internal static partial class ActiveCallLog
{
    [LoggerMessage(EventId = 2110, Level = LogLevel.Information,
        Message = "Annehmen in der Gespraechsansicht gedrueckt ({Call})")]
    public static partial void AcceptPressed(ILogger logger, string call);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Information,
        Message = "Auflegen in der Gespraechsansicht gedrueckt ({Call})")]
    public static partial void HangUpPressed(ILogger logger, string call);
}
