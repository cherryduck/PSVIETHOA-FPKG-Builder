using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PsViethoa.FpkgBuilder.App.Views;

namespace PsViethoa.FpkgBuilder.App.Services;

/// <summary>Hộp thoại chọn tệp/thư mục, thông báo, clipboard và mở thư mục — đa nền tảng qua Avalonia.</summary>
public sealed class DialogService
{
    private readonly Window _owner;

    public DialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory)
    {
        var provider = _owner.StorageProvider;
        if (!provider.CanPickFolder)
        {
            return null;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(provider, initialDirectory),
        };

        var result = await provider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string? initialDirectory, params FilePickerFileType[] fileTypes)
    {
        var provider = _owner.StorageProvider;
        if (!provider.CanOpen)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes.Length > 0 ? fileTypes : null,
            SuggestedStartLocation = await TryGetFolderAsync(provider, initialDirectory),
        };

        var result = await provider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExtension, params FilePickerFileType[] fileTypes)
    {
        var provider = _owner.StorageProvider;
        if (!provider.CanSave)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
            ShowOverwritePrompt = true,
            FileTypeChoices = fileTypes.Length > 0 ? fileTypes : null,
        };

        var result = await provider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    public async Task SetClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(_owner)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<bool> OpenFolderAsync(string path)
    {
        try
        {
            if (!System.IO.Directory.Exists(path))
            {
                return false;
            }

            var launcher = TopLevel.GetTopLevel(_owner)?.Launcher;
            if (launcher != null)
            {
                return await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    public async Task<bool> OpenUriAsync(string uri)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(_owner)?.Launcher;
            if (launcher != null)
            {
                return await launcher.LaunchUriAsync(new Uri(uri));
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    public Task ShowErrorAsync(string title, string message) =>
        MessageDialog.ShowAsync(_owner, title, message, MessageDialog.Kind.Error, "Đóng", null);

    public Task ShowInfoAsync(string title, string message) =>
        MessageDialog.ShowAsync(_owner, title, message, MessageDialog.Kind.Info, "Đóng", null);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel = "Huỷ", bool destructive = false) =>
        MessageDialog.ShowAsync(_owner, title, message, destructive ? MessageDialog.Kind.Danger : MessageDialog.Kind.Question, confirmLabel, cancelLabel);

    private static async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider provider, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
        {
            return null;
        }

        try
        {
            return await provider.TryGetFolderFromPathAsync(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
