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

    public EditorView()
    {
        InitializeComponent();
        UpdateToolColorDots();

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
        if (sender is not ToggleButton { Tag: string tag })
            return;

        var tool = Enum.Parse<BoardTool>(tag);

        // Повторный клик по активному инструменту закрывает его панель.
        if (Canvas.Tool == tool && IsToolPanelOpen())
        {
            CloseToolPanels();
            SyncToolButtons();
            return;
        }

        SelectTool(tool);
    }

    private void SelectTool(BoardTool tool)
    {
        Canvas.Tool = tool;
        CloseToolPanels();

        switch (tool)
        {
            case BoardTool.Pen:
            case BoardTool.Marker:
                ShowPenPanel(tool);
                break;

            case BoardTool.Shape:
                ShapePanel.Visibility = Visibility.Visible;
                RefreshShapePalette();
                break;

            case BoardTool.Eraser:
                EraserPanel.Visibility = Visibility.Visible;
                break;
        }

        if (tool != BoardTool.Cursor)
            Canvas.ClearSelection();

        SyncToolButtons();
        Canvas.UpdateCursor();
        Canvas.InvalidateVisual();
    }

    private void SyncToolButtons()
    {
        CursorTool.IsChecked = Canvas.Tool == BoardTool.Cursor;
        HandTool.IsChecked = Canvas.Tool == BoardTool.Hand;
        PenTool.IsChecked = Canvas.Tool == BoardTool.Pen;
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

        if (Canvas.Tool is BoardTool.Pen or BoardTool.Marker or
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

        // Ползунки работают по индексам шагов, поэтому переводим
        // сохранённое значение в ближайший шаг.
        ThicknessSlider.Value = NearestStep(ThicknessSteps,
            isMarker ? Canvas.MarkerThickness : Canvas.PenThickness);

        OpacitySlider.Value = NearestStep(OpacitySteps,
            (isMarker ? Canvas.MarkerOpacity : Canvas.PenOpacity) * 100);

        var palette = new ColorPalette(isMarker ? Canvas.MarkerColor : Canvas.PenColor);
        palette.ColorPicked += color =>
        {
            if (Canvas.Tool == BoardTool.Marker)
                Canvas.MarkerColor = color;
            else
                Canvas.PenColor = color;

            UpdateStrokePreview();
            UpdateToolColorDots();
        };
        PenPaletteHost.Content = palette;

        UpdateStrokePreview();
    }

    private void UpdateToolColorDots()
    {
        if (PenColorDot is not null)
            PenColorDot.Fill = new SolidColorBrush(Canvas.PenColor);
        if (MarkerColorDot is not null)
            MarkerColorDot.Fill = new SolidColorBrush(Canvas.MarkerColor);
    }

    private void UpdateStrokePreview()
    {
        // Ползунки объявлены в XAML раньше предпросмотра, поэтому при разборе
        // разметки этот метод вызывается, когда StrokePreview ещё не создан.
        if (Canvas is null || StrokePreview is null)
            return;

        var isMarker = Canvas.Tool == BoardTool.Marker;
        var color = isMarker ? Canvas.MarkerColor : Canvas.PenColor;
        var thickness = isMarker ? Canvas.MarkerThickness : Canvas.PenThickness;
        var opacity = isMarker ? Canvas.MarkerOpacity : Canvas.PenOpacity;

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

    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null)
            return;

        var index = Math.Clamp((int)Math.Round(e.NewValue), 0, ThicknessSteps.Length - 1);
        var value = ThicknessSteps[index];

        if (Canvas.Tool == BoardTool.Marker)
            Canvas.MarkerThickness = value;
        else
            Canvas.PenThickness = value;

        if (ThicknessValue is not null)
            ThicknessValue.Text = value.ToString("0");

        UpdateStrokePreview();
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null)
            return;

        var index = Math.Clamp((int)Math.Round(e.NewValue), 0, OpacitySteps.Length - 1);
        var percent = OpacitySteps[index];

        if (Canvas.Tool == BoardTool.Marker)
            Canvas.MarkerOpacity = percent / 100.0;
        else
            Canvas.PenOpacity = percent / 100.0;

        if (OpacityValue is not null)
            OpacityValue.Text = $"{percent:0} %";

        UpdateStrokePreview();
    }

    private void EraserSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null)
            return;

        Canvas.EraserSize = Math.Round(e.NewValue);
        if (EraserValue is not null)
            EraserValue.Text = Canvas.EraserSize.ToString("0");
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
        ShapeThicknessSlider.Value = NearestStep(ThicknessSteps, Canvas.ShapeThickness);

        var palette = new ColorPalette(Canvas.ShapeColor);
        palette.ColorPicked += color => Canvas.ShapeColor = color;
        ShapePaletteHost.Content = palette;
    }

    private void ShapeThickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null)
            return;

        var index = Math.Clamp((int)Math.Round(e.NewValue), 0, ThicknessSteps.Length - 1);
        Canvas.ShapeThickness = ThicknessSteps[index];

        if (ShapeThicknessValue is not null)
            ShapeThicknessValue.Text = Canvas.ShapeThickness.ToString("0");
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
