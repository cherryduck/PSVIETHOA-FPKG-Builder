using PsViethoa.FpkgBuilder.Core.Models;
using PsViethoa.FpkgBuilder.Core.Services;
using Xunit;

namespace PsViethoa.FpkgBuilder.Tests;

public class VersionAndFormatterTests
{
    [Theory]
    [InlineData("01.008.001", "01.008.001")]
    [InlineData("01.000.000", "01.000.000")]
    public void TryCanonicalize_KeepsCanonicalForm(string input, string expected)
    {
        Assert.True(VersionHelper.TryCanonicalize(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void TryCanonicalize_RejectsGarbage(string? input) => Assert.False(VersionHelper.TryCanonicalize(input, out _));

    [Fact]
    public void Size_UsesBinaryUnits()
    {
        Assert.Equal("1 KB", Formatters.Size(1024));
        Assert.Equal("1.5 MB", Formatters.Size(1024 * 1024 + 512 * 1024));
        Assert.Equal("512 B", Formatters.Size(512));
    }

    [Fact]
    public void Clock_ShowsHoursWhenNeeded()
    {
        Assert.Equal("05:07", Formatters.Clock(TimeSpan.FromSeconds(307)));
        Assert.Equal("1:00:05", Formatters.Clock(TimeSpan.FromSeconds(3605)));
    }

    [Fact]
    public void LogEntry_FromLibrary_StripsTimestampAndClassifies()
    {
        var warning = LogEntry.FromLibrary("[+00:00:04.560] WARNING: something odd");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("WARNING: something odd", warning.Message);

        var error = LogEntry.FromLibrary("[+00:00:04.560] [stage 2/5] ERROR: bad");
        Assert.Equal(LogLevel.Error, error.Level);

        var info = LogEntry.FromLibrary("[+00:00:00.011] Source: /tmp/x");
        Assert.Equal(LogLevel.Info, info.Level);
        Assert.Equal("Source: /tmp/x", info.Message);
    }

    [Fact]
    public void SdkVersions_ReadsMajorFromParamJsonValue()
    {
        Assert.True(SdkVersions.TryReadMajor("0x0450000000000000", out var major));
        Assert.Equal(4, major);
        Assert.False(SdkVersions.TryReadMajor("0x00", out _));
        Assert.Equal(11, SdkVersions.All.Count);
    }
}
