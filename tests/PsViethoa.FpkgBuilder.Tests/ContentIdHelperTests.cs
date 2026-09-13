using PsViethoa.FpkgBuilder.Core.Services;
using Xunit;

namespace PsViethoa.FpkgBuilder.Tests;

public class ContentIdHelperTests
{
    [Theory]
    [InlineData("UP9000-PPSA00001_00-PSVIETHOATEST001", true)]
    [InlineData("EP9000-PPSA21567_00-0000000000000000", true)]
    [InlineData("up9000-ppsa00001_00-psviethoatest001", false)]
    [InlineData("UP9000-PPSA00001_00-SHORT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ChecksFormat(string? id, bool expected) => Assert.Equal(expected, ContentIdHelper.IsValid(id));

    [Fact]
    public void TitleIdOf_ExtractsNineCharacters() =>
        Assert.Equal("PPSA00001", ContentIdHelper.TitleIdOf("UP9000-PPSA00001_00-PSVIETHOATEST001"));

    [Fact]
    public void Compose_PadsAndSanitizesLabel()
    {
        var id = ContentIdHelper.Compose("UP9000", "PPSA00001", "Play Together!");
        Assert.Equal(36, id.Length);
        Assert.True(ContentIdHelper.IsValid(id));
        Assert.StartsWith("UP9000-PPSA00001_00-PLAYTOGETHER", id);
    }

    [Fact]
    public void Suggest_FallsBackToDefaults()
    {
        var id = ContentIdHelper.Suggest(null, null);
        Assert.True(ContentIdHelper.IsValid(id));
        Assert.Contains("PPSA00000", id);
    }

    [Fact]
    public void Normalize_TrimsAndUppercases() =>
        Assert.Equal("UP9000-PPSA00001_00-PSVIETHOATEST001", ContentIdHelper.Normalize("  up9000-ppsa00001_00-psviethoatest001 "));
}
