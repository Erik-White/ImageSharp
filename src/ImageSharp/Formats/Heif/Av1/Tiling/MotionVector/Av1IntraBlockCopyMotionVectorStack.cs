// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// IBC subset of libaom's <c>setup_ref_mv_list</c> + <c>av1_find_best_ref_mvs</c>
/// (mvref_common.c). Builds a small candidate stack from already-decoded
/// neighbors of the current block (row above, column left, top-right,
/// second-outer rows/cols at offsets -3/-5/-7), selects <c>nearestmv</c> /
/// <c>nearmv</c>, and returns the predictor in 1/8-pel integer-pel units. When
/// no IBC neighbor is found in the scan window the predictor falls back to
/// libaom's <c>av1_find_ref_dv</c> synthesized vector.
/// </summary>
internal static class Av1IntraBlockCopyMotionVectorStack
{
    private const int MvRefRowCols = 3;
    private const int MaxRefMvStackSize = 8;
    private const int MaxMvRefCandidates = 2;
    private const int Block8x8Mi = 2;
    private const int Block16x16Mi = 4;
    private const int Block64x64Mi = 16;
    private const int RefCatLevel = 640;

    public delegate Av1BlockModeInfo? MiLookup(Point absoluteMi);

    /// <summary>
    /// Builds the IBC displacement-vector predictor for a block at the given
    /// mode-info position. Returns the predictor in 1/8-pel integer-pel units.
    /// </summary>
    public static Av1MotionVector BuildPredictor(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int superblockModeInfoSize,
        int modeInfoRow,
        int modeInfoColumn,
        int blockWidthMi,
        int blockHeightMi)
    {
        Span<CandidateMv> stack = stackalloc CandidateMv[MaxRefMvStackSize];
        Span<int> weights = stackalloc int[MaxRefMvStackSize];
        int count = 0;

        int rowAdj = (blockHeightMi < Block8x8Mi) && ((modeInfoRow & 1) != 0) ? 1 : 0;
        int colAdj = (blockWidthMi < Block8x8Mi) && ((modeInfoColumn & 1) != 0) ? 1 : 0;

        int maxRowOffset = ComputeMaxRowOffset(tileInfo, modeInfoRow, blockHeightMi, rowAdj);
        int maxColOffset = ComputeMaxColOffset(tileInfo, modeInfoColumn, blockWidthMi, colAdj);

        int processedRows = 0;
        int processedCols = 0;

        if (Math.Abs(maxRowOffset) >= 1)
        {
            ScanRow(
                lookupMi,
                tileInfo,
                modeInfoRow,
                modeInfoColumn,
                -1,
                blockWidthMi,
                blockHeightMi,
                maxRowOffset,
                ref count,
                ref processedRows,
                stack,
                weights);
        }

        if (Math.Abs(maxColOffset) >= 1)
        {
            ScanCol(
                lookupMi,
                tileInfo,
                modeInfoRow,
                modeInfoColumn,
                -1,
                blockWidthMi,
                blockHeightMi,
                maxColOffset,
                ref count,
                ref processedCols,
                stack,
                weights);
        }

        int blockSizeMi = Math.Max(blockWidthMi, blockHeightMi);
        if (HasTopRight(
            superblockModeInfoSize,
            modeInfoRow,
            modeInfoColumn,
            blockWidthMi,
            blockHeightMi,
            blockSizeMi))
        {
            ScanBlock(
                lookupMi,
                tileInfo,
                modeInfoRow,
                modeInfoColumn,
                -1,
                blockWidthMi,
                ref count,
                stack,
                weights);
        }

        // libaom captures nearest_refmv_count BEFORE the second-pass scans; the
        // first nearest_refmv_count entries get a REF_CAT_LEVEL boost so they
        // always rank ahead of outer candidates regardless of inner weight.
        int nearestCount = count;
        for (int idx = 0; idx < nearestCount; idx++)
        {
            weights[idx] += RefCatLevel;
        }

        ScanBlock(
            lookupMi,
            tileInfo,
            modeInfoRow,
            modeInfoColumn,
            -1,
            -1,
            ref count,
            stack,
            weights);

        for (int idx = 2; idx <= MvRefRowCols; idx++)
        {
            int rowOffset = -(idx << 1) + 1 + rowAdj;
            int colOffset = -(idx << 1) + 1 + colAdj;

            if (Math.Abs(rowOffset) <= Math.Abs(maxRowOffset) && Math.Abs(rowOffset) > processedRows)
            {
                ScanRow(
                    lookupMi,
                    tileInfo,
                    modeInfoRow,
                    modeInfoColumn,
                    rowOffset,
                    blockWidthMi,
                    blockHeightMi,
                    maxRowOffset,
                    ref count,
                    ref processedRows,
                    stack,
                    weights);
            }

            if (Math.Abs(colOffset) <= Math.Abs(maxColOffset) && Math.Abs(colOffset) > processedCols)
            {
                ScanCol(
                    lookupMi,
                    tileInfo,
                    modeInfoRow,
                    modeInfoColumn,
                    colOffset,
                    blockWidthMi,
                    blockHeightMi,
                    maxColOffset,
                    ref count,
                    ref processedCols,
                    stack,
                    weights);
            }
        }

        SortByWeight(stack, weights, 0, nearestCount);
        SortByWeight(stack, weights, nearestCount, count);

        Av1MotionVector nearest = count > 0 ? stack[0].Mv : default;
        Av1MotionVector near = count > 1 ? stack[1].Mv : default;
        ForceIntegerPel(ref nearest);
        ForceIntegerPel(ref near);

        Av1MotionVector chosen = nearest.IsZero ? near : nearest;
        if (chosen.IsZero)
        {
            chosen = SynthesizeReferenceDisplacementVector(tileInfo.ModeInfoRowStart, superblockModeInfoSize, modeInfoRow);
        }

        return chosen;
    }

    private static int ComputeMaxRowOffset(Av1TileInfo tileInfo, int mi_row, int bh, int rowAdj)
    {
        int max = -(MvRefRowCols << 1) + rowAdj;
        if (bh < Block8x8Mi)
        {
            max = -(2 << 1) + rowAdj;
        }

        return Clamp(max, tileInfo.ModeInfoRowStart - mi_row, tileInfo.ModeInfoRowEnd - mi_row - 1);
    }

    private static int ComputeMaxColOffset(Av1TileInfo tileInfo, int mi_col, int bw, int colAdj)
    {
        int max = -(MvRefRowCols << 1) + colAdj;
        if (bw < Block8x8Mi)
        {
            max = -(2 << 1) + colAdj;
        }

        return Clamp(max, tileInfo.ModeInfoColumnStart - mi_col, tileInfo.ModeInfoColumnEnd - mi_col - 1);
    }

    private static void ScanRow(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int mi_row,
        int mi_col,
        int rowOffset,
        int bw,
        int bh,
        int maxRowOffset,
        ref int count,
        ref int processedRows,
        Span<CandidateMv> stack,
        Span<int> weights)
    {
        int endMi = Math.Min(bw, tileInfo.ModeInfoColumnEnd - mi_col);
        endMi = Math.Min(endMi, Block64x64Mi);
        int colOffset = 0;
        if (Math.Abs(rowOffset) > 1)
        {
            colOffset = 1;
            if (((mi_col & 1) != 0) && bw < Block8x8Mi)
            {
                colOffset--;
            }
        }

        // libaom: use_step_16 = (xd->width >= 16) where 16 is mi-units, i.e. BLOCK_64X64.
        // It is NOT mi_size_wide[BLOCK_16X16] (which is 4 mi-units) — that constant only
        // appears as the lower bound for len once use_step_16 has fired.
        bool useStep16 = bw >= Block64x64Mi;
        int row = mi_row + rowOffset;

        for (int i = 0; i < endMi;)
        {
            int candidateCol = mi_col + colOffset + i;
            Av1BlockModeInfo? candidate = lookupMi(new Point(candidateCol, row));
            if (candidate == null)
            {
                i += 1;
                continue;
            }

            int n4w = candidate.BlockSize.Get4x4WideCount();
            int n4h = candidate.BlockSize.Get4x4HighCount();
            int len = Math.Min(bw, n4w);
            if (useStep16)
            {
                len = Math.Max(Block16x16Mi, len);
            }
            else if (Math.Abs(rowOffset) > 1)
            {
                len = Math.Max(len, Block8x8Mi);
            }

            int weight = 2;
            if (bw >= Block8x8Mi && bw <= n4w)
            {
                int inc = Math.Min(-maxRowOffset + rowOffset + 1, n4h);
                weight = Math.Max(weight, inc);
                processedRows = inc - rowOffset - 1;
            }

            AddIbcCandidate(candidate, len * weight, ref count, stack, weights);
            i += len;
        }
    }

    private static void ScanCol(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int mi_row,
        int mi_col,
        int colOffset,
        int bw,
        int bh,
        int maxColOffset,
        ref int count,
        ref int processedCols,
        Span<CandidateMv> stack,
        Span<int> weights)
    {
        int endMi = Math.Min(bh, tileInfo.ModeInfoRowEnd - mi_row);
        endMi = Math.Min(endMi, Block64x64Mi);
        int rowOffset = 0;
        if (Math.Abs(colOffset) > 1)
        {
            rowOffset = 1;
            if (((mi_row & 1) != 0) && bh < Block8x8Mi)
            {
                rowOffset--;
            }
        }

        bool useStep16 = bh >= Block64x64Mi;
        int col = mi_col + colOffset;

        for (int i = 0; i < endMi;)
        {
            int candidateRow = mi_row + rowOffset + i;
            Av1BlockModeInfo? candidate = lookupMi(new Point(col, candidateRow));
            if (candidate == null)
            {
                i += 1;
                continue;
            }

            int n4w = candidate.BlockSize.Get4x4WideCount();
            int n4h = candidate.BlockSize.Get4x4HighCount();
            int len = Math.Min(bh, n4h);
            if (useStep16)
            {
                len = Math.Max(Block16x16Mi, len);
            }
            else if (Math.Abs(colOffset) > 1)
            {
                len = Math.Max(len, Block8x8Mi);
            }

            int weight = 2;
            if (bh >= Block8x8Mi && bh <= n4h)
            {
                int inc = Math.Min(-maxColOffset + colOffset + 1, n4w);
                weight = Math.Max(weight, inc);
                processedCols = inc - colOffset - 1;
            }

            AddIbcCandidate(candidate, len * weight, ref count, stack, weights);
            i += len;
        }
    }

    private static void ScanBlock(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int mi_row,
        int mi_col,
        int rowOffset,
        int colOffset,
        ref int count,
        Span<CandidateMv> stack,
        Span<int> weights)
    {
        if (!IsInside(tileInfo, mi_row, mi_col, rowOffset, colOffset))
        {
            return;
        }

        Av1BlockModeInfo? candidate = lookupMi(new Point(mi_col + colOffset, mi_row + rowOffset));
        if (candidate == null)
        {
            return;
        }

        AddIbcCandidate(candidate, 2 * Block8x8Mi, ref count, stack, weights);
    }

    private static void AddIbcCandidate(
        Av1BlockModeInfo candidate,
        int weight,
        ref int count,
        Span<CandidateMv> stack,
        Span<int> weights)
    {
        if (!candidate.UseIntraBlockCopy)
        {
            return;
        }

        Av1MotionVector mv = candidate.DisplacementVector;
        for (int i = 0; i < count; i++)
        {
            if (stack[i].Mv == mv)
            {
                weights[i] += weight;
                return;
            }
        }

        if (count < MaxRefMvStackSize)
        {
            stack[count] = new CandidateMv(mv);
            weights[count] = weight;
            count++;
        }
    }

    private static void SortByWeight(Span<CandidateMv> stack, Span<int> weights, int start, int end)
    {
        // Bubble-sort by weight, descending — matches libaom's setup_ref_mv_list reordering.
        // libaom sorts the [0, nearest_refmv_count) and [nearest_refmv_count, refmv_count)
        // ranges separately so that nearest candidates always precede outer ones.
        int len = end;
        while (len > start)
        {
            int newLen = start;
            for (int idx = start + 1; idx < len; idx++)
            {
                if (weights[idx - 1] < weights[idx])
                {
                    (stack[idx - 1], stack[idx]) = (stack[idx], stack[idx - 1]);
                    (weights[idx - 1], weights[idx]) = (weights[idx], weights[idx - 1]);
                    newLen = idx;
                }
            }

            len = newLen;
        }
    }

    private static bool HasTopRight(int superblockMib, int mi_row, int mi_col, int bw, int bh, int bs)
    {
        int maskRow = mi_row & (superblockMib - 1);
        int maskCol = mi_col & (superblockMib - 1);
        if (bs > Block64x64Mi)
        {
            return false;
        }

        bool hasTr = !(((maskRow & bs) != 0) && ((maskCol & bs) != 0));
        while (bs < superblockMib)
        {
            if ((maskCol & bs) != 0)
            {
                if (((maskCol & (2 * bs)) != 0) && ((maskRow & (2 * bs)) != 0))
                {
                    hasTr = false;
                    break;
                }
            }
            else
            {
                break;
            }

            bs <<= 1;
        }

        bool isLastVerticalRect = (bw < bh) && (((mi_col + bw) & (bh - 1)) == 0);
        bool isFirstHorizontalRect = (bw > bh) && ((mi_row & (bw - 1)) == 0);

        if (bw < bh && !isLastVerticalRect)
        {
            hasTr = true;
        }

        if (bw > bh && !isFirstHorizontalRect)
        {
            hasTr = false;
        }

        return hasTr;
    }

    private static bool IsInside(Av1TileInfo tileInfo, int mi_row, int mi_col, int rowOffset, int colOffset)
    {
        int r = mi_row + rowOffset;
        int c = mi_col + colOffset;
        return r >= tileInfo.ModeInfoRowStart && r < tileInfo.ModeInfoRowEnd
            && c >= tileInfo.ModeInfoColumnStart && c < tileInfo.ModeInfoColumnEnd;
    }

    private static void ForceIntegerPel(ref Av1MotionVector mv)
    {
        // libaom's integer_mv_precision: round each component to the nearest multiple of 8.
        mv = new Av1MotionVector(RoundToInteger(mv.Row), RoundToInteger(mv.Col));
    }

    private static short RoundToInteger(short value)
    {
        int mod = value % 8;
        if (mod == 0)
        {
            return value;
        }

        int rounded = value - mod;
        if (Math.Abs(mod) > 4)
        {
            rounded += mod > 0 ? 8 : -8;
        }

        return (short)rounded;
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

    /// <summary>
    /// Mirrors libaom's <c>av1_find_ref_dv</c>: when the current superblock
    /// row is the tile's first SB row the predictor points one SB-width plus
    /// the IBC delay to the left; otherwise it points one SB-height up.
    /// Returned vector is in 1/8-pel units.
    /// </summary>
    private static Av1MotionVector SynthesizeReferenceDisplacementVector(
        int tileModeInfoRowStart,
        int superblockModeInfoSize,
        int modeInfoRow)
    {
        int superblockSizePixels = superblockModeInfoSize << Av1Constants.ModeInfoSizeLog2;
        if (modeInfoRow - superblockModeInfoSize < tileModeInfoRowStart)
        {
            int col = -(superblockSizePixels + Av1MotionVectorConstants.IntraBlockCopyDelayPixels) << 3;
            return new Av1MotionVector(0, (short)col);
        }

        int row = -superblockSizePixels << 3;
        return new Av1MotionVector((short)row, 0);
    }

    private readonly record struct CandidateMv(Av1MotionVector Mv);
}
