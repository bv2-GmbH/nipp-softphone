using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Stellt die Wurzelzertifikate von Windows als Datei bereit (§9.2, §14.6).
///
/// <b>Warum es diese Klasse gibt.</b> §14.6 sagt es unmissverständlich: „Das
/// SDK will eine CA-Datei, nicht den Windows-Zertifikatspeicher. Root-CAs beim
/// Start exportieren und dem Core als Datei übergeben." Genau das war nicht
/// gebaut — <c>Core.RootCa</c> blieb ungesetzt, und die Einstellung
/// „Serverzertifikat prüfen" (§9.2, Standard ein) wurde nirgends auf den Core
/// übertragen. Bei TLS, dem in §9.2 vorgesehenen Standardtransport, prüfte
/// also niemand etwas oder die Verbindung scheiterte ohne erkennbaren Grund.
///
/// <b>Was hier bewusst nicht passiert:</b> keine eigene Vertrauensliste. Was
/// Windows vertraut, vertraut nipp — Zertifikate einer internen
/// Zertifizierungsstelle liegen bei einem verwalteten Arbeitsplatz ohnehin im
/// Speicher des Rechners und kommen damit von selbst mit.
///
/// SDK-frei: diese Klasse schreibt nur eine Datei. Den Pfad an den Core gibt
/// <see cref="SettingsApplier"/> weiter (§6).
/// </summary>
public sealed class RootCertificates(ILogger<RootCertificates> logger)
{
    /// <summary>
    /// Wie lange das Bündel gilt, bevor es neu geschrieben wird.
    ///
    /// Der Export kostet im Startpfad Zeit (rund 50 Zertifikate), und
    /// Wurzelzertifikate ändern sich in Monaten, nicht in Stunden. Wer eine
    /// frisch verteilte interne CA sofort braucht, löscht die Datei.
    /// </summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>
    /// §10: unter %LOCALAPPDATA%, nicht bei der Konfiguration — die Datei ist
    /// ein Abbild des Rechners, keine Einstellung des Benutzers.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nipp",
        "rootca.pem");

    /// <summary>
    /// Liefert den Pfad zum Zertifikatsbündel und schreibt es, wenn es fehlt
    /// oder veraltet ist.
    /// </summary>
    /// <param name="path">Ablageort; <c>null</c> heisst <see cref="DefaultPath"/>.</param>
    /// <returns>
    /// Der Pfad, oder <c>null</c>, wenn sich nichts schreiben liess. Dann
    /// bleibt es bei dem, was das SDK selbst mitbringt — eine Warnung im
    /// Protokoll, kein Abbruch: ohne TLS telefoniert nipp weiter.
    /// </returns>
    public string? Ensure(string? path = null)
    {
        var target = path ?? DefaultPath;

        try
        {
            var file = new FileInfo(target);

            if (file.Exists && file.Length > 0 && DateTimeOffset.UtcNow - file.LastWriteTimeUtc < MaxAge)
            {
                return target;
            }

            var bundle = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Rechner zuerst: dort liegen die von der Administration
            // verteilten Zertifikate, und die sind im Kundeneinsatz der
            // interessante Teil.
            Collect(StoreLocation.LocalMachine, bundle, seen);
            Collect(StoreLocation.CurrentUser, bundle, seen);

            if (seen.Count == 0)
            {
                TelephonyLog.RootCaFailed(logger, "der Windows-Zertifikatspeicher lieferte keine Wurzelzertifikate");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Erst daneben, dann umbenennen: ein Absturz mitten im Schreiben
            // darf keine halbe Datei hinterlassen, an der das SDK scheitert.
            var temporary = target + ".tmp";
            File.WriteAllText(temporary, bundle.ToString());
            File.Move(temporary, target, overwrite: true);

            TelephonyLog.RootCaExported(logger, seen.Count, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Security.Cryptography.CryptographicException)
        {
            TelephonyLog.RootCaFailed(logger, $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void Collect(StoreLocation location, StringBuilder bundle, HashSet<string> seen)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            foreach (var certificate in store.Certificates)
            {
                using (certificate)
                {
                    if (!seen.Add(certificate.Thumbprint))
                    {
                        continue;
                    }

                    // Der Kommentar über dem Block hilft, wenn jemand die
                    // Datei öffnet, um zu prüfen, ob eine bestimmte CA dabei
                    // ist. PEM-Leser überlesen ihn.
                    bundle.AppendLine(CultureInfo.InvariantCulture, $"# {certificate.Subject}");
                    bundle.Append(certificate.ExportCertificatePem());
                    bundle.AppendLine();
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Ein nicht lesbarer Speicher ist kein Grund, den anderen
            // auszulassen — bei eingeschränkten Konten kommt das vor.
            TelephonyLog.RootCaFailed(logger, $"{location}: {ex.Message}");
        }
    }
}
