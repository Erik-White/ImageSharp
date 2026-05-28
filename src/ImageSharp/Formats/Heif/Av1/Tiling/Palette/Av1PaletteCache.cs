// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;

/// <summary>
/// 5.11.46. <c>get_palette_cache</c> and the related sorted-merge of two palettes.
/// Builds a deduplicated, ascending palette cache from the above and left neighbor
/// palettes for the given plane.
/// </summary>
internal static class Av1PaletteCache
{
    /// <summary>
    /// Fills <paramref name="cache"/> with the merged sorted unique colors from the
    /// above and left neighbors' palettes. The cache must be at least
    /// <c>2 * <see cref="Av1BlockModeInfo.PaletteMaxSize"/></c> entries long.
    /// </summary>
    /// <returns>The number of colors written to <paramref name="cache"/>.</returns>
    public static int Build(Av1PartitionInfo partitionInfo, Av1PlaneType plane, Span<ushort> cache)
    {
        // libaom av1_get_palette_cache (pred_common.c:73): the above neighbor's
        // palette is ignored when the current block sits on a 64-luma-pixel
        // (16 mi-row) boundary. mi_row * 4 % 64 == 0 ⇔ mi_row % 16 == 0.
        Av1BlockModeInfo? aboveMi = (partitionInfo.RowIndex & 0xF) != 0
            ? partitionInfo.AboveModeInfo
            : null;
        ReadOnlySpan<ushort> above = GetPalette(aboveMi, plane);
        ReadOnlySpan<ushort> left = GetPalette(partitionInfo.LeftModeInfo, plane);
        return MergeUnique(above, left, cache);
    }

    /// <summary>
    /// 5.11.46 sort-merge: merges two pre-sorted ascending palettes into one
    /// ascending sequence with consecutive duplicates collapsed.
    /// </summary>
    /// <returns>Length of the merged sequence.</returns>
    public static int MergeUnique(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b, Span<ushort> destination)
    {
        int count = 0;
        int aIdx = 0;
        int bIdx = 0;
        while (aIdx < a.Length && bIdx < b.Length)
        {
            ushort aValue = a[aIdx];
            ushort bValue = b[bIdx];
            if (bValue < aValue)
            {
                Append(destination, ref count, bValue);
                bIdx++;
            }
            else
            {
                Append(destination, ref count, aValue);
                aIdx++;
                if (bValue == aValue)
                {
                    bIdx++;
                }
            }
        }

        while (aIdx < a.Length)
        {
            Append(destination, ref count, a[aIdx++]);
        }

        while (bIdx < b.Length)
        {
            Append(destination, ref count, b[bIdx++]);
        }

        return count;
    }

    private static ReadOnlySpan<ushort> GetPalette(Av1BlockModeInfo? modeInfo, Av1PlaneType plane)
    {
        if (modeInfo is null)
        {
            return [];
        }

        int size = modeInfo.GetPaletteSize(plane);
        if (size == 0)
        {
            return [];
        }

        ushort[] colors = plane == Av1PlaneType.Y ? modeInfo.PaletteColorsY : modeInfo.PaletteColorsU;
        return colors.AsSpan(0, size);
    }

    private static void Append(Span<ushort> cache, ref int count, ushort value)
    {
        if (count == 0 || cache[count - 1] != value)
        {
            cache[count++] = value;
        }
    }
}
