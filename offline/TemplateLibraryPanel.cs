using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private const string GroupCustom = "custom";

    private readonly Action<BoardTemplate, Dictionary<string, double>> _insertTemplate;
    private readonly Action<string> _insertText;
    private readonly Action<CustomTemplate> _insertCustomTemplate;
    private readonly Dictionary<string, Dictionary<string, double>> _values = new();
    private readonly List<ToggleButton> _tabButtons = new();
    private readonly StackPanel _body = new();
    private string _activeGroup = GroupCoordinates;

    public TemplateLibraryPanel(
        Action<BoardTemplate, Dictionary<string, double>> insertTemplate,
        Action<string> insertText,
        Action<CustomTemplate> insertCustomTemplate)
    {
        _insertTemplate = insertTemplate;
        _insertText = insertText;
        _insertCustomTemplate = insertCustomTemplate;

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

        // Заготовка, сохранённая или удалённая где угодно (панель объекта
        // могла вызвать это, пока открыта совсем другая вкладка), обновляет
        // список «Мои», только если он сейчас на экране — не хочется дёргать
        // прокрутку, пока пользователь читает «Координаты».
        CustomTemplateStore.Changed += OnCustomTemplatesChanged;
        Unloaded += (_, _) => CustomTemplateStore.Changed -= OnCustomTemplatesChanged;
    }

    private void OnCustomTemplatesChanged()
    {
        if (_activeGroup == GroupCustom)
            ShowGroup(GroupCustom);
    }

    private UIElement BuildTabs()
    {
        var row = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 12) };

        foreach (var (group, title) in new[]
                 {
                     (GroupCoordinates, "Координаты"),
                     (GroupSigns, "Знаки"),
                     (GroupFormulas, "Формулы"),
                     (GroupCustom, "Мои")
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
        _activeGroup = group;
        _body.Children.Clear();

        switch (group)
        {
            case GroupSigns:
                _body.Children.Add(BuildSignsGrid());
                break;
            case GroupFormulas:
                _body.Children.Add(BuildFormulasList());
                break;
            case GroupCustom:
                _body.Children.Add(BuildCustomList());
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

    /// <summary>
    /// Число вводится прямо цифрами, а не выбирается кареткой на полоске —
    /// для маленького диапазона (обычно 3–10/12 делений) так быстрее и точнее.
    /// </summary>
    private UIElement BuildNumberKnob(TemplateKnob knob, Dictionary<string, double> values)
    {
        var min = (int)knob.Min;
        var max = (int)knob.Max;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"{knob.Label} ({min}–{max})",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var input = new TextBox
        {
            Text = ((int)Math.Round(values[knob.Key])).ToString(),
            Width = 52,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(4, 4, 4, 4),
            Margin = new Thickness(10, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["AppBg2"],
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            BorderBrush = (Brush)Application.Current.Resources["Accent"],
            BorderThickness = new Thickness(1),
            CaretBrush = (Brush)Application.Current.Resources["TextPrimary"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(input, 1);
        row.Children.Add(input);

        // Только цифры — нечисловой ввод отфильтровываем сразу, не постфактум.
        input.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DataObject.AddPastingHandler(input, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.Text) is not string pasted || !pasted.All(char.IsDigit))
                e.CancelCommand();
        });

        void Commit()
        {
            if (!int.TryParse(input.Text, out var parsed))
                parsed = (int)Math.Round(values[knob.Key]);

            var clamped = Math.Clamp(parsed, min, max);
            values[knob.Key] = clamped;

            if (input.Text != clamped.ToString())
                input.Text = clamped.ToString();
            input.CaretIndex = input.Text.Length;
        }

        input.LostFocus += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            Commit();
            Keyboard.ClearFocus();
            e.Handled = true;
        };

        if (knob.Suffix is not null)
        {
            var suffix = new TextBlock
            {
                Text = knob.Suffix,
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(suffix, 2);
            row.Children.Add(suffix);
        }

        return row;
    }

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

    /// <summary>
    /// Заготовки, которые сохранил сам пользователь — с доски, через меню
    /// «⋯» у выделенных объектов. Вставляются как есть, без ползунков:
    /// это уже готовый рисунок, а не рецепт с параметрами.
    /// </summary>
    private UIElement BuildCustomList()
    {
        var templates = CustomTemplateStore.Templates;

        if (templates.Count == 0)
        {
            return new TextBlock
            {
                Text = "Пока пусто. Выделите рисунок на доске и в его меню «⋯» выберите " +
                       "«Сохранить как заготовку» — она появится здесь.",
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 2, 6)
            };
        }

        var list = new StackPanel();
        foreach (var template in templates.OrderByDescending(t => t.Created))
            list.Children.Add(BuildCustomCard(template));
        return list;
    }

    private UIElement BuildCustomCard(CustomTemplate template)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBg3"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = template.Title,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var delete = new Button
        {
            Content = "🗑",
            Style = (Style)Application.Current.Resources["IconButton"],
            Width = 32,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        delete.Click += (_, _) => CustomTemplateStore.Delete(template.Id);
        Grid.SetColumn(delete, 1);
        header.Children.Add(delete);

        stack.Children.Add(header);

        var insert = new Button
        {
            Content = "Вставить",
            Style = (Style)Application.Current.Resources["AccentButton"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        insert.Click += (_, _) => _insertCustomTemplate(template);
        stack.Children.Add(insert);

        card.Child = stack;
        return card;
    }
}
