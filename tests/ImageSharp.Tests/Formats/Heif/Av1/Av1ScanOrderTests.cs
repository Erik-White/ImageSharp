// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1ScanOrderTests
{
    [Theory]
    [MemberData(nameof(GetCombinations))]
    internal void AllIndicesScannedExactlyOnce(int s, int t)
    {
        // Assign
        HashSet<short> visitedScans = [];
        Av1TransformSize transformSize = (Av1TransformSize)s;
        Av1TransformType transformType = (Av1TransformType)t;

        // Act
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(transformSize, transformType);

        // Assert
        foreach (short scan in scanOrder.Scan)
        {
            Assert.False(visitedScans.Contains(scan), $"Scan {scan} already visited before.");
            visitedScans.Add(scan);
        }
    }

    [Theory]
    [MemberData(nameof(GetCombinations))]
    internal void AllIndicesScannedAreWithinRange(int s, int t)
    {
        // Assign
        Av1TransformSize transformSize = (Av1TransformSize)s;
        Av1TransformType transformType = (Av1TransformType)t;
        int lowValue = 0;

        // Act
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(transformSize, transformType);
        int highValue = scanOrder.Scan.Length - 1;

        // Assert
        foreach (short scan in scanOrder.Scan)
        {
            Assert.InRange(scan, lowValue, highValue);
        }
    }

    [Theory]
    [MemberData(nameof(GetCombinations))]
    internal void CorrectNumberOfIndicesScanned(int s, int t)
    {
        // Assign
        Av1TransformSize transformSize = (Av1TransformSize)s;
        Av1TransformType transformType = (Av1TransformType)t;
        int width = Math.Min(transformSize.GetWidth(), 32);
        int height = Math.Min(transformSize.GetHeight(), 32);

        // Act
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(transformSize, transformType);

        // Assert
        Assert.Equal(width * height, scanOrder.Scan.Length);
    }

    [Theory]
    [MemberData(nameof(GetCombinations))]
    internal void AllIndicesAreInDiagonalOrder(int s, int t)
    {
        // Assign
        Av1TransformSize transformSize = (Av1TransformSize)s;
        Av1TransformType transformType = (Av1TransformType)t;
        int width = Math.Min(transformSize.GetWidth(), 32);
        int height = Math.Min(transformSize.GetHeight(), 32);

        // Act
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(transformSize, transformType);

        // Assert
        HashSet<int> visited = [];
        ReadOnlySpan<short> scan = scanOrder.Scan;

        // In reverse order, the indiced used in
        // <see cref="Av1SymbolContextHelper.GetBaseRangeContext2d()" /> must already be known.
        for (int i = scanOrder.Scan.Length - 1; i >= 0; i--)
        {
            visited.Add(scan[i]);
            if (scan.Length > i + 1)
            {
                Assert.Contains(scan[i + 1], visited);
            }

            if (scan.Length > i + width)
            {
                Assert.Contains(scan[i + width], visited);
            }

            if (scan.Length > i + width + 1)
            {
                Assert.Contains(scan[i + width + 1], visited);
            }
        }
    }

    /// <summary>
    /// Pins the default 4x4 scan order to the spec <c>Default_Scan_4x4</c> (spec 9.2)
    /// </summary>
    [Fact]
    public void DefaultScan4x4_MatchesSpec()
    {
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(Av1TransformSize.Size4x4, Av1TransformType.DctDct);
        short[] expected = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        Assert.Equal(expected, scanOrder.Scan.ToArray());
    }

    [Fact]
    public void DefaultScan8x8_MatchesSpec()
    {
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(Av1TransformSize.Size8x8, Av1TransformType.DctDct);
        short[] expected = [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];
        Assert.Equal(expected, scanOrder.Scan.ToArray());
    }

    [Fact]
    public void DefaultScan4x8_MatchesSpec()
    {
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(Av1TransformSize.Size4x8, Av1TransformType.DctDct);
        short[] expected = [
            0,  1,  4,  2,  5,  8,  3,  6,  9,  12, 7,  10, 13, 16, 11, 14,
            17, 20, 15, 18, 21, 24, 19, 22, 25, 28, 23, 26, 29, 27, 30, 31];
        Assert.Equal(expected, scanOrder.Scan.ToArray());
    }

    [Fact]
    public void DefaultScan8x4_MatchesSpec()
    {
        Av1ScanOrder scanOrder = Av1ScanOrderConstants.GetScanOrder(Av1TransformSize.Size8x4, Av1TransformType.DctDct);
        short[] expected = [
            0,  8, 1,  16, 9,  2, 24, 17, 10, 3, 25, 18, 11, 4,  26, 19,
            12, 5, 27, 20, 13, 6, 28, 21, 14, 7, 29, 22, 15, 30, 23, 31];
        Assert.Equal(expected, scanOrder.Scan.ToArray());
    }

    public static TheoryData<int, int> GetCombinations()
    {
        TheoryData<int, int> combinations = [];
        for (int s = 0; s < (int)Av1TransformSize.AllSizes; s++)
        {
            for (int t = 0; t < (int)Av1TransformType.AllTransformTypes; t++)
            {
                combinations.Add(s, t);
            }
        }

        return combinations;
    }
}
