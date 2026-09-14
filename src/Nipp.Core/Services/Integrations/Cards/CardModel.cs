using Nipp.Core.Services.Integrations.Context;

namespace Nipp.Core.Services.Integrations.Cards;

/// <summary>
/// Eine fertig aufgelöste Karte — die Eingabe des Renderers (§21).
///
/// <b>Hier stehen keine Ausdrücke mehr</b>, nur Werte, Sichtbarkeiten und
/// fertige Aktionen. Das ist die Naht zwischen Kern und Oberfläche: der Kern
/// rechnet, die Oberfläche zeichnet. Ein Renderer, der selbst auswerten
/// müsste, wäre nicht ohne Fenster testbar — und genau das soll er sein.
/// </summary>
public sealed record CardModel(string DefinitionId, IReadOnlyList<CardSectionModel> Sections)
{
    public static CardModel Empty { get; } = new(string.Empty, []);

    /// <summary>Ob überhaupt etwas zu zeigen ist.</summary>
    public bool HasContent =>
        Sections.Any(static s => s.Visible && s.Rows.Any(static r =>
            r.Columns.Any(static c => c.Elements.Any(static e => e.Visible))));
}

/// <summary>Ein Abschnitt, mit ausgewerteter Sichtbarkeit.</summary>
public sealed record CardSectionModel(
    string Id,
    string? Title,
    bool Visible,
    IReadOnlyList<CardRowModel> Rows);

public sealed record CardRowModel(IReadOnlyList<CardColumnModel> Columns);

public sealed record CardColumnModel(int Span, IReadOnlyList<CardElementModel> Elements);

/// <summary>
/// Ein Baustein mit fertigem Inhalt.
/// </summary>
/// <param name="Key">
/// Stabil je Element der Definition — <c>abschnitt.zeile.spalte.element</c>.
///
/// <b>Wofür der Schlüssel da ist:</b> die Karte wird bei jeder Antwort einer
/// Quelle neu aufgebaut, und das auf dem Thread, der alle 20 ms
/// <c>Core.Iterate()</c> bedient (§6). Ein Renderer, der seine Steuerelemente
/// über diesen Schlüssel wiederfindet, setzt nur Text und Sichtbarkeit, statt
/// den Baum jedes Mal neu zu erzeugen.
/// </param>
/// <param name="Visible">Ob der Baustein gezeigt wird.</param>
public abstract record CardElementModel(string Key, bool Visible);

public sealed record CardTextModel(
    string Key,
    bool Visible,
    string Text,
    CardTextStyle Style,
    int MaxLines) : CardElementModel(Key, Visible);

public sealed record CardFieldModel(
    string Key,
    bool Visible,
    string Label,
    string Value,
    bool ShowLabel = true) : CardElementModel(Key, Visible);

/// <summary>Der Ton eines Abzeichens, aus dem Ausdruck aufgelöst.</summary>
public enum CardTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

public sealed record CardBadgeModel(
    string Key,
    bool Visible,
    string Text,
    CardTone Tone) : CardElementModel(Key, Visible);

public sealed record CardDividerModel(string Key, bool Visible) : CardElementModel(Key, Visible);

/// <summary>Ein Abstand. Der Renderer macht daraus Luft, sonst nichts.</summary>
public sealed record CardSpacerModel(
    string Key,
    bool Visible,
    CardSpacerSize Size) : CardElementModel(Key, Visible);

public sealed record CardButtonModel(
    string Key,
    bool Visible,
    string Label,
    bool Enabled,
    CardResolvedAction? Action) : CardElementModel(Key, Visible);

public sealed record CardLinkModel(
    string Key,
    bool Visible,
    string Label,
    Uri? Target) : CardElementModel(Key, Visible);

public sealed record CardSourceStatusModel(
    string Key,
    bool Visible,
    string SourceId,
    string DisplayName,
    SourceState State,
    string Text) : CardElementModel(Key, Visible);

/// <summary>
/// Eine Aktion mit eingesetzten Werten — was passiert, wenn jemand drückt.
///
/// <b>Die Adresse ist hier bereits geprüft</b>: nur <c>http</c> und
/// <c>https</c> kommen als <see cref="CardOpenUrl"/> durch. Der Renderer muss
/// nicht noch einmal entscheiden, ob etwas sicher ist — er führt aus, was
/// dasteht.
/// </summary>
public abstract record CardResolvedAction;

public sealed record CardOpenUrl(Uri Target) : CardResolvedAction;

public sealed record CardDial(string Number) : CardResolvedAction;

public sealed record CardCopy(string Value) : CardResolvedAction;
