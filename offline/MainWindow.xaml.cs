using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SchoolPiBoard.Models;
using SchoolPiBoard.Services;

namespace SchoolPiBoard.Views;

public partial class MainWindow : Window
{
    public AppSettings Settings { get; }
    public BoardStore Store { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        Settings = AppSettings.Load();
        Store = new BoardStore(Settings);
        Store.Load();
        Store.AutoArchive();
        Store.CleanupArchiveFolder();

        HomeScreen.Initialize(this);
        EditorScreen.Initialize(this);
        HomeScreen.RefreshList();

        SourceInitialized += (_, _) =>
        {
            ThemeManager.Track(this);
        };

        // Сжатие архивных досок — не на старте, а когда окно уже показано.
        Loaded += (_, _) => StartArchiveCompression();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    /// <summary>Пересоздаёт хранилище после смены папки данных.</summary>
    public void ReloadStore()
    {
        Store = new BoardStore(Settings);
        Store.Load();
        HomeScreen.RefreshList();
    }

    private bool _compressing;

    /// <summary>
    /// Сжимает архивные доски в фоне. Идёт только пока открыт список досок:
    /// при открытии редактора сжатие останавливается, чтобы не мешать рисованию.
    /// </summary>
    public void StartArchiveCompression()
    {
        if (_compressing || EditorActive)
            return;

        var store = Store;
        var pending = store.Boards
            .Where(b => b.Archived && !b.Compressed && b.Items.Count > 0)
            .ToList();
        if (pending.Count == 0)
            return;

        _compressing = true;
        Task.Run(() =>
        {
            foreach (var board in pending)
            {
                var stop = false;
                var written = board.Items;
                Dispatcher.Invoke(() => stop = EditorActive || !ReferenceEquals(store, Store));
                if (stop)
                    break;

                try
                {
                    BoardStore.WriteArchiveFile(store.ArchivePath(board), written);
                }
                catch
                {
                    continue; // Не вышло — доска остаётся как есть, попробуем в следующий раз.
                }

                Dispatcher.Invoke(() =>
                {
                    if (!EditorActive && ReferenceEquals(store, Store))
                        store.FinishCompression(board, written);
                });
            }
        }).ContinueWith(_ => _compressing = false, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void OpenBoard(Board board)
    {
        try
        {
            Store.EnsureLoaded(board);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Не удалось открыть доску",
                "Файл архива этой доски не читается: " + ex.Message);
            return;
        }

        EditorScreen.LoadBoard(board);
        HomeScreen.Visibility = Visibility.Collapsed;
        EditorScreen.Visibility = Visibility.Visible;
        EditorScreen.FocusCanvas();
    }

    public void ShowHome()
    {
        EditorScreen.SaveIfDirty();
        EditorScreen.Visibility = Visibility.Collapsed;
        HomeScreen.Visibility = Visibility.Visible;
        HomeScreen.RefreshList();
        StartArchiveCompression();
    }

    private bool EditorActive => EditorScreen.Visibility == Visibility.Visible;

    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.TextBox
            or System.Windows.Controls.ComboBox
            or System.Windows.Controls.Primitives.TextBoxBase;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!EditorActive || IsTextInputFocused())
            return;

        if (EditorScreen.HandleKeyDown(e))
            e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!EditorActive)
            return;

        if (e.Key == Key.Space)
        {
            EditorScreen.SetSpaceHeld(false);
            e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        EditorScreen.SaveIfDirty();
        base.OnClosing(e);
    }
}
