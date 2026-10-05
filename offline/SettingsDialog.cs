using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

/// <summary>
/// Настройки приложения: папка, в которой хранятся доски, папка для
/// экспорта картинок, тема оформления и состояние лицензии.
/// </summary>
public class SettingsDialog : Window
{
    private readonly MainWindow _shell;
    private TextBlock _folderLabel = null!;
    private TextBlock _exportFolderLabel = null!;
    private readonly List<ToggleButton> _themeButtons = new();
    private TextBlock _licenseLabel = null!;

    public SettingsDialog(MainWindow shell)
    {
        _shell = shell;

        Title = "Настройки";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Owner = shell;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["AppBg"];
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        // Элементы окна собраны кодом и держат конкретные кисти — при смене темы
        // «на лету» сами не перекрасятся, поэтому окно пересобирается целиком.
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;

        Content = BuildContent();
    }

    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _themeButtons.Clear();
            Background = (Brush)Application.Current.Resources["AppBg"];
            Content = BuildContent();
            ThemeManager.ApplyTitleBar(this);
        });
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(24) };

        root.Children.Add(new TextBlock
        {
            Text = "Хранение данных",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = "Папка, в которой лежит файл со всеми досками. " +
                   "Данные никуда не отправляются и хранятся только на этом компьютере.",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });

        var folderBox = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBg2"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushColor"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };

        _folderLabel = new TextBlock
        {
            Text = _shell.Settings.DataFolder,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        folderBox.Child = _folderLabel;
        root.Children.Add(folderBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var change = new Button
        {
            Content = "Выбрать папку…",
            Style = (Style)Application.Current.Resources["AccentButton"]
        };
        change.Click += (_, _) => ChangeFolder();
        actions.Children.Add(change);

        var reset = new Button
        {
            Content = "По умолчанию",
            Style = (Style)Application.Current.Resources["TextButton"],
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        reset.Click += (_, _) => ApplyFolder(AppSettings.DefaultFolder);
        actions.Children.Add(reset);

        var open = new Button
        {
            Content = "Открыть в проводнике",
            Style = (Style)Application.Current.Resources["TextButton"],
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        open.Click += (_, _) => OpenInExplorer();
        actions.Children.Add(open);

        root.Children.Add(actions);

        // ============ Экспорт изображений ============

        root.Children.Add(new TextBlock
        {
            Text = "Экспорт изображений",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 26, 0, 0)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Картинки из «Экспорта в PNG» сохраняются сразу, без диалога, в подпапку «Доска Пи» внутри этой папки.",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });

        var exportFolderBox = new Border
        {
            Background = (Brush)Application.Current.Resources["AppBg2"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrushColor"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10)
        };

        _exportFolderLabel = new TextBlock
        {
            Text = _shell.Settings.ExportFolder,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        exportFolderBox.Child = _exportFolderLabel;
        root.Children.Add(exportFolderBox);

        var exportActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var changeExport = new Button
        {
            Content = "Выбрать папку…",
            Style = (Style)Application.Current.Resources["AccentButton"]
        };
        changeExport.Click += (_, _) => ChangeExportFolder();
        exportActions.Children.Add(changeExport);

        var resetExport = new Button
        {
            Content = "По умолчанию",
            Style = (Style)Application.Current.Resources["TextButton"],
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        resetExport.Click += (_, _) => ApplyExportFolder(AppSettings.DefaultExportFolder);
        exportActions.Children.Add(resetExport);

        var openExport = new Button
        {
            Content = "Открыть в проводнике",
            Style = (Style)Application.Current.Resources["TextButton"],
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        openExport.Click += (_, _) => OpenExportFolderInExplorer();
        exportActions.Children.Add(openExport);

        root.Children.Add(exportActions);

        // ============ Тема оформления ============

        root.Children.Add(new TextBlock
        {
            Text = "Тема оформления",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 26, 0, 0)
        });

        root.Children.Add(new TextBlock
        {
            Text = "«Как в системе» следует за настройкой Windows и переключается " +
                   "сама, если вы поменяете её на лету.",
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });

        root.Children.Add(BuildThemeChooser());

        // ============ Лицензия ============

        root.Children.Add(new TextBlock
        {
            Text = "Лицензия",
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 26, 0, 0)
        });

        _licenseLabel = new TextBlock
        {
            Text = DescribeLicense(),
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        root.Children.Add(_licenseLabel);

        var licenseButton = new Button
        {
            Content = "О программе и лицензии",
            Style = (Style)Application.Current.Resources["TextButton"],
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        licenseButton.Click += (_, _) =>
        {
            new LicenseInfoDialog(this).ShowDialog();
            _licenseLabel.Text = DescribeLicense();
        };
        root.Children.Add(licenseButton);

        var close = new Button
        {
            Content = "Закрыть",
            Style = (Style)Application.Current.Resources["TextButton"],
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 100,
            Margin = new Thickness(0, 20, 0, 0),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        return root;
    }

    /// <summary>Короткая строка о состоянии лицензии для раздела настроек.</summary>
    private static string DescribeLicense()
    {
        var state = LicenseManager.State;
        if (state is null)
            return "Лицензия не активирована.";

        if (state.Mode == LicenseMode.Trial)
        {
            var left = LicenseManager.TrialDaysLeft;
            return left > 0
                ? $"Пробный период · осталось {left} дн. · до {state.TrialExpiresAt.ToLocalTime():dd.MM.yyyy}"
                : "Пробный период закончился — нужен ключ.";
        }

        var devices = $"устройств занято: {Math.Max(state.DevicesUsed, 1)} из {state.DeviceLimit}";
        var email = string.IsNullOrWhiteSpace(state.Email) ? null : state.Email;

        return email is null
            ? $"Ключ {state.Key} · {devices}"
            : $"Ключ {state.Key} · {email} · {devices}";
    }

    private void ChangeFolder()
    {
        // OpenFolderDialog доступен начиная с .NET 8 — отдельная библиотека не нужна.
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для хранения досок",
            InitialDirectory = Directory.Exists(_shell.Settings.DataFolder)
                ? _shell.Settings.DataFolder
                : AppSettings.DefaultFolder
        };

        if (dialog.ShowDialog(this) == true)
            ApplyFolder(dialog.FolderName);
    }

    private void ApplyFolder(string folder)
    {
        if (string.Equals(folder, _shell.Settings.DataFolder, StringComparison.OrdinalIgnoreCase))
            return;

        var hasBoards = _shell.Store.Boards.Count > 0;
        var move = false;

        if (hasBoards)
        {
            var answer = ConfirmDialog.ShowThreeWay(this,
                "Смена папки хранения",
                $"Сейчас сохранено досок: {_shell.Store.Boards.Count}.\n\n" +
                "«Перенести» — файл с досками переедет в новую папку.\n" +
                "«Просто переключиться» — приложение начнёт работать с содержимым " +
                "новой папки, а прежние доски останутся на старом месте.",
                "Перенести", "Просто переключиться");

            if (answer is null)
                return;

            move = answer.Value;
        }

        try
        {
            _shell.Store.ChangeFolder(folder, move);
            _shell.Settings.DataFolder = folder;
            _shell.Settings.Save();
            _shell.ReloadStore();

            _folderLabel.Text = folder;
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Не удалось сменить папку", ex.Message);
        }
    }

    private void OpenInExplorer()
    {
        try
        {
            Directory.CreateDirectory(_shell.Settings.DataFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _shell.Settings.DataFolder,
                UseShellExecute = true
            });
        }
        catch
        {
            // Не критично.
        }
    }

    // =====================================================================
    //  Папка экспорта изображений
    // =====================================================================
    private void ChangeExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения картинок",
            InitialDirectory = Directory.Exists(_shell.Settings.ExportFolder)
                ? _shell.Settings.ExportFolder
                : AppSettings.DefaultExportFolder
        };

        if (dialog.ShowDialog(this) == true)
            ApplyExportFolder(dialog.FolderName);
    }

    private void ApplyExportFolder(string folder)
    {
        if (string.Equals(folder, _shell.Settings.ExportFolder, StringComparison.OrdinalIgnoreCase))
            return;

        // В отличие от папки досок, здесь нечего переносить — просто меняем,
        // куда в следующий раз откроется диалог сохранения.
        _shell.Settings.ExportFolder = folder;
        _shell.Settings.Save();
        _exportFolderLabel.Text = folder;
    }

    private void OpenExportFolderInExplorer()
    {
        try
        {
            Directory.CreateDirectory(_shell.Settings.ExportDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _shell.Settings.ExportDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Не критично.
        }
    }

    // =====================================================================
    //  Тема оформления
    // =====================================================================
    private UIElement BuildThemeChooser()
    {
        var row = new UniformGrid { Columns = 3 };

        foreach (var (value, title) in new[]
                 {
                     ("System", "Как в системе"),
                     ("Light", "Светлая"),
                     ("Dark", "Тёмная")
                 })
        {
            var button = new ToggleButton
            {
                Content = title,
                Style = (Style)Application.Current.Resources["ChoiceButton"],
                Height = 36,
                Margin = new Thickness(3),
                Tag = value,
                IsChecked = _shell.Settings.Theme == value
            };
            button.Checked += (_, _) =>
            {
                foreach (var other in _themeButtons)
                    if (!ReferenceEquals(other, button)) other.IsChecked = false;
                SetTheme(value);
            };
            _themeButtons.Add(button);
            row.Children.Add(button);
        }

        return row;
    }

    private void SetTheme(string preference)
    {
        if (_shell.Settings.Theme == preference)
            return;

        _shell.Settings.Theme = preference;
        _shell.Settings.Save();
        ThemeManager.SetPreference(preference);
    }
}
