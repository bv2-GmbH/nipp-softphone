using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Die Update-Prüfung (§9.6, ADR-039).
///
/// <para><b>Der Ablauf, und er ist eine Entscheidung, keine Bequemlichkeit:</b>
/// beim Start wird nachgesehen, aber <b>nichts geladen</b>. Gefundenes steht
/// als ruhige Zeile in den Einstellungen; geladen wird auf Knopfdruck,
/// angewandt ebenfalls — und nie, solange ein Gespräch läuft. Ein Softphone,
/// das sich selbst neu startet, ist ein verpasster Anruf.</para>
///
/// <para><b>Kein Toast.</b> Der Toast ist bei nipp die Anrufmeldung. Wer ihn
/// für ein Update benutzt, verwässert das einzige Zeichen, das sofort
/// Aufmerksamkeit verdient (ADR-030).</para>
///
/// <para><b>Ein Fehler ist ein Zustand, kein Ereignis.</b> Kein Netz, kein
/// Token, GitHub nicht erreichbar — für die Telefonie ist das alles belanglos,
/// und der nächste Start sieht wieder nach. Deshalb wirft hier nichts nach
/// aussen.</para>
/// </summary>
public sealed class UpdateService(
    ILogger<UpdateService> logger,
    IUpdateGateway gateway,
    Func<bool> isCallInProgress)
{
    private readonly ILogger<UpdateService> _logger = logger;
    private readonly IUpdateGateway _gateway = gateway;
    private readonly Func<bool> _isCallInProgress = isCallInProgress;

    /// <summary>
    /// Der Thread, dem die Oberfläche gehört.
    ///
    /// <para><b>Warum das hier stehen muss.</b> Die Prüfung wartet auf das
    /// Netz, und nach einem <c>await … ConfigureAwait(false)</c> läuft alles
    /// Weitere auf einem Threadpool-Thread — auch <see cref="Changed"/>. Das
    /// Ereignis endet im ViewModel in einem <c>OnPropertyChanged</c>, und eine
    /// gebundene WinUI-Oberfläche vom falschen Thread anzufassen ist kein
    /// Fehlverhalten, sondern ein <b>Absturz</b>.</para>
    ///
    /// <para>Gemeldet am 08.09.2026 von einem Windows-10-Arbeitsplatz mit
    /// installiertem nipp: „stürzt ab, wenn ich nach Updates suche". Auf der
    /// Entwicklungsmaschine konnte es nicht auffallen — dort ist nipp nicht
    /// installiert, der Dienst kehrt vor dem ersten <c>await</c> zurück, und
    /// der Threadwechsel findet nie statt. <b>Der Pfad lief erst auf dem
    /// Zielrechner zum ersten Mal vollständig.</b></para>
    ///
    /// <para><c>SynchronizationContext</c> statt <c>DispatcherQueue</c>, wie
    /// bei <c>ShellViewModel</c> und <c>ConnectivityMonitor</c>: Nipp.Core
    /// bleibt ohne UI-Abhängigkeit (§6). Der Dienst wird auf dem UI-Thread
    /// erzeugt, deshalb genügt das Einfangen im Feldinitialisierer.</para>
    /// </summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Wo die Prüfung steht.</summary>
    public UpdateState State { get; private set; } = UpdateState.Unknown;

    /// <summary>Die gefundene Fassung, sofern es eine gibt.</summary>
    public AvailableUpdate? Available { get; private set; }

    /// <summary>Fortschritt beim Laden, 0 bis 100.</summary>
    public int DownloadPercent { get; private set; }

    /// <summary>
    /// Der Kanal, in dem zuletzt gesucht wurde. Für die Anzeige — welcher
    /// gesucht <i>wird</i>, steht in den Einstellungen.
    /// </summary>
    public UpdateChannel Channel { get; private set; } = UpdateChannel.Stable;

    /// <summary>Ändert sich, wenn sich <see cref="State"/> ändert.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Ob ein geladenes Update jetzt angewandt werden darf.
    ///
    /// <para>Zwei Bedingungen, und die zweite ist die wichtige: es muss etwas
    /// geladen sein, und es darf <b>kein Gespräch laufen</b>. Der Knopf in den
    /// Einstellungen ist sonst abgeblendet, mit <see cref="BlockedReason"/>
    /// daneben — abgeblendet ohne Grund liest sich wie ein Fehler.</para>
    /// </summary>
    public bool CanApply => State == UpdateState.Ready && !_isCallInProgress();

    /// <summary>
    /// Warum gerade nicht angewandt werden kann, oder <c>null</c>, wenn es
    /// geht.
    /// </summary>
    public string? BlockedReason => State != UpdateState.Ready
        ? null
        : _isCallInProgress()
            ? "Während eines Gesprächs wird nicht aktualisiert."
            : null;

    /// <summary>
    /// Nachsehen, ob es etwas Neues gibt.
    ///
    /// <para><paramref name="onStart"/> unterscheidet den Lauf beim Start vom
    /// Knopfdruck: beim Start schweigt die Prüfung, wenn sie abgeschaltet ist
    /// oder nipp gar nicht installiert wurde (Entwicklungslauf aus dem
    /// Ausgabeverzeichnis). Auf Knopfdruck sagt sie, warum nichts
    /// passiert.</para>
    /// </summary>
    public async Task CheckAsync(
        UpdateSettings settings, bool onStart, CancellationToken cancellationToken = default)
    {
        if (onStart && !settings.CheckOnStart)
        {
            UpdateLog.CheckDisabled(_logger);
            return;
        }

        if (!_gateway.IsInstalled)
        {
            // Aus dem Ausgabeverzeichnis heraus gibt es nichts zu
            // aktualisieren. Kein Fehler, sondern der Entwicklungsalltag.
            UpdateLog.NotInstalled(_logger);
            SetState(UpdateState.Unknown);
            return;
        }

        Channel = settings.Channel;
        SetState(UpdateState.Checking);

        try
        {
            var found = await _gateway.CheckAsync(settings.Channel, cancellationToken)
                .ConfigureAwait(false);

            if (found is null)
            {
                Available = null;
                UpdateLog.UpToDate(_logger, UpdateChannels.NameOf(settings.Channel));
                SetState(UpdateState.UpToDate);
                return;
            }

            Available = found;
            UpdateLog.Found(_logger, found.Version, UpdateChannels.NameOf(settings.Channel), found.IsDowngrade);
            SetState(UpdateState.Available);
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Unknown);
        }
        catch (Exception ex)
        {
            // Absichtlich alles: ein gescheiterter Update-Abruf darf nichts
            // kosten ausser einer Protokollzeile.
            UpdateLog.CheckFailed(_logger, ex.GetType().Name, ex.Message);
            SetState(UpdateState.Failed);
        }
    }

    /// <summary>Das gefundene Update herunterladen. Auf Knopfdruck, nie von selbst.</summary>
    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } update || State is not (UpdateState.Available or UpdateState.Failed))
        {
            return;
        }

        DownloadPercent = 0;
        SetState(UpdateState.Downloading);

        try
        {
            await _gateway.DownloadAsync(update, OnProgress, cancellationToken).ConfigureAwait(false);
            DownloadPercent = 100;
            UpdateLog.Downloaded(_logger, update.Version);
            SetState(UpdateState.Ready);
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Available);
        }
        catch (Exception ex)
        {
            UpdateLog.DownloadFailed(_logger, ex.GetType().Name, ex.Message);
            SetState(UpdateState.Failed);
        }
    }

    /// <summary>
    /// Anwenden und neu starten. Kehrt im Erfolgsfall <b>nicht zurück</b>.
    ///
    /// <para>Prüft selbst noch einmal, ob ein Gespräch läuft. Zwischen dem
    /// Zeichnen eines Knopfes und seinem Druck kann ein Anruf hereinkommen —
    /// die Oberfläche ist der falsche Ort für diese Sicherheit.</para>
    /// </summary>
    public bool ApplyAndRestart()
    {
        if (Available is not { } update || State != UpdateState.Ready)
        {
            return false;
        }

        if (_isCallInProgress())
        {
            UpdateLog.ApplyBlockedByCall(_logger);
            return false;
        }

        UpdateLog.Applying(_logger, update.Version);
        _gateway.ApplyAndRestart(update);
        return true;
    }

    private void OnProgress(int percent)
    {
        DownloadPercent = percent;
        Raise();
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Raise();
    }

    /// <summary>
    /// Meldet die Änderung dort, wo die Oberfläche sie verarbeiten darf. Sind
    /// wir schon dort, sofort — sonst über die Nachrichtenschlange.
    /// </summary>
    private void Raise()
    {
        if (_uiContext is null || _uiContext == SynchronizationContext.Current)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _uiContext.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
    }
}
