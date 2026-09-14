using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Ein Provisionierungsprofil (§17, AP8.2–AP8.5).
///
/// Bewusst nicht dasselbe wie <see cref="NippSettings"/>: ein Profil sagt nur,
/// <b>was die Administration festlegt</b>. Alles, was es nicht nennt, bleibt so,
/// wie der Benutzer es eingestellt hat. Wäre das Profil ein vollständiger
/// Einstellungssatz, würde jeder Abruf sämtliche persönlichen Anpassungen
/// überschreiben — Lautstärke, Gerätewahl, Erscheinungsbild.
/// </summary>
/// <param name="Version">Schemaversion des Profils.</param>
/// <param name="ProfileName">Name, nur zur Wiedererkennung in Protokoll und Diagnose.</param>
/// <param name="Accounts">Konten, die eingerichtet werden. Leer heisst: keine Vorgabe.</param>
/// <param name="Team">Team-Nebenstellen für Liste und Besetztlampenfeld (§8.4).</param>
/// <param name="LockedFields">
/// Felder, die der Benutzer nicht ändern darf — als Pfade wie
/// <c>network.sip-port</c>. Siehe <see cref="PolicyService"/>: das ist ein
/// Bedienschutz, keine Sicherheitsgrenze.
/// </param>
/// <param name="Values">
/// Einzelne Einstellungswerte als Pfad-Wert-Paare. Der flache Aufbau ist
/// Absicht: er hält Parser und Anwendung klein und macht das Profil lesbar,
/// ohne für jedes neue Feld eine Klasse zu brauchen.
/// </param>
/// <param name="IntegrationsUri">
/// Woher die Integrationskonfiguration kommt (§21.3).
///
/// <b>Nur ein Verweis, nicht der Inhalt.</b> Die Werte oben sind flache
/// Pfad-Wert-Paare; ein Integrationspaket in dieser Form wären hunderte
/// Zeilen, und ein Profil, das niemand mehr lesen kann, wird auch nicht mehr
/// geprüft. Die Datei wird nach dem Start geholt — nie im Startpfad, denn
/// nichts an den Integrationen darf zwischen dem Start und dem ersten
/// möglichen Anruf stehen (§21.2).
/// </param>
public sealed record ProvisioningProfile(
    int Version,
    string? ProfileName,
    IReadOnlyList<ProvisionedAccount> Accounts,
    IReadOnlyList<TeamExtension> Team,
    IReadOnlyList<string> LockedFields,
    IReadOnlyDictionary<string, string> Values,
    string? IntegrationsUri = null)
{
    /// <summary>Ein leeres Profil — was ein fehlgeschlagener Abruf zurücklässt.</summary>
    public static ProvisioningProfile Empty { get; } = new(
        Version: 0,
        ProfileName: null,
        Accounts: [],
        Team: [],
        LockedFields: [],
        Values: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        IntegrationsUri: null);

    public bool IsEmpty =>
        Accounts.Count == 0
        && Team.Count == 0
        && LockedFields.Count == 0
        && Values.Count == 0
        && string.IsNullOrWhiteSpace(IntegrationsUri);
}

/// <summary>
/// Ein Konto aus einem Profil.
///
/// <b>Zum Passwort:</b> es darf im Profil stehen, muss aber nicht. §11 verlangt,
/// dass Zugangsdaten lokal über DPAPI abgelegt werden — das geschieht beim
/// Übernehmen. Im Profil selbst steht es im Klartext oder als HA1, und das
/// Profil liegt auf einem Webserver. Deshalb ist der bessere Weg, nur
/// Benutzername und Domäne vorzugeben und das Passwort beim Benutzer zu
/// erfragen; <c>docs/provisioning.md</c> sagt das ausdrücklich.
/// </summary>
public sealed record ProvisionedAccount(
    string Username,
    string Domain,
    string? Password,
    string? AuthUserId,
    string? DisplayName,
    SipTransport? Transport,
    string? OutboundProxy,
    int? ExpiresSeconds);
