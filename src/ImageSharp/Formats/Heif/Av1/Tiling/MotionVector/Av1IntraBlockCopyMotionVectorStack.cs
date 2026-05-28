// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// IBC subset of libaom's <c>setup_ref_mv_list</c> + <c>av1_find_best_ref_mvs</c>
/// (mvref_common.c). Builds a small candidate stack from already-decoded
/// neighbors of the current block (row above, column left, top-right, plus
/// second-outer rows/cols at offsets -3/-5), selects <c>nearestmv</c> /
/// <c>nearmv</c>, and returns the predictor in 1/8-pel integer-pel units. When
/// no IBC neighbor is found in the scan window the predictor falls back to
/// libaom's <c>av1_find_ref_dv</c> synthesized vector.
/// </summary>
internal static class Av1IntraBlockCopyMotionVectorStack
{
    private const int MvRefRowCols = 3;
    private const int MaxRefMvStackSize = 8;
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
        Span<Av1MotionVector> entries = stackalloc Av1MotionVector[MaxRefMvStackSize];
        Span<int> weights = stackalloc int[MaxRefMvStackSize];
        CandidateStack stack = new(entries, weights);

        int rowAdj = (blockHeightMi < Block8x8Mi) && ((modeInfoRow & 1) != 0) ? 1 : 0;
        int colAdj = (blockWidthMi < Block8x8Mi) && ((modeInfoColumn & 1) != 0) ? 1 : 0;

        int maxRowOffset = ComputeMaxRowOffset(tileInfo, modeInfoRow, blockHeightMi, rowAdj);
        int maxColOffset = ComputeMaxColOffset(tileInfo, modeInfoColumn, blockWidthMi, colAdj);

        int processedRows = 0;
        int processedCols = 0;

        if (Math.Abs(maxRowOffset) >= 1)
        {
            ScanRow(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, -1, blockWidthMi, maxRowOffset, ref stack, ref processedRows);
        }

        if (Math.Abs(maxColOffset) >= 1)
        {
            ScanCol(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, -1, blockHeightMi, maxColOffset, ref stack, ref processedCols);
        }

        int blockSizeMi = Math.Max(blockWidthMi, blockHeightMi);
        if (HasTopRight(superblockModeInfoSize, modeInfoRow, modeInfoColumn, blockWidthMi, blockHeightMi, blockSizeMi))
        {
            ScanBlock(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, -1, blockWidthMi, ref stack);
        }

        // libaom captures nearest_refmv_count BEFORE the second-pass scans; the
        // first nearest_refmv_count entries get a REF_CAT_LEVEL boost so they
        // always rank ahead of outer candidates regardless of inner weight.
        int nearestCount = stack.Count;
        stack.BoostNearestRange(nearestCount, RefCatLevel);

        ScanBlock(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, -1, -1, ref stack);

        for (int idx = 2; idx <= MvRefRowCols; idx++)
        {
            int rowOffset = -(idx << 1) + 1 + rowAdj;
            int colOffset = -(idx << 1) + 1 + colAdj;

            if (Math.Abs(rowOffset) <= Math.Abs(maxRowOffset) && Math.Abs(rowOffset) > processedRows)
            {
                ScanRow(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, rowOffset, blockWidthMi, maxRowOffset, ref stack, ref processedRows);
            }

            if (Math.Abs(colOffset) <= Math.Abs(maxColOffset) && Math.Abs(colOffset) > processedCols)
            {
                ScanCol(lookupMi, tileInfo, modeInfoRow, modeInfoColumn, colOffset, blockHeightMi, maxColOffset, ref stack, ref processedCols);
            }
        }

        stack.SortRange(0, nearestCount);
        stack.SortRange(nearestCount, stack.Count);

        Av1MotionVector nearest = stack.Count > 0 ? RoundToIntegerPel(stack[0]) : default;
        Av1MotionVector near = stack.Count > 1 ? RoundToIntegerPel(stack[1]) : default;

        Av1MotionVector chosen = nearest.IsZero ? near : nearest;
        if (chosen.IsZero)
        {
            chosen = SynthesizeReferenceDisplacementVector(tileInfo.ModeInfoRowStart, superblockModeInfoSize, modeInfoRow);
        }

        return chosen;
    }

    private static int ComputeMaxRowOffset(Av1TileInfo tileInfo, int modeInfoRow, int blockHeightMi, int rowAdj)
    {
        int max = -(MvRefRowCols << 1) + rowAdj;
        if (blockHeightMi < Block8x8Mi)
        {
            max = -(2 << 1) + rowAdj;
        }

        return Math.Clamp(max, tileInfo.ModeInfoRowStart - modeInfoRow, tileInfo.ModeInfoRowEnd - modeInfoRow - 1);
    }

    private static int ComputeMaxColOffset(Av1TileInfo tileInfo, int modeInfoColumn, int blockWidthMi, int colAdj)
    {
        int max = -(MvRefRowCols << 1) + colAdj;
        if (blockWidthMi < Block8x8Mi)
        {
            max = -(2 << 1) + colAdj;
        }

        return Math.Clamp(max, tileInfo.ModeInfoColumnStart - modeInfoColumn, tileInfo.ModeInfoColumnEnd - modeInfoColumn - 1);
    }

    private static void ScanRow(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int modeInfoRow,
        int modeInfoColumn,
        int rowOffset,
        int blockWidthMi,
        int maxRowOffset,
        ref CandidateStack stack,
        ref int processedRows)
    {
        int endMi = Math.Min(blockWidthMi, tileInfo.ModeInfoColumnEnd - modeInfoColumn);
        endMi = Math.Min(endMi, Block64x64Mi);
        int colOffset = 0;
        if (Math.Abs(rowOffset) > 1)
        {
            colOffset = 1;
            if (((modeInfoColumn & 1) != 0) && blockWidthMi < Block8x8Mi)
            {
                colOffset--;
            }
        }

        // libaom: use_step_16 = (xd->width >= 16) where 16 is mi-units, i.e. BLOCK_64X64.
        // It is NOT mi_size_wide[BLOCK_16X16] (which is 4 mi-units) — that constant only
        // appears as the lower bound for len once use_step_16 has fired.
        bool useStep16 = blockWidthMi >= Block64x64Mi;
        int row = modeInfoRow + rowOffset;

        for (int i = 0; i < endMi;)
        {
            int candidateCol = modeInfoColumn + colOffset + i;
            Av1BlockModeInfo? candidate = lookupMi(new Point(candidateCol, row));
            if (candidate == null)
            {
                i++;
                continue;
            }

            int candidateWidthMi = candidate.BlockSize.Get4x4WideCount();
            int candidateHeightMi = candidate.BlockSize.Get4x4HighCount();
            int len = Math.Min(blockWidthMi, candidateWidthMi);
            if (useStep16)
            {
                len = Math.Max(Block16x16Mi, len);
            }
            else if (Math.Abs(rowOffset) > 1)
            {
                len = Math.Max(len, Block8x8Mi);
            }

            int weight = 2;
            if (blockWidthMi >= Block8x8Mi && blockWidthMi <= candidateWidthMi)
            {
                int inc = Math.Min(-maxRowOffset + rowOffset + 1, candidateHeightMi);
                weight = Math.Max(weight, inc);
                processedRows = inc - rowOffset - 1;
            }

            stack.Add(candidate, len * weight);
            i += len;
        }
    }

    private static void ScanCol(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int modeInfoRow,
        int modeInfoColumn,
        int colOffset,
        int blockHeightMi,
        int maxColOffset,
        ref CandidateStack stack,
        ref int processedCols)
    {
        int endMi = Math.Min(blockHeightMi, tileInfo.ModeInfoRowEnd - modeInfoRow);
        endMi = Math.Min(endMi, Block64x64Mi);
        int rowOffset = 0;
        if (Math.Abs(colOffset) > 1)
        {
            rowOffset = 1;
            if (((modeInfoRow & 1) != 0) && blockHeightMi < Block8x8Mi)
            {
                rowOffset--;
            }
        }

        bool useStep16 = blockHeightMi >= Block64x64Mi;
        int col = modeInfoColumn + colOffset;

        for (int i = 0; i < endMi;)
        {
            int candidateRow = modeInfoRow + rowOffset + i;
            Av1BlockModeInfo? candidate = lookupMi(new Point(col, candidateRow));
            if (candidate == null)
            {
                i++;
                continue;
            }

            int candidateWidthMi = candidate.BlockSize.Get4x4WideCount();
            int candidateHeightMi = candidate.BlockSize.Get4x4HighCount();
            int len = Math.Min(blockHeightMi, candidateHeightMi);
            if (useStep16)
            {
                len = Math.Max(Block16x16Mi, len);
            }
            else if (Math.Abs(colOffset) > 1)
            {
                len = Math.Max(len, Block8x8Mi);
            }

            int weight = 2;
            if (blockHeightMi >= Block8x8Mi && blockHeightMi <= candidateHeightMi)
            {
                int inc = Math.Min(-maxColOffset + colOffset + 1, candidateWidthMi);
                weight = Math.Max(weight, inc);
                processedCols = inc - colOffset - 1;
            }

            stack.Add(candidate, len * weight);
            i += len;
        }
    }

    private static void ScanBlock(
        MiLookup lookupMi,
        Av1TileInfo tileInfo,
        int modeInfoRow,
        int modeInfoColumn,
        int rowOffset,
        int colOffset,
        ref CandidateStack stack)
    {
        if (!IsInside(tileInfo, modeInfoRow, modeInfoColumn, rowOffset, colOffset))
        {
            return;
        }

        Av1BlockModeInfo? candidate = lookupMi(new Point(modeInfoColumn + colOffset, modeInfoRow + rowOffset));
        if (candidate == null)
        {
            return;
        }

        stack.Add(candidate, 2 * Block8x8Mi);
    }

    private static bool HasTopRight(int superblockMib, int modeInfoRow, int modeInfoColumn, int blockWidthMi, int blockHeightMi, int blockSizeMi)
    {
        int maskRow = modeInfoRow & (superblockMib - 1);
        int maskCol = modeInfoColumn & (superblockMib - 1);
        if (blockSizeMi > Block64x64Mi)
        {
            return false;
        }

        bool hasTr = !(((maskRow & blockSizeMi) != 0) && ((maskCol & blockSizeMi) != 0));
        while (blockSizeMi < superblockMib)
        {
            if ((maskCol & blockSizeMi) != 0)
            {
                if (((maskCol & (2 * blockSizeMi)) != 0) && ((maskRow & (2 * blockSizeMi)) != 0))
                {
                    hasTr = false;
                    break;
                }
            }
            else
            {
                break;
            }

            blockSizeMi <<= 1;
        }

        bool isLastVerticalRect = (blockWidthMi < blockHeightMi) && (((modeInfoColumn + blockWidthMi) & (blockHeightMi - 1)) == 0);
        bool isFirstHorizontalRect = (blockWidthMi > blockHeightMi) && ((modeInfoRow & (blockWidthMi - 1)) == 0);

        if (blockWidthMi < blockHeightMi && !isLastVerticalRect)
        {
            hasTr = true;
        }

        if (blockWidthMi > blockHeightMi && !isFirstHorizontalRect)
        {
            hasTr = false;
        }

        return hasTr;
    }

    private static bool IsInside(Av1TileInfo tileInfo, int modeInfoRow, int modeInfoColumn, int rowOffset, int colOffset)
    {
        int r = modeInfoRow + rowOffset;
        int c = modeInfoColumn + colOffset;
        return r >= tileInfo.ModeInfoRowStart && r < tileInfo.ModeInfoRowEnd
            && c >= tileInfo.ModeInfoColumnStart && c < tileInfo.ModeInfoColumnEnd;
    }

    // libaom's integer_mv_precision: round each component to the nearest multiple of 8.
    private static Av1MotionVector RoundToIntegerPel(Av1MotionVector mv)
        => new(RoundToInteger(mv.Row), RoundToInteger(mv.Col));

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

    /// <summary>
    /// Bounded candidate stack backed by caller-supplied spans. Owns dedup and
    /// per-range bubble-sort so the scan helpers don't have to thread three
    /// parallel parameters (entries, weights, count) through every signature.
    /// </summary>
    private ref struct CandidateStack
    {
        private readonly Span<Av1MotionVector> entries;
        private readonly Span<int> weights;
        private int count;

        public CandidateStack(Span<Av1MotionVector> entries, Span<int> weights)
        {
            this.entries = entries;
            this.weights = weights;
            this.count = 0;
        }

        public readonly int Count => this.count;

        public readonly Av1MotionVector this[int index] => this.entries[index];

        public readonly void BoostNearestRange(int nearestCount, int boost)
        {
            for (int i = 0; i < nearestCount; i++)
            {
                this.weights[i] += boost;
            }
        }

        public void Add(Av1BlockModeInfo candidate, int weight)
        {
            if (!candidate.UseIntraBlockCopy)
            {
                return;
            }

            Av1MotionVector mv = candidate.DisplacementVector;
            for (int i = 0; i < this.count; i++)
            {
                if (this.entries[i] == mv)
                {
                    this.weights[i] += weight;
                    return;
                }
            }

            if (this.count < this.entries.Length)
            {
                this.entries[this.count] = mv;
                this.weights[this.count] = weight;
                this.count++;
            }
        }

        // Bubble-sort by weight, descending — matches libaom's setup_ref_mv_list reordering.
        // libaom sorts the [0, nearest_refmv_count) and [nearest_refmv_count, refmv_count)
        // ranges separately so that nearest candidates always precede outer ones.
        public readonly void SortRange(int start, int end)
        {
            int len = end;
            while (len > start)
            {
                int newLen = start;
                for (int idx = start + 1; idx < len; idx++)
                {
                    if (this.weights[idx - 1] < this.weights[idx])
                    {
                        (this.entries[idx - 1], this.entries[idx]) = (this.entries[idx], this.entries[idx - 1]);
                        (this.weights[idx - 1], this.weights[idx]) = (this.weights[idx], this.weights[idx - 1]);
                        newLen = idx;
                    }
                }

                len = newLen;
            }
        }
    }
}
