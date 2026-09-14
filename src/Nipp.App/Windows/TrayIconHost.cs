using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using Nipp.App.Theming;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.App.Windows;

/// <summary>
/// Das Symbol im Infobereich (§10, AP7.5).
///
/// „Kontextmenü: Öffnen, Präsenz setzen, Stumm, Beenden. Schliessen des
/// Fensters beendet die App nicht."
///
/// §20.4: das Symbol wechselt mit dem Erscheinungsbild — auf dunkler
/// Taskleiste ist die helle Fassung unlesbar und umgekehrt.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly ISipService _sip;
    private readonly ThemeService _theme;
    private readonly CallPartyResolver _party;
    private readonly SettingsService _settings;
    private readonly ILogger<TrayIconHost> _logger;

    private TrayIconWithContextMenu? _icon;

    /// <summary>
    /// Das Symbol, dessen Handle der Infobereich zeichnet. <b>Muss am Leben
    /// bleiben</b>, solange es angezeigt wird — siehe <see cref="LoadIcon"/>.
    /// </summary>
    private System.Drawing.Icon? _iconResource;

    private bool _disposed;

    public TrayIconHost(
        ISipService sip,
        ThemeService theme,
        CallPartyResolver party,
        SettingsService settings,
        ILogger<TrayIconHost> logger)
    {
        _sip = sip;
        _theme = theme;
        _party = party;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Der Benutzer will das Fenster sehen.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Der Benutzer will nipp wirklich beenden.</summary>
    public event EventHandler? ExitRequested;

    public void Start()
    {
        try
        {
            _iconResource = LoadIcon();

            _icon = new TrayIconWithContextMenu
            {
                Icon = _iconResource.Handle,
                ToolTip = "nipp",
            };

            BuildMenu();

            _icon.MessageWindow.MouseEventReceived += OnMouseEvent;

            // <b>Der eigentliche Weg zurueck ins Fenster.</b> Siehe
            // OnKeyboardEvent — ein Klick auf das Symbol kommt hier an, nicht
            // beim Mausereignis.
            _icon.MessageWindow.KeyboardEventReceived += OnKeyboardEvent;

            _icon.Create();

            _sip.RegistrationChanged += OnRegistrationChanged;
            _sip.CallStateChanged += OnCallStateChanged;

            // ADR-055: das Menue traegt die verbleibende Zeit, also gehoert es
            // neu gebaut, wenn «Nicht stoeren» sich aendert — auch beim
            // Ablaufen, das niemand angeklickt hat.
            _sip.DoNotDisturbChanged += OnDoNotDisturbChanged;

            // Der Name aus einem fremden System kommt nach dem letzten
            // Zustandswechsel — ohne dieses Abonnement bliebe im Tooltip die
            // Nummer stehen (ADR-043).
            _party.PartyChanged += OnPartyChanged;
            _theme.EffectiveThemeChanged += OnThemeChanged;

            TrayLog.Started(_logger);
        }
        catch (Exception ex)
        {
            // Ohne Infobereich-Symbol läuft nipp weiter — es ist Bequemlichkeit,
            // keine Voraussetzung.
            TrayLog.StartFailed(_logger, ex.Message);
        }
    }

    /// <summary>
    /// Ein Klick auf das Symbol im Infobereich (§10) — und <b>hier</b> kommt er
    /// an, nicht beim Mausereignis.
    ///
    /// <para><b>Der Befund vom 07.09.2026.</b> „Ich habe die App geschlossen
    /// und kann sie nicht mehr öffnen per Doppelklick auf das Tray-Symbol."
    /// Abonniert war nur <c>MouseEventReceived</c> mit
    /// <c>IconLeftDoubleClick</c> — ein Ereignis, das nie eintrifft.</para>
    ///
    /// <para><b>Warum nicht.</b> Ein Symbol im Infobereich läuft in einem von
    /// zwei Nachrichtenmodi, und H.NotifyIcon wählt ohne Zutun
    /// <c>NOTIFYICON_VERSION_4</c> (die Eigenschaft <c>Version</c> steht auf
    /// <c>Vista</c> und hat einen <b>privaten</b> Setter). In diesem Modus
    /// schickt die Shell keine Mausnachrichten mehr, sondern
    /// <c>NIN_SELECT</c> — und das meldet die Bibliothek als
    /// <c>KeyboardEvent.Select</c>, also über ein ganz anderes Ereignis. Ein
    /// Doppelklick existiert dort begrifflich nicht.</para>
    ///
    /// <para><b>Nachgemessen</b>, indem die Nachrichten von Hand an das
    /// MessageWindow geschickt wurden: <c>WM_LBUTTONDBLCLK</c> ergibt
    /// <c>IconDoubleClick</c> und <c>IconLeftDoubleClick</c> (der alte Handler
    /// hätte also funktioniert — im klassischen Modus, den nipp nicht
    /// benutzt), <c>NIN_SELECT</c> ergibt beim Mausereignis <b>nichts</b>.</para>
    ///
    /// <para><c>KeySelect</c> ist derselbe Vorgang über die Tastatur — das
    /// Symbol mit den Pfeiltasten anwählen und Eingabe drücken. Auch das soll
    /// das Fenster holen; <c>ContextMenu</c> nicht, das gehört dem Menü.</para>
    /// </summary>
    private void OnKeyboardEvent(object? sender, MessageWindow.KeyboardEventReceivedEventArgs e)
    {
        TrayLog.MouseEvent(_logger, e.KeyboardEvent.ToString());

        if (e.KeyboardEvent is KeyboardEvent.Select or KeyboardEvent.KeySelect)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Ein Mausereignis am Symbol — im klassischen Nachrichtenmodus.
    ///
    /// <para>Bleibt abonniert, obwohl nipp im neuen Modus läuft und dort
    /// nichts davon eintrifft: welchen Modus die Bibliothek wählt, ist ihre
    /// Entscheidung und kann sich mit einer neuen Fassung ändern. Beide Wege
    /// führen zum selben Ziel, und der doppelte Aufruf schadet nicht —
    /// <c>ShowFromTray</c> ist wiederholbar.</para>
    ///
    /// <para>Jedes empfangene Ereignis steht auf Debug im Protokoll. Ohne das
    /// war „der Doppelklick tut nichts" nicht von „hier kommt gar nichts an"
    /// zu unterscheiden, und genau daran hing dieser Befund.</para>
    /// </summary>
    private void OnMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        TrayLog.MouseEvent(_logger, e.MouseEvent.ToString());

        // Rechts und Mitte gehören dem Kontextmenü — nur Links und der
        // allgemeine Doppelklick holen das Fenster.
        var oeffnen = e.MouseEvent
            is MouseEvent.IconLeftMouseUp
            or MouseEvent.IconLeftDoubleClick
            or MouseEvent.IconDoubleClick;

        if (oeffnen)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// «Nicht stören» hat sich geändert — das Menü trägt die verbleibende
    /// Zeit und gehört neu gebaut (ADR-055).
    ///
    /// <para>Ohne Marshalling, wie die Nachbarn: gesetzt wird es vom
    /// Kontextmenü oder vom Pump, und beide laufen auf dem UI-Thread.</para>
    /// </summary>
    private void OnDoNotDisturbChanged(object? sender, DoNotDisturb zustand) => BuildMenu();

    private void BuildMenu()
    {
        if (_icon is null)
        {
            return;
        }

        // Der Eintrag sagt, was er tut, und ist tot, wenn er nichts tun kann
        // (C10).
        //
        // <b>Vorher stand dort immer «Stumm schalten»</b>, und ohne laufendes
        // Gespraech tat ein Klick darauf stillschweigend nichts. Ein Klick
        // ohne Wirkung und ohne Wort ist von einem Fehler nicht zu
        // unterscheiden — dieselbe Luecke, die dieses Projekt bei den
        // HID-Reports und beim Infobereich-Symbol zweimal teuer bezahlt hat,
        // nur trifft sie hier den Benutzer.
        var gespraech = _sip.ActiveCalls.FirstOrDefault(c => c.Status == CallStatus.Connected);

        var stumm = new PopupMenuItem(
            gespraech is { IsMuted: true } ? "Stummschaltung aufheben" : "Stumm schalten",
            async (_, _) => await ToggleMuteAsync())
        {
            Enabled = gespraech is not null,
        };

        var menu = new PopupMenu
        {
            Items =
            {
                new PopupMenuItem("Öffnen", (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty)),
                new PopupMenuSeparator(),
                stumm,
            },
        };

        // Das Wiedergabegerät auch von hier aus (ADR-046) — ohne das Fenster zu
        // öffnen. nipp lebt im Infobereich (§10), und genau dort steht man,
        // wenn das Headset gerade abgezogen wurde.
        //
        // Als flache Einträge und nicht als Untermenü: H.NotifyIcon kennt keines,
        // und bei drei bis vier Geräten wäre es ohnehin ein Klick zu viel.
        foreach (var eintrag in PlaybackMenuItems())
        {
            menu.Items.Add(eintrag);
        }

        // §10, ADR-055: «Nicht stören». Hier und nicht in den Einstellungen —
        // wer in eine Besprechung geht, hat keine Zeit für zwei Ebenen, und
        // nipp lebt im Infobereich.
        menu.Items.Add(new PopupMenuSeparator());

        foreach (var eintrag in NichtStoerenMenuItems())
        {
            menu.Items.Add(eintrag);
        }

        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(new PopupMenuItem("Beenden", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon.ContextMenu = menu;
    }

    /// <summary>
    /// Die Wiedergabegeräte als Menüeinträge — mit einem Haken am aktiven.
    ///
    /// <para>Gibt es nur eines, steht gar keines da: eine Wahl zwischen einer
    /// Möglichkeit ist keine, und das Menü im Infobereich soll kurz bleiben.</para>
    /// </summary>
    private IEnumerable<PopupItem> PlaybackMenuItems()
    {
        var geraete = _sip.GetAudioDevices().Where(static d => d.CanPlay).ToList();

        if (geraete.Count < 2)
        {
            yield break;
        }

        yield return new PopupMenuSeparator();

        // Wofuer die Liste gilt (C10). Ohne diese Zeile standen dort vier
        // Geraetenamen ohne ein Wort darueber — Mikrofon? Klingelgeraet?
        // Wiedergabe? Ein toter Eintrag, weil er nur beschriftet.
        yield return new PopupMenuItem("Wiedergabe", static (_, _) => { }) { Enabled = false };

        var aktiv = _settings.Current.Audio.OutputDeviceId;

        yield return new PopupMenuItem(
            aktiv is null ? "✓ Windows-Standard" : "Windows-Standard",
            (_, _) => UsePlaybackDevice(null));

        foreach (var geraet in geraete)
        {
            var gewaehlt = string.Equals(geraet.Id, aktiv, StringComparison.Ordinal);
            var id = geraet.Id;

            yield return new PopupMenuItem(
                gewaehlt ? $"✓ {geraet.Name}" : geraet.Name,
                (_, _) => UsePlaybackDevice(id));
        }
    }

    /// <summary>
    /// Stellt das Wiedergabegerät um — über die Einstellungen, wie überall
    /// sonst: <c>Save</c> löst <c>Changed</c> aus, und daran hängt das
    /// Anwenden.
    /// </summary>
    private void UsePlaybackDevice(string? deviceId)
    {
        var jetzt = _settings.Current;

        if (string.Equals(jetzt.Audio.OutputDeviceId, deviceId, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Save(jetzt with { Audio = jetzt.Audio with { OutputDeviceId = deviceId } });

        // Das Menü trägt den Haken; es muss neu gebaut werden, damit er wandert.
        BuildMenu();
    }

    private async Task ToggleMuteAsync()
    {
        // §10 nennt „Stumm" im Kontextmenü. Sinnvoll ist es nur mit laufendem
        // Gespräch — ohne eines passiert schlicht nichts, statt eine
        // Fehlermeldung zu zeigen, die niemand gerufen hat.
        var call = _sip.ActiveCalls.FirstOrDefault(c => c.Status == CallStatus.Connected);

        if (call is not null)
        {
            await _sip.SetMutedAsync(call.Handle, !call.IsMuted).ConfigureAwait(false);
        }
    }

    private void OnRegistrationChanged(object? sender, RegistrationChangedEventArgs e) =>
        UpdateToolTip();

    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        UpdateToolTip();

        // Und das Menü: «Stumm schalten» hängt am laufenden Gespräch (C10).
        // Ein Menü, das beim Start gebaut wird und danach nie wieder, zeigt
        // den Zustand von damals.
        BuildMenu();
    }

    private void OnPartyChanged(object? sender, CallHandle handle) =>
        UpdateToolTip();

    private void UpdateToolTip()
    {
        if (_icon is null)
        {
            return;
        }

        // ActiveCalls ist eine IReadOnlyList — der Indexer ist hier
        // schlicht das Richtige (CA1826).
        var call = _sip.ActiveCalls.Count > 0 ? _sip.ActiveCalls[0] : null;

        _icon.UpdateToolTip(call is not null
            ? $"nipp — {_party.Describe(call)}"
            : _sip.RegistrationStatus == RegistrationStatus.Registered
                ? "nipp — angemeldet"
                : "nipp — nicht angemeldet");
    }

    /// <summary>§20.4: das Symbol folgt dem Erscheinungsbild.</summary>
    private void OnThemeChanged(object? sender, Microsoft.UI.Xaml.ElementTheme e)
    {
        if (_icon is null)
        {
            return;
        }

        try
        {
            var neu = LoadIcon();
            var vorher = _iconResource;

            _iconResource = neu;
            _icon.UpdateIcon(neu.Handle);

            // Erst jetzt: bis UpdateIcon zurückkommt, zeichnet der
            // Infobereich noch das bisherige Handle. Andersherum wäre es
            // derselbe Fehler wie vorher, nur für einen kürzeren Moment.
            vorher?.Dispose();
        }
        catch (Exception ex)
        {
            TrayLog.IconFailed(_logger, ex.Message);
        }
    }

    /// <summary>
    /// Lädt die Fassung, die auf der aktuellen Taskleiste lesbar ist.
    ///
    /// Achtung, der Dreh: bei <b>dunklem</b> Erscheinungsbild braucht es die
    /// <b>helle</b> Zeichnung — das Symbol steht auf dunklem Grund.
    ///
    /// <b>Das <see cref="System.Drawing.Icon"/> bleibt am Leben</b>, solange
    /// der Infobereich sein Handle zeichnet. Die erste Fassung stand hier mit
    /// <c>using</c>:
    ///
    /// <code>
    /// using var icon = new Icon(path);
    /// return icon.Handle;          // beim Return bereits freigegeben
    /// </code>
    ///
    /// <c>Icon.Dispose</c> ruft <c>DestroyIcon</c>; zurück kam eine Zahl, die
    /// auf nichts mehr zeigte. Der Infobereich hatte nipp dann **ohne
    /// Symbol** — und das Protokoll meldete trotzdem Erfolg, weil aus Sicht
    /// von nipp alles gutgegangen war.
    ///
    /// Sichtbar war es nur manchmal: ein freigegebenes Handle wird nicht
    /// sofort ungültig, sondern erst, wenn Windows den Platz neu vergibt.
    /// </summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray.ico");

        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        }

        // Mit Groessenangabe, nicht ohne (W1.4, Befund D9).
        //
        // <b>new Icon(path) nimmt die 32-px-Ebene</b>, und die Shell skaliert
        // sie auf 16, 20 oder 24 herunter — obwohl die Datei genau diese
        // Ebenen enthaelt. Das Ergebnis ist ein weicher Rand bei 100 Prozent
        // Skalierung. GetSystemMetrics(SM_CXSMICON) sagt, welche Groesse die
        // Shell will; bei 150 Prozent sind das 24 statt 16.
        var kante = GetSystemMetrics(SM_CXSMICON);

        return kante > 0
            ? new System.Drawing.Icon(path, kante, kante)
            : new System.Drawing.Icon(path);
    }

    /// <summary>Die Kantenlaenge eines kleinen Symbols in der Shell.</summary>
    private const int SM_CXSMICON = 49;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// Die Einträge für «Nicht stören» (§10, ADR-055).
    ///
    /// <para><b>Zwei Dauern und ein Aufheben, nicht ein Schalter.</b> Ein
    /// Schalter, den man einschaltet und vergisst, nimmt Anrufe entgegen, die
    /// niemand hört. Ist es aktiv, steht statt der beiden Dauern nur das
    /// Aufheben da — mit der verbleibenden Zeit, damit niemand raten muss.</para>
    ///
    /// <para>Flach und ohne Untermenü: H.NotifyIcon kennt keines, und zwei
    /// Einträge sind kein Menü.</para>
    /// </summary>
    private IEnumerable<PopupMenuItem> NichtStoerenMenuItems()
    {
        var jetzt = DateTimeOffset.UtcNow;

        if (_sip.DoNotDisturb.Describe(jetzt) is { } rest)
        {
            yield return new PopupMenuItem(
                $"Klingelton wieder ein ({rest})",
                (_, _) => _sip.SetDoNotDisturb(null));

            yield break;
        }

        yield return new PopupMenuItem(
            "Klingelton stumm für 30 Minuten",
            (_, _) => _sip.SetDoNotDisturb(TimeSpan.FromMinutes(30)));

        yield return new PopupMenuItem(
            "Klingelton stumm für 60 Minuten",
            (_, _) => _sip.SetDoNotDisturb(TimeSpan.FromMinutes(60)));
    }

    /// <summary>
    /// Zeigt eine kurze Sprechblase am Symbol (W1.3, Befund C10).
    ///
    /// <para>Für den einen Hinweis, den nipp beim ersten Schliessen gibt.
    /// Bewusst über den Infobereich und nicht über ein Fenster: das Fenster
    /// ist in diesem Moment gerade weg.</para>
    ///
    /// <para><b>Ein Fehlschlag ist folgenlos.</b> Windows unterdrückt
    /// Sprechblasen je nach Einstellung des Benutzers, und ein Hinweis, der
    /// nicht kommt, darf nichts kosten.</para>
    /// </summary>
    public void ShowHint(string titel, string text)
    {
        try
        {
            _icon?.ShowNotification(titel, text, NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            TrayLog.HintFailed(_logger, ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _sip.RegistrationChanged -= OnRegistrationChanged;
        _sip.CallStateChanged -= OnCallStateChanged;
        _party.PartyChanged -= OnPartyChanged;
        _theme.EffectiveThemeChanged -= OnThemeChanged;

        _icon?.Dispose();
        _icon = null;

        // Nach dem Symbol, nicht davor.
        _iconResource?.Dispose();
        _iconResource = null;
    }
}

internal static partial class TrayLog
{
    [LoggerMessage(EventId = 3200, Level = LogLevel.Information,
        Message = "Symbol im Infobereich angelegt")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning,
        Message = "Symbol im Infobereich nicht moeglich: {Reason}")]
    public static partial void StartFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning,
        Message = "Symbol liess sich nicht wechseln: {Reason}")]
    public static partial void IconFailed(ILogger logger, string reason);

    // Debug, aber nicht ohne Grund: welche Meldung ein Klick auf das Symbol
    // ueberhaupt ausloest, haengt am Nachrichtenmodus der Shell und ist von
    // aussen nicht zu sehen. Ohne diese Zeile war „Doppelklick oeffnet nicht"
    // nicht von „Klick kommt gar nicht an" zu unterscheiden.
    [LoggerMessage(EventId = 3203, Level = LogLevel.Debug,
        Message = "Infobereich: {Event}")]
    public static partial void MouseEvent(ILogger logger, string @event);

    // W1.3: die Sprechblase kam nicht durch. Windows unterdrueckt sie je nach
    // Einstellung; folgenlos, aber ohne diese Zeile waere «der Hinweis kam
    // nie» nicht von «er wurde nie gezeigt» zu unterscheiden.
    [LoggerMessage(EventId = 3040, Level = LogLevel.Debug,
        Message = "Der Hinweis im Infobereich liess sich nicht zeigen ({ExceptionType})")]
    public static partial void HintFailed(ILogger logger, string exceptionType);
}
