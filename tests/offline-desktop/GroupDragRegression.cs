using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;

internal static class GroupDragRegression
{
    private sealed class TestCanvas : BoardCanvas
    {
        public void ReleaseDrag() => OnMouseUp(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
    }

    public static void Run()
    {
        var canvas = new TestCanvas();
        var vertices = new[] { new Point(30, 90), new Point(63, 30), new Point(110, 80) };
        var board = new Board { BackgroundColor = "#FFFFFFFF", Grid = GridStyle.Solid };
        for (var i = 0; i < vertices.Length; i++)
        {
            var line = new BoardItem { Kind = ItemKind.Shape, Shape = ShapeKind.Line, Z = i,
                StrokeColor = "#FFFF0000", Thickness = 4 };
            line.SetPoints(new[] { vertices[i], vertices[(i + 1) % 3] });
            board.Items.Add(line);
        }
        canvas.LoadBoard(board);
        canvas.Measure(new Size(400, 240));
        canvas.Arrange(new Rect(0, 0, 400, 240));
        canvas.Selection.AddRange(canvas.Items.AsEnumerable().Reverse());
        canvas.GroupSelection();
        var bounds = canvas.SelectionWorldBounds();
        var begin = typeof(BoardCanvas).GetMethod("BeginTransform", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var move = typeof(BoardCanvas).GetMethod("ContinueTransform", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var start = vertices[0];
        begin.Invoke(canvas, new object[] { HandleKind.None, start });

        foreach (var delta in new[] { new Vector(135, 12), new Vector(175, 64) })
        {
            var point = start + delta;
            move.Invoke(canvas, new object[] { canvas.ToScreen(point), point });
            CheckFrame(delta);
        }
        canvas.ReleaseDrag();
        CheckFrame(new Vector(175, 64));

        void CheckFrame(Vector delta)
        {
            var expectedBounds = bounds;
            expectedBounds.Offset(delta);
            if (canvas.SelectionWorldBounds() != expectedBounds)
                throw new Exception("Рамка группы не совпадает с перенесённым треугольником.");
            canvas.UpdateLayout();
            var image = new RenderTargetBitmap(400, 240, 96, 96, PixelFormats.Pbgra32);
            image.Render(canvas);
            for (var i = 0; i < 3; i++)
            {
                var original = board.Items[i];
                var line = canvas.Items.Single(item => item.Id == original.Id);
                var a = vertices[i] + delta;
                var b = vertices[(i + 1) % 3] + delta;
                if (!line.Points.SequenceEqual(new[] { a.X, a.Y, b.X, b.Y }))
                    throw new Exception("При переносе линия получила координаты чужой стороны.");
                var mid = a + (b - a) * 0.5;
                if (!RedAt(mid) || RedAt(mid - delta))
                    throw new Exception("Сторона исчезла или оставила след на прежнем месте.");
            }

            bool RedAt(Point point)
            {
                var pixel = new byte[4];
                image.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
                return pixel[2] > 180 && pixel[1] < 90 && pixel[0] < 90;
            }
        }
    }
}
