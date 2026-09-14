using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Cards;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Beantwortet an <b>einer</b> Stelle, wie der Gesprächspartner heisst
/// (ADR-043).
///
/// <para><b>Der Denkfehler, den dieser Dienst behebt, steckte in einem
/// Namen.</b> Bis zum 12.09.2026 gab es <c>CallInfo.DisplayLabel</c> — den
/// einzigen richtungsneutralen „Namen" im System —, und er schaute nie auf
/// Kontakte. Bei eingehenden Anrufen kaschierte das der Anzeigename der
/// Anlage; bei <b>ausgehenden</b> ist der leer, und deshalb stand dort die
/// blosse Nummer. Die Auflösung selbst lief längst richtungsneutral, nur eben
/// an zwei Stellen und nicht dort, wo die Gesprächsansicht hinsieht.</para>
///
/// <para><b>Zwei Fragen, die denselben Namen tragen</b> — und deshalb zwei
/// Methoden:</para>
/// <list type="bullet">
///   <item>
///     <see cref="NameOf"/> — „wie heisst der Gesprächspartner?" Antwort: ein
///     Name oder <b>nichts</b>. Eine Nummer ist kein Name, und sie darf
///     niemals als Titel erscheinen.
///   </item>
///   <item>
///     <see cref="Describe"/> — „was steht in dieser Zeile?" Antwort: ein Name,
///     sonst die formatierte Nummer, sonst „Unbekannt". <b>Nie leer.</b>
///   </item>
/// </list>
///
/// <para><b>Der Zwischenspeicher ist nicht optional.</b> Die lokale Auflösung
/// läuft über alle Kontakte — bei über hundert Outlook-Einträgen — und sie
/// läuft auf dem Thread, der alle 20 ms <c>Core.Iterate()</c> bedient. Gemerkt
/// wird je <see cref="CallHandle"/>: ein Ein-Platz-Speicher verfehlte bei zwei
/// Gesprächen jedes Mal, wenn die Ansicht zwischen ihnen wechselt.</para>
///
/// <para><b>Gemerkt wird nur der lokale Schritt, nicht das Ergebnis.</b> Sonst
/// fröre der Titel auf dem ein, was vor der Antwort des Fremdsystems bekannt
/// war — und genau die soll ja nachkommen.</para>
///
/// <para><b>Dieser Dienst protokolliert nichts.</b> Er hat keinen Logger, und
/// das ist Absicht: das Einzige, was man hier schreiben wollte, wären Name und
/// Nummer (§21.2, ADR-022).</para>
/// </summary>
public sealed class CallPartyResolver : IDisposable
{
    private readonly ClipResolver _clip;
    private readonly ICallContextSnapshots? _context;

    /// <summary>
    /// Was zu einem Gespräch schon aufgelöst wurde.
    ///
    /// <b>Ein <see cref="CallHandle"/> ist eine Guid und wird nie
    /// wiederverwendet</b> — „einmal je Gespräch auflösen" ist damit exakt
    /// erfüllt, ohne dass ein Eintrag veralten kann.
    /// </summary>
    private readonly Dictionary<CallHandle, Eintrag> _bekannt = [];

    private readonly object _gate = new();
    private bool _disposed;

    public CallPartyResolver(ClipResolver clip, ICallContextSnapshots? context = null)
    {
        _clip = clip;
        _context = context;

        if (_context is not null)
        {
            _context.ContextChanged += OnContextChanged;
        }
    }

    /// <summary>
    /// Zu diesem Gespräch gibt es einen neuen Namen.
    ///
    /// <b>Nur bei echter Änderung.</b> Jede Quellenantwort veröffentlicht einen
    /// Schnappschuss; ohne diese Prüfung zeichnete die Oberfläche vier Mal je
    /// Anruf neu.
    /// </summary>
    public event EventHandler<CallHandle>? PartyChanged;

    /// <summary>
    /// Der Name des Gesprächspartners, oder <c>null</c> — <b>nie eine
    /// Nummer</b>.
    ///
    /// <para>Die Reihenfolge der Rückfälle:</para>
    /// <list type="number">
    ///   <item>
    ///     <b>Der lokale Kontakt</b> (Team vor Outlook). Zuerst, weil es ihn
    ///     auch ohne eingerichtete Integration gibt — Telefonieren hängt von
    ///     keiner Quelle ab (§21.2).
    ///   </item>
    ///   <item>
    ///     <b>Ein Name aus einer externen Quelle</b>, über die Bedeutung
    ///     gefragt und nicht über einen Feldnamen. Bei ausgehenden Anrufen ist
    ///     das der häufige Fall: die Nummer kennt nur das CRM.
    ///   </item>
    ///   <item>
    ///     <b>Der Anzeigename aus der Signalisierung</b> — aber nur, wenn er
    ///     nicht bloss die Nummer wiederholt. Manche Anlagen schicken genau
    ///     das, und dann stünde die Nummer als „Name" da.
    ///   </item>
    /// </list>
    /// </summary>
    public string? NameOf(CallInfo call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (LokalerName(call) is { Length: > 0 } lokal)
        {
            return lokal;
        }

        if (ContextRoles.Text(_context?.SnapshotFor(call.Handle), FieldRole.Name) is { Length: > 0 } fremd)
        {
            return fremd;
        }

        if (call.RemoteDisplayName is { Length: > 0 } signalisiert
            && !ClipResolver.IsSameNumber(signalisiert, call.RemoteNumber))
        {
            return signalisiert;
        }

        return null;
    }

    /// <summary>
    /// Was in einer Zeile steht: der Name, sonst die formatierte Nummer, sonst
    /// „Unbekannt". <b>Nie leer</b> — eine Zeile ohne Text sieht aus wie ein
    /// Fehler, und ohne Nummer kann niemand zurückrufen.
    /// </summary>
    public string Describe(CallInfo call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (NameOf(call) is { Length: > 0 } name)
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(call.RemoteNumber)
            ? "Unbekannt"
            : PhoneNumberFormat.ForDisplay(call.RemoteNumber);
    }

    /// <summary>
    /// Vergisst, was zu einem beendeten Gespräch gemerkt war.
    ///
    /// <b>Nicht nötig für die Richtigkeit</b> — eine Kennung kommt nie wieder —,
    /// aber ein Speicher, der nur wächst, ist eine Wette auf die Laufzeit.
    /// </summary>
    public void Forget(CallHandle handle)
    {
        lock (_gate)
        {
            _bekannt.Remove(handle);
        }
    }

    private string? LokalerName(CallInfo call)
    {
        lock (_gate)
        {
            if (_bekannt.TryGetValue(call.Handle, out var eintrag)
                && string.Equals(eintrag.Number, call.RemoteNumber, StringComparison.Ordinal))
            {
                return eintrag.ClipName;
            }
        }

        // Ausserhalb der Sperre: der Lauf ueber die Kontakte ist der teure
        // Teil, und er braucht nichts aus dem Speicher.
        var name = _clip.ResolveName(call.RemoteNumber);

        lock (_gate)
        {
            // Mehr als zwei Gespraeche gibt es nicht; die Grenze ist die
            // Vorsorge gegen einen Pfad, den heute niemand kennt.
            if (_bekannt.Count > 8)
            {
                _bekannt.Clear();
            }

            _bekannt[call.Handle] = new Eintrag(call.RemoteNumber, name, name);
        }

        return name;
    }

    private void OnContextChanged(object? sender, ContextSnapshot snapshot)
    {
        var handle = snapshot.Call;

        string? neu;

        lock (_gate)
        {
            if (!_bekannt.TryGetValue(handle, out var eintrag))
            {
                // Zu diesem Gespraech hat noch niemand gefragt — dann gibt es
                // auch nichts nachzumelden.
                return;
            }

            neu = eintrag.ClipName
                ?? ContextRoles.Text(snapshot, FieldRole.Name);

            if (string.Equals(neu, eintrag.LetzterName, StringComparison.Ordinal))
            {
                return;
            }

            _bekannt[handle] = eintrag with { LetzterName = neu };
        }

        PartyChanged?.Invoke(this, handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_context is not null)
        {
            _context.ContextChanged -= OnContextChanged;
        }
    }

    /// <param name="Number">Die Nummer, zu der aufgelöst wurde.</param>
    /// <param name="ClipName">Der lokal gefundene Name, oder <c>null</c>.</param>
    /// <param name="LetzterName">Was zuletzt gemeldet wurde — für die Änderungsprüfung.</param>
    private sealed record Eintrag(string? Number, string? ClipName, string? LetzterName);
}
