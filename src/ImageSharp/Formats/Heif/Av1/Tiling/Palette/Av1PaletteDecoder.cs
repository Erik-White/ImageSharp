// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Prediction;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;

/// <summary>
/// Decodes the palette mode information (5.11.46) and palette tokens (5.11.49)
/// for a single block. The decoded base colors and per-sample color index map
/// are stored on the block's <see cref="Av1BlockModeInfo"/> so subsequent
/// blocks can use them as neighbor cache contributions and so the predictor
/// can write reconstructed samples without re-parsing.
/// </summary>
internal static class Av1PaletteDecoder
{
    /// <summary>
    /// 5.11.46. Reads <c>palette_mode_info</c> for the block.
    /// </summary>
    public static void ReadPaletteModeInfo(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        ObuSequenceHeader sequenceHeader)
    {
        Av1BlockModeInfo modeInfo = partitionInfo.ModeInfo;
        Av1BlockSize blockSize = modeInfo.BlockSize;
        int bsizeCtx = blockSize.Get4x4WidthLog2() + blockSize.Get4x4HeightLog2() - 2;
        int bitDepth = sequenceHeader.ColorConfig.BitDepth.GetBitCount();

        int paletteSizeY = 0;
        int paletteSizeUv = 0;

        if (modeInfo.YMode == Av1PredictionMode.DC)
        {
            int paletteModeCtx = NeighborHasPalette(partitionInfo.AboveModeInfo, Av1PlaneType.Y)
                + NeighborHasPalette(partitionInfo.LeftModeInfo, Av1PlaneType.Y);
            if (reader.ReadHasPaletteY(bsizeCtx, paletteModeCtx))
            {
                paletteSizeY = reader.ReadPaletteSizeY(bsizeCtx) + 2;
                modeInfo.PaletteColorsY = ReadYPalette(ref reader, partitionInfo, paletteSizeY, bitDepth);
            }
        }

        if (!sequenceHeader.ColorConfig.IsMonochrome && partitionInfo.IsChroma && modeInfo.UvMode == Av1PredictionMode.DC)
        {
            int paletteUvModeCtx = paletteSizeY > 0 ? 1 : 0;
            if (reader.ReadHasPaletteUv(paletteUvModeCtx))
            {
                paletteSizeUv = reader.ReadPaletteSizeUv(bsizeCtx) + 2;
                modeInfo.PaletteColorsU = ReadUPalette(ref reader, partitionInfo, paletteSizeUv, bitDepth);
                modeInfo.PaletteColorsV = ReadVPalette(ref reader, paletteSizeUv, bitDepth);
            }
        }

        modeInfo.SetPaletteSizes(paletteSizeY, paletteSizeUv);
    }

    /// <summary>
    /// 5.11.49. Reads <c>palette_tokens</c> for any active palette planes on this block.
    /// </summary>
    public static void ReadPaletteTokens(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        ObuSequenceHeader sequenceHeader)
    {
        Av1BlockModeInfo modeInfo = partitionInfo.ModeInfo;
        Av1BlockSize blockSize = modeInfo.BlockSize;

        int paletteSizeY = modeInfo.GetPaletteSize(Av1PlaneType.Y);
        if (paletteSizeY != 0)
        {
            PaletteBlockExtents extents = PaletteBlockExtents.For(blockSize, partitionInfo, plane: 0, subX: false, subY: false);
            modeInfo.ColorIndexMapY = new byte[extents.Width * extents.Height];
            modeInfo.ColorMapWidthY = extents.Width;
            DecodeColorMap(ref reader, paletteSizeY, extents, Av1PlaneType.Y, modeInfo.ColorIndexMapY);
        }

        int paletteSizeUv = modeInfo.GetPaletteSize(Av1PlaneType.Uv);
        if (paletteSizeUv != 0)
        {
            bool subX = sequenceHeader.ColorConfig.SubSamplingX;
            bool subY = sequenceHeader.ColorConfig.SubSamplingY;
            PaletteBlockExtents extents = PaletteBlockExtents.For(blockSize, partitionInfo, plane: 1, subX, subY);
            modeInfo.ColorIndexMapUv = new byte[extents.Width * extents.Height];
            modeInfo.ColorMapWidthUv = extents.Width;
            DecodeColorMap(ref reader, paletteSizeUv, extents, Av1PlaneType.Uv, modeInfo.ColorIndexMapUv);
        }
    }

    private static int NeighborHasPalette(Av1BlockModeInfo? neighbor, Av1PlaneType plane)
        => (neighbor?.GetPaletteSize(plane) ?? 0) > 0 ? 1 : 0;

    /// <summary>
    /// 5.11.46 <c>palette_colors_y</c>: cache + first-color + delta-coded scheme. Y deltas have
    /// a +1 bias (deltas of 0 are forbidden because the sequence is strictly ascending) and the
    /// running range is reduced by 1 below max to match the bias.
    /// </summary>
    private static ushort[] ReadYPalette(
        ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, int paletteSize, int bitDepth)
        => ReadCachedPalette(ref reader, partitionInfo, Av1PlaneType.Y, paletteSize, bitDepth, deltaBias: 1, rangeBias: 1);

    /// <summary>
    /// 5.11.46 <c>palette_colors_u</c>: same shape as Y, but deltas are not biased and the
    /// range starts at <c>(1 &lt;&lt; BitDepth) - first_color</c>.
    /// </summary>
    private static ushort[] ReadUPalette(
        ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, int paletteSize, int bitDepth)
        => ReadCachedPalette(ref reader, partitionInfo, Av1PlaneType.Uv, paletteSize, bitDepth, deltaBias: 0, rangeBias: 0);

    private static ushort[] ReadCachedPalette(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        Av1PlaneType plane,
        int paletteSize,
        int bitDepth,
        int deltaBias,
        int rangeBias)
    {
        Span<ushort> colorCache = stackalloc ushort[2 * Av1BlockModeInfo.PaletteMaxSize];
        Span<ushort> cachedColors = stackalloc ushort[Av1BlockModeInfo.PaletteMaxSize];
        int cacheLength = Av1PaletteCache.Build(partitionInfo, plane, colorCache);
        ushort[] palette = new ushort[paletteSize];

        int idx = 0;
        for (int i = 0; i < cacheLength && idx < paletteSize; i++)
        {
            if (reader.ReadLiteral(1) != 0)
            {
                cachedColors[idx++] = colorCache[i];
            }
        }

        if (idx == paletteSize)
        {
            cachedColors[..paletteSize].CopyTo(palette);
            return palette;
        }

        int nCached = idx;
        palette[idx++] = (ushort)reader.ReadLiteral(bitDepth);
        if (idx < paletteSize)
        {
            int bits = bitDepth - 3 + reader.ReadLiteral(2);
            int range = (1 << bitDepth) - palette[idx - 1] - rangeBias;
            for (; idx < paletteSize; idx++)
            {
                int delta = reader.ReadLiteral(bits) + deltaBias;
                int v = Math.Clamp(palette[idx - 1] + delta, 0, (1 << bitDepth) - 1);
                palette[idx] = (ushort)v;
                range -= palette[idx] - palette[idx - 1];
                bits = Math.Min(bits, (int)Av1Math.CeilLog2((uint)Math.Max(0, range)));
            }
        }

        SortMerge(palette, cachedColors, paletteSize, nCached);
        return palette;
    }

    /// <summary>
    /// V palette per 5.11.46: optional sign-bit delta coding, otherwise per-sample literals.
    /// </summary>
    private static ushort[] ReadVPalette(ref Av1SymbolDecoder reader, int paletteSize, int bitDepth)
    {
        ushort[] palette = new ushort[paletteSize];
        bool deltaEncoded = reader.ReadLiteral(1) != 0;
        if (!deltaEncoded)
        {
            for (int i = 0; i < paletteSize; i++)
            {
                palette[i] = (ushort)reader.ReadLiteral(bitDepth);
            }

            return palette;
        }

        int maxValue = 1 << bitDepth;
        int bits = bitDepth - 4 + reader.ReadLiteral(2);
        palette[0] = (ushort)reader.ReadLiteral(bitDepth);
        for (int i = 1; i < paletteSize; i++)
        {
            int delta = reader.ReadLiteral(bits);
            if (delta != 0 && reader.ReadLiteral(1) != 0)
            {
                delta = -delta;
            }

            int v = palette[i - 1] + delta;
            if (v < 0)
            {
                v += maxValue;
            }
            else if (v >= maxValue)
            {
                v -= maxValue;
            }

            palette[i] = (ushort)v;
        }

        return palette;
    }

    /// <summary>
    /// Sorted merge of the cached colors at <c>cached[0..nCached)</c> and the
    /// delta-decoded colors at <c>palette[nCached..paletteSize)</c> into a single
    /// ascending sequence at <c>palette[0..paletteSize)</c>. When the values are
    /// equal the cache is preferred; this matches the AV1 spec's sort-then-merge
    /// description in 5.11.46.
    /// </summary>
    internal static void SortMerge(Span<ushort> palette, ReadOnlySpan<ushort> cached, int paletteSize, int nCached)
    {
        if (nCached == 0)
        {
            return;
        }

        int cacheIdx = 0;
        int transIdx = nCached;
        for (int i = 0; i < paletteSize; i++)
        {
            if (cacheIdx < nCached &&
                (transIdx >= paletteSize || cached[cacheIdx] <= palette[transIdx]))
            {
                palette[i] = cached[cacheIdx++];
            }
            else
            {
                palette[i] = palette[transIdx++];
            }
        }
    }

    /// <summary>
    /// Decodes the per-sample color index map via a wavefront over diagonals
    /// (i = r + c) using 3-neighbor context. The wavefront covers only the
    /// on-screen <see cref="PaletteBlockExtents.Rows"/> × <see cref="PaletteBlockExtents.Cols"/>;
    /// off-screen samples in the block-plane buffer remain zero so the
    /// predictor never indexes uninitialised data.
    /// </summary>
    private static void DecodeColorMap(
        ref Av1SymbolDecoder reader,
        int paletteSize,
        PaletteBlockExtents extents,
        Av1PlaneType plane,
        Span<byte> colorMap)
    {
        Span<byte> colorOrder = stackalloc byte[Av1BlockModeInfo.PaletteMaxSize];

        // First sample: ns(paletteSize).
        colorMap[0] = (byte)ReadNonSymmetric(ref reader, paletteSize);

        for (int i = 1; i < extents.Rows + extents.Cols - 1; i++)
        {
            int jStart = Math.Min(i, extents.Cols - 1);
            int jEnd = Math.Max(0, i - extents.Rows + 1);
            for (int j = jStart; j >= jEnd; j--)
            {
                int r = i - j;
                int colorCtx = Av1PaletteColorContext.Compute(
                    colorMap, extents.Width, r, j, paletteSize, colorOrder, out _);
                int colorIdx = plane == Av1PlaneType.Y
                    ? reader.ReadPaletteColorIdxY(paletteSize, colorCtx)
                    : reader.ReadPaletteColorIdxUv(paletteSize, colorCtx);
                colorMap[(r * extents.Width) + j] = colorOrder[colorIdx];
            }
        }
    }

    /// <summary>
    /// 4.10.7. <c>ns(n)</c> — non-symmetric encoded value in the range [0, n).
    /// </summary>
    internal static int ReadNonSymmetric(ref Av1SymbolDecoder reader, int n)
    {
        int w = (int)Av1Math.CeilLog2((uint)(n + 1));
        int m = (1 << w) - n;
        int v = reader.ReadLiteral(w - 1);
        if (v < m)
        {
            return v;
        }

        return (v << 1) - m + reader.ReadLiteral(1);
    }
}
