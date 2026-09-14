using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.App.Windows;

/// <summary>
/// Der Toast für eingehende Anrufe (§8.6, AP7.3).
///
/// „Annehmen / Ablehnen. Der Toast erscheint, auch wenn das Fenster
/// geschlossen ist." — durch die COM-Aktivierung des
/// <c>AppNotificationManager</c> gilt das sogar dann, wenn nipp gar nicht
/// läuft: Windows startet die App und reicht das Argument durch.
///
/// <b>Braucht eine Paketidentität.</b> Unpackaged funktioniert das nur, wenn
/// <c>Register()</c> die COM-Registrierung selbst anlegt — deshalb wird das
/// Scheitern hier nicht als Fehler behandelt, sondern protokolliert. Ein
/// Softphone ohne Toast ist unbequem, eines das deswegen nicht startet ist
/// kaputt.
/// </summary>
public sealed class ToastService : IDisposable
{
    private const string ArgumentKey = "nippAction";
    private const string CallKey = "nippCall";

    private readonly ISipService _sip;
    private readonly SettingsService _settings;
    private readonly CallerContextService _context;
    private readonly CardResolver _cards;
    private readonly CallPartyResolver _party;
    private readonly ILogger<ToastService> _logger;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, CallHandle> _shown = [];

    /// <summary>
    /// Was zuletzt im Toast eines Anrufs stand.
    ///
    /// <b>Damit er nicht blinkt:</b> jede Quelle meldet sich einzeln, und ein
    /// Toast wird ersetzt, indem er neu gezeigt wird. Ohne diesen Vergleich
    /// erschiene er bei einem Anruf drei- bis viermal neu auf dem Bildschirm —
    /// mit demselben Text.
    /// </summary>
    private readonly Dictionary<string, ToastLines> _lines = [];

    private bool _registered;
    private bool _disposed;

    public ToastService(
        ISipService sip,
        SettingsService settings,
        CallerContextService context,
        CardResolver cards,
        CallPartyResolver party,
        ILogger<ToastService> logger,
        DispatcherQueue dispatcher)
    {
        _sip = sip;
        _settings = settings;
        _context = context;
        _cards = cards;
        _party = party;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    /// <summary>Wird ausgelöst, wenn der Benutzer im Toast annimmt — das Fenster soll dann nach vorne.</summary>
    public event EventHandler? CallAccepted;

    /// <summary>
    /// Ein Klick auf den Toast selbst — das Fenster gehoert nach vorn (W1.3,
    /// Befund C3).
    /// </summary>
    public event EventHandler? OpenRequested;

    /// <summary>
    /// Ob ein eingehender Anruf als Benachrichtigung sichtbar wird (C2).
    ///
    /// <para><b>Wer das liest, fragt nach dem einzigen Zeichen.</b> nipp lebt
    /// im Infobereich; klingelt es und der Toast kommt nicht, gibt es ohne
    /// weiteres Zutun <b>gar kein</b> Zeichen. Genau daran haengt, ob das
    /// Fenster sich beim Klingeln selbst nach vorn holen muss — siehe
    /// <c>MainWindow.OnCallStateChanged</c>.</para>
    ///
    /// <para>Falsch heisst: <c>Register()</c> ist gescheitert. Das ist kein
    /// Ausnahmefall — packaged ist es bis heute so (T110), und deshalb wird
    /// unpackaged ausgeliefert.</para>
    /// </summary>
    public bool NotificationsAvailable => _registered;

    public void Start()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();

            _registered = true;
            _sip.CallStateChanged += OnCallStateChanged;
            _sip.AutoAnswerFailed += OnAutoAnswerFailed;

            // §8.6 und ADR-030: der Kontext kommt nach dem Toast und ersetzt
            // ihn. Nie umgekehrt — auf eine Quelle zu warten, hiesse den
            // Toast eines klingelnden Anrufs zu verzögern.
            _context.ContextChanged += OnContextChanged;

            ToastLog.Registered(_logger);
        }
        catch (Exception ex)
        {
            // §8.6 ist wichtig, aber nicht existenziell: ohne Toast muss das
            // Fenster offen sein, mehr nicht.
            ToastLog.RegistrationFailed(_logger, Describe(ex));
        }
    }

    /// <summary>
    /// Beschreibt eine Ausnahme so, dass sie im Protokoll etwas taugt.
    ///
    /// <para><c>Register()</c> wirft eine COM-Ausnahme, deren
    /// <c>Message</c> „Unbekannter Fehler" lautet — und mit dieser Auskunft
    /// steht man vor derselben Frage wie ohne sie. Das HRESULT unterscheidet
    /// eine fehlende Paketidentität von einer Registrierung, die nicht mehr
    /// zum gebauten Paket passt.</para>
    /// </summary>
    private static string Describe(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException com
            ? $"COMException 0x{com.HResult:X8} ({com.Message})"
            : $"{ex.GetType().Name}: {ex.Message}";

    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        if (!_registered)
        {
            return;
        }

        // Nur beim Übergang, nicht bei jeder Zustandsmeldung — sonst blinkt
        // der Toast im Sekundentakt neu auf.
        // Die Regel steht an CallStateEventArgs. Hier ausgeschrieben war sie
        // nie wahr — der Toast erschien nie, und damit fiel §8.6 aus ("Der
        // Toast erscheint, auch wenn das Fenster geschlossen ist"): der einzige
        // Hinweis, solange nipp im Infobereich lebt.
        if (e.IsNewIncoming)
        {
            // §8.6: „Auto-Annahme überspringt den Toast, spielt aber einen
            // kurzen Hinweiston." Den Ton spielt der Telefoniedienst beim
            // Annehmen; hier bleibt der Toast weg.
            if (_settings.Current.Advanced.AutoAnswer)
            {
                return;
            }

            Show(e.Call);
        }
        else if (e.Call.Status is not CallStatus.Incoming)
        {
            Hide(e.Call.Handle);
        }
    }

    /// <summary>
    /// Das automatische Annehmen ist gescheitert — der Toast wird nachgeholt.
    ///
    /// Ohne das bliebe der Anruf unsichtbar: die Benachrichtigung wurde wegen
    /// der Einstellung unterdrückt, das Fenster ist womoeglich geschlossen,
    /// und der Anruf klingelt bis zur Zeitüberschreitung ins Leere.
    /// </summary>
    private void OnAutoAnswerFailed(object? sender, CallInfo call) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (_registered && call.Status == CallStatus.Incoming)
            {
                Show(call);
            }
        });

    /// <summary>
    /// Der Anruferkontext ist da oder hat sich ergänzt (§21, ADR-030).
    ///
    /// <para>Ersetzt den Toast, der schon steht — der Anruf klingelt ja noch.
    /// <b>Nur wenn sich der Text wirklich ändert:</b> ein Ersetzen lässt den
    /// Toast neu aufscheinen, und bei zwei Quellen plus dem lokalen
    /// Adressbuch wären das drei Auftritte für denselben Inhalt.</para>
    /// </summary>
    private void OnContextChanged(object? sender, ContextSnapshot snapshot) =>
        _dispatcher.TryEnqueue(() =>
        {
            var tag = snapshot.Call.ToString();

            // Kein Toast (mehr) zu diesem Anruf: schon angenommen, abgelehnt,
            // beendet — oder wegen Auto-Annahme nie gezeigt. Dann ist auch
            // nichts nachzutragen.
            if (!_registered || !_shown.ContainsKey(tag))
            {
                return;
            }

            var call = _sip.ActiveCalls.FirstOrDefault(c => c.Handle == snapshot.Call);

            if (call is not { Status: CallStatus.Incoming })
            {
                return;
            }

            Publish(call, tag, Lines(call, snapshot));
        });

    /// <summary>
    /// Die Textzeilen zu einem Anruf. Ohne Kontext bleibt es bei Name oder
    /// Nummer — genau wie vor ADR-030.
    ///
    /// <para><b>Eine eingerichtete Toast-Karte schlägt die mitgelieferte
    /// Zusammensetzung</b> (K5, ADR-034). Ohne eigene Karte liefert der
    /// Auflöser eine leere, und es bleibt bei dem Verhalten, das am Gerät
    /// belegt ist.</para>
    /// </summary>
    private ToastLines Lines(CallInfo call, ContextSnapshot? snapshot) =>
        ToastComposer.Compose(
            snapshot ?? _context.SnapshotFor(call.Handle),
            _party.Describe(call),
            call.RemoteNumber,
            _cards.For(CardKind.Toast));

    private void Show(CallInfo call)
    {
        var tag = call.Handle.ToString();
        _shown[tag] = call.Handle;

        // Den Kontext gleich mitnehmen, falls er schon da ist: beide hängen am
        // selben CallStateChanged, und wer zuerst gerufen wird, entscheidet die
        // Reihenfolge der Abonnements. Bei einer zweiten Anfrage derselben
        // Nummer liegt die Antwort ohnehin im Zwischenspeicher (fünf Minuten).
        Publish(call, tag, Lines(call, snapshot: null));
    }

    private void Publish(CallInfo call, string tag, ToastLines lines)
    {
        // Derselbe Text ergibt keinen neuen Toast — siehe _lines.
        if (_lines.TryGetValue(tag, out var previous) && previous == lines)
        {
            return;
        }

        _lines[tag] = lines;

        // §8.6: läuft schon ein Gespräch, heisst die Schaltfläche
        // „Annehmen und halten" statt „Annehmen".
        var hasOther = _sip.ActiveCalls.Any(c => c.Handle != call.Handle);
        var acceptLabel = hasOther ? "Annehmen und halten" : "Annehmen";

        try
        {
            var builder = new AppNotificationBuilder()

                // Ein gewöhnlicher Toast verschwindet nach wenigen Sekunden,
                // wird vom Fokus-Assistenten unterdrückt und erscheint nicht
                // über einer Vollbildanwendung — für einen eingehenden Anruf
                // ist das genau der falsche Moment, unsichtbar zu sein.
                // IncomingCall bleibt stehen, bis jemand reagiert.
                .SetScenario(AppNotificationScenario.IncomingCall)

                // W1.3 (Befund C3): ein Klick auf den Toast selbst holt das
                // Fenster.
                //
                // <b>Windows-Konvention, und bis zum 13.09.2026 tat er
                // nichts.</b> Ohne Standardargument kam der Behandler ohne
                // ArgumentKey an und kehrte still zurueck — wer neben die
                // Knoepfe traf, sah gar keine Reaktion, und das Fenster blieb
                // im Infobereich. Gerade beim eingehenden Anruf ist der Toast
                // oft das einzige Zeichen, und die Flaeche dazwischen ist
                // groesser als jeder Knopf.
                .AddArgument(ArgumentKey, "open")
                .AddArgument(CallKey, tag)

                // Stumm: geklingelt wird über das Klingelgerät (§9.4), und ein
                // zweiter Ton aus der Benachrichtigung wäre nur lauter, nicht
                // deutlicher.
                .MuteAudio()

                // Zeile 1 ist wer anruft, nicht der Anlass: „Eingehender
                // Anruf" stand hier, bis der Kontext dazukam (ADR-030), und
                // kostete eine der drei Zeilen, die Windows zulässt. Dass es
                // ein Anruf ist, sagen die Knöpfe darunter.
                .AddText(lines.Line1)
                .AddButton(Styled(
                    new AppNotificationButton(acceptLabel)
                        .AddArgument(ArgumentKey, "accept")
                        .AddArgument(CallKey, tag),
                    AppNotificationButtonStyle.Success))
                .AddButton(Styled(
                    new AppNotificationButton("Ablehnen")
                        .AddArgument(ArgumentKey, "decline")
                        .AddArgument(CallKey, tag),
                    AppNotificationButtonStyle.Critical));

            // Die beiden Kontextzeilen, jede nur wenn sie etwas sagt. Windows
            // nimmt höchstens DREI Textelemente; mit Zeile 1 sind das genau
            // diese hier, und ein viertes würde der Builder ablehnen.
            if (lines.Line2 is { Length: > 0 } zweite)
            {
                builder.AddText(zweite);
            }

            if (lines.Line3 is { Length: > 0 } dritte)
            {
                builder.AddText(dritte);
            }

            // Die Nummer klein unter allem. Sie stand vorher in Zeile 1, wenn
            // kein Name bekannt war — dort steht sie weiter, denn dann ist sie
            // das Einzige, was man hat.
            if (lines.Attribution is { Length: > 0 } nummer && lines.HasContext)
            {
                builder.SetAttributionText(nummer);
            }

            var notification = builder.BuildNotification();

            notification.Tag = tag;
            notification.ExpiresOnReboot = true;

            AppNotificationManager.Default.Show(notification);

            // Ohne Feldinhalt und ohne Nummer (§21.2): wie viele Zeilen
            // dastanden, genügt zur Fehlersuche. Was darin stand, gehört nicht
            // in ein Protokoll, das später beim Support landet.
            ToastLog.Shown(_logger, lines.HasContext ? "mit Kontext" : "ohne Kontext");
        }
        catch (Exception ex)
        {
            ToastLog.ShowFailed(_logger, ex.Message);
        }
    }

    /// <summary>
    /// Färbt einen Knopf, sofern Windows farbige Toast-Knöpfe kennt — grün für
    /// „Annehmen", rot für „Ablehnen".
    /// <para>
    /// Die Prüfung ist nicht Zierde: <c>hint-buttonStyle</c> ist eine neuere
    /// Toast-Fähigkeit, und im Feld stehen Windows-10-Arbeitsplätze — von
    /// einem kam am 08.09.2026 die Absturzmeldung zur Update-Prüfung. Kennt
    /// Windows den Stil nicht, erscheint der Toast unverändert in Grau.
    /// </para>
    /// </summary>
    private static AppNotificationButton Styled(
        AppNotificationButton button, AppNotificationButtonStyle style)
    {
        if (!AppNotificationButton.IsButtonStyleSupported())
        {
            return button;
        }

        return button.SetButtonStyle(style);
    }

    private void Hide(CallHandle handle)
    {
        var tag = handle.ToString();

        _lines.Remove(tag);

        if (!_shown.Remove(tag))
        {
            return;
        }

        try
        {
            _ = AppNotificationManager.Default.RemoveByTagAsync(tag);
        }
        catch (Exception ex)
        {
            ToastLog.ShowFailed(_logger, ex.Message);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Kommt nicht auf dem UI-Thread — und der Telefonie-Dienst erwartet
        // genau den (§6).
        _dispatcher.TryEnqueue(async () =>
        {
            if (!args.Arguments.TryGetValue(ArgumentKey, out var action)
                || !args.Arguments.TryGetValue(CallKey, out var tag)
                || !_shown.TryGetValue(tag, out var handle))
            {
                return;
            }

            ToastLog.Invoked(_logger, action);

            try
            {
                switch (action)
                {
                    case "accept":
                        await _sip.AcceptAsync(handle).ConfigureAwait(true);
                        CallAccepted?.Invoke(this, EventArgs.Empty);
                        break;

                    case "decline":
                        await _sip.HangUpAsync(handle).ConfigureAwait(true);
                        break;

                    case "open":
                        // Ein Klick auf den Toast selbst: Fenster holen, mehr
                        // nicht. Den Anruf nimmt nur an, wer «Annehmen»
                        // trifft — ein Fehlgriff auf die Flaeche darf kein
                        // Gespraech entgegennehmen (ADR-049).
                        OpenRequested?.Invoke(this, EventArgs.Empty);
                        break;

                    default:
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                ToastLog.ActionFailed(_logger, action, ex.Message);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registered)
        {
            _sip.CallStateChanged -= OnCallStateChanged;
            _sip.AutoAnswerFailed -= OnAutoAnswerFailed;
            _context.ContextChanged -= OnContextChanged;

            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception)
            {
                // Beim Herunterfahren nicht mehr wichtig.
            }
        }
    }
}

internal static partial class ToastLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Benachrichtigungen angemeldet")]
    public static partial void Registered(ILogger logger);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning,
        Message = "Benachrichtigungen nicht verfuegbar: {Reason}. nipp laeuft weiter, "
            + "eingehende Anrufe erscheinen dann nur im offenen Fenster.")]
    public static partial void RegistrationFailed(ILogger logger, string reason);

    // Kein Name und keine Nummer: der Toast traegt seit ADR-030 den
    // Anruferkontext, und was darin steht, gehoert nicht in ein Protokoll,
    // das spaeter an den Support geht (Paragraph 21.2).
    [LoggerMessage(EventId = 3102, Level = LogLevel.Debug,
        Message = "Toast angezeigt, {Detail}")]
    public static partial void Shown(ILogger logger, string detail);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning,
        Message = "Toast liess sich nicht anzeigen: {Reason}")]
    public static partial void ShowFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Information,
        Message = "Toast-Aktion: {Action}")]
    public static partial void Invoked(ILogger logger, string action);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Warning,
        Message = "Toast-Aktion {Action} fehlgeschlagen: {Reason}")]
    public static partial void ActionFailed(ILogger logger, string action, string reason);
}
