// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;

/// <summary>
/// 5.11.50. Palette color context function. Computes the context for the
/// next palette color index from the (left, top-left, top) neighbors.
/// </summary>
internal static class Av1PaletteColorContext
{
    private const int NumNeighbors = 3;

    /// <summary>Spec table: weights[ left, top-left, top ] = { 2, 1, 2 }.</summary>
    private static readonly int[] NeighborWeights = [2, 1, 2];

    /// <summary>Spec table: Palette_Color_Hash_Multipliers[ ] = { 1, 2, 2 }.</summary>
    private static readonly int[] HashMultipliers = [1, 2, 2];

    /// <summary>
    /// Builds the color order for the current sample, returns the context index
    /// to use for reading <c>palette_color_idx</c>, and writes the inverse mapping
    /// for the current sample's color into <paramref name="colorIdx"/>.
    /// </summary>
    /// <param name="colorMap">Flat row-major index map for the block plane.</param>
    /// <param name="stride">Stride of <paramref name="colorMap"/> in samples.</param>
    /// <param name="r">Row of the current sample.</param>
    /// <param name="c">Column of the current sample.</param>
    /// <param name="paletteSize">Palette size for this plane.</param>
    /// <param name="colorOrder">
    /// Output of length <see cref="Av1BlockModeInfo.PaletteMaxSize"/>; receives the
    /// color order with the top <see cref="NumNeighbors"/> scoring colors first.
    /// </param>
    /// <param name="colorIdx">
    /// Output: the position in <paramref name="colorOrder"/> of the color at
    /// <c>colorMap[r, c]</c>. Used during decoding to look up the current sample's
    /// transmitted index after color-order shuffling.
    /// </param>
    public static int Compute(
        ReadOnlySpan<byte> colorMap,
        int stride,
        int r,
        int c,
        int paletteSize,
        Span<byte> colorOrder,
        out int colorIdx)
    {
        Span<int> neighbors =
        [
            c > 0 ? colorMap[(r * stride) + (c - 1)] : -1,
            c > 0 && r > 0 ? colorMap[((r - 1) * stride) + (c - 1)] : -1,
            r > 0 ? colorMap[((r - 1) * stride) + c] : -1,
        ];

        Span<int> scores = stackalloc int[Av1BlockModeInfo.PaletteMaxSize];
        scores.Clear();
        for (int i = 0; i < NumNeighbors; i++)
        {
            if (neighbors[i] >= 0)
            {
                scores[neighbors[i]] += NeighborWeights[i];
            }
        }

        Span<int> inverseColorOrder = stackalloc int[Av1BlockModeInfo.PaletteMaxSize];
        for (int i = 0; i < Av1BlockModeInfo.PaletteMaxSize; i++)
        {
            colorOrder[i] = (byte)i;
            inverseColorOrder[i] = i;
        }

        // Selection sort: float the top NumNeighbors scoring colors into positions 0..NumNeighbors-1.
        for (int i = 0; i < NumNeighbors; i++)
        {
            int max = scores[i];
            int maxIdx = i;
            for (int j = i + 1; j < paletteSize; j++)
            {
                if (scores[j] > max)
                {
                    max = scores[j];
                    maxIdx = j;
                }
            }

            if (maxIdx == i)
            {
                continue;
            }

            int maxScore = scores[maxIdx];
            byte maxColor = colorOrder[maxIdx];
            for (int k = maxIdx; k > i; k--)
            {
                scores[k] = scores[k - 1];
                colorOrder[k] = colorOrder[k - 1];
                inverseColorOrder[colorOrder[k]] = k;
            }

            scores[i] = maxScore;
            colorOrder[i] = maxColor;
            inverseColorOrder[colorOrder[i]] = i;
        }

        colorIdx = inverseColorOrder[colorMap[(r * stride) + c]];

        int hash = 0;
        for (int i = 0; i < NumNeighbors; i++)
        {
            hash += scores[i] * HashMultipliers[i];
        }

        return Av1DefaultDistributions.PaletteColorIndexContextLookup[hash];
    }
}
