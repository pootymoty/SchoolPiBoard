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

        HomeScreen.Initialize(this);
        EditorScreen.Initialize(this);
        HomeScreen.RefreshList();

        SourceInitialized += (_, _) =>
        {
            ThemeManager.Track(this);
        };

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

    public void OpenBoard(Board board)
    {
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
