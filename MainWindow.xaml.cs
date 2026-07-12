using System.Windows;
using System.Windows.Controls;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Shibori;

public partial class MainWindow : Window
{
    private readonly DisplayConfigurationService displayService = new();
    private bool suppressCheckEvents;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        try
        {
            suppressCheckEvents = true;
            MonitorList.ItemsSource = displayService.GetMonitors();
            suppressCheckEvents = false;
            UpdateState("準備完了");
        }
        catch (Exception ex) { suppressCheckEvents = false; ShowError(ex); }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e)
    {
        if (suppressCheckEvents || sender is not WpfCheckBox { Tag: MonitorInfo monitor } checkBox) return;
        if (checkBox.IsChecked == true)
        {
            if (!displayService.HasBackup) return;
            try { displayService.Restore(); UpdateState("元の表示構成に戻しました"); Reload(); }
            catch (Exception ex) { checkBox.IsChecked = false; ShowError(ex); }
            return;
        }

        if (monitor.IsPrimary)
        {
            suppressCheckEvents = true;
            checkBox.IsChecked = true;
            suppressCheckEvents = false;
            MessageBox.Show(this, "メインモニターは切断できません。", "Shibori", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            displayService.Pause([monitor]);
            UpdateState($"{monitor.DeviceName} を切断しました");
        }
        catch (Exception ex)
        {
            suppressCheckEvents = true;
            checkBox.IsChecked = true;
            suppressCheckEvents = false;
            ShowError(ex);
        }
    }

    private void UpdateState(string message)
    {
        StatusText.Text = message;
        BackupText.Text = displayService.GetBackupTime() is { } time
            ? $"バックアップ: {time.ToLocalTime():yyyy/MM/dd HH:mm}"
            : "バックアップなし";
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Shibori", MessageBoxButton.OK, MessageBoxImage.Error);
}
