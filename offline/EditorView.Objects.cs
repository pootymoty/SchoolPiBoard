using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Whiteboard.Models;
using Whiteboard.Rendering;

namespace Whiteboard.Views;

public partial class EditorView
{
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
            if (Clipboard.ContainsImage())
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap is not null)
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
            // Формат буфера может быть нестандартным — просто идём дальше.
        }

        return false;
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
