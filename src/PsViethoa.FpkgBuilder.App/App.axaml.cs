using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PsViethoa.FpkgBuilder.App.Services;
using PsViethoa.FpkgBuilder.App.ViewModels;
using PsViethoa.FpkgBuilder.App.Views;

namespace PsViethoa.FpkgBuilder.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = SettingsService.Load();
        Core.Localization.Loc.Current.SetLanguage(settings.Language);
        RequestedThemeVariant = settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(settings, new DialogService(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(bool dark)
    {
        if (Current != null)
        {
            Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
