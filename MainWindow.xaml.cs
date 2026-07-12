using System.Diagnostics;
using System.Reflection;
using System.Windows;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Shibori;

public partial class MainWindow : Window
{
    private readonly DisplayConfigurationService displayService = new();
    private bool suppressCheckEvents;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        InfoButton.ToolTip = $"Shibori v{version}";
        Loaded += async (_, _) =>
        {
            Reload();
            await CheckForUpdateAsync();
        };
    }

    private void Reload()
    {
        try
        {
            suppressCheckEvents = true;
            MonitorList.ItemsSource = displayService.GetMonitors();
            suppressCheckEvents = false;
        }
        catch (Exception ex) { suppressCheckEvents = false; ShowError(ex); }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (suppressCheckEvents || busy || sender is not WpfCheckBox { Tag: MonitorInfo monitor } checkBox) return;
        if (checkBox.IsChecked == true)
        {
            RunDisplayOperation(() => displayService.Restore(monitor));
            return;
        }

        if (monitor.IsPrimary)
        {
            suppressCheckEvents = true;
            checkBox.IsChecked = true;
            suppressCheckEvents = false;
            ShowError(new InvalidOperationException("メインモニターは切断できません。"), "Shibori");
            return;
        }

        RunDisplayOperation(() => displayService.Pause([monitor]));
    }

    private void RunDisplayOperation(Action operation)
    {
        busy = true;
        MonitorList.IsEnabled = false;
        try { operation(); Reload(); }
        catch (Exception ex) { Reload(); ShowError(ex); }
        finally { busy = false; MonitorList.IsEnabled = true; }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        CopyableDialog.Show(this, "Shiboriについて", $"Shibori\nバージョン {version}\n\nGitHub\nhttps://github.com/VarYUvrc/Shibori");
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var update = await UpdateChecker.FindAsync();
            if (update is null || !IsLoaded) return;
            if (CopyableDialog.Show(this, "アップデートがあります", $"現在のバージョン: {UpdateChecker.CurrentVersion}\n最新バージョン: {update.TagName}\n\nGitHub Releases:\n{update.Url}", "GitHubで開く"))
                Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
        }
        catch { /* 更新確認の失敗はアプリ本体の操作を妨げない */ }
    }

    private void ShowError(Exception ex, string title = "Shibori") => CopyableDialog.Show(this, title, ex.Message);
}

internal static class CopyableDialog
{
    public static bool Show(Window owner, string title, string message, string? primaryButton = null)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 560, Height = 300, MinWidth = 420, MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false };
        var text = new System.Windows.Controls.TextBox
        {
            Text = message,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            Focusable = true,
            IsTabStop = true,
            Cursor = System.Windows.Input.Cursors.IBeam,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(4)
        };
        var primary = new System.Windows.Controls.Button { Content = primaryButton ?? "閉じる", MinWidth = 100, IsDefault = true };
        primary.Click += (_, _) => { dialog.DialogResult = primaryButton is not null; dialog.Close(); };
        var close = new System.Windows.Controls.Button { Content = "閉じる", MinWidth = 80, IsCancel = true };
        close.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        if (primaryButton is not null) buttons.Children.Add(primary); buttons.Children.Add(close);
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(text);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
