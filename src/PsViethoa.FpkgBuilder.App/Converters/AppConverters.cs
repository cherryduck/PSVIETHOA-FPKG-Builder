using Avalonia.Data.Converters;
using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.App.Converters;

/// <summary>Bộ chuyển đổi dùng trong XAML.</summary>
public static class AppConverters
{
    public static readonly IValueConverter Size = new FuncValueConverter<long, string>(Formatters.Size);

    public static readonly IValueConverter Percent = new FuncValueConverter<double, string>(value => $"{value:0}%");

    public static readonly IValueConverter Clock = new FuncValueConverter<TimeSpan, string>(Formatters.Clock);

    public static readonly IValueConverter NotEmpty = new FuncValueConverter<string?, bool>(value => !string.IsNullOrWhiteSpace(value));

    public static readonly IValueConverter Empty = new FuncValueConverter<string?, bool>(string.IsNullOrWhiteSpace);

    public static readonly IValueConverter FolderName = new FuncValueConverter<string?, string>(value =>
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(value);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    });
}
