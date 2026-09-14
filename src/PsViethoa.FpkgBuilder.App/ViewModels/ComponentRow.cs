using Avalonia.Media;
using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.App.ViewModels;

/// <summary>Một dòng trong ô "Thành phần & plugin": tên, mô tả tình trạng và chấm màu theo trạng thái.</summary>
public sealed class ComponentRow
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush NotApplicableBrush = new SolidColorBrush(Color.Parse("#6B7280"));

    public ComponentRow(ComponentStatus status)
    {
        Status = status;
    }

    public ComponentStatus Status { get; }

    public string Name => Status.Name;

    public string Detail => Status.Detail;

    public bool CanFix => Status.CanFix;

    public IBrush Brush => Status.State switch
    {
        ComponentState.Ok => OkBrush,
        ComponentState.Warning => WarningBrush,
        ComponentState.Missing => MissingBrush,
        _ => NotApplicableBrush,
    };
}
