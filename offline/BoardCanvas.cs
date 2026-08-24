using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Rendering;

public enum BoardTool
{
    Cursor,
    Hand,
    Pen,
    Pen2,
    Marker,
    Eraser,
    Text,
    Shape
}

public enum HandleKind
{
    None, NW, N, NE, E, SE, S, SW, W, Rotate, LineStart, LineEnd
}

/// <summary>
/// Холст доски: собственная камера (сдвиг + масштаб), отрисовка объектов,
/// выделение, трансформации и ластик. Границ у холста нет — сдвиг не ограничен.
/// </summary>
public class BoardCanvas : FrameworkElement
{
    // ---- настройка поведения ----
    private const double StraightenHoldMs = 500;
    private const double StraightenMinLengthPx = 45;
    private const double StraightenMaxDeviation = 0.10;
    private const double ShiftAngleStep = Math.PI / 12.0; // 15°
    private const double EraserSpeedFull = 2400;
    private const double EraserMaxGrowth = 0.55;
    private const double HandleSizePx = 7;
    private const double RotateHandleOffsetPx = 30;
    private const double StrokeMergeDistancePx = 25;
    private const double StrokeMergeDelayMs = 500;

    // ---- документ ----
    public Board? Board { get; private set; }
    public List<BoardItem> Items { get; private set; } = new();
    public List<BoardItem> Selection { get; } = new();

    // ---- камера ----
    public double Zoom { get; private set; } = 1.0;
    public Point Offset { get; private set; } = new(0, 0);

    // ---- инструменты ----
    private BoardTool _tool = BoardTool.Cursor;
    public BoardTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
                return;
            _tool = value;
            _lastFreeStrokeItem = null;
            _lastFreeStrokeFinishedAt = DateTime.MinValue;
        }
    }
    public ShapeKind ShapeTool { get; set; } = ShapeKind.Rectangle;

    public Color PenColor { get; set; } = Colors.White;
    public Color PenCustomColor { get; set; } = Colors.White;
    public Color Pen2Color { get; set; } = Colors.Red;
    public Color Pen2CustomColor { get; set; } = Colors.Red;
    public Color MarkerColor { get; set; } = Color.FromRgb(0xFB, 0xBC, 0x04);
    public Color MarkerCustomColor { get; set; } = Color.FromRgb(0xFB, 0xBC, 0x04);
    public Color ShapeColor { get; set; } = Color.FromRgb(0x4D, 0xD0, 0xE1);
    public Color TextColor { get; set; } = Colors.White;

    // Значения по умолчанию совпадают с шагами ползунков,
    // иначе при открытии панели ползунок «прыгал» бы к ближайшему шагу.
    public double PenThickness { get; set; } = 5;
    public double Pen2Thickness { get; set; } = 5;
    public double MarkerThickness { get; set; } = 15;
    public double ShapeThickness { get; set; } = 5;
    public LineStyle ShapeLineStyle { get; set; } = LineStyle.Solid;
    public double PenOpacity { get; set; } = 1.0;
    public double Pen2Opacity { get; set; } = 1.0;
    public double MarkerOpacity { get; set; } = 0.5;
    public double EraserSize { get; set; } = 26;

    // ---- события для оболочки ----
    public event Action? Changed;             // документ изменён (нужно сохранить)
    public event Action? SelectionChanged;    // изменился состав выделения
    public event Action? ViewChanged;         // изменился масштаб или сдвиг
    public event Action<BoardItem>? EditTextRequested;

    // ---- внутреннее состояние ввода ----
    private bool _spaceHeld;
    private bool _panning;
    private Point _panStartScreen;
    private Point _panStartOffset;

    private bool _drawing;
    private BoardItem? _draft;
    private Point _drawStartWorld;
    private readonly List<Point> _draftPoints = new();
    private bool _forceStraight;
    private bool _straightStroke;
    private bool _shiftStraight;
    private DateTime _lastMoveTime = DateTime.Now;
    private Point _lastMoveScreen;
    private readonly DispatcherTimer _straightenTimer;

    private bool _marquee;
    private Point _marqueeStartWorld;
    private Point _marqueeCurrentWorld;
    private readonly List<Point> _lassoPoints = new();
    private bool _marqueeIsRect;

    private HandleKind _activeHandle = HandleKind.None;
    private bool _dragging;
    private Point _dragStartWorld;
    private List<BoardItem> _dragOriginals = new();
    private Rect _dragOriginalBounds;

    private Point? _eraserScreen;
    private Point? _toolCursorScreen;
    private double _eraserVisualRadius;
    private Point? _lastEraseWorld;
    private DateTime _lastEraseTime;
    private bool _eraseChanged;

    private BoardItem? _lastFreeStrokeItem;
    private Point _lastFreeStrokeEnd;
    private DateTime _lastFreeStrokeFinishedAt = DateTime.MinValue;

    private readonly Stack<List<BoardItem>> _undo = new();
    private readonly Stack<List<BoardItem>> _redo = new();
    private List<BoardItem>? _pendingUndo;

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    public BoardCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;

        // Проверяем автоматическое выпрямление независимо от MouseMove.
        // Это позволяет показать переход в режим прямой сразу после паузы,
        // даже если курсор в этот момент уже не двигается.
        _straightenTimer = new DispatcherTimer(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(30) };
        _straightenTimer.Tick += (_, _) => CheckStraightenHold();
    }

    // =====================================================================
    //  Документ
    // =====================================================================
    public void LoadBoard(Board board)
    {
        Board = board;
        Items = board.Items.Select(i => i.Clone()).ToList();
        Selection.Clear();
        _undo.Clear();
        _redo.Clear();
        _lastFreeStrokeItem = null;
        _lastFreeStrokeFinishedAt = DateTime.MinValue;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void CommitToBoard()
    {
        if (Board is null)
            return;
        Board.Items = Items.Select(i => i.Clone()).ToList();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
        InvalidateVisual();
    }

    // =====================================================================
    //  Отмена и повтор
    // =====================================================================
    private List<BoardItem> Snapshot() => Items.Select(i => i.Clone()).ToList();

    public void BeginChange() => _pendingUndo = Snapshot();

    public void CommitChange()
    {
        if (_pendingUndo is null)
            return;

        _undo.Push(_pendingUndo);
        if (_undo.Count > 20)
        {
            var kept = _undo.ToArray().Take(20).Reverse().ToList();
            _undo.Clear();
            foreach (var s in kept)
                _undo.Push(s);
        }

        _redo.Clear();
        _pendingUndo = null;
        RaiseChanged();
    }

    public void CancelChange() => _pendingUndo = null;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        _redo.Push(Snapshot());
        if (_redo.Count > 20)
        {
            var kept = _redo.ToArray().Take(20).Reverse().ToList();
            _redo.Clear();
            foreach (var s in kept)
                _redo.Push(s);
        }
        Items = _undo.Pop();
        Selection.Clear();
        SelectionChanged?.Invoke();
        RaiseChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        _undo.Push(Snapshot());
        if (_undo.Count > 20)
        {
            var kept = _undo.ToArray().Take(20).Reverse().ToList();
            _undo.Clear();
            foreach (var s in kept)
                _undo.Push(s);
        }
        Items = _redo.Pop();
        Selection.Clear();
        SelectionChanged?.Invoke();
        RaiseChanged();
    }

    // =====================================================================
    //  Камера
    // =====================================================================
    public Point ToWorld(Point screen) =>
        new(screen.X / Zoom + Offset.X, screen.Y / Zoom + Offset.Y);

    public Point ToScreen(Point world) =>
        new((world.X - Offset.X) * Zoom, (world.Y - Offset.Y) * Zoom);

    public Rect VisibleWorld()
    {
        var w = Math.Max(1, ActualWidth);
        var h = Math.Max(1, ActualHeight);
        return new Rect(Offset.X, Offset.Y, w / Zoom, h / Zoom);
    }

    /// <summary>Масштабирование относительно точки экрана — она остаётся на месте.</summary>
    public void ZoomAt(Point screenAnchor, double factor)
    {
        var target = Math.Clamp(Zoom * factor, 0.02, 20.0);
        if (Math.Abs(target - Zoom) < 1e-9)
            return;

        var worldBefore = ToWorld(screenAnchor);
        Zoom = target;
        Offset = new Point(
            worldBefore.X - screenAnchor.X / Zoom,
            worldBefore.Y - screenAnchor.Y / Zoom);

        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    public void ZoomToCenter(double factor) =>
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    public void SetZoom(double zoom) =>
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), zoom / Zoom);

    /// <summary>
    /// Устанавливает масштаб и, если есть выделение, центрирует камеру на нём.
    /// Это используется при нажатии на индикатор масштаба.
    /// </summary>
    public void SetZoomAndCenterOnSelection(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.02, 20.0);

        if (Selection.Count == 0)
        {
            SetZoom(zoom);
            return;
        }

        var bounds = SelectionWorldBounds();
        if (bounds.IsEmpty)
        {
            SetZoom(zoom);
            return;
        }

        Zoom = zoom;

        var center = new Point(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);

        Offset = new Point(
            center.X - ActualWidth / (2 * Zoom),
            center.Y - ActualHeight / (2 * Zoom));

        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    public void PanBy(double dxScreen, double dyScreen)
    {
        Offset = new Point(Offset.X - dxScreen / Zoom, Offset.Y - dyScreen / Zoom);
        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Габариты всего содержимого доски.</summary>
    public Rect ContentBounds()
    {
        var bounds = Rect.Empty;
        foreach (var item in Items)
        {
            var r = ItemRenderer.RotatedBounds(item);
            bounds = bounds.IsEmpty ? r : Rect.Union(bounds, r);
        }
        return bounds;
    }

    /// <summary>Вписывает всё содержимое в экран; на пустой доске — масштаб 100 %.</summary>
    public void FitToContent()
    {
        if (ActualWidth < 10 || ActualHeight < 10)
            return;

        var bounds = ContentBounds();
        if (bounds.IsEmpty)
        {
            Zoom = 1.0;
            Offset = new Point(-ActualWidth / 2, -ActualHeight / 2);
            ViewChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        const double margin = 90;
        var zoomX = Math.Max(1, ActualWidth - margin) / Math.Max(1, bounds.Width);
        var zoomY = Math.Max(1, ActualHeight - margin) / Math.Max(1, bounds.Height);

        Zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.02, 4.0);

        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Offset = new Point(
            center.X - ActualWidth / (2 * Zoom),
            center.Y - ActualHeight / (2 * Zoom));

        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    // =====================================================================
    //  Отрисовка
    // =====================================================================
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 1 || height < 1)
            return;

        var background = ItemRenderer.ParseColor(Board?.BackgroundColor ?? "#FF1B1B1F")
                          ?? Color.FromRgb(0x1B, 0x1B, 0x1F);

        dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));

        // Переходим в мировые координаты: всё дальнейшее рисуется без пересчёта.
        dc.PushTransform(new MatrixTransform(Zoom, 0, 0, Zoom, -Offset.X * Zoom, -Offset.Y * Zoom));

        GridPainter.Draw(dc, Board?.Grid ?? GridStyle.Square, background, VisibleWorld(), Zoom, Board?.GridColor);

        var ppd = PixelsPerDip;
        foreach (var item in Items.OrderBy(i => i.Z))
            ItemRenderer.Draw(dc, item, ppd);

        if (_draft is not null)
            ItemRenderer.Draw(dc, _draft, ppd);

        dc.Pop();

        DrawSelectionVisuals(dc);
        DrawMarquee(dc);
        DrawEraserCursor(dc);
        DrawDrawingToolCursor(dc);
    }

    /// <summary>
    /// Рамка и маркеры рисуются в экранных координатах: их размер
    /// не должен зависеть от масштаба доски.
    /// </summary>
    private void DrawSelectionVisuals(DrawingContext dc)
    {
        if (Selection.Count == 0)
            return;

        var accent = Color.FromRgb(0x5B, 0x6C, 0xF7);
        var pen = new Pen(new SolidColorBrush(accent), 1.6)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        };

        var rect = SelectionScreenRect();
        dc.DrawRectangle(null, pen, rect);

        // При множественном выделении подсвечиваем каждый объект отдельно.
        if (Selection.Count > 1)
        {
            var thin = new Pen(new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)), 1);
            foreach (var item in Selection)
                dc.DrawRectangle(null, thin, ScreenRectOf(item));
        }

        var handleFill = new SolidColorBrush(Colors.White);
        var handlePen = new Pen(new SolidColorBrush(accent), 2);

        if (Selection.Count == 1 && IsEndpointEditableLine(Selection[0]) &&
            TryGetLineEndpoints(Selection[0], out var lineStart, out var lineEnd))
        {
            // У линии/стрелки только две ручки. Потянув одну, пользователь
            // одновременно вращает и растягивает её вокруг второго конца.
            foreach (var point in new[] { ToScreen(lineStart), ToScreen(lineEnd) })
            {
                dc.DrawEllipse(handleFill, handlePen, point, HandleSizePx / 2, HandleSizePx / 2);
            }
            return;
        }

        if (!Selection.All(IsStandardTransformable))
            return;

        foreach (var (_, point) in HandlePositions(rect))
        {
            dc.DrawRectangle(handleFill, handlePen, new Rect(
                point.X - HandleSizePx / 2, point.Y - HandleSizePx / 2,
                HandleSizePx, HandleSizePx));
        }

        var rotate = RotateHandlePosition(rect);
        dc.DrawLine(new Pen(new SolidColorBrush(accent), 1.4),
            new Point(rect.X + rect.Width / 2, rect.Y), rotate);

        // Понятный значок вращения: закрученная стрелка вместо обычной точки.
        var rotateGeometry = new StreamGeometry();
        using (var g = rotateGeometry.Open())
        {
            var r = HandleSizePx * 1.15;
            var cx = rotate.X;
            var cy = rotate.Y;
            g.BeginFigure(new Point(cx + r * 0.85, cy - r * 0.15), false, false);
            g.BezierTo(
                new Point(cx + r * 0.55, cy - r * 1.0),
                new Point(cx - r * 0.75, cy - r * 0.95),
                new Point(cx - r * 0.95, cy - r * 0.10), true, false);
            g.BezierTo(
                new Point(cx - r * 1.05, cy + r * 0.70),
                new Point(cx - r * 0.05, cy + r * 1.05),
                new Point(cx + r * 0.65, cy + r * 0.55), true, false);
            g.BeginFigure(new Point(cx + r * 0.85, cy + r * 0.20), false, false);
            g.LineTo(new Point(cx + r * 0.88, cy + r * 0.75), true, false);
            g.LineTo(new Point(cx + r * 0.32, cy + r * 0.58), true, false);
        }
        rotateGeometry.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2.0) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, rotateGeometry);
    }

    private void DrawMarquee(DrawingContext dc)
    {
        if (!_marquee)
            return;

        var accent = Color.FromArgb(60, 0x5B, 0x6C, 0xF7);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0x6C, 0xF7)), 1.2)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
        };

        if (_marqueeIsRect)
        {
            var a = ToScreen(_marqueeStartWorld);
            var b = ToScreen(_marqueeCurrentWorld);
            dc.DrawRectangle(new SolidColorBrush(accent), pen, new Rect(a, b));
        }
        else if (_lassoPoints.Count > 1)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToScreen(_lassoPoints[0]), true, true);
                for (var i = 1; i < _lassoPoints.Count; i++)
                    ctx.LineTo(ToScreen(_lassoPoints[i]), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(new SolidColorBrush(accent), pen, geometry);
        }
    }

    private void DrawEraserCursor(DrawingContext dc)
    {
        if (Tool != BoardTool.Eraser || _eraserScreen is not { } center)
            return;

        // Двойная обводка: контур виден и на светлом, и на тёмном фоне.
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 3),
            center, _eraserVisualRadius, _eraserVisualRadius);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 1.4),
            center, _eraserVisualRadius, _eraserVisualRadius);
    }

    private void DrawDrawingToolCursor(DrawingContext dc)
    {
        if (_toolCursorScreen is not { } center || Tool is not (BoardTool.Pen or BoardTool.Pen2 or BoardTool.Marker))
            return;

        var marker = Tool == BoardTool.Marker;
        var color = marker ? MarkerColor : Tool == BoardTool.Pen2 ? Pen2Color : PenColor;
        var opacity = marker ? MarkerOpacity : Tool == BoardTool.Pen2 ? Pen2Opacity : PenOpacity;
        var thickness = marker ? MarkerThickness : Tool == BoardTool.Pen2 ? Pen2Thickness : PenThickness;
        var radius = Math.Max(2.0, thickness * Zoom / 2.0);

        // Небольшая контрастная окантовка делает точку видимой на любом фоне.
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), 1.5),
            center, radius + 1.0, radius + 1.0);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255), color.R, color.G, color.B)),
            null, center, radius, radius);
    }

    // =====================================================================
    //  Выделение: геометрия рамки и маркеров
    // =====================================================================
    public Rect SelectionWorldBounds()
    {
        var bounds = Rect.Empty;
        foreach (var item in Selection)
        {
            var r = ItemRenderer.RotatedBounds(item);
            bounds = bounds.IsEmpty ? r : Rect.Union(bounds, r);
        }
        return bounds;
    }

    public Rect SelectionScreenRect()
    {
        var world = SelectionWorldBounds();
        if (world.IsEmpty)
            return Rect.Empty;

        var a = ToScreen(world.TopLeft);
        var b = ToScreen(world.BottomRight);
        return new Rect(a, b);
    }

    private Rect ScreenRectOf(BoardItem item)
    {
        var world = ItemRenderer.RotatedBounds(item);
        return new Rect(ToScreen(world.TopLeft), ToScreen(world.BottomRight));
    }

    private static IEnumerable<(HandleKind Kind, Point Position)> HandlePositions(Rect r)
    {
        yield return (HandleKind.NW, new Point(r.Left, r.Top));
        yield return (HandleKind.N, new Point(r.Left + r.Width / 2, r.Top));
        yield return (HandleKind.NE, new Point(r.Right, r.Top));
        yield return (HandleKind.E, new Point(r.Right, r.Top + r.Height / 2));
        yield return (HandleKind.SE, new Point(r.Right, r.Bottom));
        yield return (HandleKind.S, new Point(r.Left + r.Width / 2, r.Bottom));
        yield return (HandleKind.SW, new Point(r.Left, r.Bottom));
        yield return (HandleKind.W, new Point(r.Left, r.Top + r.Height / 2));
    }

    private static Point RotateHandlePosition(Rect r) =>
        new(r.Left + r.Width / 2, r.Top - RotateHandleOffsetPx);

    private void UpdateResizeCursor(Point screen)
    {
        var handle = HitHandle(screen);
        Cursor = handle switch
        {
            HandleKind.NW or HandleKind.SE => Cursors.SizeNWSE,
            HandleKind.NE or HandleKind.SW => Cursors.SizeNESW,
            HandleKind.N or HandleKind.S => Cursors.SizeNS,
            HandleKind.E or HandleKind.W => Cursors.SizeWE,
            HandleKind.Rotate => Cursors.Hand,
            HandleKind.LineStart or HandleKind.LineEnd => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private HandleKind HitHandle(Point screen)
    {
        if (Selection.Count == 0)
            return HandleKind.None;

        // Для одиночной прямой/стрелки и для выпрямленного штриха пера/маркера
        // используются только два конца. Перетягивание одного конца автоматически
        // и вращает, и растягивает линию, оставляя второй конец неподвижным.
        if (Selection.Count == 1 && IsEndpointEditableLine(Selection[0]))
        {
            var item = Selection[0];
            if (TryGetLineEndpoints(item, out var start, out var end))
            {
                if (ItemRenderer.Distance(screen, ToScreen(start)) <= HandleSizePx * 1.8)
                    return HandleKind.LineStart;
                if (ItemRenderer.Distance(screen, ToScreen(end)) <= HandleSizePx * 1.8)
                    return HandleKind.LineEnd;
            }
            return HandleKind.None;
        }

        // Масштабирование/вращение обычными ручками доступно только фигурам,
        // изображениям и тексту. Рисованные штрихи не получают дорогую
        // трансформацию через габаритную рамку.
        if (!Selection.All(IsStandardTransformable))
            return HandleKind.None;

        var rect = SelectionScreenRect();
        if (rect.IsEmpty)
            return HandleKind.None;

        if (ItemRenderer.Distance(screen, RotateHandlePosition(rect)) <= HandleSizePx * 1.6)
            return HandleKind.Rotate;

        foreach (var (kind, point) in HandlePositions(rect))
        {
            if (Math.Abs(screen.X - point.X) <= HandleSizePx &&
                Math.Abs(screen.Y - point.Y) <= HandleSizePx)
                return kind;
        }

        return HandleKind.None;
    }

    private static bool IsStandardTransformable(BoardItem item) =>
        item.Kind is ItemKind.Shape or ItemKind.Image or ItemKind.Text;

    private static bool IsEndpointEditableLine(BoardItem item) =>
        (item.Kind == ItemKind.Shape && (item.Shape == ShapeKind.Line || item.Shape == ShapeKind.Arrow)) ||
        (item.Kind == ItemKind.Stroke && item.IsStraightStroke);

    private static bool TryGetLineEndpoints(BoardItem item, out Point start, out Point end)
    {
        start = end = default;
        if (item.Kind == ItemKind.Shape && item.Points.Count >= 4)
        {
            start = new Point(item.Points[0], item.Points[1]);
            end = new Point(item.Points[2], item.Points[3]);
            return true;
        }

        if (item.Kind == ItemKind.Stroke && item.IsStraightStroke)
        {
            var points = item.EnumeratePoints().ToList();
            if (points.Count >= 2)
            {
                start = points[0];
                end = points[^1];
                return true;
            }
        }
        return false;
    }

    public BoardItem? HitItem(Point world)
    {
        var tolerance = 6 / Zoom;
        foreach (var item in Items.OrderByDescending(i => i.Z))
        {
            if (ItemRenderer.HitTest(item, world, tolerance))
                return item;
        }
        return null;
    }

    // =====================================================================
    //  Клавиатура (вызывается из окна)
    // =====================================================================
    public void SetSpaceHeld(bool held)
    {
        _spaceHeld = held;
        UpdateCursor();
    }

    public bool SpaceHeld => _spaceHeld;

    public void UpdateCursor()
    {
        if (_panning || _spaceHeld || Tool == BoardTool.Hand)
        {
            Cursor = _panning ? Cursors.ScrollAll : Cursors.Hand;
            return;
        }

        Cursor = Tool switch
        {
            BoardTool.Cursor => Cursors.Arrow,
            BoardTool.Text => Cursors.IBeam,
            BoardTool.Eraser => Cursors.None,
            BoardTool.Pen or BoardTool.Pen2 or BoardTool.Marker => Cursors.None,
            _ => Cursors.Cross
        };
        InvalidateVisual();
    }

    // =====================================================================
    //  Мышь
    // =====================================================================
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var screen = e.GetPosition(this);
        _toolCursorScreen = screen;
        var world = ToWorld(screen);

        // Панорама: средняя кнопка всегда, либо пробел + правая кнопка.
        if (e.ChangedButton == MouseButton.Middle ||
            (_spaceHeld && e.ChangedButton == MouseButton.Right))
        {
            StartPan(screen);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        // Двойной клик по фигуре или надписи открывает ввод текста.
        // FrameworkElement не имеет OnMouseDoubleClick, поэтому считаем клики сами.
        if (e.ClickCount == 2 && Tool == BoardTool.Cursor)
        {
            var target = HitItem(world);
            if (target is not null && target.Kind is ItemKind.Shape or ItemKind.Text)
            {
                EditTextRequested?.Invoke(target);
                e.Handled = true;
                return;
            }
        }

        if (Tool == BoardTool.Hand || _spaceHeld)
        {
            StartPan(screen);
            return;
        }

        switch (Tool)
        {
            case BoardTool.Cursor:
                StartCursorAction(screen, world);
                break;

            case BoardTool.Eraser:
                BeginChange();
                _eraseChanged = false;
                _lastEraseWorld = null;
                _lastEraseTime = DateTime.Now;
                EraseAt(world, EraserSize / 2 / Zoom);
                _drawing = true;
                break;

            case BoardTool.Text:
                CreateTextAt(world);
                break;

            case BoardTool.Pen:
            case BoardTool.Pen2:
            case BoardTool.Marker:
                StartStroke(screen, world);
                break;

            case BoardTool.Shape:
                StartShape(world);
                break;
        }

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var screen = e.GetPosition(this);
        _toolCursorScreen = screen;
        if (!_dragging && !_drawing && Tool == BoardTool.Cursor)
            UpdateResizeCursor(screen);
        if (Tool is BoardTool.Pen or BoardTool.Pen2 or BoardTool.Marker)
            InvalidateVisual();
        var world = ToWorld(screen);

        if (Tool == BoardTool.Eraser)
        {
            _eraserScreen = screen;
            if (!_drawing)
                _eraserVisualRadius = EraserSize / 2;
            InvalidateVisual();
        }

        if (_panning)
        {
            // WPF может временно вернуть курсор окна к Arrow при обработке
            // MouseMove. Явно удерживаем ScrollAll на каждом событии движения,
            // пока идёт панорамирование.
            Cursor = Cursors.ScrollAll;

            Offset = new Point(
                _panStartOffset.X - (screen.X - _panStartScreen.X) / Zoom,
                _panStartOffset.Y - (screen.Y - _panStartScreen.Y) / Zoom);
            ViewChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (_dragging)
        {
            ContinueTransform(screen, world);
            return;
        }

        if (_marquee)
        {
            _marqueeCurrentWorld = world;
            if (!_marqueeIsRect)
                _lassoPoints.Add(world);
            InvalidateVisual();
            return;
        }

        if (!_drawing)
            return;

        switch (Tool)
        {
            case BoardTool.Eraser:
                ContinueErase(world, screen);
                break;

            case BoardTool.Pen:
            case BoardTool.Pen2:
            case BoardTool.Marker:
                ContinueStroke(screen, world);
                break;

            case BoardTool.Shape:
                ContinueShape(world);
                break;
        }
    }


    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _toolCursorScreen = null;
        _eraserScreen = null;
        if (Tool == BoardTool.Cursor) Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_panning && (e.ChangedButton == MouseButton.Middle ||
                          e.ChangedButton == MouseButton.Right ||
                          e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            ReleaseMouseCapture();
            UpdateCursor();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        ReleaseMouseCapture();

        if (_dragging)
        {
            _dragging = false;
            _activeHandle = HandleKind.None;
            CommitChange();
            return;
        }

        if (_marquee)
        {
            FinishMarquee();
            return;
        }

        if (!_drawing)
            return;

        _drawing = false;

        switch (Tool)
        {
            case BoardTool.Eraser:
                if (_eraseChanged)
                    CommitChange();
                else
                    CancelChange();
                _lastEraseWorld = null;
                break;

            case BoardTool.Pen:
            case BoardTool.Pen2:
            case BoardTool.Marker:
                FinishStroke();
                break;

            case BoardTool.Shape:
                FinishShape();
                break;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // Плавное масштабирование: коэффициент пропорционален величине прокрутки,
        // без привязки к фиксированным значениям — картинка не «перескакивает».
        // 1.0012 даёт около 1.15× за один щелчок колеса.
        var factor = Math.Pow(1.0012, e.Delta);
        ZoomAt(e.GetPosition(this), factor);
        e.Handled = true;
    }

    private void StartPan(Point screen)
    {
        _panning = true;
        _panStartScreen = screen;
        _panStartOffset = Offset;
        CaptureMouse();
        UpdateCursor();
    }

    // =====================================================================
    //  Инструмент «Курсор»
    // =====================================================================
    private void StartCursorAction(Point screen, Point world)
    {
        var handle = HitHandle(screen);
        if (handle != HandleKind.None)
        {
            BeginTransform(handle, world);
            return;
        }

        var hit = HitItem(world);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (hit is not null)
        {
            if (ctrl)
            {
                if (Selection.Contains(hit))
                    Selection.Remove(hit);
                else
                    Selection.Add(hit);
                SelectionChanged?.Invoke();
                InvalidateVisual();
                return;
            }

            if (!Selection.Contains(hit))
            {
                Selection.Clear();
                Selection.Add(hit);
                SelectionChanged?.Invoke();
            }

            BeginTransform(HandleKind.None, world);
            return;
        }

        // Клик по пустому месту — рамка выделения.
        if (!ctrl && Selection.Count > 0)
        {
            Selection.Clear();
            SelectionChanged?.Invoke();
        }

        _marquee = true;
        _marqueeIsRect = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _marqueeStartWorld = world;
        _marqueeCurrentWorld = world;
        _lassoPoints.Clear();
        _lassoPoints.Add(world);
        InvalidateVisual();
    }

    private void FinishMarquee()
    {
        _marquee = false;

        if (_marqueeIsRect)
        {
            var rect = new Rect(_marqueeStartWorld, _marqueeCurrentWorld);
            if (rect.Width > 2 || rect.Height > 2)
            {
                foreach (var item in Items)
                {
                    if (rect.Contains(ItemRenderer.RotatedBounds(item)) ||
                        rect.IntersectsWith(ItemRenderer.RotatedBounds(item)))
                    {
                        if (!Selection.Contains(item))
                            Selection.Add(item);
                    }
                }
            }
        }
        else if (_lassoPoints.Count > 2)
        {
            foreach (var item in Items)
            {
                var center = ItemRenderer.RotatedBounds(item);
                var probe = new Point(center.X + center.Width / 2, center.Y + center.Height / 2);
                if (PointInPolygon(_lassoPoints, probe))
                {
                    if (!Selection.Contains(item))
                        Selection.Add(item);
                }
            }
        }

        _lassoPoints.Clear();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    private static bool PointInPolygon(List<Point> polygon, Point p)
    {
        var inside = false;
        var j = polygon.Count - 1;

        for (var i = 0; i < polygon.Count; i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            if (pi.Y > p.Y != pj.Y > p.Y &&
                p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-9) + pi.X)
                inside = !inside;

            j = i;
        }
        return inside;
    }

    // =====================================================================
    //  Перемещение, изменение размера, поворот
    // =====================================================================
    private void BeginTransform(HandleKind handle, Point world)
    {
        if (Selection.Count == 0)
            return;

        if (handle is not (HandleKind.None or HandleKind.LineStart or HandleKind.LineEnd or HandleKind.Rotate) &&
            !Selection.All(IsStandardTransformable))
            return;

        if (handle == HandleKind.Rotate && !Selection.All(IsStandardTransformable))
            return;

        BeginChange();
        _dragging = true;
        _activeHandle = handle;
        _dragStartWorld = world;
        _dragOriginals = Selection.Select(i => i.Clone()).ToList();
        _dragOriginalBounds = SelectionWorldBounds();
    }

    private void ContinueTransform(Point screen, Point world)
    {
        if (Selection.Count == 0 || _dragOriginals.Count == 0)
            return;

        if (_activeHandle is HandleKind.LineStart or HandleKind.LineEnd)
        {
            if (Selection.Count == 1 && TryGetLineEndpoints(_dragOriginals[0], out var start, out var end))
            {
                var fixedPoint = _activeHandle == HandleKind.LineStart ? end : start;
                var movingPoint = world;
                var target = Selection[0];

                // Не меняем вторую точку: она является центром вращения.
                target.Points.Clear();
                if (_activeHandle == HandleKind.LineStart)
                {
                    target.Points.Add(movingPoint.X);
                    target.Points.Add(movingPoint.Y);
                    target.Points.Add(fixedPoint.X);
                    target.Points.Add(fixedPoint.Y);
                }
                else
                {
                    target.Points.Add(fixedPoint.X);
                    target.Points.Add(fixedPoint.Y);
                    target.Points.Add(movingPoint.X);
                    target.Points.Add(movingPoint.Y);
                }

                if (target.Kind == ItemKind.Stroke)
                {
                    target.SetPoints(new[] { movingPoint, fixedPoint });
                    if (_activeHandle == HandleKind.LineEnd)
                        target.SetPoints(new[] { fixedPoint, movingPoint });
                }
                else
                {
                    target.X = Math.Min(movingPoint.X, fixedPoint.X);
                    target.Y = Math.Min(movingPoint.Y, fixedPoint.Y);
                    target.W = Math.Max(0.01, Math.Abs(movingPoint.X - fixedPoint.X));
                    target.H = Math.Max(0.01, Math.Abs(movingPoint.Y - fixedPoint.Y));
                    target.Rotation = 0;
                }
            }
        }
        else if (_activeHandle == HandleKind.None)
        {
            var dx = world.X - _dragStartWorld.X;
            var dy = world.Y - _dragStartWorld.Y;

            for (var i = 0; i < Selection.Count; i++)
                MoveItem(Selection[i], _dragOriginals[i], dx, dy);
        }
        else if (_activeHandle == HandleKind.Rotate)
        {
            var center = new Point(
                _dragOriginalBounds.X + _dragOriginalBounds.Width / 2,
                _dragOriginalBounds.Y + _dragOriginalBounds.Height / 2);

            var startAngle = Math.Atan2(_dragStartWorld.Y - center.Y, _dragStartWorld.X - center.X);
            var nowAngle = Math.Atan2(world.Y - center.Y, world.X - center.X);
            var delta = (nowAngle - startAngle) * 180 / Math.PI;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                delta = Math.Round(delta / 15) * 15;

            for (var i = 0; i < Selection.Count; i++)
                RotateItem(Selection[i], _dragOriginals[i], center, delta);
        }
        else
        {
            var target = ResizeBounds(_dragOriginalBounds, _activeHandle, world,
                                       Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

            for (var i = 0; i < Selection.Count; i++)
                ScaleItem(Selection[i], _dragOriginals[i], _dragOriginalBounds, target);
        }

        // Перерисовываем каждый кадр — пользователь видит результат вживую.
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    private static void MoveItem(BoardItem target, BoardItem original, double dx, double dy)
    {
        target.X = original.X + dx;
        target.Y = original.Y + dy;

        if (original.StrokeSegments.Count > 0)
        {
            target.StrokeSegments = original.StrokeSegments.Select(segment =>
            {
                var shifted = new List<double>(segment.Count);
                for (var i = 0; i + 1 < segment.Count; i += 2)
                {
                    shifted.Add(segment[i] + dx);
                    shifted.Add(segment[i + 1] + dy);
                }
                return shifted;
            }).ToList();
            target.Points.Clear();
            target.RecalculateBoundsFromSegments();
        }
        else if (original.Points.Count >= 2)
        {
            target.Points.Clear();
            for (var i = 0; i + 1 < original.Points.Count; i += 2)
            {
                target.Points.Add(original.Points[i] + dx);
                target.Points.Add(original.Points[i + 1] + dy);
            }
        }
    }

    private static void RotateItem(BoardItem target, BoardItem original, Point pivot, double degrees)
    {
        target.Rotation = original.Rotation + degrees;

        var angle = degrees * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var center = original.Center;
        var dx = center.X - pivot.X;
        var dy = center.Y - pivot.Y;

        var newCenter = new Point(
            dx * cos - dy * sin + pivot.X,
            dx * sin + dy * cos + pivot.Y);

        target.X = newCenter.X - original.W / 2;
        target.Y = newCenter.Y - original.H / 2;

        if (original.Points.Count >= 2)
        {
            // Точки только сдвигаем вслед за центром: сам разворот выполняет
            // поле Rotation при отрисовке, иначе поворот применился бы дважды.
            target.Points.Clear();
            for (var i = 0; i + 1 < original.Points.Count; i += 2)
            {
                target.Points.Add(original.Points[i] + (target.X - original.X));
                target.Points.Add(original.Points[i + 1] + (target.Y - original.Y));
            }
        }
    }

    private static void ScaleItem(BoardItem target, BoardItem original, Rect from, Rect to)
    {
        var sx = from.Width < 1e-6 ? 1 : to.Width / from.Width;
        var sy = from.Height < 1e-6 ? 1 : to.Height / from.Height;

        target.X = to.X + (original.X - from.X) * sx;
        target.Y = to.Y + (original.Y - from.Y) * sy;
        target.W = Math.Max(1, original.W * sx);
        target.H = Math.Max(1, original.H * sy);

        if (original.Points.Count >= 2)
        {
            target.Points.Clear();
            for (var i = 0; i + 1 < original.Points.Count; i += 2)
            {
                target.Points.Add(to.X + (original.Points[i] - from.X) * sx);
                target.Points.Add(to.Y + (original.Points[i + 1] - from.Y) * sy);
            }

            // Толщину меняем только у рукописных штрихов: у фигур
            // толщина контура задаётся отдельно и не должна «плыть».
            if (original.Kind == ItemKind.Stroke)
                target.Thickness = Math.Max(0.5, original.Thickness * (sx + sy) / 2);
        }

        if (original.Kind == ItemKind.Text)
            target.FontSize = Math.Max(6, original.FontSize * sy);
    }

    private static Rect ResizeBounds(Rect original, HandleKind handle, Point world, bool keepRatio)
    {
        double left = original.Left, top = original.Top;
        double right = original.Right, bottom = original.Bottom;

        switch (handle)
        {
            case HandleKind.NW: left = world.X; top = world.Y; break;
            case HandleKind.N: top = world.Y; break;
            case HandleKind.NE: right = world.X; top = world.Y; break;
            case HandleKind.E: right = world.X; break;
            case HandleKind.SE: right = world.X; bottom = world.Y; break;
            case HandleKind.S: bottom = world.Y; break;
            case HandleKind.SW: left = world.X; bottom = world.Y; break;
            case HandleKind.W: left = world.X; break;
        }

        var width = Math.Max(2, Math.Abs(right - left));
        var height = Math.Max(2, Math.Abs(bottom - top));

        if (keepRatio && original.Width > 1e-6 && original.Height > 1e-6)
        {
            var ratio = original.Width / original.Height;
            if (width / height > ratio)
                width = height * ratio;
            else
                height = width / ratio;
        }

        var x = Math.Min(left, right);
        var y = Math.Min(top, bottom);

        // Якорем остаётся противоположный маркеру угол.
        if (handle is HandleKind.NW or HandleKind.W or HandleKind.SW)
            x = original.Right - width;
        if (handle is HandleKind.NW or HandleKind.N or HandleKind.NE)
            y = original.Bottom - height;

        return new Rect(x, y, width, height);
    }

    // =====================================================================
    //  Рисование штрихов
    // =====================================================================
    private void StartStroke(Point screen, Point world)
    {
        _drawing = true;
        _forceStraight = false;
        _shiftStraight = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _straightStroke = _shiftStraight;
        _drawStartWorld = world;
        _lastMoveTime = DateTime.Now;
        _lastMoveScreen = screen;
        _straightenTimer.Start();

        _draftPoints.Clear();
        _draftPoints.Add(world);

        var marker = Tool == BoardTool.Marker;
        _draft = new BoardItem
        {
            Kind = ItemKind.Stroke,
            Marker = marker,
            StrokeSource = marker ? "Marker" : Tool == BoardTool.Pen2 ? "Pen2" : "Pen",
            IsStraightStroke = false,
            StrokeColor = (marker ? MarkerColor : Tool == BoardTool.Pen2 ? Pen2Color : PenColor).ToString(),
            Thickness = marker ? MarkerThickness : Tool == BoardTool.Pen2 ? Pen2Thickness : PenThickness,
            Opacity = marker ? MarkerOpacity : Tool == BoardTool.Pen2 ? Pen2Opacity : PenOpacity,
            Z = NextZ(),
            StrokeSegments = new List<List<double>>()
        };
        _draft.SetPoints(_draftPoints);
        InvalidateVisual();
    }

    private void ContinueStroke(Point screen, Point world)
    {
        if (_draft is null)
            return;

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // Если Shift был зажат в начале, сам штрих остаётся прямым до конца
        // текущего жеста. После отпускания Shift ограничение по углу снимается,
        // но режим прямой не отключается.
        if (shift)
        {
            _straightStroke = true;
            _shiftStraight = true;
            _draft.IsStraightStroke = true;
        }

        if (ItemRenderer.Distance(screen, _lastMoveScreen) > 3)
        {
            _lastMoveScreen = screen;
            _lastMoveTime = DateTime.Now;
        }

        if (_straightStroke)
        {
            var end = world;

            if (shift)
                end = SnapLineAngle(_drawStartWorld, world, ShiftAngleStep);

            _draftPoints.Clear();
            _draftPoints.Add(_drawStartWorld);
            _draftPoints.Add(end);
        }
        else
        {
            var last = _draftPoints[^1];
            if (ItemRenderer.Distance(last, world) >= 1.2 / Zoom)
                _draftPoints.Add(world);
        }

        _draft.SetPoints(_draftPoints);
        InvalidateVisual();
    }

    private void CheckStraightenHold()
    {
        if (!_drawing || _draft is null || Tool is not (BoardTool.Pen or BoardTool.Pen2 or BoardTool.Marker))
            return;

        if (_straightStroke || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if ((DateTime.Now - _lastMoveTime).TotalMilliseconds < StraightenHoldMs)
            return;

        if (!LooksStraight())
            return;

        _forceStraight = true;
        _straightStroke = true;
        _draft.IsStraightStroke = true;
        _draftPoints.Clear();
        _draftPoints.Add(_drawStartWorld);
        _draftPoints.Add(_lastMoveScreen == default ? _drawStartWorld : ToWorld(_lastMoveScreen));
        _draft.SetPoints(_draftPoints);
        InvalidateVisual();
    }

    private static Point SnapLineAngle(Point start, Point end, double step)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9)
            return start;

        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / step) * step;
        return new Point(
            start.X + length * Math.Cos(snapped),
            start.Y + length * Math.Sin(snapped));
    }

    /// <summary>Достаточно ли штрих похож на прямую, чтобы его выпрямить.</summary>
    private bool LooksStraight()
    {
        if (_draftPoints.Count < 4)
            return false;

        var first = _draftPoints[0];
        var last = _draftPoints[^1];

        double length = 0;
        for (var i = 1; i < _draftPoints.Count; i++)
            length += ItemRenderer.Distance(_draftPoints[i - 1], _draftPoints[i]);

        if (length * Zoom < StraightenMinLengthPx)
            return false;

        double deviation = 0;
        for (var i = 1; i < _draftPoints.Count - 1; i++)
            deviation = Math.Max(deviation,
                ItemRenderer.DistanceToSegment(_draftPoints[i], first, last));

        return deviation <= length * StraightenMaxDeviation;
    }

    private void FinishStroke()
    {
        if (_draft is null)
            return;

        if (_draftPoints.Count >= 2 ||
            (_draftPoints.Count == 1 && Tool != BoardTool.Shape))
        {
            if (_draftPoints.Count == 1)
            {
                var p = _draftPoints[0];
                _draftPoints.Add(new Point(p.X + 0.6, p.Y + 0.6));
                _draft.SetPoints(_draftPoints);
            }

            var isFreeStroke = !_draft.IsStraightStroke;
            var endPoint = _draftPoints[^1];
            var now = DateTime.Now;
            var mergeCandidate = isFreeStroke &&
                                  _lastFreeStrokeItem is not null &&
                                  Items.Contains(_lastFreeStrokeItem) &&
                                  ReferenceEquals(Items[^1], _lastFreeStrokeItem) &&
                                  (now - _lastFreeStrokeFinishedAt).TotalMilliseconds <= StrokeMergeDelayMs &&
                                  ItemRenderer.Distance(_lastFreeStrokeEnd, _draftPoints[0]) <= StrokeMergeDistancePx / Zoom &&
                                  CanMergeStrokes(_lastFreeStrokeItem, _draft);

            BeginChange();

            if (mergeCandidate)
            {
                // Несколько близких касаний становятся одним BoardItem, но
                // остаются отдельными внутренними сегментами. Ctrl+Z поэтому
                // откатывает последнее касание, а не всё слово.
                _lastFreeStrokeItem!.AddStrokeSegment(_draftPoints);
            }
            else
            {
                Items.Add(_draft);
            }

            CommitChange();

            if (isFreeStroke)
            {
                _lastFreeStrokeItem = mergeCandidate ? _lastFreeStrokeItem : _draft;
                _lastFreeStrokeEnd = endPoint;
                _lastFreeStrokeFinishedAt = now;
            }
            else
            {
                _lastFreeStrokeItem = null;
                _lastFreeStrokeFinishedAt = DateTime.MinValue;
            }
        }

        _draft = null;
        _draftPoints.Clear();
        _forceStraight = false;
        _straightStroke = false;
        _shiftStraight = false;
        _straightenTimer.Stop();
        InvalidateVisual();
    }

    private static bool CanMergeStrokes(BoardItem a, BoardItem b) =>
        a.Kind == ItemKind.Stroke && b.Kind == ItemKind.Stroke &&
        !a.IsStraightStroke && !b.IsStraightStroke &&
        a.Marker == b.Marker &&
        string.Equals(a.StrokeSource, b.StrokeSource, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.StrokeColor, b.StrokeColor, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(a.Thickness - b.Thickness) < 0.001 &&
        Math.Abs(a.Opacity - b.Opacity) < 0.001 &&
        a.LineStyle == b.LineStyle;

    // =====================================================================
    //  Рисование фигур
    // =====================================================================
    private void StartShape(Point world)
    {
        _drawing = true;
        _drawStartWorld = world;

        _draft = new BoardItem
        {
            Kind = ItemKind.Shape,
            Shape = ShapeTool,
            X = world.X,
            Y = world.Y,
            W = 1,
            H = 1,
            StrokeColor = ShapeColor.ToString(),
            FillColor = "",
            Thickness = ShapeThickness,
            LineStyle = ShapeLineStyle,
            Opacity = 1.0,
            TextColor = TextColor.ToString(),
            Z = NextZ()
        };

        // У линии и стрелки хранятся собственные концы — только так
        // они могут смотреть в любую сторону, а не по диагонали габаритов.
        if (IsLineShape(ShapeTool))
            _draft.SetPoints(new[] { world, world });

        InvalidateVisual();
    }

    private static bool IsLineShape(ShapeKind kind) =>
        kind is ShapeKind.Line or ShapeKind.Arrow;

    private void ContinueShape(Point world)
    {
        if (_draft is null)
            return;

        if (IsLineShape(_draft.Shape))
        {
            var end = world;

            // Shift для линии — привязка к шагу 45°: удобно строить
            // горизонтали, вертикали и ровные диагонали.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var dx = world.X - _drawStartWorld.X;
                var dy = world.Y - _drawStartWorld.Y;
                var angle = Math.Round(Math.Atan2(dy, dx) / ShiftAngleStep) * ShiftAngleStep;
                var length = Math.Sqrt(dx * dx + dy * dy);
                end = new Point(
                    _drawStartWorld.X + length * Math.Cos(angle),
                    _drawStartWorld.Y + length * Math.Sin(angle));
            }

            _draft.SetPoints(new[] { _drawStartWorld, end });
            InvalidateVisual();
            return;
        }

        var rect = new Rect(_drawStartWorld, world);

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var side = Math.Max(rect.Width, rect.Height);
            var x = world.X >= _drawStartWorld.X ? _drawStartWorld.X : _drawStartWorld.X - side;
            var y = world.Y >= _drawStartWorld.Y ? _drawStartWorld.Y : _drawStartWorld.Y - side;
            rect = new Rect(x, y, side, side);
        }

        _draft.X = rect.X;
        _draft.Y = rect.Y;
        _draft.W = Math.Max(0.5, rect.Width);
        _draft.H = Math.Max(0.5, rect.Height);
        InvalidateVisual();
    }

    private void FinishShape()
    {
        if (_draft is null)
            return;

        var isLine = IsLineShape(_draft.Shape);
        var bigEnough = isLine
            ? _draft.W > 2 || _draft.H > 2
            : _draft.W > 2 && _draft.H > 2;

        if (bigEnough)
        {
            BeginChange();
            Items.Add(_draft);
            var created = _draft;
            CommitChange();

            Selection.Clear();
            Selection.Add(created);
            SelectionChanged?.Invoke();
        }

        _draft = null;
        InvalidateVisual();
    }

    public int NextZ() => Items.Count == 0 ? 0 : Items.Max(i => i.Z) + 1;

    // =====================================================================
    //  Текст
    // =====================================================================
    private void CreateTextAt(Point world)
    {
        var item = new BoardItem
        {
            Kind = ItemKind.Text,
            X = world.X,
            Y = world.Y,
            W = 220,
            H = 32,
            Text = "",
            FontSize = 20,
            StrokeColor = TextColor.ToString(),
            Z = NextZ()
        };

        BeginChange();
        Items.Add(item);
        CommitChange();

        Selection.Clear();
        Selection.Add(item);
        SelectionChanged?.Invoke();

        EditTextRequested?.Invoke(item);
    }

    public void ApplyTextEdit(BoardItem item, string text)
    {
        BeginChange();
        item.Text = text;

        if (item.Kind == ItemKind.Text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Items.Remove(item);
                Selection.Remove(item);
                SelectionChanged?.Invoke();
            }
            else
            {
                var size = ItemRenderer.MeasureText(text, item.FontSize, PixelsPerDip, 800);
                item.W = size.Width + 8;
                item.H = size.Height + 4;
            }
        }

        CommitChange();
    }

    // =====================================================================
    //  Ластик
    // =====================================================================
    private void ContinueErase(Point world, Point screen)
    {
        var now = DateTime.Now;
        var radius = EraserSize / 2 / Zoom;

        if (_lastEraseWorld is { } previous)
        {
            var seconds = Math.Max(0.001, (now - _lastEraseTime).TotalSeconds);
            var speed = ItemRenderer.Distance(screen, ToScreen(previous)) / seconds;

            // При быстром движении круг стирания заметно, но умеренно растёт.
            var growth = Math.Min(1.0, speed / EraserSpeedFull) * EraserMaxGrowth;
            radius *= 1 + growth;
            _eraserVisualRadius = radius * Zoom;

            // Промежуточные шаги — чтобы при рывке не оставалось пропусков.
            var distance = ItemRenderer.Distance(previous, world);
            var steps = Math.Clamp((int)(distance / Math.Max(0.001, radius * 0.6)), 1, 60);

            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                EraseAt(new Point(
                    previous.X + (world.X - previous.X) * t,
                    previous.Y + (world.Y - previous.Y) * t), radius);
            }
        }
        else
        {
            _eraserVisualRadius = radius * Zoom;
            EraseAt(world, radius);
        }

        _lastEraseWorld = world;
        _lastEraseTime = now;
        InvalidateVisual();
    }

    /// <summary>
    /// Стирает круг: штрихи разрезаются на части, прочие объекты удаляются
    /// целиком при касании.
    /// </summary>
    private void EraseAt(Point center, double radius)
    {
        var changed = false;

        foreach (var item in Items.ToList())
        {
            // Изображение ластиком вообще не затрагивается. Удаление картинки
            // выполняется только кнопкой удаления. Нарисованные поверх неё
            // штрихи остаются обычными Stroke и стираются независимо.
            if (item.Kind == ItemKind.Image)
                continue;

            if (item.Kind != ItemKind.Stroke)
            {
                if (item.Kind == ItemKind.Shape)
                {
                    if (!ItemRenderer.HitTest(item, center, radius))
                        continue;

                    if (item.Shape == ShapeKind.Line && item.Points.Count >= 4)
                    {
                        // Прямая остаётся тем же BoardItem. Следы ластика
                        // записываются в ErasePoints, без создания фрагментов.
                        var a = new Point(item.Points[0], item.Points[1]);
                        var b = new Point(item.Points[2], item.Points[3]);
                        var limit = radius + item.Thickness / 2;
                        if (ItemRenderer.DistanceToSegment(center, a, b) <= limit)
                        {
                            item.ErasePoints.Add(center.X);
                            item.ErasePoints.Add(center.Y);
                            item.ErasePoints.Add(radius);
                            changed = true;
                        }
                        continue;
                    }

                    // Остальные фигуры и стрелки стираются целиком.
                    changed = true;
                    Selection.Remove(item);
                    Items.Remove(item);
                    continue;
                }

                // Текст при попадании удаляется целиком.
                if (item.Kind == ItemKind.Text && ItemRenderer.HitTest(item, center, radius))
                {
                    changed = true;
                    Selection.Remove(item);
                    Items.Remove(item);
                }
                continue;
            }

            var segments = item.EnumerateStrokeSegments().ToList();
            if (segments.Count == 0)
                continue;

            var keptSegments = new List<List<Point>>();
            var itemChanged = false;

            foreach (var segment in segments)
            {
                var kept = ClipStrokeSegment(segment, center, radius + item.Thickness / 2);
                if (kept.Count != 1 || kept[0].Count != segment.Count ||
                    (kept.Count == 1 && !PointsEqual(kept[0], segment)))
                    itemChanged = true;

                keptSegments.AddRange(kept);
            }

            if (!itemChanged)
                continue;

            changed = true;
            if (keptSegments.Count == 0)
            {
                Selection.Remove(item);
                Items.Remove(item);
                continue;
            }

            // ВАЖНО: объект не делится на новые BoardItem. После стирания
            // внутри одного объекта просто становится больше сегментов.
            item.SetStrokeSegments(keptSegments);
        }

        if (!changed)
            return;

        _lastFreeStrokeItem = null;
        _lastFreeStrokeFinishedAt = DateTime.MinValue;
        _eraseChanged = true;
        SelectionChanged?.Invoke();
    }

    private static bool PointsEqual(IReadOnlyList<Point> a, IReadOnlyList<Point> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (ItemRenderer.Distance(a[i], b[i]) > 1e-9) return false;
        return true;
    }

    /// <summary>Удаляет часть одного сегмента, возвращая 0..N оставшихся сегментов.</summary>
    private static List<List<Point>> ClipStrokeSegment(IReadOnlyList<Point> points, Point center, double radius)
    {
        if (points.Count < 2)
            return new List<List<Point>>();

        var result = new List<List<Point>>();
        var current = new List<Point>();

        void Flush()
        {
            if (current.Count >= 2)
                result.Add(current);
            current = new List<Point>();
        }

        void AppendOutside(Point a, Point b)
        {
            if (current.Count == 0)
                current.Add(a);
            else if (ItemRenderer.Distance(current[^1], a) > 1e-7)
                current.Add(a);

            if (ItemRenderer.Distance(current[^1], b) > 1e-7)
                current.Add(b);
        }

        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;

            if (len2 < 1e-12)
            {
                if (ItemRenderer.Distance(a, center) > radius)
                    AppendOutside(a, a);
                else
                    Flush();
                continue;
            }

            var cuts = new List<double> { 0, 1 };
            var fx = a.X - center.X;
            var fy = a.Y - center.Y;
            var A = len2;
            var B = 2 * (fx * dx + fy * dy);
            var C = fx * fx + fy * fy - radius * radius;
            var discriminant = B * B - 4 * A * C;

            if (discriminant >= 0)
            {
                var root = Math.Sqrt(Math.Max(0, discriminant));
                var t1 = (-B - root) / (2 * A);
                var t2 = (-B + root) / (2 * A);
                if (t1 > 0 && t1 < 1) cuts.Add(t1);
                if (t2 > 0 && t2 < 1) cuts.Add(t2);
            }

            cuts.Sort();
            for (var c = 0; c + 1 < cuts.Count; c++)
            {
                var t0 = cuts[c];
                var t1 = cuts[c + 1];
                if (t1 - t0 < 1e-9)
                    continue;

                var tm = (t0 + t1) / 2;
                var mid = new Point(a.X + dx * tm, a.Y + dy * tm);
                var p0 = new Point(a.X + dx * t0, a.Y + dy * t0);
                var p1 = new Point(a.X + dx * t1, a.Y + dy * t1);

                if (ItemRenderer.Distance(mid, center) > radius)
                    AppendOutside(p0, p1);
                else
                    Flush();
            }
        }

        Flush();
        return result;
    }

    // =====================================================================
    //  Операции над выделением
    // =====================================================================
    public void SelectAll()
    {
        Selection.Clear();
        Selection.AddRange(Items);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0)
            return;
        Selection.Clear();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        if (Selection.Count == 0)
            return;

        _lastFreeStrokeItem = null;
        _lastFreeStrokeFinishedAt = DateTime.MinValue;
        BeginChange();
        foreach (var item in Selection)
        {
            Items.Remove(item);
            ItemRenderer.DropImageCache(item.Id);
        }
        Selection.Clear();
        CommitChange();
        SelectionChanged?.Invoke();
    }

    public List<BoardItem> CopySelection() =>
        Selection.Select(i => i.Clone()).ToList();

    public void PasteItems(List<BoardItem> items, bool offset = true)
    {
        if (items.Count == 0)
            return;

        BeginChange();
        Selection.Clear();

        var shift = offset ? 26 / Zoom : 0;
        foreach (var source in items)
        {
            var copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Z = NextZ();
            MoveItem(copy, source, shift, shift);
            Items.Add(copy);
            Selection.Add(copy);
        }

        CommitChange();
        SelectionChanged?.Invoke();
    }

    public void DuplicateSelection() => PasteItems(CopySelection());

    public void BringToFront()
    {
        if (Selection.Count == 0)
            return;

        BeginChange();
        var top = NextZ();
        foreach (var item in Selection)
            item.Z = top++;
        CommitChange();
    }

    public void SendToBack()
    {
        if (Selection.Count == 0)
            return;

        BeginChange();
        var bottom = Items.Count == 0 ? 0 : Items.Min(i => i.Z) - Selection.Count;
        foreach (var item in Selection)
            item.Z = bottom++;
        CommitChange();
    }

    public void ApplyToSelection(Action<BoardItem> action)
    {
        if (Selection.Count == 0)
            return;

        BeginChange();
        foreach (var item in Selection)
            action(item);
        CommitChange();
    }

    public void AddItem(BoardItem item, bool select = true)
    {
        BeginChange();
        item.Z = NextZ();
        Items.Add(item);
        CommitChange();

        if (select)
        {
            Selection.Clear();
            Selection.Add(item);
            SelectionChanged?.Invoke();
        }
    }

    public void ClearBoard()
    {
        if (Items.Count == 0)
            return;

        BeginChange();
        Items.Clear();
        Selection.Clear();
        CommitChange();
        SelectionChanged?.Invoke();
    }
}
