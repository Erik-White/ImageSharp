// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Microsoft.Diagnostics.Symbols;
using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1SymbolContextTests
{
    [Theory]
    [MemberData(nameof(GetLowLevelContextEndOfBlockData))]
    public void TestLowLevelContextEndOfBlockAccuracy(int width, int height, int index)
    {
        // Arrange
        Size size = new(width, height);
        Av1LevelBuffer levels = new(Configuration.Default, size);
        int blockWidthLog2 = Av1Math.Log2(width);
        int expectedContext = GetExpectedLowerLevelContextEndOfBlock(blockWidthLog2, height, index);

        // Act
        int actualContext = Av1SymbolContextHelper.GetLowerLevelContextEndOfBlock(levels, index);

        // Assert
        Assert.Equal(expectedContext, actualContext);
    }

    [Theory]
    [MemberData(nameof(GetExtendedTransformIndicesData))]
    public void RoundTripExtendedTransformIndices(int setType, int index)
    {
        // Arrange
        Av1TransformSetType transformSetType = (Av1TransformSetType)setType;

        // Act
        Av1TransformType transformType = Av1SymbolContextHelper.ExtendedTransformInverse[(int)transformSetType][index];
        int actualIndex = Av1SymbolContextHelper.ExtendedTransformIndices[(int)transformSetType][(int)transformType];

        // Assert
        Assert.Equal(actualIndex, index);
    }

    /// <summary>
    /// Pins the 2D coefficient-base context offset (spec 8.3.2
    /// <c>Coeff_Base_Ctx_Offset[txSz][Min(row,4)][Min(col,4)]</c>) for the non-square shapes
    /// whose offset table is asymmetric.
    /// </summary>
    [Theory]
    // width < height: row < 2 maps to 11.
    [InlineData((int)Av1TransformSize.Size32x64, 1, 0, 11)]
    [InlineData((int)Av1TransformSize.Size32x64, 0, 1, 11)]
    [InlineData((int)Av1TransformSize.Size16x64, 1, 0, 11)]
    [InlineData((int)Av1TransformSize.Size8x16, 2, 0, 11)]
    // width > height: col < 2 maps to 16.
    [InlineData((int)Av1TransformSize.Size64x32, 1, 0, 16)]
    [InlineData((int)Av1TransformSize.Size64x32, 0, 1, 16)]
    [InlineData((int)Av1TransformSize.Size64x16, 1, 0, 16)]
    [InlineData((int)Av1TransformSize.Size16x8, 0, 1, 16)]
    // col == 2 on a width > height shape falls outside the col < 2 band: 6.
    [InlineData((int)Av1TransformSize.Size64x32, 2, 0, 6)]
    [InlineData((int)Av1TransformSize.Size16x8, 2, 0, 6)]
    public void GetNzMapContext_NonSquare_MatchesSpec(int transformSize, int x, int y, int expected)
        => Assert.Equal(expected, Av1NzMap.GetNzMapContext((Av1TransformSize)transformSize, new Point(x, y)));

    public static TheoryData<int, int, int> GetLowLevelContextEndOfBlockData()
    {
        TheoryData<int, int, int> result = [];
        for (int y = 1; y < 6; y++)
        {
            for (int x = 1; x < 6; x++)
            {
                int total = (1 << x) * (1 << y);
                for (int i = 0; i < total; i++)
                {
                    result.Add(1 << x, 1 << y, i);
                }
            }
        }

        return result;
    }

    public static TheoryData<int, int> GetExtendedTransformIndicesData()
    {
        TheoryData<int, int> result = [];
        for (Av1TransformSetType setType = Av1TransformSetType.DctOnly; setType < Av1TransformSetType.AllSets; setType++)
        {
            int count = Av1SymbolContextHelper.GetExtendedTransformTypeCount(setType);
            for (int index = 0; index < count; index++)
            {
                result.Add((int)setType, index);
            }
        }

        return result;
    }

    /// <summary>
    /// SVT: get_lower_levels_ctx_eob
    /// </summary>
    internal static int GetExpectedLowerLevelContextEndOfBlock(int blockWidthLog2, int height, int scanIndex)
    {
        if (scanIndex == 0)
        {
            return 0;
        }

        if (scanIndex <= height << blockWidthLog2 >> 3)
        {
            return 1;
        }

        if (scanIndex <= height << blockWidthLog2 >> 2)
        {
            return 2;
        }

        return 3;
    }
}
