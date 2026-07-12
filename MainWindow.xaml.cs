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
        if (suppressCheckEvents || busy || sender is not WpfCheckBox { Tag: MonitorInfo monitor } checkBox) return;
        if (checkBox.IsChecked == true)
        {
            RunDisplayOperation(displayService.Restore, "元の表示構成に戻しました");
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

        RunDisplayOperation(() => displayService.Pause([monitor]), $"{monitor.DeviceName} を一時停止しました");
    }

    private void RunDisplayOperation(Action operation, string successMessage)
    {
        busy = true;
        MonitorList.IsEnabled = false;
        try
        {
            operation();
            UpdateState(successMessage);
            Reload();
        }
        catch (Exception ex)
        {
            Reload();
            ShowError(ex);
        }
        finally
        {
            busy = false;
            MonitorList.IsEnabled = true;
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
