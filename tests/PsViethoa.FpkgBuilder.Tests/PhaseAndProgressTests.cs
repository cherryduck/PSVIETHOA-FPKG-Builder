using System.Diagnostics;
using PsViethoa.FpkgBuilder.Core.Services;
using Xunit;

namespace PsViethoa.FpkgBuilder.Tests;

public class PhaseAndProgressTests
{
    [Theory]
    [InlineData("[+00:00:00.029]  [inner]   read 11/208 (  5%): /data/small/file_003.txt (52 bytes)", "inner-read", 5.0)]
    [InlineData("[+00:00:00.500]  [inner]   data 40% (3/5): /eboot.bin -> 1,000 bytes (Kraken, ratio 50.0 %)", "inner-data", 40.0)]
    [InlineData("[+00:00:00.071]  [inner] Compressing and writing AFID-ordered inner data with 15 built-in Kraken worker(s)...", "inner-data", 0.0)]
    [InlineData("[+00:00:00.923] [stage 1/5] Inner image complete: 25,755,648 bytes in 00:00:00.896.", "inner-data", 100.0)]
    [InlineData("[+00:00:00.923] [stage 2/5] Generating NAPS file, block and integrity tables...", "naps", 0.0)]
    [InlineData("[+00:00:00.956] [stage 3/5] Writing and hashing outer-PFS data (15 worker(s)): 10% (2.5 MiB / 24.6 MiB).", "outer", 10.0)]
    [InlineData("[+00:00:00.942] [stage 3/5] Writing and hashing outer-PFS data (15 worker(s)): started (24.6 MiB total).", "outer", 0.0)]
    [InlineData("[+00:00:00.987] [stage 3/5] Outer PFS complete: 26,214,400 bytes, 400 digest blocks in 00:00:00.053.", "keys", 0.0)]
    [InlineData("[+00:00:04.516] [stage 4/5] Writing CNT bodies and outer image (13 entries)...", "cnt", 0.0)]
    [InlineData("[+00:00:04.543] [finalize] NAPS plaintext integrity tables (SHA3/ihsh/rhsh): 30% (119 / 394 blocks).", "finalize", 30.0)]
    [InlineData("[+00:00:04.562] Build finished in 00:00:04.562; output 26,696,230 bytes (25.46 MiB), warnings=0.", "finalize", 100.0)]
    public void TryParse_RecognisesLibraryProgressLines(string line, string expectedKey, double expectedPercent)
    {
        Assert.True(PhaseCatalog.TryParse(line, out var phase, out var percent));
        Assert.Equal(expectedKey, phase.Key);
        Assert.Equal(expectedPercent, percent);
    }

    [Theory]
    [InlineData("[+00:00:00.016]   input: eboot.bin (1,048,576 bytes; 1.00 MiB)")]
    [InlineData("PS5 public RSA profile loaded (passcode[7] + mount-image + token).")]
    [InlineData("")]
    public void TryParse_IgnoresNonProgressLines(string line) => Assert.False(PhaseCatalog.TryParse(line, out _, out _));

    [Fact]
    public void StripTimestamp_RemovesRelativePrefixOnly()
    {
        Assert.Equal("Done (FIH).", PhaseCatalog.StripTimestamp("[+00:00:04.560] Done (FIH)."));
        Assert.Equal("[stage 1/5] x", PhaseCatalog.StripTimestamp("[stage 1/5] x"));
    }

    [Fact]
    public void Tracker_OverallPercentIsMonotonicAndCompletes()
    {
        var stopwatch = Stopwatch.StartNew();
        var tracker = new ProgressTracker(stopwatch, PhaseCatalog.Sequence(computeSha256: false));
        var lines = new[]
        {
            "[+0] Source scan: 3 files",
            "[+0]  [inner]   read 1/3 ( 33%): /a",
            "[+0]  [inner]   read 3/3 (100%): /c",
            "[+0]  [inner] Compressing and writing AFID-ordered inner data with 2 built-in Kraken worker(s)...",
            "[+0]  [inner]   data 50% (1/2): /a -> 1 bytes (Kraken, ratio 1 %)",
            "[+0]  [inner]   data 100% (2/2): /b -> 1 bytes (stored, ratio 100 %)",
            "[+0] [stage 1/5] Inner image complete: 1 bytes in 00:00:00.001.",
            "[+0] [stage 2/5] Generating NAPS file, block and integrity tables...",
            "[+0] [stage 3/5] Writing and hashing outer-PFS data (2 worker(s)): started (1 MiB total).",
            "[+0] [stage 3/5] Writing and hashing outer-PFS data (2 worker(s)): 50% (0.5 MiB / 1 MiB).",
            "[+0] [stage 3/5] Outer PFS complete: 1 bytes, 1 digest blocks in 00:00:00.001.",
            "[+0] [stage 4/5] Writing CNT bodies and outer image (13 entries)...",
            "[+0] [finalize] NAPS plaintext integrity tables (SHA3/ihsh/rhsh): 50% (1 / 2 blocks).",
            "[+0] Build finished in 00:00:00.010; output 1 bytes (1 B), warnings=0.",
        };

        var previous = -1.0;
        foreach (var line in lines)
        {
            if (tracker.TryUpdate(line, out var snapshot))
            {
                Assert.True(snapshot.OverallPercent >= previous, $"{line} lùi từ {previous} về {snapshot.OverallPercent}");
                Assert.InRange(snapshot.OverallPercent, 0, 100);
                previous = snapshot.OverallPercent;
            }
        }

        Assert.True(previous > 90, $"tiến trình chỉ đạt {previous}%");
        var verify = tracker.EnterPhase(PhaseCatalog.Verify);
        Assert.Equal(PhaseCatalog.Verify.Name, verify.Phase);
        var done = tracker.Complete();
        Assert.True(done.IsComplete);
        Assert.Equal(100, done.OverallPercent);
        Assert.Equal(TimeSpan.Zero, done.Eta);
    }

    [Fact]
    public void Tracker_IgnoresLateLinesFromEarlierPhase()
    {
        var tracker = new ProgressTracker(Stopwatch.StartNew(), PhaseCatalog.Sequence(false));
        tracker.TryUpdate("[+0] [stage 4/5] Writing CNT bodies and outer image (13 entries)...", out var cnt);
        tracker.TryUpdate("[+0]  [inner]   data 10% (1/9): /late", out var late);
        Assert.Equal(cnt.OverallPercent, late.OverallPercent);
    }
}
