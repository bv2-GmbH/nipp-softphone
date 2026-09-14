namespace Nipp.Core.Services.Settings;

/// <summary>
/// Welche Einstellungen die Administration festgelegt hat (§17, AP8.4).
///
/// <b>Das ist ein Bedienschutz, keine Sicherheitsgrenze.</b> Der Satz steht so
/// in §17 und wird hier wiederholt, weil er leicht vergessen wird: eine
/// gesperrte Einstellung ist in der Oberfläche ausgegraut, mehr nicht. Wer die
/// Konfigurationsdatei unter <c>%APPDATA%</c> mit einem Texteditor öffnet,
/// ändert sie trotzdem — das Betriebssystem gibt sie dem angemeldeten Benutzer,
/// und daran ändert nipp nichts. Wer wirklich verhindern will, dass etwas
/// verstellt wird, braucht Gruppenrichtlinien oder Dateirechte.
///
/// Die Pfade folgen der Schreibweise des Profils: <c>network.sip-port</c>,
/// <c>audio.playback-volume</c>, <c>accounts</c>. Ein Pfad sperrt auch alles
/// darunter, damit <c>accounts</c> nicht für jedes Konto einzeln wiederholt
/// werden muss.
/// </summary>
public sealed class PolicyService
{
    private HashSet<string> _locked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wird ausgelöst, wenn ein neues Profil andere Sperren bringt.</summary>
    public event EventHandler? Changed;

    /// <summary>Ob überhaupt etwas gesperrt ist — die Oberfläche zeigt dann einen Hinweis.</summary>
    public bool HasAnyLock => _locked.Count > 0;

    /// <summary>Die gesperrten Pfade, für Diagnose und Anzeige.</summary>
    public IReadOnlyCollection<string> LockedFields => _locked;

    /// <summary>Übernimmt die Sperren eines Profils und ersetzt die bisherigen.</summary>
    public void Apply(IReadOnlyList<string> lockedFields)
    {
        var updated = new HashSet<string>(lockedFields, StringComparer.OrdinalIgnoreCase);

        if (updated.SetEquals(_locked))
        {
            return;
        }

        _locked = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ob ein Feld gesperrt ist. Trifft auch zu, wenn ein übergeordneter Pfad
    /// gesperrt wurde: <c>network</c> sperrt <c>network.sip-port</c> mit.
    /// </summary>
    public bool IsLocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _locked.Count == 0)
        {
            return false;
        }

        if (_locked.Contains(path))
        {
            return true;
        }

        var separator = path.LastIndexOf('.');

        while (separator > 0)
        {
            if (_locked.Contains(path[..separator]))
            {
                return true;
            }

            separator = path.LastIndexOf('.', separator - 1);
        }

        return false;
    }

    /// <summary>
    /// Der Tooltip für ein gesperrtes Bedienelement. §17 gibt den Wortlaut vor;
    /// er steht hier an einer Stelle, damit er nicht in fünf Ansichten leicht
    /// unterschiedlich auftaucht.
    /// </summary>
    public static string LockedHint => "Von der Administration festgelegt";
}
