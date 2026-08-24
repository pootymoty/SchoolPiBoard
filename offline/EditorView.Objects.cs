using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SchoolPiBoard.Models;
using SchoolPiBoard.Rendering;

namespace SchoolPiBoard.Views;

public partial class EditorView
{
    private BoardItem? _lineStyleTargetItem;
    // =====================================================================
    //  Панель выделенного объекта
    // =====================================================================
    private void OnSelectionChanged()
    {
        UpdateObjectPanelPosition();
        UpdateObjectPanelSwatches();
    }

    /// <summary>Панель следует за объектом и живёт над ним.</summary>
    private void UpdateObjectPanelPosition()
    {
        if (Canvas.Selection.Count == 0 || Canvas.Tool != BoardTool.Cursor)
        {
            ObjectPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = Canvas.SelectionScreenRect();
        if (rect.IsEmpty)
        {
            ObjectPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ObjectPanel.Visibility = Visibility.Visible;
        ObjectPanel.UpdateLayout();

        var panelWidth = ObjectPanel.ActualWidth > 0 ? ObjectPanel.ActualWidth : 300;
        var panelHeight = ObjectPanel.ActualHeight > 0 ? ObjectPanel.ActualHeight : 50;

        var left = rect.X + rect.Width / 2 - panelWidth / 2;
        var top = rect.Y - panelHeight - 46;

        // Запоминаем сторону панели: палитры и другие детали должны
        // открываться в ту же сторону, что и основная панель объекта.
        _objectPanelAbove = top >= 8;

        // Если сверху нет места, показываем панель под объектом.
        if (!_objectPanelAbove)
            top = rect.Bottom + 16;

        left = Math.Clamp(left, 8, Math.Max(8, Canvas.ActualWidth - panelWidth - 8));
        top = Math.Clamp(top, 8, Math.Max(8, Canvas.ActualHeight - panelHeight - 8));

        ObjectPanel.Margin = new Thickness(left, top, 0, 0);
    }

    private void UpdateObjectPanelSwatches()
    {
        var item = Canvas.Selection.FirstOrDefault();
        if (item is null)
            return;

        FillSwatch.Background = string.IsNullOrEmpty(item.FillColor)
            ? System.Windows.Media.Brushes.Transparent
            : ItemRenderer.ParseBrush(item.FillColor, System.Windows.Media.Brushes.Transparent);

        StrokeSwatch.BorderBrush = item.Thickness <= 0.01
            ? System.Windows.Media.Brushes.Transparent
            : ItemRenderer.ParseBrush(item.StrokeColor, System.Windows.Media.Brushes.Gray);

        var isShape = item.Kind == ItemKind.Shape;
        var isLineShape = isShape && (item.Shape == ShapeKind.Line || item.Shape == ShapeKind.Arrow);
        var isStraightStroke = item.Kind == ItemKind.Stroke && item.IsStraightStroke;
        var isImage = item.Kind == ItemKind.Image;

        // Заливка нужна только настоящим фигурам. Ни линии/стрелки, ни
        // рукописные штрихи пера/маркера не имеют параметра заливки.
        ObjectFillButton.Visibility = isShape && !isLineShape
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Тип линии доступен для фигур и для прямых, полученных пером/маркером.
        ObjectLineStyleButton.Visibility = isShape || isStraightStroke
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (isShape || isStraightStroke)
            SetLineStylePreview(ObjectLineStylePreview, item.LineStyle);

        // Текст внутри объекта не нужен для линий, стрелок, прямых штрихов
        // и изображений. Для остальных редактируемых объектов кнопка остаётся.
        ObjectTextButton.Visibility =
            isImage || item.Kind == ItemKind.Stroke || isLineShape
                ? Visibility.Collapsed
                : Visibility.Visible;

        // Изображение («Рисунок») не имеет отдельной границы, которой можно
        // управлять из панели объекта.
        ObjectStrokeButton.Visibility = isImage
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ObjectFill_Click(object sender, RoutedEventArgs e)
    {
        var item = Canvas.Selection.FirstOrDefault();
        if (item is null)
            return;

        var current = ItemRenderer.ParseColor(item.FillColor) ?? Colors.Gray;
        var palette = new ColorPalette(current, allowNone: true, noneCaption: "Прозрачный");

        var popup = ShowPalettePopup(palette, ObjectFillButton);

        palette.ColorPicked += color =>
        {
            Canvas.ApplyToSelection(i => i.FillColor = color.ToString());
            UpdateObjectPanelSwatches();
        };

        palette.NonePicked += () =>
        {
            // Нельзя одновременно убрать и заливку, и границу — объект стал бы невидимым.
            if (Canvas.Selection.Any(i => i.Thickness <= 0.01))
            {
                ConfirmDialog.Info(Window.GetWindow(this)!, "Нельзя применить",
                    "У объекта уже убрана граница. Если сделать фон прозрачным, " +
                    "объект станет невидимым.\n\nСначала верните границу.");
                return;
            }

            Canvas.ApplyToSelection(i => i.FillColor = "");
            UpdateObjectPanelSwatches();
            popup.IsOpen = false;
        };
    }

    private void ObjectLineStyleButton_Click(object sender, RoutedEventArgs e)
    {
        var item = Canvas.Selection.FirstOrDefault(i =>
            i.Kind == ItemKind.Shape || (i.Kind == ItemKind.Stroke && i.IsStraightStroke));
        if (item is null)
            return;

        _lineStyleTargetItem = item;
        SetLineStylePreview(ObjectLineStylePreview, item.LineStyle);
        ObjectLineStylePopup.PlacementTarget = ObjectLineStyleButton;
        ObjectLineStylePopup.Placement = _objectPanelAbove
            ? System.Windows.Controls.Primitives.PlacementMode.Top
            : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ObjectLineStylePopup.IsOpen = true;
    }

    private void ObjectStroke_Click(object sender, RoutedEventArgs e)
    {
        var item = Canvas.Selection.FirstOrDefault();
        if (item is null)
            return;

        var current = ItemRenderer.ParseColor(item.StrokeColor) ?? Colors.Gray;
        var palette = new ColorPalette(current, allowNone: true, noneCaption: "Без границы");

        var popup = ShowPalettePopup(palette, ObjectStrokeButton);

        palette.ColorPicked += color =>
        {
            Canvas.ApplyToSelection(i =>
            {
                i.StrokeColor = color.ToString();
                if (i.Thickness <= 0.01)
                    i.Thickness = 3;
            });
            UpdateObjectPanelSwatches();
        };

        palette.NonePicked += () =>
        {
            if (Canvas.Selection.Any(i => string.IsNullOrEmpty(i.FillColor)))
            {
                ConfirmDialog.Info(Window.GetWindow(this)!, "Нельзя применить",
                    "У объекта прозрачный фон. Если убрать ещё и границу, " +
                    "объект станет невидимым.\n\nСначала задайте цвет заливки.");
                return;
            }

            Canvas.ApplyToSelection(i => i.Thickness = 0);
            UpdateObjectPanelSwatches();
            popup.IsOpen = false;
        };
    }

    private System.Windows.Controls.Primitives.Popup ShowPalettePopup(
        ColorPalette palette, UIElement anchor)
    {
        CloseObjectPalettePopup();

        var border = new Border
        {
            Style = (Style)FindResource("FloatingPanel"),
            Padding = new Thickness(14),
            Child = palette
        };

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = border,
            PlacementTarget = anchor,
            Placement = _objectPanelAbove
                ? System.Windows.Controls.Primitives.PlacementMode.Top
                : System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        _activeObjectPalettePopup = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeObjectPalettePopup, popup))
                _activeObjectPalettePopup = null;
        };
        popup.IsOpen = true;

        return popup;
    }

    private void ObjectText_Click(object sender, RoutedEventArgs e)
    {
        var item = Canvas.Selection.FirstOrDefault();
        if (item is not null)
            BeginTextEdit(item);
    }

    private void ObjectCopy_Click(object sender, RoutedEventArgs e) => CopySelection();

    private void ObjectDelete_Click(object sender, RoutedEventArgs e) => Canvas.DeleteSelection();

    private void ObjectMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        menu.Items.Add(MakeMenuItem("На передний план", () => Canvas.BringToFront()));
        menu.Items.Add(MakeMenuItem("На задний план", () => Canvas.SendToBack()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Дублировать", () => Canvas.DuplicateSelection()));

        menu.IsOpen = true;
    }

    private static MenuItem MakeMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // =====================================================================
    //  Ввод текста поверх холста
    // =====================================================================
    private void BeginTextEdit(BoardItem item)
    {
        _editingItem = item;

        var rect = new Rect(
            Canvas.ToScreen(new Point(item.X, item.Y)),
            Canvas.ToScreen(new Point(item.X + item.W, item.Y + item.H)));

        TextEditor.Text = item.Text;
        TextEditor.FontSize = Math.Clamp(item.FontSize * Canvas.Zoom, 10, 60);
        TextEditor.Width = Math.Max(160, rect.Width);
        TextEditor.MinHeight = Math.Max(34, rect.Height);
        TextEditor.Margin = new Thickness(
            Math.Clamp(rect.X, 4, Math.Max(4, Canvas.ActualWidth - TextEditor.Width - 4)),
            Math.Clamp(rect.Y, 4, Math.Max(4, Canvas.ActualHeight - 60)),
            0, 0);

        TextEditor.Visibility = Visibility.Visible;
        TextEditor.Focus();
        TextEditor.SelectAll();
    }

    private void TextEditor_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter завершает ввод, Shift+Enter переносит строку.
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitTextEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTextEdit();
            e.Handled = true;
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e) => CommitTextEdit();

    private void CommitTextEdit()
    {
        if (_editingItem is null)
            return;

        var item = _editingItem;
        _editingItem = null;

        TextEditor.Visibility = Visibility.Collapsed;
        Canvas.ApplyTextEdit(item, TextEditor.Text);
        Canvas.Focus();
    }

    private void CancelTextEdit()
    {
        _editingItem = null;
        TextEditor.Visibility = Visibility.Collapsed;
        Canvas.Focus();
    }

    // =====================================================================
    //  Буфер обмена
    // =====================================================================
    private void CopySelection()
    {
        if (Canvas.Selection.Count == 0)
            return;

        _clipboard = Canvas.CopySelection();
    }

    private void CutSelection()
    {
        if (Canvas.Selection.Count == 0)
            return;

        _clipboard = Canvas.CopySelection();
        Canvas.DeleteSelection();
    }

    /// <summary>
    /// Ctrl+V: сначала картинка из системного буфера, затем текст,
    /// затем то, что было скопировано внутри доски.
    /// </summary>
    private void PasteClipboard()
    {
        if (TryPasteImage())
            return;

        if (TryPasteText())
            return;

        if (_clipboard.Count > 0)
        {
            Canvas.PasteItems(_clipboard);
            SelectTool(BoardTool.Cursor);
        }
    }

    private bool TryPasteImage()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null)
                return false;

            // Telegram и некоторые другие приложения кладут изображение в
            // несколько форматов. Сначала пробуем обычный WPF BitmapSource,
            // затем PNG/JPEG/BMP и DIB-представления.
            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                if (TryDecodeClipboardObject(data.GetData(DataFormats.Bitmap), out var bitmap))
                {
                    AddImage(bitmap);
                    return true;
                }
            }

            foreach (var format in new[] { "PNG", "image/png", "JFIF", "JPEG", "image/jpeg", "BMP", "image/bmp" })
            {
                if (!data.GetDataPresent(format))
                    continue;
                if (TryDecodeClipboardObject(data.GetData(format), out var bitmap))
                {
                    AddImage(bitmap);
                    return true;
                }
            }

            foreach (var format in new[] { "DIBV5", DataFormats.Dib })
            {
                if (!data.GetDataPresent(format))
                    continue;
                if (TryDecodeDib(data.GetData(format), out var bitmap))
                {
                    AddImage(bitmap);
                    return true;
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                foreach (var file in Clipboard.GetFileDropList())
                {
                    if (file is null || !IsImageFile(file))
                        continue;

                    AddImage(LoadBitmap(file));
                    return true;
                }
            }
        }
        catch
        {
            // Clipboard может быть занят приложением-источником или отдавать
            // нестандартное представление. В этом случае пробуем текст/внутреннюю вставку.
        }

        return false;
    }

    private static bool TryDecodeClipboardObject(object? value, out BitmapSource bitmap)
    {
        bitmap = null!;
        try
        {
            if (value is BitmapSource source)
            {
                if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || !HasVisiblePixels(source))
                    return false;

                bitmap = EnsureFrozen(source);
                return true;
            }

            if (value is byte[] bytes && bytes.Length > 0)
                return TryDecodeImageBytes(bytes, out bitmap);

            if (value is MemoryStream stream)
            {
                return TryDecodeImageBytes(stream.ToArray(), out bitmap);
            }

            if (value is Stream genericStream)
            {
                using var ms = new MemoryStream();
                genericStream.CopyTo(ms);
                return TryDecodeImageBytes(ms.ToArray(), out bitmap);
            }
        }
        catch { }

        return false;
    }

    private static bool TryDecodeImageBytes(byte[] bytes, out BitmapSource bitmap)
    {
        bitmap = null!;
        try
        {
            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            bitmap = image;
            return image.PixelWidth > 0 && image.PixelHeight > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasVisiblePixels(BitmapSource source)
    {
        try
        {
            // Telegram в некоторых сценариях отдаёт WPF BitmapSource с нулевой
            // альфой. Такой объект формально является изображением, но на доске
            // полностью прозрачен. Проверяем несколько строк пикселей, чтобы
            // такой формат не был принят раньше нормального DIB/PNG-представления.
            var format = source.Format;
            var hasAlpha = format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 ||
                           format == PixelFormats.Prgba64 || format == PixelFormats.Rgba128Float;
            if (!hasAlpha)
                return true;

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var sampleHeight = Math.Min(converted.PixelHeight, 256);
            var sample = new byte[stride * sampleHeight];
            converted.CopyPixels(new Int32Rect(0, 0, converted.PixelWidth, sampleHeight), sample, stride, 0);

            for (var i = 3; i < sample.Length; i += 4)
                if (sample[i] != 0)
                    return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static BitmapSource EnsureFrozen(BitmapSource source)
    {
        if (source.IsFrozen)
            return source;
        var copy = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        copy.Freeze();
        return copy;
    }

    private static bool TryDecodeDib(object? value, out BitmapSource bitmap)
    {
        // В WPF DIB обычно приходит как MemoryStream/byte[]. Для DIB без
        // BITMAPFILEHEADER добавляем стандартный 14-байтовый BMP-заголовок.
        bitmap = null!;
        try
        {
            byte[]? dib = value switch
            {
                byte[] b => b,
                MemoryStream ms => ms.ToArray(),
                Stream stream => ReadAll(stream),
                _ => null
            };
            if (dib is null || dib.Length < 4)
                return false;

            // DIBV5/ DIB: первые 4 байта — размер BITMAPINFOHEADER.
            var fileSize = 14 + dib.Length;
            var pixelOffset = 14 + 40;
            if (dib.Length >= 40)
            {
                var headerSize = BitConverter.ToInt32(dib, 0);
                if (headerSize >= 40 && headerSize <= dib.Length)
                    pixelOffset = 14 + headerSize;
            }

            using var msOut = new MemoryStream(fileSize);
            using (var bw = new BinaryWriter(msOut, System.Text.Encoding.Default, true))
            {
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write((short)0);
                bw.Write((short)0);
                bw.Write(pixelOffset);
                bw.Write(dib);
            }
            msOut.Position = 0;
            return TryDecodeImageBytes(msOut.ToArray(), out bitmap);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private bool TryPasteText()
    {
        try
        {
            if (!Clipboard.ContainsText())
                return false;

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var center = ViewportCenterWorld();
            var size = ItemRenderer.MeasureText(text.Trim(), 20, 1.0, 700);

            Canvas.AddItem(new BoardItem
            {
                Kind = ItemKind.Text,
                Text = text.Trim(),
                X = center.X - size.Width / 2,
                Y = center.Y - size.Height / 2,
                W = size.Width + 8,
                H = size.Height + 4,
                FontSize = 20,
                StrokeColor = Canvas.TextColor.ToString()
            });

            SelectTool(BoardTool.Cursor);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff";
    }

    private Point ViewportCenterWorld() =>
        Canvas.ToWorld(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));

    // =====================================================================
    //  Изображения
    // =====================================================================
    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|Все файлы|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            AddImage(LoadBitmap(dialog.FileName));
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(Window.GetWindow(this)!, "Не удалось открыть изображение", ex.Message);
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void AddImage(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var ms = new MemoryStream();
        encoder.Save(ms);

        var scale = Math.Min(1.0, 460.0 / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var width = bitmap.PixelWidth * scale / Canvas.Zoom;
        var height = bitmap.PixelHeight * scale / Canvas.Zoom;
        var center = ViewportCenterWorld();

        Canvas.AddItem(new BoardItem
        {
            Kind = ItemKind.Image,
            X = center.X - width / 2,
            Y = center.Y - height / 2,
            W = width,
            H = height,
            ImageBase64 = Convert.ToBase64String(ms.ToArray())
        });

        SelectTool(BoardTool.Cursor);
    }

    // =====================================================================
    //  Экспорт в PNG
    // =====================================================================
    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_board is null)
            return;

        var bounds = Canvas.ContentBounds();
        if (bounds.IsEmpty)
        {
            ConfirmDialog.Info(Window.GetWindow(this)!, "Доска пуста",
                "На доске пока нет содержимого, экспортировать нечего.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт доски",
            Filter = "PNG-изображение|*.png",
            FileName = SanitizeFileName(_board.Name) + ".png"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ExportToPng(bounds, dialog.FileName);
            ConfirmDialog.Info(Window.GetWindow(this)!, "Экспорт завершён", dialog.FileName);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(Window.GetWindow(this)!, "Не удалось сохранить файл", ex.Message);
        }
    }

    /// <summary>Экспортируется всё содержимое доски, а не видимая область экрана.</summary>
    private void ExportToPng(Rect bounds, string path)
    {
        const double padding = 48;
        bounds.Inflate(padding, padding);

        const int maxSide = 8000;
        var scale = Math.Min(1.0, maxSide / Math.Max(bounds.Width, bounds.Height));

        var pixelWidth = Math.Max(1, (int)(bounds.Width * scale));
        var pixelHeight = Math.Max(1, (int)(bounds.Height * scale));

        var background = ItemRenderer.ParseColor(_board!.BackgroundColor)
                          ?? Color.FromRgb(0x1B, 0x1B, 0x1F);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null,
                new Rect(0, 0, bounds.Width, bounds.Height));

            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

            GridPainter.Draw(dc, _board.Grid, background, bounds, 1.0);

            foreach (var item in Canvas.Items.OrderBy(i => i.Z))
                ItemRenderer.Draw(dc, item, 1.0);

            dc.Pop();
        }

        var target = new RenderTargetBitmap(pixelWidth, pixelHeight,
                                             96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    // =====================================================================
    //  Фон, справка
    // =====================================================================
    private void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var show = BackgroundPanelHost.Visibility != Visibility.Visible;
        CloseTransientPanels();
        BackgroundPanelHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var show = HelpPanel.Visibility != Visibility.Visible;
        CloseTransientPanels();
        HelpPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseHelp_Click(object sender, RoutedEventArgs e) =>
        HelpPanel.Visibility = Visibility.Collapsed;

    private void BuildHelpContent()
    {
        (string Action, string Keys)[] rows =
        {
            ("Перемещение по холсту", "Средняя кнопка мыши, либо Пробел + правая кнопка"),
            ("Масштаб", "Колесо мыши"),
            ("Ровная линия", "Удерживайте Shift при рисовании"),
            ("Автовыпрямление", "Задержите перо на месте, не отпуская кнопку"),
            ("Правильная фигура", "Shift при построении фигуры"),
            ("Выделение рамкой", "Shift + выделение (иначе — свободное лассо)"),
            ("Добавить к выделению", "Ctrl + клик по объекту"),
            ("Текст в фигуре", "Двойной клик по фигуре"),
            ("Отменить / повторить", "Ctrl+Z / Ctrl+Y"),
            ("Копировать / вырезать / вставить", "Ctrl+C / Ctrl+X / Ctrl+V"),
            ("Дублировать", "Ctrl+D"),
            ("Выделить всё", "Ctrl+A"),
            ("Удалить выделенное", "Delete"),
            ("Вернуться к курсору", "Esc")
        };

        foreach (var (action, keys) in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new TextBlock
            {
                Text = action,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(left, 0);
            row.Children.Add(left);

            var right = new TextBlock
            {
                Text = keys,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(right, 1);
            row.Children.Add(right);

            HelpContent.Children.Add(row);
        }
    }
}
