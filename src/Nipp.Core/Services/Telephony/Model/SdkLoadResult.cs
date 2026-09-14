namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Ergebnis der SDK-Ladeprüfung. SDK-freier Typ — §6 verlangt, dass kein
/// <c>Linphone.*</c> den Telefonie-Dienst verlässt, damit ViewModels und
/// Diagnose gegen eigene Modelle arbeiten.
/// </summary>
/// <param name="Loaded">Ob die native Kette geladen werden konnte.</param>
/// <param name="SdkVersion">
/// Version, die das SDK meldet. Achtung: das ist die Nebenversion (etwa
/// „5.5.0"), nicht die Patchversion des verbauten Pakets — die ist über die
/// API nicht feststellbar. Verlässlich ist nur die SHA256 in docs/sdk-setup.md.
/// </param>
/// <param name="GrammarFileCount">Gefundene belr-Grammatiken; 0 bedeutet, dass ein Core-Start scheitern würde.</param>
/// <param name="PluginFileCount">Gefundene Mediastreamer-Plugins; ohne libmswasapi.dll gibt es kein Audio.</param>
/// <param name="Error">Fehlerbeschreibung, wenn <paramref name="Loaded"/> falsch ist.</param>
public sealed record SdkLoadResult(
    bool Loaded,
    string? SdkVersion,
    int GrammarFileCount,
    int PluginFileCount,
    string? Error)
{
    /// <summary>
    /// Ob die Laufzeit vollständig ist — geladen und mit allen Dateien, die
    /// ein Gespräch braucht. <c>Loaded</c> allein genügt nicht: die Kette kann
    /// laden und trotzdem stumm bleiben.
    /// </summary>
    public bool IsComplete => Loaded && GrammarFileCount > 0 && PluginFileCount > 0;
}
