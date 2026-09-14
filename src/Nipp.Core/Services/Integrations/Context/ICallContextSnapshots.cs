using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Was zu einem laufenden Anruf aus externen Quellen bekannt ist.
///
/// <para><b>Warum es diese Schnittstelle gibt, obwohl es nur eine Umsetzung
/// gibt.</b> Sie ist die Testnaht. <see cref="CallerContextService"/> zu bauen
/// verlangt einen Telefoniedienst, eine Registry, den Konfigurationsspeicher,
/// die Einstellungen und einen Logger; eine Attrappe dieser Schnittstelle
/// braucht drei Zeilen. Dasselbe Muster wie bei <c>ISipEventPump</c>.</para>
///
/// <para><c>CallerContextService</c> erfüllt sie <b>ohne eine einzige neue
/// Codezeile</b> — beide Mitglieder gibt es dort bereits mit genau dieser
/// Signatur.</para>
/// </summary>
public interface ICallContextSnapshots
{
    /// <summary>Was zu einem Anruf bekannt ist, oder <c>null</c>.</summary>
    ContextSnapshot? SnapshotFor(CallHandle handle);

    /// <summary>Ein neuer Stand zu einem Anruf.</summary>
    event EventHandler<ContextSnapshot>? ContextChanged;
}
