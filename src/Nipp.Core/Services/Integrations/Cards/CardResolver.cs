using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Welche Karte für welche Art gilt (§21, ADR-032).
///
/// <para><b>Die Regel in einem Satz:</b> die Karte aus der Konfiguration,
/// sonst die mitgelieferte. Und wenn die aus der Konfiguration nicht
/// übersetzbar ist, ebenfalls die mitgelieferte — <b>plus einen Befund</b>.
/// I4 verlangt genau das: „eine Karte mit Fehler in der Definition zeigt einen
/// Hinweis statt abzustürzen".</para>
///
/// <para><b>Warum das eine eigene Klasse ist und keine Zeile im ViewModel.</b>
/// Es gibt drei Empfänger für dieselbe Frage — die Gesprächsansicht, der Toast
/// und der Designer in seiner Vorschau. Genau diese Bauart hat schon einmal
/// Geld gekostet: die Frage „beginnt hier ein Anruf zu klingeln?" stand wörtlich
/// gleich in <c>MainWindow</c> und im <c>ToastService</c> und war an beiden
/// Stellen falsch. Zwei Kopien einer Regel sind zwei Gelegenheiten, sie falsch
/// zu haben, und eine Gelegenheit, nur die eine zu korrigieren.</para>
///
/// <para><b>Übersetzt wird beim Laden der Konfiguration, nicht bei jedem
/// Anruf.</b> Ein Parser im Anrufpfad wäre die falsche Arbeit auf dem Thread,
/// der alle 20 ms <c>Core.Iterate()</c> bedient (§6).</para>
/// </summary>
public sealed class CardResolver : IDisposable
{
    private readonly IntegrationConfigStore _config;
    private readonly ILogger<CardResolver> _logger;

    /// <summary>Je Art die übersetzte Karte, die gerade gilt.</summary>
    private Dictionary<CardKind, CompiledCard> _compiled = [];

    private IReadOnlyList<ValidationIssue> _issues = [];
    private bool _disposed;

    public CardResolver(IntegrationConfigStore config, ILogger<CardResolver> logger)
    {
        _config = config;
        _logger = logger;

        _config.Changed += OnConfigChanged;

        Rebuild();
    }

    /// <summary>
    /// Wird ausgelöst, nachdem die Karten neu übersetzt sind.
    ///
    /// Die Empfänger halten eine <see cref="CompiledCard"/> und müssen sie
    /// nach einer Änderung neu holen — sonst zeigt die Gesprächsansicht die
    /// Karte von vorher, und niemand versteht, warum eine gespeicherte
    /// Änderung nicht ankommt.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Was an den Karten zu bemängeln war. Steht in den Einstellungen neben
    /// den Befunden der Quellen.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>Die übersetzte Karte einer Art. Nie <c>null</c>, notfalls leer.</summary>
    public CompiledCard For(CardKind kind) =>
        _compiled.TryGetValue(kind, out var card) ? card : new CompiledCard();

    /// <summary>
    /// Die Beschreibung, die gerade gilt — für den Designer, der sie zum
    /// Bearbeiten braucht, und für die Anzeige „7 Zeilen" in den
    /// Einstellungen.
    /// </summary>
    public CardDefinition? DefinitionFor(CardKind kind) =>
        FromConfig(kind) ?? DefaultCards.For(kind);

    /// <summary>Ob diese Art aus der Konfiguration kommt oder mitgeliefert ist.</summary>
    public bool IsCustom(CardKind kind) => FromConfig(kind) is not null;

    private CardDefinition? FromConfig(CardKind kind) =>
        _config.Current.Cards.FirstOrDefault(c => c.Kind == kind);

    private void OnConfigChanged(object? sender, IntegrationConfig config) => Rebuild();

    private void Rebuild()
    {
        var compiled = new Dictionary<CardKind, CompiledCard>();
        var issues = new List<ValidationIssue>();

        foreach (var kind in Enum.GetValues<CardKind>())
        {
            var eigene = FromConfig(kind);

            if (eigene is not null)
            {
                if (CardLayoutEngine.TryCompile(eigene, out var karte, out var fehler))
                {
                    compiled[kind] = karte;
                    continue;
                }

                // §15: Ursache und Abhilfe. Und der Rückfall wird benannt —
                // sonst sieht der Benutzer die mitgelieferte Karte und hält
                // seine eigene für gespeichert.
                foreach (var eintrag in fehler)
                {
                    issues.Add(new ValidationIssue(
                        $"cards[{eigene.Id}]",
                        IssueSeverity.Error,
                        $"{eintrag} Bis das behoben ist, gilt die mitgelieferte Karte."));
                }

                CardLog.CardBroken(_logger, eigene.Id, fehler.Count);
            }

            if (CardLayoutEngine.TryCompile(DefaultCards.For(kind), out var mitgeliefert, out _))
            {
                compiled[kind] = mitgeliefert;
            }
        }

        _compiled = compiled;
        _issues = issues;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _config.Changed -= OnConfigChanged;
    }
}
