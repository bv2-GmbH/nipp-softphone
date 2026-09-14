using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;
using Velopack;
using Velopack.Sources;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Der Zugang zu den Releases auf GitHub (ADR-039).
///
/// <para><b>Das Repo ist privat</b>, solange die Lizenzfrage nicht entschieden
/// ist (`docs/licensing.md`). Ein Release-Asset dort ist ohne Anmeldung nicht
/// abrufbar, deshalb das Token. Es steht <b>nicht</b> im Quelltext: es kommt
/// über die Provisionierung an den Arbeitsplatz und liegt über DPAPI im
/// <see cref="SecretStore"/> — derselbe Weg wie das SIP-Passwort. Fehlt es,
/// ist die Update-Prüfung schlicht aus.</para>
///
/// <para>Nach dem Öffentlichmachen (docs/plans/RELEASE-PLAN.md R10) fällt das Token
/// ersatzlos weg; <c>GithubSource</c> nimmt dann <c>null</c>. Sonst ändert
/// sich nichts.</para>
/// </summary>
public sealed class VelopackUpdateGateway(
    ILogger<VelopackUpdateGateway> logger,
    SecretStore secrets) : IUpdateGateway
{
    /// <summary>
    /// Der Schlüssel im <see cref="SecretStore"/>. Das Präfix trennt ihn von
    /// den SIP-Passwörtern (<c>sip:</c>) und den Integrationen
    /// (<c>integration:</c>).
    /// </summary>
    public const string TokenKey = "update:github-token";

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
    private readonly SecretStore _secrets = secrets;
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

        // Kehrt nicht zurueck: der Prozess wird beendet und neu gestartet.
        ManagerFor(_managerChannel ?? UpdateChannel.Stable)
            .ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

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

        var token = _secrets.Get(TokenKey);
        var source = new GithubSource(
            RepositoryUrl,
            string.IsNullOrWhiteSpace(token) ? null : token,
            prerelease: channel == UpdateChannel.Beta);

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
