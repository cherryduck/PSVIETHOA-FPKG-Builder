using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using PsViethoa.FpkgBuilder.Core.Localization;

namespace PsViethoa.FpkgBuilder.App.Localization;

/// <summary>
/// Markup extension {l:T Key}: binding một chiều tới Loc.Current[Key], tự cập nhật khi đổi ngôn ngữ.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBindingExtension($"[{Key}]")
        {
            Mode = BindingMode.OneWay,
            Source = Loc.Current,
        }.ProvideValue(serviceProvider);
}
