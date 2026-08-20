using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchoolPiBoard.Models;

namespace SchoolPiBoard.Views;

public partial class HomeView : UserControl
{
    private const int PageSize = 10;

    private MainWindow _shell = null!;
    private bool _showingArchive;
    private bool _newestFirst = true;
    private int _currentPage;
    private int _pageCount = 1;
    private int _filteredCount;

    /// <summary>Варианты фильтра по дате изменения.</summary>
    private static readonly (string Caption, int Days)[] DateFilters =
    {
        ("За всё время", 0),
        ("За сегодня", 1),
        ("За неделю", 7),
        ("За месяц", 30),
        ("За полгода", 180)
    };

    public HomeView()
    {
        InitializeComponent();

        foreach (var (caption, _) in DateFilters)
            DateFilter.Items.Add(caption);
        DateFilter.SelectedIndex = 0;
    }

    public void Initialize(MainWindow shell) => _shell = shell;

    // =====================================================================
    //  Выборка, фильтрация и сортировка
    // =====================================================================
    private List<Board> BuildFilteredList()
    {
        IEnumerable<Board> query = _shell.Store.Boards
            .Where(b => b.Archived == _showingArchive);

        // Поиск по названию — без учёта регистра и по подстроке.
        var search = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(b =>
                b.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        var days = DateFilters[Math.Max(0, DateFilter.SelectedIndex)].Days;
        if (days > 0)
        {
            var cutoff = DateTime.Now.AddDays(-days);
            query = query.Where(b => b.Modified >= cutoff);
        }

        query = _newestFirst
            ? query.OrderByDescending(b => b.Modified)
            : query.OrderBy(b => b.Modified);

        return query.ToList();
    }

    public void RefreshList(bool resetPage = false)
    {
        if (_shell is null)
            return;

        if (resetPage)
            _currentPage = 0;

        var all = BuildFilteredList();
        _filteredCount = all.Count;
        _pageCount = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        _currentPage = Math.Clamp(_currentPage, 0, _pageCount - 1);

        BoardList.ItemsSource = all
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .ToList();

        UpdateEmptyHint(all.Count);
        UpdatePager();

        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateEmptyHint(int count)
    {
        if (count > 0)
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var searching = !string.IsNullOrWhiteSpace(SearchBox.Text) || DateFilter.SelectedIndex > 0;

        EmptyHint.Text = searching
            ? "Ничего не найдено.\nПопробуйте изменить запрос или фильтр по дате."
            : _showingArchive
                ? "В архиве пока пусто."
                : "Пока нет ни одной доски.\nНажмите «+ Новая доска».";

        EmptyHint.Visibility = Visibility.Visible;
    }

    // =====================================================================
    //  Пагинация
    // =====================================================================
    private void UpdatePager()
    {
        var from = _filteredCount == 0 ? 0 : _currentPage * PageSize + 1;
        var to = Math.Min(_filteredCount, (_currentPage + 1) * PageSize);

        PageInfo.Text = _filteredCount == 0
            ? "Досок нет"
            : $"Показано {from}–{to} из {_filteredCount}";

        // Пагинация нужна только когда страниц больше одной.
        PagerPanel.Visibility = _pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        FirstPageButton.IsEnabled = PrevPageButton.IsEnabled = _currentPage > 0;
        NextPageButton.IsEnabled = LastPageButton.IsEnabled = _currentPage < _pageCount - 1;

        BuildPageButtons();
    }

    /// <summary>Номера страниц: показываем окно вокруг текущей, с многоточиями.</summary>
    private void BuildPageButtons()
    {
        PageButtons.Children.Clear();
        if (_pageCount <= 1)
            return;

        var pages = new List<int>();
        const int window = 1;

        for (var i = 0; i < _pageCount; i++)
        {
            if (i == 0 || i == _pageCount - 1 || Math.Abs(i - _currentPage) <= window)
                pages.Add(i);
        }

        var previous = -1;
        foreach (var page in pages)
        {
            if (previous >= 0 && page - previous > 1)
            {
                PageButtons.Children.Add(new TextBlock
                {
                    Text = "…",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                });
            }

            var button = new Button
            {
                Content = (page + 1).ToString(),
                Style = (Style)FindResource("IconButton"),
                Width = 34,
                Height = 32,
                Tag = page,
                FontSize = 13
            };

            if (page == _currentPage)
            {
                button.Background = (Brush)FindResource("SurfaceActive");
                button.Foreground = (Brush)FindResource("Accent");
                button.FontWeight = FontWeights.SemiBold;
            }

            button.Click += (s, _) =>
            {
                if (s is Button { Tag: int target })
                {
                    _currentPage = target;
                    RefreshList();
                }
            };

            PageButtons.Children.Add(button);
            previous = page;
        }
    }

    private void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = 0;
        RefreshList();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = Math.Max(0, _currentPage - 1);
        RefreshList();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = Math.Min(_pageCount - 1, _currentPage + 1);
        RefreshList();
    }

    private void LastPage_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = _pageCount - 1;
        RefreshList();
    }

    // =====================================================================
    //  Поиск и сортировка
    // =====================================================================
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshList(resetPage: true);

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e) =>
        RefreshList(resetPage: true);

    private void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        _newestFirst = !_newestFirst;
        SortArrow.Text = _newestFirst ? "↓" : "↑";
        SortText.Text = _newestFirst ? "Сначала новые" : "Сначала старые";
        RefreshList(resetPage: true);
    }

    // =====================================================================
    //  Действия с досками
    // =====================================================================
    private void ArchiveToggle_Click(object sender, RoutedEventArgs e)
    {
        _showingArchive = !_showingArchive;
        TitleText.Text = _showingArchive ? "Архив досок" : "Мои доски";
        ArchiveToggleButton.Content = _showingArchive ? "← Активные" : "Архив";
        RefreshList(resetPage: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsDialog(_shell).ShowDialog();
        RefreshList();
    }

    private void NewBoard_Click(object sender, RoutedEventArgs e)
    {
        var suggested = $"Доска {_shell.Store.Boards.Count + 1}";
        var name = PromptDialog.Show(Window.GetWindow(this)!, "Новая доска",
                                      "Название доски:", suggested);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var board = _shell.Store.CreateBoard(name);

        if (_showingArchive)
            ArchiveToggle_Click(sender, e);
        else
            RefreshList(resetPage: true);

        _shell.OpenBoard(board);
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Board board })
            _shell.OpenBoard(board);
    }

    /// <summary>Все действия со строкой спрятаны в меню «три точки».</summary>
    private void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Board board)
            return;

        // Клик по кнопке не должен открывать доску.
        e.Handled = true;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        menu.Items.Add(MenuItem("Открыть", () => _shell.OpenBoard(board)));
        menu.Items.Add(MenuItem("Переименовать", () => Rename(board)));
        menu.Items.Add(MenuItem(_showingArchive ? "Вернуть из архива" : "В архив",
                                 () => ToggleArchive(board)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Удалить", () => Delete(board)));

        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void Rename(Board board)
    {
        var name = PromptDialog.Show(Window.GetWindow(this)!, "Переименовать доску",
                                      "Новое название:", board.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        board.Name = name.Trim();
        _shell.Store.Save();
        RefreshList();
    }

    private void ToggleArchive(Board board)
    {
        board.Archived = !_showingArchive;
        if (!board.Archived)
            board.Modified = DateTime.Now;

        _shell.Store.Save();
        RefreshList();
    }

    private void Delete(Board board)
    {
        var confirmed = ConfirmDialog.Show(Window.GetWindow(this)!,
            "Удалить доску",
            $"Доска «{board.Name}» будет удалена безвозвратно.\n" +
            "Восстановить её не получится.",
            "Удалить", danger: true);

        if (!confirmed)
            return;

        _shell.Store.DeleteBoard(board);
        RefreshList();
    }
}
