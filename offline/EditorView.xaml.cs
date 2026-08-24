using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;

namespace SchoolPiBoard.Views;

public partial class EditorView : UserControl
{
    private MainWindow _shell = null!;
    private Board? _board;
    private bool _dirty;

    private readonly DispatcherTimer _autoSave = new();
    private List<BoardItem> _clipboard = new();
    private BoardItem? _editingItem;
    private bool _objectPanelAbove;
    private Popup? _activeObjectPalettePopup;
    private readonly HashSet<BoardTool> _toolInitialized = new();

    public EditorView()
    {
        InitializeComponent();
        UpdateToolColorDots();

        BuildChoiceOptions();
        BuildShapeButtons();
        BuildHelpContent();
        InitializeTimer();

        Canvas.Changed += OnCanvasChanged;
        Canvas.SelectionChanged += OnSelectionChanged;
        Canvas.ViewChanged += OnViewChanged;
        Canvas.EditTextRequested += BeginTextEdit;
        Canvas.SizeChanged += (_, _) => UpdateObjectPanelPosition();

        // Клик по холсту закрывает всплывающие панели.
        // Для рисующих инструментов панель параметров скрывается сразу
        // при начале взаимодействия с холстом, чтобы не перекрывать рисунок.
        Canvas.PreviewMouseDown += Canvas_PreviewMouseDown;

        _autoSave.Interval = TimeSpan.FromSeconds(3);
        _autoSave.Tick += (_, _) => SaveIfDirty();
        _autoSave.Start();

        Loaded += (_, _) => SelectTool(BoardTool.Cursor);
    }

    public void Initialize(MainWindow shell) => _shell = shell;

    public void FocusCanvas() => Canvas.Focus();

    // =====================================================================
    //  Загрузка и сохранение
    // =====================================================================
    public void LoadBoard(Board board)
    {
        SaveIfDirty();

        _board = board;
        BoardTitle.Text = board.Name;

        // Большая доска может собираться заметное время, поэтому
        // показываем индикатор и продолжаем после отрисовки кадра.
        ShowLoading(true);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Canvas.LoadBoard(board);
                _dirty = false;
                SaveIndicator.Text = "";

                CloseTransientPanels();
                _toolInitialized.Clear();
                SelectTool(BoardTool.Cursor);

                BackgroundContent.Content = new BackgroundPanel(board, () =>
                {
                    Canvas.InvalidateVisual();
                    MarkDirty();
                });

                // Открывая доску, показываем всё её содержимое целиком.
                Canvas.FitToContent();
            }
            finally
            {
                ShowLoading(false);
            }
        }), DispatcherPriority.Background);
    }

    /// <summary>Показывает или прячет индикатор загрузки с вращающейся дугой.</summary>
    private void ShowLoading(bool visible)
    {
        if (LoadingOverlay is null)
            return;

        if (visible)
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            LoadingRotation.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            LoadingRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCanvasChanged() => MarkDirty();

    private void MarkDirty()
    {
        _dirty = true;
        SaveIndicator.Text = "есть несохранённые изменения";
        UpdateUndoRedoState();
    }

    public void SaveIfDirty()
    {
        if (_board is null || !_dirty)
            return;

        Canvas.CommitToBoard();
        _shell.Store.TouchModified(_board);
        _shell.Store.Save();

        _dirty = false;
        SaveIndicator.Text = "сохранено";
    }

    private void UpdateUndoRedoState()
    {
        UndoButton.IsEnabled = Canvas.CanUndo;
        RedoButton.IsEnabled = Canvas.CanRedo;
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _shell.ShowHome();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_board is null)
            return;

        var name = PromptDialog.Show(Window.GetWindow(this)!, "Переименовать доску",
                                      "Новое название:", _board.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        _board.Name = name.Trim();
        BoardTitle.Text = _board.Name;
        _shell.Store.Save();
    }

    // =====================================================================
    //  Инструменты
    // =====================================================================
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag }) return;
        var tool = Enum.Parse<BoardTool>(tag);

        if (Canvas.Tool == tool)
        {
            // Второй клик по уже выбранному инструменту — явное открытие настроек.
            if (IsToolPanelOpen()) CloseToolPanels();
            else OpenToolPanel(tool);
            SyncToolButtons();
            return;
        }

        SelectTool(tool);
    }

    private void SelectTool(BoardTool tool)
    {
        Canvas.Tool = tool;
        CloseToolPanels();

        // При переключении инструментов панель не всплывает автоматически.
        // Исключение — первый выбор этого инструмента после открытия доски.
        if (!_toolInitialized.Contains(tool))
        {
            _toolInitialized.Add(tool);
            OpenToolPanel(tool);
        }

        if (tool != BoardTool.Cursor) Canvas.ClearSelection();
        SyncToolButtons();
        Canvas.UpdateCursor();
        Canvas.InvalidateVisual();
    }

    private void OpenToolPanel(BoardTool tool)
    {
        CloseToolPanels();
        switch (tool)
        {
            case BoardTool.Pen:
            case BoardTool.Pen2:
            case BoardTool.Marker:
                ShowPenPanel(tool); break;
            case BoardTool.Shape:
                ShapePanel.Visibility = Visibility.Visible;
                RefreshShapePalette(); break;
            case BoardTool.Eraser:
                EraserPanel.Visibility = Visibility.Visible; break;
        }
    }

    private void SyncToolButtons()
    {
        CursorTool.IsChecked = Canvas.Tool == BoardTool.Cursor;
        HandTool.IsChecked = Canvas.Tool == BoardTool.Hand;
        PenTool.IsChecked = Canvas.Tool == BoardTool.Pen;
        Pen2Tool.IsChecked = Canvas.Tool == BoardTool.Pen2;
        MarkerTool.IsChecked = Canvas.Tool == BoardTool.Marker;
        EraserTool.IsChecked = Canvas.Tool == BoardTool.Eraser;
        TextTool.IsChecked = Canvas.Tool == BoardTool.Text;
        ShapeTool.IsChecked = Canvas.Tool == BoardTool.Shape;
    }

    private bool IsToolPanelOpen() =>
        PenPanel.Visibility == Visibility.Visible ||
        ShapePanel.Visibility == Visibility.Visible ||
        EraserPanel.Visibility == Visibility.Visible;

    private void CloseToolPanels()
    {
        PenPanel.Visibility = Visibility.Collapsed;
        ShapePanel.Visibility = Visibility.Collapsed;
        EraserPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Закрывает справку, таймер и панель фона (но не панель инструмента).</summary>
    private void CloseTransientPanels()
    {
        HelpPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Collapsed;
        BackgroundPanelHost.Visibility = Visibility.Collapsed;
    }

    private void Canvas_PreviewMouseDown(object? sender, MouseButtonEventArgs e)
    {
        CloseTransientPanels();

        if (Canvas.Tool is BoardTool.Pen or BoardTool.Pen2 or BoardTool.Marker or
            BoardTool.Eraser or BoardTool.Shape)
        {
            CloseToolPanels();
            CloseObjectPalettePopup();
        }
    }

    private void CloseObjectPalettePopup()
    {
        if (_activeObjectPalettePopup is null)
            return;

        _activeObjectPalettePopup.IsOpen = false;
        _activeObjectPalettePopup = null;
    }

    // =====================================================================
    //  Панель пера: у каждого инструмента свои цвет и толщина
    // =====================================================================
    private void ShowPenPanel(BoardTool tool)
    {
        PenPanel.Visibility = Visibility.Visible;
        var isMarker = tool == BoardTool.Marker;
        var isPen2 = tool == BoardTool.Pen2;
        var thickness = isMarker ? Canvas.MarkerThickness : isPen2 ? Canvas.Pen2Thickness : Canvas.PenThickness;
        var opacity = isMarker ? Canvas.MarkerOpacity : isPen2 ? Canvas.Pen2Opacity : Canvas.PenOpacity;
        var color = isMarker ? Canvas.MarkerColor : isPen2 ? Canvas.Pen2Color : Canvas.PenColor;
        SetChoice(ThicknessOptions, ThicknessSteps, thickness);
        SetChoice(OpacityOptions, OpacitySteps, opacity * 100);

        var palette = new ColorPalette(color);
        palette.ColorPicked += picked =>
        {
            if (Canvas.Tool == BoardTool.Marker) Canvas.MarkerColor = picked;
            else if (Canvas.Tool == BoardTool.Pen2) Canvas.Pen2Color = picked;
            else Canvas.PenColor = picked;
            UpdateStrokePreview();
            UpdateToolColorDots();
        };
        PenPaletteHost.Content = palette;
        UpdateStrokePreview();
    }

    private void BuildChoiceOptions()
    {
        BuildChoiceGroup(ThicknessOptions, ThicknessSteps, (i, value) =>
        {
            if (Canvas.Tool == BoardTool.Marker) Canvas.MarkerThickness = value;
            else if (Canvas.Tool == BoardTool.Pen2) Canvas.Pen2Thickness = value;
            else if (Canvas.Tool == BoardTool.Shape) Canvas.ShapeThickness = value;
            else Canvas.PenThickness = value;
            UpdateStrokePreview();
            if (Canvas.Tool == BoardTool.Shape) HighlightShapeButtons();
        }, true);
        BuildChoiceGroup(OpacityOptions, OpacitySteps, (i, value) =>
        {
            var v = value / 100.0;
            if (Canvas.Tool == BoardTool.Marker) Canvas.MarkerOpacity = v;
            else if (Canvas.Tool == BoardTool.Pen2) Canvas.Pen2Opacity = v;
            else Canvas.PenOpacity = v;
            UpdateStrokePreview();
        }, false);
        BuildChoiceGroup(ShapeThicknessOptions, ThicknessSteps, (_, value) => Canvas.ShapeThickness = value, true);
        BuildEraserSizeOptions();
    }

    private static readonly double[] EraserSizeSteps = { 8, 16, 26, 60, 120 };

    private void BuildEraserSizeOptions()
    {
        EraserSizeOptions.Children.Clear();
        for (var i = 0; i < EraserSizeSteps.Length; i++)
        {
            var index = i;
            var toggle = new ToggleButton
            {
                Content = new Border
                {
                    Width = 34, Height = 34, Background = Brushes.Transparent,
                    Child = new Ellipse
                    {
                        Width = new[] { 5.0, 7.5, 12.0, 20.0, 30.0 }[i],
                        Height = new[] { 5.0, 7.5, 12.0, 20.0, 30.0 }[i],
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Style = (Style)FindResource("ChoiceButton"),
                Margin = new Thickness(2),
                Height = 42
            };
            toggle.Checked += (_, _) =>
            {
                foreach (var other in EraserSizeOptions.Children.OfType<ToggleButton>())
                    if (!ReferenceEquals(other, toggle)) other.IsChecked = false;
                Canvas.EraserSize = EraserSizeSteps[index];
                Canvas.InvalidateVisual();
            };
            EraserSizeOptions.Children.Add(toggle);
        }

        SetChoice(EraserSizeOptions, EraserSizeSteps, Canvas.EraserSize);
    }

    private void BuildChoiceGroup(UniformGrid grid, double[] values, Action<int,double> changed, bool sizePreview)
    {
        grid.Children.Clear();
        for (var i = 0; i < values.Length; i++)
        {
            var index = i;
            var toggle = new ToggleButton
            {
                Content = sizePreview ? CreateSizeOptionVisual(i + 1, values[i]) : CreateOpacityOptionVisual(i + 1, values[i]),
                Style = (Style)FindResource("ChoiceButton"),
                Tag = index,
                Margin = new Thickness(2),
                Height = 42
            };
            toggle.Checked += (_, _) =>
            {
                foreach (var other in grid.Children.OfType<ToggleButton>())
                    if (!ReferenceEquals(other, toggle)) other.IsChecked = false;
                changed(index, values[index]);
            };
            grid.Children.Add(toggle);
        }
    }

    private static FrameworkElement CreateSizeOptionVisual(int number, double size)
    {
        // Номер намеренно не показывается: размер должен читаться визуально по точке.
        return new Border
        {
            Width = 34, Height = 34, Background = Brushes.Transparent,
            Child = new Ellipse
            {
                Width = Math.Clamp(size, 3, 26),
                Height = Math.Clamp(size, 3, 26),
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static FrameworkElement CreateOpacityOptionVisual(int number, double percent)
    {
        // Процент намеренно не показывается: прозрачность читается по самой точке.
        return new Ellipse
        {
            Width = 22, Height = 22,
            Fill = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp((int)Math.Round(percent * 2.55), 0, 255),
                255, 255, 255))
        };
    }

    private void SetChoice(UniformGrid grid, double[] values, double value)
    {
        var index = NearestStep(values, value);
        for (var i = 0; i < grid.Children.Count; i++)
            if (grid.Children[i] is ToggleButton b) b.IsChecked = i == index;
    }

    private void UpdateToolColorDots()
    {
        if (PenColorDot is not null) PenColorDot.Fill = new SolidColorBrush(Canvas.PenColor);
        if (Pen2ColorDot is not null) Pen2ColorDot.Fill = new SolidColorBrush(Canvas.Pen2Color);
        if (MarkerColorDot is not null) MarkerColorDot.Fill = new SolidColorBrush(Canvas.MarkerColor);
    }

    private void UpdateStrokePreview()
    {
        if (Canvas is null || StrokePreview is null) return;
        var isMarker = Canvas.Tool == BoardTool.Marker;
        var isPen2 = Canvas.Tool == BoardTool.Pen2;
        var color = isMarker ? Canvas.MarkerColor : isPen2 ? Canvas.Pen2Color : Canvas.PenColor;
        var thickness = isMarker ? Canvas.MarkerThickness : isPen2 ? Canvas.Pen2Thickness : Canvas.PenThickness;
        var opacity = isMarker ? Canvas.MarkerOpacity : isPen2 ? Canvas.Pen2Opacity : Canvas.PenOpacity;
        StrokePreview.Background = new SolidColorBrush(color);
        StrokePreview.Height = Math.Clamp(thickness, 1, 26);
        StrokePreview.Opacity = opacity;
        Canvas.InvalidateVisual();
    }

    // Ползунки переключаются только по этим значениям — промежуточных нет.
    private static readonly double[] ThicknessSteps = { 1, 5, 10, 15, 20, 30 };
    private static readonly double[] OpacitySteps = { 20, 40, 50, 70, 100 };

    private static int NearestStep(double[] steps, double value)
    {
        var best = 0;
        for (var i = 1; i < steps.Length; i++)
        {
            if (Math.Abs(steps[i] - value) < Math.Abs(steps[best] - value))
                best = i;
        }
        return best;
    }

    private void EraserSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Оставлено для совместимости со старыми пользовательскими настройками.
        if (Canvas is not null)
            Canvas.EraserSize = Math.Round(e.NewValue);
    }

    // =====================================================================
    //  Панель фигур
    // =====================================================================
    private void BuildShapeButtons()
    {
        (string Glyph, ShapeKind Kind, string Tip)[] shapes =
        {
            ("／", ShapeKind.Line, "Линия"),
            ("↗", ShapeKind.Arrow, "Стрелка"),
            ("▭", ShapeKind.Rectangle, "Прямоугольник"),
            ("◯", ShapeKind.Ellipse, "Овал"),
            ("△", ShapeKind.Triangle, "Треугольник"),
            ("⏢", ShapeKind.Trapezoid, "Трапеция"),
            ("▱", ShapeKind.Parallelogram, "Параллелограмм"),
            ("◇", ShapeKind.Rhombus, "Ромб")
        };

        foreach (var (glyph, kind, tip) in shapes)
        {
            var button = new Button
            {
                Content = glyph,
                FontSize = 18,
                Width = 60,
                Height = 42,
                Margin = new Thickness(2),
                Style = (Style)FindResource("IconButton"),
                Tag = kind,
                ToolTip = tip
            };
            button.Click += (s, _) =>
            {
                if (s is Button { Tag: ShapeKind picked })
                {
                    Canvas.ShapeTool = picked;
                    ShapeTool.Content = ((Button)s).Content;
                    HighlightShapeButtons();
                }
            };
            ShapeButtons.Children.Add(button);
        }

        HighlightShapeButtons();
    }

    private void HighlightShapeButtons()
    {
        foreach (var child in ShapeButtons.Children)
        {
            if (child is Button button && button.Tag is ShapeKind kind)
            {
                button.Background = kind == Canvas.ShapeTool
                    ? (Brush)FindResource("SurfaceActive")
                    : System.Windows.Media.Brushes.Transparent;
            }
        }
    }

    private void RefreshShapePalette()
    {
        SetChoice(ShapeThicknessOptions, ThicknessSteps, Canvas.ShapeThickness);
        UpdateLineStylePreview();

        var palette = new ColorPalette(Canvas.ShapeColor);
        palette.ColorPicked += color => Canvas.ShapeColor = color;
        ShapePaletteHost.Content = palette;
    }

    private void ShapeLineStyleButton_Click(object sender, RoutedEventArgs e)
    {
        _lineStyleTargetItem = null;
        ShapeLineStylePopup.PlacementTarget = ShapeLineStyleButton;
        ShapeLineStylePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        UpdateLineStylePreview();
        ShapeLineStylePopup.IsOpen = true;
    }

    private void LineStyleOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<LineStyle>(tag, out var style))
        {
            if (_lineStyleTargetItem is not null)
            {
                Canvas.ApplyToSelection(i => i.LineStyle = style);
                UpdateObjectPanelSwatches();
                if (Canvas.Selection.FirstOrDefault() is { } selected)
                    SetLineStylePreview(ObjectLineStylePreview, selected.LineStyle);
            }
            else
            {
                Canvas.ShapeLineStyle = style;
                UpdateLineStylePreview();
            }

            _lineStyleTargetItem = null;
            ShapeLineStylePopup.IsOpen = false;
            ObjectLineStylePopup.IsOpen = false;
        }
    }

    private static void SetLineStylePreview(Line target, LineStyle style)
    {
        target.StrokeDashArray = style switch
        {
            LineStyle.Dash => new DoubleCollection { 4, 3 },
            LineStyle.DashDot => new DoubleCollection { 4, 2, 1, 2 },
            LineStyle.Dot => new DoubleCollection { 1, 2.5 },
            _ => null
        };
    }

    private void UpdateLineStylePreview()
    {
        if (ShapeLineStylePreview is not null)
            SetLineStylePreview(ShapeLineStylePreview, Canvas.ShapeLineStyle);
    }

    // =====================================================================
    //  Масштаб
    // =====================================================================
    private void OnViewChanged()
    {
        ZoomLabel.Text = $"{Math.Round(Canvas.Zoom * 100)} %";
        UpdateObjectPanelPosition();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Canvas.ZoomToCenter(1.15);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomToCenter(1 / 1.15);
    private void ZoomLabel_Click(object sender, MouseButtonEventArgs e) =>
        Canvas.SetZoomAndCenterOnSelection(1.0);
    private void FitToContent_Click(object sender, RoutedEventArgs e) => Canvas.FitToContent();

    // =====================================================================
    //  Отмена, повтор, очистка
    // =====================================================================
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Undo();
        UpdateUndoRedoState();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Redo();
        UpdateUndoRedoState();
    }

    private void ClearBoard_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas.Items.Count == 0)
            return;

        var confirmed = ConfirmDialog.Show(Window.GetWindow(this)!,
            "Очистить доску",
            "Всё содержимое доски будет удалено. Действие можно отменить сочетанием Ctrl+Z.",
            "Очистить", danger: true);

        if (confirmed)
            Canvas.ClearBoard();
    }

    // =====================================================================
    //  Клавиатура
    // =====================================================================
    public void SetSpaceHeld(bool held) => Canvas.SetSpaceHeld(held);

    /// <summary>Возвращает true, если событие обработано.</summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: Canvas.Undo(); UpdateUndoRedoState(); return true;
                case Key.Y: Canvas.Redo(); UpdateUndoRedoState(); return true;
                case Key.C: CopySelection(); return true;
                case Key.X: CutSelection(); return true;
                case Key.V: PasteClipboard(); return true;
                case Key.D: Canvas.DuplicateSelection(); return true;
                case Key.A: Canvas.SelectAll(); return true;
                case Key.S: SaveIfDirty(); return true;
            }
        }

        switch (e.Key)
        {
            case Key.Space:
                Canvas.SetSpaceHeld(true);
                return true;

            case Key.Delete:
            case Key.Back:
                Canvas.DeleteSelection();
                return true;

            case Key.Escape:
                // Esc всегда возвращает к курсору и закрывает всплывающие панели.
                CloseTransientPanels();
                CloseToolPanels();
                Canvas.ClearSelection();
                SelectTool(BoardTool.Cursor);
                return true;
        }

        return false;
    }
}
