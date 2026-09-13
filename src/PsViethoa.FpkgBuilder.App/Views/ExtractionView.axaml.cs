using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PsViethoa.FpkgBuilder.App.ViewModels;

namespace PsViethoa.FpkgBuilder.App.Views;

/// <summary>
/// Chế độ "Giải nén gói": nhận kéo–thả tệp .pkg (chặn handler của cửa sổ chính trong vùng này),
/// làm nổi hàng đang có tiêu điểm để xem trước, Enter trong ô lọc áp dụng bộ lọc ngay.
/// </summary>
public partial class ExtractionView : UserControl
{
    public ExtractionView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        FileList.AddHandler(GotFocusEvent, OnFileListGotFocus, RoutingStrategies.Bubble);
        FileList.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        FilterBox.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);
    }

    private ExtractionViewModel? ViewModel => DataContext as ExtractionViewModel;

    /// <summary>Esc huỷ lượt trích xuất đang chạy (KeyBinding Esc của cửa sổ chính chỉ huỷ việc tạo gói).</summary>
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { CanCancel: true } viewModel)
        {
            viewModel.CancelWorkCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFileListGotFocus(object? sender, GotFocusEventArgs e) => HighlightFrom(e.Source);

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e) => HighlightFrom(e.Source);

    private void HighlightFrom(object? source)
    {
        if (source is Visual visual && ViewModel != null)
        {
            var row = visual as ListBoxItem ?? visual.FindAncestorOfType<ListBoxItem>();
            if (row?.DataContext is PackageEntryItem item)
            {
                ViewModel.Highlight(item);
            }
        }
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel != null)
        {
            ViewModel.ApplyFilterNow();
            e.Handled = true;
        }
    }

    /// <summary>Tệp .pkg đầu tiên trong dữ liệu kéo–thả (null nếu không có).</summary>
    private static string? GetDroppedPackage(DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items == null)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (item is IStorageFile)
            {
                var path = item.TryGetLocalPath();
                if (path != null && File.Exists(path) && path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!IsVisible || ViewModel == null)
        {
            return;
        }

        e.DragEffects = !ViewModel.IsBusy && GetDroppedPackage(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!IsVisible || ViewModel == null)
        {
            return;
        }

        e.Handled = true;
        if (ViewModel.IsBusy)
        {
            return;
        }

        var package = GetDroppedPackage(e);
        if (package != null)
        {
            ViewModel.LoadPackage(package);
        }
    }
}
