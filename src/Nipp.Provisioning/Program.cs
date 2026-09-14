using System.Globalization;
using System.Text;
using Nipp.Core.Services.Settings;
using Nipp.Provisioning;

// Kommandozeilen-Generator für Provisionierungsprofile (§17, AP8.5).
//
// Drei Befehle, mehr braucht niemand:
//
//   nippprov neu     <datei> [--profil NAME]   leeres Profil mit Kommentaren
//   nippprov pruefen <datei>                   liest es und sagt, was drinsteht
//   nippprov schema                            gibt das Schema aus
//
// Warum kein Argumentparser aus einem Paket: drei Befehle mit je einem
// Argument rechtfertigen keine Abhängigkeit, die mitgepflegt werden will.

// Ohne das zeigt die Windows-Konsole in ihrer Standardcodepage aus jedem
// Umlaut ein Fragezeichen — und die Ausgabe erklaert Dinge, die man lesen
// koennen muss.
Console.OutputEncoding = Encoding.UTF8;

return Generator.Run(args);

namespace Nipp.Provisioning
{
    /// <summary>Der Generator selbst — als Klasse, damit er testbar bleibt.</summary>
    internal static class Generator
    {
        public static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "neu" or "new" => CreateTemplate(args),
                "pruefen" or "prüfen" or "check" => Check(args),
                "schema" => PrintSchema(),
                "--hilfe" or "--help" or "-h" or "/?" => PrintUsage(),
                _ => Unknown(args[0]),
            };
        }

        private static int Unknown(string command)
        {
            Console.Error.WriteLine($"Unbekannter Befehl '{command}'.");
            Console.Error.WriteLine();
            PrintUsage();

            return 1;
        }

        private static int PrintUsage()
        {
            Console.WriteLine("""
                nippprov — Provisionierungsprofile für nipp (§17)

                  nippprov neu <datei> [--profil NAME]
                      Legt ein kommentiertes Profil an, das sich ausfüllen lässt.

                  nippprov pruefen <datei>
                      Liest ein Profil und zeigt, was nipp daraus machen würde.
                      Genau der Parser, der auch in der App läuft — ein Profil,
                      das hier durchgeht, geht dort auch durch.

                  nippprov schema
                      Gibt die zulässigen Elemente und Einstellungspfade aus.

                Ein Profil gehört auf einen Webserver unter der Adresse, die in
                den Einstellungen als Provisioning-Adresse eingetragen ist, oder
                als nipp-factory.xml nach %PROGRAMDATA%\\bv2\\nipp.
                """);

            return 0;
        }

        private static int CreateTemplate(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Es fehlt der Dateiname: nippprov neu <datei>");
                return 1;
            }

            var path = args[1];
            var name = ReadOption(args, "--profil") ?? "kundenname";

            if (File.Exists(path))
            {
                // Ein Profil zu überschreiben, an dem jemand gerade gearbeitet
                // hat, wäre die unangenehmste Art, Zeit zu verlieren.
                Console.Error.WriteLine($"Die Datei {path} gibt es schon. Erst umbenennen oder löschen.");
                return 1;
            }

            try
            {
                File.WriteAllText(path, Template(name));
                Console.WriteLine($"Profil angelegt: {Path.GetFullPath(path)}");
                Console.WriteLine("Ausfüllen, dann mit 'nippprov pruefen' gegenlesen.");

                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Schreiben nicht möglich: {ex.Message}");
                return 1;
            }
        }

        private static int Check(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Es fehlt der Dateiname: nippprov pruefen <datei>");
                return 1;
            }

            var path = args[1];

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Die Datei {path} gibt es nicht.");
                return 1;
            }

            string xml;

            try
            {
                xml = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Lesen nicht möglich: {ex.Message}");
                return 1;
            }

            if (!Core.Services.Settings.ProvisioningParser.TryParse(xml, out var profile, out var error))
            {
                Console.Error.WriteLine($"Das Profil ist unbrauchbar: {error}");
                return 1;
            }

            if (error is { Length: > 0 })
            {
                Console.WriteLine($"Hinweis: {error}");
                Console.WriteLine();
            }

            Console.WriteLine($"Profil:        {profile.ProfileName ?? "ohne Namen"}");
            Console.WriteLine($"Schemaversion: {profile.Version}");
            Console.WriteLine();

            Console.WriteLine($"Konten ({profile.Accounts.Count}):");

            foreach (var account in profile.Accounts)
            {
                var transport = account.Transport?.ToString() ?? "TLS (Standard)";
                var secret = account.Password is { Length: > 0 } ? " · Passwort im Profil" : string.Empty;

                Console.WriteLine(
                    $"  {account.Username}@{account.Domain} über {transport}{secret}");
            }

            if (profile.Accounts.Any(static a => a.Password is { Length: > 0 }))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "  Achtung: dieses Profil enthält Passwörter im Klartext. Es liegt auf einem");
                Console.WriteLine(
                    "  Webserver — nur über https ausliefern und den Zugriff einschränken. Besser");
                Console.WriteLine(
                    "  ist, das Passwort wegzulassen und den Benutzer danach zu fragen.");
            }

            Console.WriteLine();
            Console.WriteLine($"Team ({profile.Team.Count}):");

            foreach (var member in profile.Team)
            {
                var watched = member.SipAddress is { Length: > 0 }
                    ? "mit Besetztlampenfeld"
                    : "ohne Besetztlampenfeld (keine SIP-Adresse angegeben)";

                Console.WriteLine($"  {member.Extension} · {member.DisplayName} · {watched}");
            }

            Console.WriteLine();
            Console.WriteLine($"Einstellungen ({profile.Values.Count}):");

            foreach (var (key, value) in profile.Values.OrderBy(static v => v.Key, StringComparer.Ordinal))
            {
                var known = KnownPaths.Contains(key)
                    ? FactoryOnlyPaths.Contains(key)
                        ? "   ← nur in nipp-factory.xml wirksam (ADR-012)"
                        : string.Empty
                    : "   ← unbekannt, wird übergangen";

                Console.WriteLine($"  {key} = {value}{known}");
            }

            Console.WriteLine();
            Console.WriteLine($"Gesperrte Felder ({profile.LockedFields.Count}):");

            foreach (var field in profile.LockedFields)
            {
                Console.WriteLine($"  {field}");
            }

            if (profile.LockedFields.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "  Gesperrte Felder sind ein Bedienschutz, keine Sicherheitsgrenze: sie sind in");
                Console.WriteLine(
                    "  der Oberfläche ausgegraut, aber die Konfigurationsdatei gehört dem Benutzer.");
            }

            return 0;
        }

        private static int PrintSchema()
        {
            Console.WriteLine("""
                Aufbau eines Profils
                ════════════════════

                <nipp-provisioning version="1" profile="NAME">
                  <accounts>
                    <account username="…" domain="…"          (beide Pflicht)
                             display-name="…" auth-user-id="…"
                             transport="udp|tcp|tls"
                             outbound-proxy="…" expires="600"
                             password="…" />                   (höchstens zehn)
                  </accounts>
                  <team>
                    <extension name="…" number="…" sip="…"
                               mobile="…" group="…" />     (sip nur für BLF)
                  </team>
                  <settings>
                    <set path="…" value="…" />
                  </settings>
                  <locked>
                    <field>…</field>
                  </locked>
                </nipp-provisioning>

                Einstellungspfade
                ═════════════════
                """);

            foreach (var path in KnownPaths.OrderBy(static p => p, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {path}");
            }

            Console.WriteLine();
            Console.WriteLine("""
                Für <locked> genügt auch ein Oberpfad: 'network' sperrt alles
                unter network.*, 'accounts' sperrt die ganze Kontoverwaltung.

                Zwei Pfade wirken nur in der mitgelieferten nipp-factory.xml,
                nicht in einem Profil vom Server (ADR-012):
                  advanced.provisioning-uri
                  advanced.allow-insecure-provisioning
                Ein Profil, das die Bezugsquelle umschreiben könnte, würde
                dauerhaft bestimmen, woher dieser Arbeitsplatz seine Konten
                bezieht.

                Ein Profil wird nur über https geholt. Steht die Anlage ohne
                TLS da, muss in den Einstellungen unter „Erweitert"
                ausdrücklich erlaubt werden, unverschlüsselt zu provisionieren.
                """);

            return 0;
        }

        /// <summary>
        /// Die Pfade, die <c>ProvisioningService</c> auswertet — aus dem Kern,
        /// nicht abgeschrieben (ADR-054).
        ///
        /// <para><b>Hier stand bis zum 13.09.2026 eine eigene Liste</b>, mit
        /// dem Kommentar «beide können auseinanderlaufen». Sie waren es
        /// bereits: <c>audio.ringtone</c>, <c>update.channel</c>,
        /// <c>update.check-on-start</c> und <c>update.token</c> fehlten. Ein
        /// gültiges Update-Profil meldete <c>pruefen</c> als unbekannt, und
        /// <c>neu</c> erzeugte kein Vollprofil.</para>
        /// </summary>
        private static readonly HashSet<string> KnownPaths =
            new(ProvisioningCatalog.AllPaths, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pfade, die nur die <b>mitgelieferte</b> Konfiguration setzen darf
        /// (ADR-012). Ein Profil aus dem Netz, das sie nennt, wird sie
        /// uebergehen sehen — deshalb steht der Hinweis schon hier.
        /// </summary>
        private static readonly HashSet<string> FactoryOnlyPaths =
            new(
                ProvisioningCatalog.Entries
                    .Where(static e => e.FactoryOnly)
                    .Select(static e => e.Path),
                StringComparer.OrdinalIgnoreCase);

        private static string? ReadOption(string[] args, string name)
        {
            var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string Template(string profileName) =>
            string.Format(
                CultureInfo.InvariantCulture,
                """
                <?xml version="1.0" encoding="utf-8"?>

                <!--
                  Provisionierungsprofil für nipp.

                  Was hier steht, gilt für alle Arbeitsplätze, die dieses Profil
                  laden. Was NICHT hier steht, bleibt so, wie der Benutzer es
                  eingestellt hat — das Profil ist eine Vorgabe, kein
                  vollständiger Einstellungssatz.

                  Erzeugt am {1} mit nippprov.
                -->

                <nipp-provisioning version="1" profile="{0}">

                  <!--
                    Konten. Höchstens zehn (§20.2).

                    Das Passwort lässt sich mit password="…" mitgeben, sollte es
                    aber nicht: das Profil liegt auf einem Webserver, und wer es
                    abrufen kann, hat dann die Zugangsdaten. Ohne password fragt
                    nipp den Benutzer einmal danach und legt es lokal über DPAPI
                    ab (§11).
                  -->
                  <accounts>
                    <account username="151"
                             domain="pbx.example.ch"
                             display-name="Vorname Nachname"
                             transport="tls"
                             expires="600" />
                  </accounts>

                  <!--
                    Team-Nebenstellen. Sie erscheinen in der Kontaktliste, und
                    nur für sie wird Präsenz abonniert (§14.8) — jede kostet die
                    Anlage ein dauerhaftes SUBSCRIBE. Ohne sip="…" steht die
                    Nebenstelle in der Liste, wird aber nicht beobachtet.
                  -->
                  <team>
                    <extension name="Empfang" number="150" sip="sip:150@pbx.example.ch" />
                    <extension name="Pikett" number="151" mobile="+41791234567" group="Support" />
                  </team>

                  <!--
                    Einzelne Einstellungen. 'nippprov schema' zeigt alle Pfade.
                  -->
                  <settings>
                    <set path="advanced.country-prefix" value="+41" />
                    <set path="nat.encryption" value="Srtp" />
                    <set path="nat.encryption-mandatory" value="false" />
                  </settings>

                  <!--
                    Gesperrte Felder: in der Oberfläche ausgegraut mit dem
                    Hinweis „Von der Administration festgelegt".

                    Das ist ein Bedienschutz, keine Sicherheitsgrenze. Die
                    Konfigurationsdatei unter %APPDATA% gehört dem angemeldeten
                    Benutzer, und daran ändert eine Sperre nichts.
                  -->
                  <locked>
                    <field>accounts</field>
                    <field>network</field>
                  </locked>

                </nipp-provisioning>
                """,
                profileName,
                // Festes Schweizer Datumsformat, aber mit der invarianten
                // Kultur: InvariantGlobalization ist eingeschaltet, und
                // GetCultureInfo("de-CH") wirft dann.
                DateTimeOffset.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
    }
}
