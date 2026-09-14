using Linphone;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Telephony.Model;

// Achtung, Namensfalle: unser Namespace heisst Nipp.Core, die zentrale
// SDK-Klasse heisst Linphone.Core. Innerhalb von Nipp.Core.* löst der
// Compiler ein blankes "Core" auf den eigenen Namespace auf, nicht auf die
// SDK-Klasse — "Core.Version" ist dann ein Fehler, der auf einen fehlenden
// Assemblyverweis hindeutet, obwohl alles vorhanden ist. Dieser Alias macht
// die Absicht eindeutig und ist in jeder Datei unter Services/Telephony/
// zu wiederholen.
using LinphoneCore = Linphone.Core;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Prüft, ob sich das Linphone SDK aus dieser Anwendung heraus laden lässt —
/// das Akzeptanzkriterium von AP2.4 (§12, M1).
///
/// Bewusst getrennt von <c>SipService</c>: diese Klasse baut keinen Core auf
/// und registriert nichts, sie beantwortet nur die Frage „ist die native Kette
/// vollständig und ladbar". Sie bleibt über M1 hinaus nützlich — für die
/// Diagnose nach §9.6, wenn beim Kunden das Audio fehlt.
///
/// §6: eine der wenigen Dateien mit <c>using Linphone</c>. Der
/// Architekturtest in Nipp.Architecture.Tests erzwingt, dass es dabei bleibt.
/// </summary>
public sealed class SdkLoadProbe(ILogger<SdkLoadProbe> logger)
{
    private SdkLoadResult? _cached;

    /// <summary>
    /// Lädt die Factory, setzt die Ressourcenpfade und liest ab, was das SDK
    /// sieht. Wirft nicht — ein Fehlschlag ist ein Ergebnis, kein Absturz:
    /// die App soll auch dann starten und den Grund anzeigen können.
    ///
    /// Das Ergebnis wird gemerkt. Die Prüfung ist zwar unschädlich zu
    /// wiederholen — die Factory ist ein Singleton und die Pfade sind
    /// idempotent —, aber sie doppelt zu protokollieren macht das Log beim
    /// Start unnötig unklar: es sah aus, als würde das SDK zweimal geladen.
    /// </summary>
    public SdkLoadResult Probe(string dataDirectory)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var factory = Factory.Instance;

            // §6: die Verzeichnisse müssen vor jedem Core-Start stehen.
            // Ohne abschliessendes Trennzeichen — das SDK hängt selbst einen
            // Slash an und erzeugt sonst Pfade mit '\/'.
            var appDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var data = dataDirectory.TrimEnd('\\', '/');

            Directory.CreateDirectory(data);
            factory.ConfigDir = data;
            factory.DataDir = data;
            factory.CacheDir = data;

            // Die Ressourcenpfade explizit setzen. Zwei Gründe:
            // 1. belr lädt seine Grammatiken zur Laufzeit aus Dateien; ohne
            //    sie stirbt jeder Core-Start (in AP2.1 so erlebt).
            // 2. MspluginsDir macht den Plugin-Pfad unabhängig von den
            //    DLL-Suchpfaden — der Hebel für MSIX (§14.3).
            factory.TopResourcesDir = Path.Combine(appDirectory, "share");
            factory.DataResourcesDir = Path.Combine(appDirectory, "share");

            // Die Klänge liegen im SDK-Paket eine Ebene tiefer, als es der
            // Name vermuten lässt: share\sounds\linphone, die Klingeltöne
            // darunter in rings\. Beide Pfade zeigten bisher auf share\sounds
            // — das SDK suchte seinen Standardklingelton also direkt dort und
            // meldete bei jedem Start „Default local ringtone file … does not
            // exist". Eingehende Anrufe klingelten am Gerät nicht.
            factory.SoundResourcesDir = Path.Combine(appDirectory, "share", "sounds", "linphone");
            factory.RingResourcesDir = Path.Combine(appDirectory, "share", "sounds", "linphone", "rings");
            factory.MspluginsDir = Path.Combine(appDirectory, "lib", "mediastreamer", "plugins");

            var grammarDirectory = Path.Combine(appDirectory, "share", "belr", "grammars");
            var grammarCount = Directory.Exists(grammarDirectory)
                ? Directory.GetFiles(grammarDirectory, "*.belr").Length
                : 0;

            var pluginCount = Directory.Exists(factory.MspluginsDir)
                ? Directory.GetFiles(factory.MspluginsDir, "*.dll").Length
                : 0;

            var result = new SdkLoadResult(
                Loaded: true,
                SdkVersion: LinphoneCore.Version,
                GrammarFileCount: grammarCount,
                PluginFileCount: pluginCount,
                Error: null);

            TelephonyLog.SdkLoaded(logger, result.SdkVersion, grammarCount, pluginCount);

            if (grammarCount == 0)
            {
                TelephonyLog.GrammarsMissing(logger, grammarDirectory);
            }

            if (pluginCount == 0)
            {
                TelephonyLog.PluginsMissing(logger, factory.MspluginsDir);
            }

            _cached = result;
            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            // Die drei Fälle, die §14.2 und §14.4 beschreiben: eine DLL der
            // Kette fehlt, die statische Initialisierung des Wrappers scheitert,
            // oder die Bitness passt nicht.
            TelephonyLog.SdkLoadFailed(logger, ex, ex.GetType().Name);

            _cached = new SdkLoadResult(
                Loaded: false,
                SdkVersion: null,
                GrammarFileCount: 0,
                PluginFileCount: 0,
                Error: $"{ex.GetType().Name}: {ex.Message}");

            return _cached;
        }
    }
}
