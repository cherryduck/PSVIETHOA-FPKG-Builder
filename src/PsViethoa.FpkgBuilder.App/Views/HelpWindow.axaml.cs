using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PsViethoa.FpkgBuilder.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        VersionText.Text = $"{AppInfo.Name} {AppInfo.Version} · {Core.Localization.Loc.F("App.Credits", Core.Services.BuildEngine.LibraryVersion)}";
    }

    /// <summary>Cuộn nội dung hướng dẫn xuống cuối (dùng khi chụp màn hình kiểm thử).</summary>
    public void ScrollToEnd()
    {
        HelpScroll.ScrollToEnd();
        HelpScroll.UpdateLayout();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void OpenAuthor_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(AppInfo.AuthorUrl));
        }
        catch (Exception)
        {
        }
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
