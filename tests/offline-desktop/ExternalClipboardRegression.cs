using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;
using SchoolPiBoard.Views;

internal static class ExternalClipboardRegression
{
    private const string Sample = "Пример π\nВторая строка";

    public static void Read(string kind)
    {
        if (kind == "text")
        {
            if (Clipboard.GetText(TextDataFormat.UnicodeText) != Sample)
                throw new Exception("Внешний процесс получил другой текст.");
            return;
        }
        if (Clipboard.GetImage() is not { PixelWidth: 2, PixelHeight: 1 } ||
            Clipboard.GetData("PNG") is not MemoryStream png)
            throw new Exception("Нет изображения в буфере Windows.");
        png.Position = 0;
        var frame = BitmapFrame.Create(png);
        var pixels = new byte[8];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 8, 0);
        if (!pixels.SequenceEqual(new byte[] { 0, 0, 255, 255, 0, 255, 0, 128 }))
            throw new Exception("Изменились пиксели или прозрачность PNG.");
    }

    public static void Run(EditorView editor)
    {
        var canvas = (BoardCanvas)editor.FindName("Canvas");
        var copy = typeof(EditorView).GetMethod("TryCopySelection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var paste = typeof(EditorView).GetMethod("PasteClipboard", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32,
            null, new byte[] { 0, 0, 255, 255, 0, 255, 0, 128 }, 8)));
        using var stream = new MemoryStream();
        png.Save(stream);
        var items = new[] {
            new BoardItem { Kind = ItemKind.Text, Text = Sample },
            new BoardItem { Kind = ItemKind.Image, ImageBase64 = Convert.ToBase64String(stream.ToArray()) }
        };
        foreach (var item in items)
        {
            canvas.LoadBoard(new Board());
            canvas.Items.Add(item);
            canvas.Selection.Add(item);
            if (copy.Invoke(editor, null) is not true) throw new Exception("Копирование не выполнено.");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--read-clipboard");
            start.ArgumentList.Add(item.Kind == ItemKind.Text ? "text" : "image");
            using var reader = Process.Start(start)!;
            if (!reader.WaitForExit(20000) || reader.ExitCode != 0)
                throw new Exception("Внешний процесс не смог вставить объект.");

            canvas.LoadBoard(new Board());
            paste.Invoke(editor, new object?[] { null });
            var pasted = canvas.Items.Single();
            if (pasted.Kind != item.Kind || pasted.Id == item.Id || pasted.Text != item.Text ||
                pasted.ImageBase64 != item.ImageBase64 || pasted.W != item.W)
                throw new Exception("Не сохранился объект при переносе на другую доску.");
        }
        Clipboard.Clear();
    }
}
