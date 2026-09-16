using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

/// <summary>
/// Всплывающая панель «Заготовки»: готовые чертежи (сейчас — группа
/// «Координаты»), а также быстрая вставка знаков и формул простым текстом.
///
/// Заготовка настраивается ползунками и переключателями до вставки —
/// значения хранятся здесь, в панели, а не в уже вставленных объектах:
/// иначе пересчёт по новому числу делений стирал бы то, что человек
/// дорисовал и подписал поверх чертежа.
/// </summary>
public class TemplateLibraryPanel : UserControl
{
    private const string GroupCoordinates = "coordinates";
    private const string GroupSigns = "signs";
    private const string GroupFormulas = "formulas";

    private readonly Action<BoardTemplate, Dictionary<string, double>> _insertTemplate;
    private readonly Action<string> _insertText;
    private readonly Dictionary<string, Dictionary<string, double>> _values = new();
    private readonly List<ToggleButton> _tabButtons = new();
    private readonly StackPanel _body = new();

    public TemplateLibraryPanel(
        Action<BoardTemplate, Dictionary<string, double>> insertTemplate,
        Action<string> insertText)
    {
        _insertTemplate = insertTemplate;
        _insertText = insertText;

        foreach (var template in BoardTemplateLibrary.All)
            _values[template.Id] = new Dictionary<string, double>(template.Defaults);

        var root = new StackPanel { Width = 360 };

        root.Children.Add(new TextBlock
        {
            Text = "Заготовки",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        root.Children.Add(BuildTabs());
        root.Children.Add(_body);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        ShowGroup(GroupCoordinates);
    }

    private UIElement BuildTabs()
    {
        var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 12) };

        foreach (var (group, title) in new[]
                 {
                     (GroupCoordinates, "Координаты"),
                     (GroupSigns, "Знаки"),
                     (GroupFormulas, "Формулы")
                 })
        {
            var tab = new ToggleButton
            {
                Content = title,
                Style = (Style)Application.Current.Resources["ChoiceButton"],
                Height = 36,
                Margin = new Thickness(3),
                Tag = group
            };
            tab.Checked += (_, _) =>
            {
                foreach (var other in _tabButtons)
                    if (!ReferenceEquals(other, tab)) other.IsChecked = false;
                ShowGroup(group);
            };
            _tabButtons.Add(tab);
            row.Children.Add(tab);
        }

        _tabButtons[0].IsChecked = true;
        return row;
    }

    private void ShowGroup(string group)
    {
        _body.Children.Clear();

        switch (group)
        {
            case GroupSigns:
                _body.Children.Add(BuildSignsGrid());
                break;
            case GroupFormulas:
                _body.Children.Add(BuildFormulasList());
                break;
            default:
                foreach (var template in BoardTemplateLibrary.All)
                    if (template.Group == group)
                        _body.Children.Add(BuildTemplateCard(template));
                break;
        }
    }

    private UIElement BuildTemplateCard(BoardTemplate template)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBg3"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = template.Title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = template.Hint,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 10)
        });

        var values = _values[template.Id];
        foreach (var knob in template.Knobs)
            stack.Children.Add(knob.Kind == KnobKind.Toggle
                ? BuildToggleKnob(knob, values)
                : BuildNumberKnob(knob, values));

        var insert = new Button
        {
            Content = "Вставить",
            Style = (Style)Application.Current.Resources["AccentButton"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        insert.Click += (_, _) => _insertTemplate(template, new Dictionary<string, double>(values));
        stack.Children.Add(insert);

        card.Child = stack;
        return card;
    }

    private UIElement BuildNumberKnob(TemplateKnob knob, Dictionary<string, double> values)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = knob.Label,
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        header.Children.Add(label);

        var valueText = new TextBlock
        {
            Text = FormatKnobValue(values[knob.Key], knob.Suffix),
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);

        Grid.SetRow(header, 0);
        row.Children.Add(header);

        var slider = new Slider
        {
            Minimum = knob.Min,
            Maximum = knob.Max,
            Value = values[knob.Key],
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Margin = new Thickness(0, 2, 0, 0)
        };
        slider.ValueChanged += (_, e) =>
        {
            values[knob.Key] = Math.Round(e.NewValue);
            valueText.Text = FormatKnobValue(values[knob.Key], knob.Suffix);
        };
        Grid.SetRow(slider, 1);
        row.Children.Add(slider);

        return row;
    }

    private static string FormatKnobValue(double value, string? suffix) =>
        suffix is null ? value.ToString("0") : value.ToString("0") + suffix;

    private UIElement BuildToggleKnob(TemplateKnob knob, Dictionary<string, double> values)
    {
        var toggle = new ToggleButton
        {
            Content = knob.Label,
            Style = (Style)Application.Current.Resources["ChoiceButton"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 0, 8),
            IsChecked = values[knob.Key] > 0
        };
        toggle.Checked += (_, _) => values[knob.Key] = 1;
        toggle.Unchecked += (_, _) => values[knob.Key] = 0;
        return toggle;
    }

    private UIElement BuildSignsGrid()
    {
        var grid = new UniformGrid { Columns = 5 };

        foreach (var sign in QuickInsertLibrary.Signs)
        {
            var button = new Button
            {
                Content = sign,
                FontSize = 18,
                Width = 60,
                Height = 44,
                Margin = new Thickness(2),
                Style = (Style)Application.Current.Resources["IconButton"]
            };
            button.Click += (_, _) => _insertText(sign);
            grid.Children.Add(button);
        }

        return grid;
    }

    private UIElement BuildFormulasList()
    {
        var list = new StackPanel();

        foreach (var formula in QuickInsertLibrary.Formulas)
        {
            var button = new Button
            {
                Content = formula,
                Style = (Style)Application.Current.Resources["TextButton"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(10, 8, 10, 8)
            };
            button.Click += (_, _) => _insertText(formula);
            list.Children.Add(button);
        }

        return list;
    }
}
