using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SchoolPiBoard.Views;

public partial class EditorView
{
    private readonly DispatcherTimer _timerTick = new();
    private TimeSpan _timerTotal = TimeSpan.FromMinutes(5);
    private TimeSpan _timerRemaining = TimeSpan.FromMinutes(5);
    private bool _timerRunning;

    private void InitializeTimer()
    {
        _timerTick.Interval = TimeSpan.FromMilliseconds(200);
        _timerTick.Tick += TimerTick;
        UpdateTimerDisplay();
    }

    private void TimerButton_Click(object sender, RoutedEventArgs e)
    {
        var show = TimerPanel.Visibility != Visibility.Visible;
        CloseTransientPanels();
        TimerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        if (!_timerRunning)
            return;

        _timerRemaining -= _timerTick.Interval;

        if (_timerRemaining <= TimeSpan.Zero)
        {
            _timerRemaining = TimeSpan.Zero;
            StopTimer();
            ShowTimerFinished();
        }

        UpdateTimerDisplay();
    }

    private void StopTimer()
    {
        _timerRunning = false;
        _timerTick.Stop();
        TimerPlayButton.Content = "▶";
    }

    /// <summary>
    /// Сообщение показывается независимо от того, открыта ли панель таймера:
    /// пользователь мог её закрыть и продолжить работу.
    /// </summary>
    private void ShowTimerFinished()
    {
        TimerFinished.Visibility = Visibility.Visible;

        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch
        {
            // Звук необязателен.
        }
    }

    private void TimerFinishedClose_Click(object sender, RoutedEventArgs e)
    {
        TimerFinished.Visibility = Visibility.Collapsed;
        _timerRemaining = _timerTotal;
        UpdateTimerDisplay();
    }

    private void TimerPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_timerRunning)
        {
            StopTimer();
            return;
        }

        if (_timerRemaining <= TimeSpan.Zero)
            _timerRemaining = _timerTotal;

        _timerRunning = true;
        _timerTick.Start();
        TimerPlayButton.Content = "❚❚";
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();
        _timerRemaining = _timerTotal;
        UpdateTimerDisplay();
    }

    private void TimerPlus_Click(object sender, RoutedEventArgs e) => ShiftTimer(1);
    private void TimerMinus_Click(object sender, RoutedEventArgs e) => ShiftTimer(-1);

    private void ShiftTimer(int minutes)
    {
        _timerTotal = TimeSpan.FromMinutes(
            Math.Clamp(_timerTotal.TotalMinutes + minutes, 1, 180));

        if (!_timerRunning)
            _timerRemaining = _timerTotal;

        UpdateTimerDisplay();
    }

    private void TimerMinutes_Click(object sender, MouseButtonEventArgs e) => EditTimerPart(true);
    private void TimerSeconds_Click(object sender, MouseButtonEventArgs e) => EditTimerPart(false);

    private void EditTimerPart(bool minutes)
    {
        var current = minutes
            ? ((int)_timerTotal.TotalMinutes).ToString()
            : _timerTotal.Seconds.ToString();

        var input = PromptDialog.Show(Window.GetWindow(this)!, "Таймер",
            minutes ? "Минуты (0–180):" : "Секунды (0–59):", current);

        if (!int.TryParse(input, out var value))
            return;

        var total = minutes
            ? TimeSpan.FromMinutes(Math.Clamp(value, 0, 180)) + TimeSpan.FromSeconds(_timerTotal.Seconds)
            : TimeSpan.FromMinutes((int)_timerTotal.TotalMinutes) + TimeSpan.FromSeconds(Math.Clamp(value, 0, 59));

        if (total <= TimeSpan.Zero)
            total = TimeSpan.FromMinutes(1);

        _timerTotal = total;
        StopTimer();
        _timerRemaining = _timerTotal;
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        if (TimerMinutes is null)
            return;

        var shown = _timerRunning || _timerRemaining < _timerTotal ? _timerRemaining : _timerTotal;

        TimerMinutes.Text = ((int)shown.TotalMinutes).ToString("00");
        TimerSeconds.Text = shown.Seconds.ToString("00");

        UpdateTimerArc(_timerTotal.TotalSeconds <= 0
            ? 0
            : shown.TotalSeconds / _timerTotal.TotalSeconds);
    }

    /// <summary>Рисует дугу оставшегося времени по кругу.</summary>
    private void UpdateTimerArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        const double radius = 85;
        var center = new Point(radius, radius);

        if (fraction >= 0.999)
        {
            TimerArc.Data = new EllipseGeometry(center, radius, radius);
            return;
        }

        if (fraction <= 0.001)
        {
            TimerArc.Data = Geometry.Empty;
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(
            center.X + radius * Math.Sin(angle),
            center.Y - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            angle > Math.PI,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        TimerArc.Data = geometry;
    }
}
