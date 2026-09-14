using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nipp.App.Theming;
using Nipp.Core.Services.Integrations.Cards;

namespace Nipp.App.Controls.Cards;

/// <summary>
/// Zeichnet eine aufgelöste Karte (§21).
///
/// <b>Dieser Renderer entscheidet nichts.</b> Was sichtbar ist, was dasteht
/// und was eine Schaltfläche tut, hat die <c>CardLayoutEngine</c> längst
/// ausgerechnet — hier wird nur gezeichnet. Die Trennung ist der Grund, warum
/// sich die Kartenlogik ohne Fenster prüfen lässt.
///
/// <b>Kein freies Markup</b> (§21.2): jeder Baustein wird auf ein festes
/// Steuerelement abgebildet, und was der Renderer nicht kennt, lässt er weg.
/// Eine Konfigurationsdatei kann damit keine Oberfläche erzeugen, die hier
/// nicht vorgesehen ist.
///
/// <b>Warum ein Neuaufbau vertretbar ist.</b> Die Karte hat ein Dutzend
/// Zeilen, und sie wird nur neu gebaut, wenn eine Quelle antwortet — also
/// zwei- bis viermal je Anruf, nicht bei jedem Iterate. Elemente über ihren
/// Schlüssel wiederzuverwenden wäre messbar schneller und deutlich schwerer
/// richtig zu bekommen; sollte es je auffallen, ist der Schlüssel dafür da
/// (<c>CardElementModel.Key</c>).
/// </summary>
public sealed class CardView : ContentControl
{
    /// <summary>Wird ausgelöst, wenn jemand eine Schaltfläche oder einen Verweis betätigt.</summary>
    public event EventHandler<CardResolvedAction>? ActionInvoked;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(CardModel),
        typeof(CardView),
        new PropertyMetadata(null, OnModelChanged));

    /// <summary>Die Karte, die gezeichnet wird.</summary>
    public CardModel? Model
    {
        get => (CardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((CardView)sender).Rebuild();

    private void Rebuild()
    {
        if (Model is not { } model || !model.HasContent)
        {
            Content = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        var root = new StackPanel { Spacing = 8 };

        foreach (var section in model.Sections.Where(static s => s.Visible))
        {
            root.Children.Add(BuildSection(section));
        }

        Content = root;
        Visibility = Visibility.Visible;
    }

    private StackPanel BuildSection(CardSectionModel section)
    {
        var panel = new StackPanel { Spacing = 4 };

        if (section.Title is { Length: > 0 } title)
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Style = Resource<Style>("BodyStrongTextBlockStyle"),
            });
        }

        foreach (var row in section.Rows)
        {
            if (BuildRow(row) is { } element)
            {
                panel.Children.Add(element);
            }
        }

        return panel;
    }

    /// <summary>
    /// Eine Zeile als Raster.
    ///
    /// <b>Der Umbruch bei schmaler Ansicht fehlt hier bewusst.</b> Das Fenster
    /// ist rund 400 Pixel breit (§20.1), und das Raster hat sechs Einheiten —
    /// eine Zeile aus zwei Hälften ist damit bereits die schmalste sinnvolle
    /// Aufteilung. Wer eine Karte baut, die enger wird, sieht es in der
    /// Vorschau.
    /// </summary>
    private Grid? BuildRow(CardRowModel row)
    {
        var visible = row.Columns
            .Where(static c => c.Elements.Any(static e => e.Visible))
            .ToList();

        if (visible.Count == 0)
        {
            return null;
        }

        var grid = new Grid { ColumnSpacing = 8 };

        foreach (var column in visible)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Clamp(column.Span, 1, CardLayout.Columns), GridUnitType.Star),
            });
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var stack = new StackPanel { Spacing = 2 };

            foreach (var element in visible[i].Elements.Where(static e => e.Visible))
            {
                if (Build(element) is { } control)
                {
                    stack.Children.Add(control);
                }
            }

            Grid.SetColumn(stack, i);
            grid.Children.Add(stack);
        }

        return grid;
    }

    /// <summary>
    /// Ein Baustein.
    ///
    /// <c>null</c> für alles Unbekannte — das ist der Fall, wenn eine Karte
    /// aus einer neueren Version einen Typ mitbringt, den dieser Renderer
    /// nicht kennt. Sie zeigt dann weniger, statt abzustürzen.
    /// </summary>
    private FrameworkElement? Build(CardElementModel element) => element switch
    {
        CardTextModel text => new TextBlock
        {
            Text = text.Text,
            MaxLines = text.MaxLines,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = text.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Style = StyleFor(text.Style),
            Foreground = text.Style is CardTextStyle.Caption or CardTextStyle.Subtitle
                ? Converters.ThemeBrushes.Get("CardSecondaryTextBrush")
                : null,
        },

        CardFieldModel field => BuildField(field),
        CardBadgeModel badge => BuildBadge(badge),

        CardDividerModel => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Background = Converters.ThemeBrushes.Get("CardDividerBrush"),
        },

        // Ein Abstand ist Luft und sonst nichts. Für die Sprachausgabe ist er
        // unsichtbar (AccessibilityView.Raw): ein leeres Element vorzulesen
        // hiesse, eine Pause zum Inhalt zu machen.
        CardSpacerModel spacer => BuildSpacer(spacer),

        CardButtonModel button => BuildButton(button),
        CardLinkModel link => BuildLink(link),

        CardSourceStatusModel status => new TextBlock
        {
            Text = status.Text,
            TextWrapping = TextWrapping.Wrap,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>(status.State switch
            {
                Nipp.Core.Services.Integrations.Context.SourceState.Error
                    or Nipp.Core.Services.Integrations.Context.SourceState.Timeout
                    => "StatusFailedBrush",
                _ => "TextFillColorTertiaryBrush",
            }),
        },

        _ => null,
    };

    /// <summary>
    /// Ein Abstand mit einer der drei Grössen (ADR-037).
    ///
    /// Die Pixelwerte stehen hier und nicht in der Kartenbeschreibung: eine
    /// Konfigurationsdatei setzt keine Bildschirmmasse (§21.2), und was
    /// „mittel" heisst, entscheidet die Oberfläche.
    /// </summary>
    private static Border BuildSpacer(CardSpacerModel spacer)
    {
        var border = new Border
        {
            Height = spacer.Size switch
            {
                CardSpacerSize.Small => 4,
                CardSpacerSize.Large => 24,
                _ => 12,
            },
        };

        AutomationProperties.SetAccessibilityView(border, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        return border;
    }

    /// <summary>
    /// Eine Beschriftung mit Wert.
    ///
    /// <para><b>Ohne Beschriftung fällt die Spalte weg</b>, nicht nur ihr Text
    /// (ADR-037): die 110 Pixel Mindestbreite blieben sonst stehen, und der
    /// Wert stünde eingerückt an einer leeren Stelle — genau das, was „nur den
    /// Wert" vermeiden soll.</para>
    ///
    /// <para><b>Die Beschriftung bleibt trotzdem hörbar.</b> Sie wandert als
    /// <c>AutomationProperties.Name</c> an den Wert. §8.4: eine Aussage, die
    /// nur an der Darstellung hängt, kommt bei einer Sprachausgabe nicht
    /// an.</para>
    /// </summary>
    private static Grid BuildField(CardFieldModel field)
    {
        var grid = new Grid { ColumnSpacing = 8 };

        var value = new TextBlock
        {
            Text = field.Value,
            Style = Resource<Style>("BodyTextBlockStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (!field.ShowLabel)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (field.Label is { Length: > 0 })
            {
                AutomationProperties.SetName(value, $"{field.Label}: {field.Value}");
            }

            grid.Children.Add(value);

            return grid;
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = field.Label,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Converters.ThemeBrushes.Get("CardSecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);

        return grid;
    }

    /// <summary>
    /// Ein Abzeichen. <b>Der Ton wird auch als Text getragen</b> — über
    /// <c>AutomationProperties</c> —, weil §8.4 verlangt, dass eine Aussage
    /// nie allein an der Farbe hängt.
    /// </summary>
    private static Border BuildBadge(CardBadgeModel badge)
    {
        // Der Ton steht als RAND UND SCHRIFT da, nicht als Flaeche (ADR-044).
        //
        // Vorher war er der Hintergrund, und der Text erbte seine Farbe vom
        // Thema: im dunklen Erscheinungsbild stand damit fast weisse Schrift
        // auf StatusProgress (#FCE100, Gelb) — rund 1,4:1, praktisch unlesbar.
        // Eine Tonfarbe als Flaeche verlangt einen mitgesetzten Vordergrund,
        // und der muesste je Thema ein anderer sein.
        //
        // Als Vordergrund sind dieselben Pinsel dagegen in beiden Themen
        // geprueft — die Kontaktliste zeigt ihre Praesenz seit jeher so, und
        // AccountStateCatalog faerbt seinen Fehlertext ebenso.
        var ton = Resource<Brush>(badge.Tone switch
        {
            CardTone.Success => "StatusRegisteredBrush",
            CardTone.Warning => "StatusProgressBrush",
            CardTone.Danger => "StatusFailedBrush",
            CardTone.Info => "AccentTextFillColorPrimaryBrush",
            _ => "CardSecondaryTextBrush",
        });

        var beschriftung = new TextBlock
        {
            Text = badge.Text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
        };

        if (ton is not null)
        {
            beschriftung.Foreground = ton;
        }

        var border = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = ThemeValues.Radius("ControlCornerRadius", 4),
            Background = Converters.ThemeBrushes.Get("CardFillBrush"),
            BorderBrush = ton,
            BorderThickness = new Thickness(ton is null ? 0 : 1),
            Child = beschriftung,
        };

        AutomationProperties.SetName(border, $"{badge.Text} ({Describe(badge.Tone)})");

        return border;
    }

    private static string Describe(CardTone tone) => tone switch
    {
        CardTone.Success => "gut",
        CardTone.Warning => "Achtung",
        CardTone.Danger => "kritisch",
        CardTone.Info => "Hinweis",
        _ => "neutral",
    };

    private Button BuildButton(CardButtonModel model)
    {
        var button = new Button
        {
            Content = model.Label,
            IsEnabled = model.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (model.Action is { } action)
        {
            button.Click += (_, _) => ActionInvoked?.Invoke(this, action);
        }

        return button;
    }

    private HyperlinkButton BuildLink(CardLinkModel model)
    {
        var link = new HyperlinkButton { Content = model.Label, Padding = new Thickness(0) };

        if (model.Target is { } target)
        {
            link.Click += (_, _) => ActionInvoked?.Invoke(this, new CardOpenUrl(target));
        }

        return link;
    }

    private static Style? StyleFor(CardTextStyle style) => Resource<Style>(style switch
    {
        CardTextStyle.Title => "SubtitleTextBlockStyle",
        CardTextStyle.Subtitle => "BodyStrongTextBlockStyle",
        CardTextStyle.Caption => "CaptionTextBlockStyle",
        _ => "BodyTextBlockStyle",
    });

    /// <summary>
    /// Eine Ressource der Anwendung.
    ///
    /// <c>null</c>, wenn es sie nicht gibt: ein fehlender Schlüssel darf die
    /// Karte nicht kosten. <c>XamlResourceTests</c> prüft die Verweise im
    /// XAML, aber dieser Renderer baut seine Elemente im Code — dort greift
    /// der Test nicht.
    /// </summary>
    private static T? Resource<T>(string key)
        where T : class =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;
}
