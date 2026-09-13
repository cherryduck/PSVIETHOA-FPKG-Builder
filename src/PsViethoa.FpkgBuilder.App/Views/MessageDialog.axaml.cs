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

        var result = await dialog.ShowDialog<bool?>(owner);
        Services.DebugLog.Write($"MessageDialog result: {result?.ToString() ?? "null"}");
        return result == true;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        Services.DebugLog.Write("MessageDialog confirm click");
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Services.DebugLog.Write("MessageDialog cancel click");
        Close(false);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            Services.DebugLog.Write("MessageDialog escape key");
            e.Handled = true;
            Close(false);
        }
        else if (e.Key == Avalonia.Input.Key.Enter)
        {
            Services.DebugLog.Write("MessageDialog enter key");
            e.Handled = true;
            Close(true);
        }
    }
}
