using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace PsViethoa.FpkgBuilder.App.Views;

/// <summary>Hộp thoại thông báo/xác nhận đơn giản, đồng bộ với theme của ứng dụng.</summary>
public partial class MessageDialog : Window
{
    public enum Kind
    {
        Info,
        Question,
        Error,
        Danger,
    }

    public MessageDialog()
    {
        InitializeComponent();
        Opened += (_, _) => Services.DebugLog.Write($"MessageDialog opened: {Title}");
        Closed += (_, _) => Services.DebugLog.Write($"MessageDialog closed: {Title}");
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, Kind kind, string confirmLabel, string? cancelLabel)
    {
        var dialog = new MessageDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;

        if (cancelLabel == null)
        {
            dialog.CancelButton.IsVisible = false;
        }
        else
        {
            dialog.CancelButton.Content = cancelLabel;
        }

        var (icon, badge, foreground) = kind switch
        {
            Kind.Error => ("IconAlertCircle", "DangerSoftBrush", "DangerBrush"),
            Kind.Danger => ("IconAlert", "DangerSoftBrush", "DangerBrush"),
            Kind.Question => ("IconHelp", "InfoSoftBrush", "InfoBrush"),
            _ => ("IconInfo", "AccentSoftBrush", "AccentBrush"),
        };

        if (Application.Current != null)
        {
            if (Application.Current.TryGetResource(icon, Application.Current.ActualThemeVariant, out var geometry) && geometry is Geometry g)
            {
                dialog.KindIcon.Data = g;
            }

            if (Application.Current.TryGetResource(badge, Application.Current.ActualThemeVariant, out var badgeBrush) && badgeBrush is IBrush b)
            {
                dialog.IconBadge.Background = b;
            }

            if (Application.Current.TryGetResource(foreground, Application.Current.ActualThemeVariant, out var fgBrush) && fgBrush is IBrush f)
            {
                dialog.KindIcon.Foreground = f;
            }
        }

        if (kind == Kind.Danger)
        {
            dialog.ConfirmButton.Classes.Add("danger");
        }
        else
        {
            dialog.ConfirmButton.Classes.Add("primary");
            dialog.ConfirmButton.MinHeight = 36;
            dialog.ConfirmButton.FontSize = 14;
        }

        var result = await dialog.ShowDialog<int?>(owner);
        Services.DebugLog.Write($"MessageDialog result: {result?.ToString() ?? "null"}");
        return result == ConfirmResult;
    }

    public const int ConfirmResult = 0;
    public const int AltResult = 1;
    public const int CancelResult = 2;

    /// <summary>Hộp thoại ba lựa chọn (ví dụ: ghi đè / giữ bản cũ / huỷ). Trả về mã ConfirmResult, AltResult hoặc CancelResult.</summary>
    public static async Task<int> ShowChoiceAsync(Window owner, string title, string message, string confirmLabel, string altLabel, string cancelLabel)
    {
        var dialog = new MessageDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;
        dialog.ConfirmButton.Classes.Add("primary");
        dialog.ConfirmButton.MinHeight = 36;
        dialog.ConfirmButton.FontSize = 14;
        dialog.AltButton.Content = altLabel;
        dialog.AltButton.IsVisible = true;
        dialog.CancelButton.Content = cancelLabel;

        if (Application.Current != null &&
            Application.Current.TryGetResource("IconHelp", Application.Current.ActualThemeVariant, out var geometry) && geometry is Geometry g)
        {
            dialog.KindIcon.Data = g;
        }

        var result = await dialog.ShowDialog<int?>(owner);
        return result ?? CancelResult;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        Services.DebugLog.Write("MessageDialog confirm click");
        Close(ConfirmResult);
    }

    private void Alt_Click(object? sender, RoutedEventArgs e)
    {
        Services.DebugLog.Write("MessageDialog alt click");
        Close(AltResult);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Services.DebugLog.Write("MessageDialog cancel click");
        Close(CancelResult);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            Services.DebugLog.Write("MessageDialog escape key");
            e.Handled = true;
            Close(CancelResult);
        }
        else if (e.Key == Avalonia.Input.Key.Enter)
        {
            Services.DebugLog.Write("MessageDialog enter key");
            e.Handled = true;
            Close(ConfirmResult);
        }
    }
}
