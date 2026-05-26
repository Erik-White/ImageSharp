// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// AV1 in-loop deblocking filter. Implements section 7.14 of the AV1 specification:
/// vertical boundaries for all planes are filtered first, then horizontal boundaries.
/// </summary>
internal class Av1LoopFilterDecoder
{
    private const int MiSize = 4;
    private const int MiSizeLog2 = Av1Constants.ModeInfoSizeLog2;

    private const int VertEdge = 0;
    private const int HorzEdge = 1;

    // Maps tx_size dim_log2 (0..4 for 4x4 .. 64x64) to outer-edge filter length for luma.
    private static readonly int[] TransformDimensionToFilterLength = [4, 8, 14, 14, 14];

    private readonly ObuSequenceHeader sequenceHeader;
    private readonly ObuFrameHeader frameHeader;
    private readonly Av1FrameInfo frameInfo;
    private readonly Av1FrameBuffer<byte> frameBuffer;
    private readonly Av1LoopFilterContext loopFilterContext;
    private readonly int superblockMiSize;
    private readonly int superblockMiSizeLog2;

    public Av1LoopFilterDecoder(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
    {
        this.sequenceHeader = sequenceHeader;
        this.frameHeader = frameHeader;
        this.frameInfo = frameInfo;
        this.frameBuffer = frameBuffer;
        this.loopFilterContext = new();
        this.superblockMiSizeLog2 = sequenceHeader.SuperblockSizeLog2 - MiSizeLog2;
        this.superblockMiSize = 1 << this.superblockMiSizeLog2;
    }

    public void DecodeFrame()
    {
        ObuLoopFilterParameters lfParams = this.frameHeader.LoopFilterParameters;
        if (lfParams.FilterLevel[0] == 0 && lfParams.FilterLevel[1] == 0
            && lfParams.FilterLevelU == 0 && lfParams.FilterLevelV == 0)
        {
            return;
        }

        this.loopFilterContext.Initialize(lfParams);

        int superblockSizeLog2 = this.sequenceHeader.SuperblockSizeLog2;
        int frameWidthInSuperblocks = Av1Math.DivideLog2Ceiling(this.frameHeader.FrameSize.FrameWidth, superblockSizeLog2);
        int frameHeightInSuperblocks = Av1Math.DivideLog2Ceiling(this.frameHeader.FrameSize.FrameHeight, superblockSizeLog2);

        int planeCount = this.sequenceHeader.ColorConfig.IsMonochrome ? 1 : Av1Constants.MaxPlanes;

        for (int sbY = 0; sbY < frameHeightInSuperblocks; sbY++)
        {
            for (int sbX = 0; sbX < frameWidthInSuperblocks; sbX++)
            {
                int miRow = sbY << this.superblockMiSizeLog2;
                int miCol = sbX << this.superblockMiSizeLog2;

                for (int plane = 0; plane < planeCount; plane++)
                {
                    this.FilterSuperblockPlaneVertical(plane, miRow, miCol);
                }

                for (int plane = 0; plane < planeCount; plane++)
                {
                    this.FilterSuperblockPlaneHorizontal(plane, miRow, miCol);
                }
            }
        }
    }

    private void FilterSuperblockPlaneVertical(int plane, int miRow, int miCol)
    {
        int subX = (plane > 0 && this.sequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
        int subY = (plane > 0 && this.sequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;

        int planeWidth = (this.frameHeader.FrameSize.FrameWidth + ((1 << subX) - 1)) >> subX;
        int planeHeight = (this.frameHeader.FrameSize.FrameHeight + ((1 << subY) - 1)) >> subY;

        int planeMiRows = (this.frameHeader.ModeInfoRowCount + ((1 << subY) - 1)) >> subY;
        int planeMiCols = (this.frameHeader.ModeInfoColumnCount + ((1 << subX) - 1)) >> subX;
        int yRange = Math.Min(planeMiRows - (miRow >> subY), this.superblockMiSize >> subY);
        int xRange = Math.Min(planeMiCols - (miCol >> subX), this.superblockMiSize >> subX);

        Span<byte> dst = this.GetPlaneBuffer((Av1Plane)plane, subX, subY, out int stride);

        for (int y = 0; y < yRange; y++)
        {
            int rowBase = ((miRow * MiSize) >> subY) + (y * MiSize);
            for (int x = 0; x < xRange;)
            {
                int currX = ((miCol * MiSize) >> subX) + (x * MiSize);
                int currY = rowBase;

                Av1TransformSize tx = this.SetLpfParameters(out Av1DeblockingParameters parameters, VertEdge, currX, currY, plane, subX, subY, planeWidth, planeHeight);
                if (tx == Av1TransformSize.Invalid)
                {
                    parameters.FilterLength = 0;
                    tx = Av1TransformSize.Size4x4;
                }

                if (parameters.FilterLength != 0)
                {
                    int pixelOffset = ((rowBase + 1) * stride) + currX;
                    ApplyVerticalFilter(dst, pixelOffset, stride, parameters);
                }

                int advanceUnits = tx.Get4x4WideCount();
                x += advanceUnits;
            }
        }
    }

    private void FilterSuperblockPlaneHorizontal(int plane, int miRow, int miCol)
    {
        int subX = (plane > 0 && this.sequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
        int subY = (plane > 0 && this.sequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;

        int planeWidth = (this.frameHeader.FrameSize.FrameWidth + ((1 << subX) - 1)) >> subX;
        int planeHeight = (this.frameHeader.FrameSize.FrameHeight + ((1 << subY) - 1)) >> subY;

        int planeMiRows = (this.frameHeader.ModeInfoRowCount + ((1 << subY) - 1)) >> subY;
        int planeMiCols = (this.frameHeader.ModeInfoColumnCount + ((1 << subX) - 1)) >> subX;
        int yRange = Math.Min(planeMiRows - (miRow >> subY), this.superblockMiSize >> subY);
        int xRange = Math.Min(planeMiCols - (miCol >> subX), this.superblockMiSize >> subX);

        Span<byte> dst = this.GetPlaneBuffer((Av1Plane)plane, subX, subY, out int stride);

        for (int x = 0; x < xRange; x++)
        {
            int colBase = ((miCol * MiSize) >> subX) + (x * MiSize);
            for (int y = 0; y < yRange;)
            {
                int currX = colBase;
                int currY = ((miRow * MiSize) >> subY) + (y * MiSize);

                Av1TransformSize tx = this.SetLpfParameters(out Av1DeblockingParameters parameters, HorzEdge, currX, currY, plane, subX, subY, planeWidth, planeHeight);
                if (tx == Av1TransformSize.Invalid)
                {
                    parameters.FilterLength = 0;
                    tx = Av1TransformSize.Size4x4;
                }

                if (parameters.FilterLength != 0)
                {
                    int pixelOffset = ((currY + 1) * stride) + colBase;
                    ApplyHorizontalFilter(dst, pixelOffset, stride, parameters);
                }

                int advanceUnits = tx.Get4x4HighCount();
                y += advanceUnits;
            }
        }
    }

    private static void ApplyVerticalFilter(Span<byte> dst, int offset, int stride, in Av1DeblockingParameters parameters)
    {
        Av1LoopFilterThreshold lfthr = parameters.Threshold!;
        switch (parameters.FilterLength)
        {
            case 4:
                Av1LoopFilterPrimitives.LpfVertical4(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 6:
                Av1LoopFilterPrimitives.LpfVertical6(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 8:
                Av1LoopFilterPrimitives.LpfVertical8(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 14:
                Av1LoopFilterPrimitives.LpfVertical14(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
        }
    }

    private static void ApplyHorizontalFilter(Span<byte> dst, int offset, int stride, in Av1DeblockingParameters parameters)
    {
        Av1LoopFilterThreshold lfthr = parameters.Threshold!;
        switch (parameters.FilterLength)
        {
            case 4:
                Av1LoopFilterPrimitives.LpfHorizontal4(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 6:
                Av1LoopFilterPrimitives.LpfHorizontal6(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 8:
                Av1LoopFilterPrimitives.LpfHorizontal8(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
            case 14:
                Av1LoopFilterPrimitives.LpfHorizontal14(dst, offset, stride, lfthr.MbLimit, lfthr.Limit, lfthr.HevThreshold);
                break;
        }
    }

    /// <summary>
    /// Computes the deblocking parameters for the edge crossing pixel (x, y) on the given plane.
    /// Implements section 7.14.2 (edge loop filter process) and 7.14.3 (filter size process) of the
    /// AV1 specification.
    /// </summary>
    private Av1TransformSize SetLpfParameters(out Av1DeblockingParameters parameters, int edgeDir, int x, int y, int plane, int subX, int subY, int planeWidth, int planeHeight)
    {
        parameters = default;

        if (planeWidth <= x || planeHeight <= y)
        {
            return Av1TransformSize.Size4x4;
        }

        // Sub-8x8 chroma: align to bottom-right of the co-located 8x8 luma block.
        int miRow = subY | ((y << subY) >> MiSizeLog2);
        int miCol = subX | ((x << subX) >> MiSizeLog2);

        if (!this.TryGetModeInfo(miRow, miCol, out Av1BlockModeInfo? mbmi) || mbmi is null)
        {
            return Av1TransformSize.Invalid;
        }

        Av1TransformSize ts = this.GetTransformSize(mbmi, miRow, miCol, plane, subX, subY);

        int coord = (edgeDir == VertEdge) ? x : y;
        int transformMask = (edgeDir == VertEdge) ? ts.GetWidth() - 1 : ts.GetHeight() - 1;
        bool tuEdge = (coord & transformMask) == 0;

        if (!tuEdge)
        {
            return ts;
        }

        if (coord == 0)
        {
            return ts;
        }

        // Previous MI position across the edge.
        int prevMiRow = (edgeDir == VertEdge) ? miRow : miRow - (1 << subY);
        int prevMiCol = (edgeDir == VertEdge) ? miCol - (1 << subX) : miCol;
        if (!this.TryGetModeInfo(prevMiRow, prevMiCol, out Av1BlockModeInfo? prevMbmi) || prevMbmi is null)
        {
            return Av1TransformSize.Invalid;
        }

        Av1TransformSize prevTs = this.GetTransformSize(prevMbmi, prevMiRow, prevMiCol, plane, subX, subY);

        // Spec section 7.14.2 also requires skipping the filter for non-PU edges where both
        // sides have skip_txfm set. skip_txfm is only meaningful on inter blocks, so for
        // intra-only HEIF streams that condition is never met and the gating check is omitted.
        int level = this.GetFilterLevel(edgeDir, plane);

        if (level != 0)
        {
            int dim = (edgeDir == VertEdge)
                ? Math.Min(ts.GetBlockWidthLog2() - 2, prevTs.GetBlockWidthLog2() - 2)
                : Math.Min(ts.GetBlockHeightLog2() - 2, prevTs.GetBlockHeightLog2() - 2);

            int filterLength = (plane != 0)
                ? ((dim == 0) ? 4 : 6)
                : TransformDimensionToFilterLength[dim];

            parameters.FilterLength = (byte)filterLength;
            parameters.Threshold = this.loopFilterContext.GetThreshold(level);
        }

        return ts;
    }

    private bool TryGetModeInfo(int miRow, int miCol, out Av1BlockModeInfo? mbmi)
    {
        mbmi = null;
        if (miRow < 0 || miCol < 0)
        {
            return false;
        }

        if (miRow >= this.frameHeader.ModeInfoRowCount || miCol >= this.frameHeader.ModeInfoColumnCount)
        {
            return false;
        }

        int sbX = miCol >> this.superblockMiSizeLog2;
        int sbY = miRow >> this.superblockMiSizeLog2;
        int miInSbX = miCol - (sbX << this.superblockMiSizeLog2);
        int miInSbY = miRow - (sbY << this.superblockMiSizeLog2);

        mbmi = this.frameInfo.GetModeInfo(new Point(sbX, sbY), new Point(miInSbX, miInSbY));
        return mbmi != null;
    }

    private Av1TransformSize GetTransformSize(Av1BlockModeInfo mbmi, int miRow, int miCol, int plane, int subX, int subY)
    {
        if (this.frameHeader.LosslessArray[mbmi.SegmentId])
        {
            return Av1TransformSize.Size4x4;
        }

        if (plane == 0)
        {
            int sbX = miCol >> this.superblockMiSizeLog2;
            int sbY = miRow >> this.superblockMiSizeLog2;
            Av1SuperblockInfo sbInfo = this.frameInfo.GetSuperblock(new Point(sbX, sbY));
            Span<Av1TransformInfo> txY = sbInfo.GetTransformInfoY();
            int idx = mbmi.FirstTransformLocation[(int)Av1PlaneType.Y];
            if (idx < txY.Length && txY[idx] != null)
            {
                return txY[idx].Size;
            }

            return mbmi.BlockSize.GetMaximumTransformSize();
        }

        return mbmi.BlockSize.GetMaxUvTransformSize(subX != 0, subY != 0);
    }

    /// <summary>
    /// Computes the per-block filter level. Implements the adaptive filter strength selection
    /// process of section 7.14.5 of the AV1 specification, restricted to intra-only streams: the
    /// segmentation feature path is omitted, and only the INTRA_FRAME ref delta is applied (the
    /// mode delta is always zero because <c>modeType</c> is 0 for intra modes per section 7.14.4).
    /// </summary>
    private int GetFilterLevel(int edgeDir, int plane)
    {
        ObuLoopFilterParameters lfParams = this.frameHeader.LoopFilterParameters;
        int baseLevel;
        if (plane == 0)
        {
            baseLevel = lfParams.FilterLevel[edgeDir];
        }
        else if (plane == 1)
        {
            baseLevel = lfParams.FilterLevelU;
        }
        else
        {
            baseLevel = lfParams.FilterLevelV;
        }

        int level = baseLevel;

        if (lfParams.ReferenceDeltaModeEnabled)
        {
            int scale = 1 << (level >> 5);
            level += lfParams.ReferenceDeltas[0] * scale;
            level = Math.Clamp(level, 0, Av1LoopFilterContext.MaxLoopFilter);
        }

        return level;
    }

    private Span<byte> GetPlaneBuffer(Av1Plane plane, int subX, int subY, out int stride)
        => this.frameBuffer.DeriveBlockPointer(plane, new Point(0, 0), subX, subY, out stride);

    private struct Av1DeblockingParameters
    {
        public byte FilterLength;
        public Av1LoopFilterThreshold? Threshold;
    }
}
