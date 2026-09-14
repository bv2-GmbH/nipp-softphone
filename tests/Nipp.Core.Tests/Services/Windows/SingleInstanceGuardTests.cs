using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// W2.4 und Befund B23: nipp läuft einmal, auch ohne Paketidentität.
///
/// <para><b>Der Befund.</b> §10 verlangt eine Instanz, und
/// <c>AppInstance.FindOrRegisterForKey</c> liefert sie nur <em>mit</em>
/// Paketidentität. Ausgeliefert wird unpackaged — der Normalfall im Feld war
/// also der Fall ohne Schutz, und der Kommentar im Code gab es zu: «dann läuft
/// nipp eben mehrfach».</para>
///
/// <para><b>Der zweite Wächter läuft auf einem eigenen Thread, und das ist
/// keine Umständlichkeit.</b> Ein Mutex ist <b>pro Thread</b> reentrant: liefen
/// beide im selben Testthread, bekäme der zweite die Sperre sofort, und der
/// Test wäre grün, ohne etwas zu prüfen. Genau so ist der erste Anlauf
/// ausgegangen. In der Praxis sind es zwei Prozesse; ein eigener Thread ist die
/// nächstliegende Nachbildung davon.</para>
///
/// <para><b>Und sie laufen nacheinander</b>, nicht parallel: sie teilen sich
/// eine maschinenweite Sperre und einen Pipenamen — genau das ist ihr
/// Gegenstand.</para>
/// </summary>
[Collection("Einzelinstanz")]
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Der_erste_bekommt_die_Sperre()
    {
        using var erster = new SingleInstanceGuard(NullLogger.Instance);

        Assert.True(erster.TryBecomeFirstInstance(string.Empty));
    }

    [Fact]
    public void Der_zweite_bekommt_sie_nicht()
    {
        using var erster = new SingleInstanceGuard(NullLogger.Instance);
        Assert.True(erster.TryBecomeFirstInstance(string.Empty));

        Assert.False(AufEigenemThread(static g => g.TryBecomeFirstInstance(string.Empty)));
    }

    [Fact]
    public void Nach_dem_Freigeben_bekommt_der_naechste_sie()
    {
        // Die Gegenprobe. Eine Sperre, die den Neustart blockiert, wäre
        // schlimmer als zwei Instanzen — und genau das passiert, wenn Dispose
        // den Mutex nicht freigibt.
        var erster = new SingleInstanceGuard(NullLogger.Instance);
        Assert.True(erster.TryBecomeFirstInstance(string.Empty));
        erster.Dispose();

        Assert.True(AufEigenemThread(static g => g.TryBecomeFirstInstance(string.Empty)));
    }

    [Fact]
    public async Task Der_zweite_reicht_seine_Nummer_weiter()
    {
        // Der eigentliche Zweck: ein tel:-Klick aus Outlook startet einen
        // zweiten Prozess mit der Nummer in der Kommandozeile. Die darf nicht
        // verlorengehen, nur weil schon ein nipp läuft.
        using var erster = new SingleInstanceGuard(NullLogger.Instance);
        Assert.True(erster.TryBecomeFirstInstance(string.Empty));

        var empfangen = new TaskCompletionSource<string>();
        erster.Activated += text => empfangen.TrySetResult(text);

        Assert.False(AufEigenemThread(static g => g.TryBecomeFirstInstance("+41791234567")));

        var fertig = await Task.WhenAny(empfangen.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(fertig == empfangen.Task, "Die laufende Instanz hat nichts empfangen.");
        Assert.Equal("+41791234567", await empfangen.Task);
    }

    /// <summary>
    /// Führt eine Wächter-Handlung auf einem eigenen Thread aus und gibt sie
    /// danach frei — die Nachbildung eines zweiten Prozesses.
    /// </summary>
    private static bool AufEigenemThread(Func<SingleInstanceGuard, bool> was)
    {
        var ergebnis = false;

        var thread = new Thread(() =>
        {
            using var zweiter = new SingleInstanceGuard(NullLogger.Instance);
            ergebnis = was(zweiter);
        });

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Der zweite Waechter haengt.");

        return ergebnis;
    }
}

/// <summary>
/// Hält die Tests der Einzelinstanz auseinander — sie teilen sich eine
/// maschinenweite Sperre.
/// </summary>
[CollectionDefinition("Einzelinstanz", DisableParallelization = true)]
public sealed class EinzelinstanzSammlung;
