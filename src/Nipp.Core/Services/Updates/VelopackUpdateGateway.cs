using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;
using Velopack;
using Velopack.Sources;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Der Zugang zu den Releases auf GitHub (ADR-039, Token abgelöst durch
/// ADR-083).
///
/// <para><b>Das Repo ist öffentlich, und es gibt kein Token mehr.</b>
/// <c>GithubSource</c> bekommt immer <c>null</c> — auch dann, wenn auf dem
/// Arbeitsplatz noch ein alter Eintrag im <c>SecretStore</c> liegt. Der Grund
/// ist kein Stil, sondern ein Ausfall: ein <i>widerrufenes</i> Token, das
/// mitgeschickt wird, lässt GitHub auch öffentliche Ressourcen mit 401
/// «Bad credentials» ablehnen. Aufgeräumt wird der alte Eintrag in
/// <see cref="ObsoleteUpdateToken"/>.</para>
/// </summary>
public sealed class VelopackUpdateGateway(
    ILogger<VelopackUpdateGateway> logger) : IUpdateGateway
{
    /// <summary>
    /// Wohin geschaut wird. Eine Konstante und keine Einstellung: wer die
    /// Update-Quelle aus der Ferne ändern kann, kann beliebigen Code auf den
    /// Arbeitsplatz bringen — dieselbe Überlegung wie bei der
    /// Provisioning-Adresse (ADR-012).
    ///
    /// <para><b>Seit dem 14.09.2026 zeigt sie auf das neue, öffentliche
    /// Repo.</b> Genau deshalb ist die Fassung, die diese Änderung zuerst
    /// trägt, <b>die Brücke</b>: sie wird noch im ALTEN Repo veröffentlicht,
    /// damit die installierten Arbeitsplätze sie dort finden — und ab dann
    /// von selbst im neuen suchen. Wer die Reihenfolge vertauscht, lässt jeden
    /// Arbeitsplatz zurück, der noch die alte Fassung hat: die Prüfung
    /// scheitert still, und niemand merkt es
    /// (<c>docs/plans/OEFFENTLICH-PLAN.md</c>).</para>
    /// </summary>
    public const string RepositoryUrl = "https://github.com/bv2-GmbH/nipp-softphone";

    private readonly ILogger<VelopackUpdateGateway> _logger = logger;
    private UpdateManager? _manager;
    private UpdateChannel? _managerChannel;

    /// <inheritdoc />
    public bool IsInstalled
    {
        get
        {
            try
            {
                return ManagerFor(UpdateChannel.Stable).IsInstalled;
            }
            catch (Exception ex)
            {
                UpdateLog.GatewayUnavailable(_logger, ex.GetType().Name);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string? CurrentVersion
    {
        get
        {
            try
            {
                return ManagerFor(UpdateChannel.Stable).CurrentVersion?.ToString();
            }
            catch (Exception ex)
            {
                UpdateLog.GatewayUnavailable(_logger, ex.GetType().Name);
                return null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<AvailableUpdate?> CheckAsync(
        UpdateChannel channel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = await ManagerFor(channel).CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        return new AvailableUpdate(
            info.TargetFullRelease.Version.ToString(),
            info.IsDowngrade,
            info);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(
        AvailableUpdate update, Action<int>? progress, CancellationToken cancellationToken)
    {
        if (update.Handle is not UpdateInfo info)
        {
            throw new ArgumentException(
                "Dieses Update stammt nicht von diesem Gateway.", nameof(update));
        }

        await ManagerFor(_managerChannel ?? UpdateChannel.Stable)
            .DownloadUpdatesAsync(info, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ApplyAndRestart(AvailableUpdate update)
    {
        if (update.Handle is not UpdateInfo info)
        {
            throw new ArgumentException(
                "Dieses Update stammt nicht von diesem Gateway.", nameof(update));
        }

        // <b>Der Updater wartet, statt uns abzuschiessen</b> (14.09.2026).
        //
        // <para>Vorher stand hier <c>ApplyUpdatesAndRestart</c>. Das beendet
        // den Prozess sofort — mitten in einer Anmeldung, ohne Abmeldung beim
        // Server — und zeigt dabei einen Fortschrittsdialog <b>mit
        // OK-Knopf</b>. Der Knopf sah aus wie eine Bestätigung und brach das
        // Update ab; gemeldet am 14.09.2026, mit Bild.</para>
        //
        // <para><c>WaitExitThenApplyUpdates</c> kennt dafür ein
        // <c>silent</c>: kein Fenster, keine Dialoge. Es kehrt zurück, und
        // <b>die Anwendung muss sich danach selbst beenden</b> — was hier der
        // bessere Weg ist, weil nipp sich dabei ordentlich abmeldet. Der
        // Updater wartet 60 Sekunden darauf; dauert das Herunterfahren
        // länger, gibt er auf und das Update bleibt liegen.</para>
        ManagerFor(_managerChannel ?? UpdateChannel.Stable)
            .WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: true);
    }

    /// <summary>
    /// Die Quelle für einen Kanal — <b>ohne Zugriffstoken, immer</b>. Eigene
    /// Methode, damit ein Test es prüfen kann: dass hier <c>null</c> steht,
    /// ist die Zusage aus ADR-083, und ein Token in der Quelle wäre der
    /// Ausfall, den sie verhindert.
    /// </summary>
    public static GithubSource CreateSource(UpdateChannel channel) =>
        new(RepositoryUrl, accessToken: null, prerelease: channel == UpdateChannel.Beta);

    /// <summary>
    /// Ein <see cref="UpdateManager"/> für den gewünschten Kanal, gemerkt,
    /// solange der Kanal derselbe bleibt.
    ///
    /// <para><b>Der Kanal steckt in zwei Dingen gleichzeitig</b>, und beide
    /// müssen zusammenpassen: <c>ExplicitChannel</c> bestimmt, welche
    /// <c>releases.*.json</c> gelesen wird, das <c>prerelease</c>-Flag der
    /// Quelle, ob GitHub-Vorabversionen überhaupt in Betracht kommen. Beta
    /// wird mit <c>--pre</c> hochgeladen; ohne das Flag fände der Abruf sie
    /// nicht.</para>
    ///
    /// <para><c>AllowVersionDowngrade</c> steht an, und zwar absichtlich: der
    /// Wechsel von beta zurück auf stable ist der Weg zu einer <b>niedrigeren</b>
    /// Versionsnummer. Ohne das Flag wäre beta eine Einbahnstrasse, aus der man
    /// nur durch Neuinstallation herauskäme.</para>
    /// </summary>
    private UpdateManager ManagerFor(UpdateChannel channel)
    {
        if (_manager is not null && _managerChannel == channel)
        {
            return _manager;
        }

        var source = CreateSource(channel);

        var options = new UpdateOptions
        {
            ExplicitChannel = UpdateChannels.NameOf(channel),
            AllowVersionDowngrade = true,
        };

        _manager = new UpdateManager(source, options);
        _managerChannel = channel;
        return _manager;
    }
}
