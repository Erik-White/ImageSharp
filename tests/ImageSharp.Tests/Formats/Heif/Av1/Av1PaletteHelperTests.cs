// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1PaletteHelperTests
{
    [Fact]
    public void SortMerge_NoCache_LeavesPaletteUnchanged()
    {
        ushort[] palette = [10, 20, 30];
        ushort[] cached = [];
        Av1PaletteDecoder.SortMerge(palette, cached, paletteSize: 3, nCached: 0);
        Assert.Equal(new ushort[] { 10, 20, 30 }, palette);
    }

    [Fact]
    public void SortMerge_AllCached_FillsPaletteFromCache()
    {
        ushort[] palette = new ushort[3];
        ushort[] cached = [5, 15, 25];
        Av1PaletteDecoder.SortMerge(palette, cached, paletteSize: 3, nCached: 3);
        Assert.Equal(new ushort[] { 5, 15, 25 }, palette);
    }

    [Fact]
    public void SortMerge_InterleavedSorted()
    {
        // Cached: [10, 30] at indices 0..1; transmitted [20] at index 2.
        // Expected merge: [10, 20, 30].
        ushort[] palette = [0, 0, 20];
        ushort[] cached = [10, 30, 0, 0, 0, 0, 0, 0];
        Av1PaletteDecoder.SortMerge(palette, cached, paletteSize: 3, nCached: 2);
        Assert.Equal(new ushort[] { 10, 20, 30 }, palette);
    }

    [Fact]
    public void SortMerge_TransmittedSmallerThanCached()
    {
        // Cached: [50, 60]; transmitted [10, 20] at indices 2..3.
        // Expected: [10, 20, 50, 60].
        ushort[] palette = [0, 0, 10, 20];
        ushort[] cached = [50, 60, 0, 0, 0, 0, 0, 0];
        Av1PaletteDecoder.SortMerge(palette, cached, paletteSize: 4, nCached: 2);
        Assert.Equal(new ushort[] { 10, 20, 50, 60 }, palette);
    }

    [Fact]
    public void SortMerge_EqualValuesPickCacheFirst()
    {
        // Equal cache and transmitted values: cache wins (spec sorted-merge uses '<=').
        ushort[] palette = [0, 20, 30];
        ushort[] cached = [20, 0, 0, 0, 0, 0, 0, 0];
        Av1PaletteDecoder.SortMerge(palette, cached, paletteSize: 3, nCached: 1);
        Assert.Equal(new ushort[] { 20, 20, 30 }, palette);
    }

    [Fact]
    public void ColorContext_FirstColumnOfRow_UsesTopAndTopLeft()
    {
        // 2x3 color map; we ask for context at (1, 1).
        // Neighbors: left=colorMap[3]=1, top-left=colorMap[0]=0, top=colorMap[1]=1.
        byte[] map = [0, 1, 0, 1, 1, 0];
        Span<byte> colorOrder = stackalloc byte[8];
        int ctx = Av1PaletteColorContext.Compute(
            map, stride: 3, r: 1, c: 1, paletteSize: 2, colorOrder, out int colorIdx);

        // scores[0]=1 (top-left), scores[1]=2+2=4 (left+top).
        // Selection sort produces scores[0]=4, scores[1]=1.
        // Hash = 4*1 + 1*2 + 0*2 = 6 -> lookup table index 6 = 3.
        Assert.Equal(3, ctx);
        Assert.Equal(0, colorIdx); // current cell colorMap[(1*3)+1]=1; sort moves color 1 to index 0.
    }

    [Fact]
    public void ColorContext_TopRow_OnlyLeftNeighbor()
    {
        // First row of map: only the left neighbor exists.
        byte[] map = [3, 3, 3];
        Span<byte> colorOrder = stackalloc byte[8];
        int ctx = Av1PaletteColorContext.Compute(
            map, stride: 3, r: 0, c: 1, paletteSize: 4, colorOrder, out int colorIdx);

        // Only left neighbor counts: scores[3]=2.
        // After sort: scores[0]=2, scores[1..]=0.
        // Hash = 2*1 + 0 + 0 = 2 -> lookup table index 2 = 0.
        Assert.Equal(0, ctx);
        Assert.Equal(0, colorIdx); // colorMap value 3 sorted to index 0.
    }

    [Fact]
    public void ColorContext_AllNeighborsSameColor_HashIsFive()
    {
        // All three neighbors are color 0; current cell is color 0.
        byte[] map = [0, 0, 0, 0];
        Span<byte> colorOrder = stackalloc byte[8];
        int ctx = Av1PaletteColorContext.Compute(
            map, stride: 2, r: 1, c: 1, paletteSize: 2, colorOrder, out int colorIdx);

        // scores[0] = 2+1+2 = 5; scores[1] = 0.
        // After sort: scores[0]=5, scores[1]=0.
        // Hash = 5*1 + 0*2 + 0*2 = 5 -> lookup[5] = 4.
        Assert.Equal(4, ctx);
        Assert.Equal(0, colorIdx);
    }

    [Fact]
    public void ColorContext_HashLookupTableLayoutIsStable()
    {
        // Negative entries are unreachable (the spec hash range guarantees a positive lookup index
        // for any reachable hash value). Guard the table layout itself.
        Assert.Equal(
            new[] { -1, -1, 0, -1, -1, 4, 3, 2, 1 },
            Av1DefaultDistributions.PaletteColorIndexContextLookup);
    }

    [Fact]
    public void PaletteCache_MergeUnique_DropsDuplicates()
    {
        ushort[] above = [10, 30, 50];
        ushort[] left = [10, 20, 50, 60];
        Span<ushort> cache = stackalloc ushort[16];
        int n = Av1PaletteCache.MergeUnique(above, left, cache);

        Assert.Equal(5, n);
        Assert.Equal(new ushort[] { 10, 20, 30, 50, 60 }, cache[..n].ToArray());
    }

    [Fact]
    public void PaletteCache_MergeUnique_OneSideEmpty()
    {
        ushort[] above = [];
        ushort[] left = [5, 15, 25];
        Span<ushort> cache = stackalloc ushort[8];
        int n = Av1PaletteCache.MergeUnique(above, left, cache);

        Assert.Equal(3, n);
        Assert.Equal(new ushort[] { 5, 15, 25 }, cache[..n].ToArray());
    }

}
