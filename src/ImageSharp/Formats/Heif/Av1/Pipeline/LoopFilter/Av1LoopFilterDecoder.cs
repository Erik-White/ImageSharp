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
internal sealed class Av1LoopFilterDecoder
{
    private const int MiSize = 4;
    private const int MiSizeLog2 = Av1Constants.ModeInfoSizeLog2;

    // Maps tx_size dim_log2 (0..4 for 4x4 .. 64x64) to outer-edge filter length for luma.
    private static readonly byte[] LumaFilterLengthByDimensionLog2 = [4, 8, 14, 14, 14];

    private readonly ObuSequenceHeader sequenceHeader;
    private readonly ObuFrameHeader frameHeader;
    private readonly Av1FrameInfo frameInfo;
    private readonly Av1FrameBuffer<byte> frameBuffer;
    private readonly Av1LoopFilterContext context;
    private readonly int superblockMiSize;
    private readonly int superblockMiSizeLog2;

    public Av1LoopFilterDecoder(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
    {
        this.sequenceHeader = sequenceHeader;
        this.frameHeader = frameHeader;
        this.frameInfo = frameInfo;
        this.frameBuffer = frameBuffer;
        this.context = new Av1LoopFilterContext(frameHeader.LoopFilterParameters);
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

        int superblockSizeLog2 = this.sequenceHeader.SuperblockSizeLog2;
        int frameWidthInSuperblocks = Av1Math.DivideLog2Ceiling(this.frameHeader.FrameSize.FrameWidth, superblockSizeLog2);
        int frameHeightInSuperblocks = Av1Math.DivideLog2Ceiling(this.frameHeader.FrameSize.FrameHeight, superblockSizeLog2);
        int planeCount = this.sequenceHeader.ColorConfig.IsMonochrome ? 1 : Av1Constants.MaxPlanes;

        PlaneState[] verticalStates = new PlaneState[planeCount];
        PlaneState[] horizontalStates = new PlaneState[planeCount];
        for (int plane = 0; plane < planeCount; plane++)
        {
            verticalStates[plane] = this.CreatePlaneState(plane, Av1EdgeDirection.Vertical);
            horizontalStates[plane] = this.CreatePlaneState(plane, Av1EdgeDirection.Horizontal);
        }

        // Within each superblock all planes are filtered vertically, then all planes are filtered
        // horizontally. Wide filters touch pixels in the previous superblock, so this per-SB
        // ordering — rather than per-plane-then-per-SB — is observable at superblock boundaries.
        for (int sbY = 0; sbY < frameHeightInSuperblocks; sbY++)
        {
            for (int sbX = 0; sbX < frameWidthInSuperblocks; sbX++)
            {
                int miRow = sbY << this.superblockMiSizeLog2;
                int miCol = sbX << this.superblockMiSizeLog2;

                for (int plane = 0; plane < planeCount; plane++)
                {
                    this.FilterSuperblockPlane(verticalStates[plane], miRow, miCol);
                }

                for (int plane = 0; plane < planeCount; plane++)
                {
                    this.FilterSuperblockPlane(horizontalStates[plane], miRow, miCol);
                }
            }
        }
    }

    private PlaneState CreatePlaneState(int plane, Av1EdgeDirection direction)
    {
        bool isChroma = plane > 0;
        int subX = (isChroma && this.sequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
        int subY = (isChroma && this.sequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;
        this.GetPlaneBuffer((Av1Plane)plane, subX, subY, out int stride);

        return new PlaneState
        {
            Plane = plane,
            Direction = direction,
            SubX = subX,
            SubY = subY,
            Width = (this.frameHeader.FrameSize.FrameWidth + ((1 << subX) - 1)) >> subX,
            Height = (this.frameHeader.FrameSize.FrameHeight + ((1 << subY) - 1)) >> subY,
            ModeInfoRows = (this.frameHeader.ModeInfoRowCount + ((1 << subY) - 1)) >> subY,
            ModeInfoCols = (this.frameHeader.ModeInfoColumnCount + ((1 << subX) - 1)) >> subX,
            Stride = stride,
            FilterLevel = this.GetFilterLevel(direction, plane),
        };
    }

    // DeriveBlockPointer returns a span offset one row before the requested location; passing
    // y=1 cancels that out so the returned span starts at the plane origin.
    private Span<byte> GetPlaneBuffer(Av1Plane plane, int subX, int subY, out int stride)
        => this.frameBuffer.DeriveBlockPointer(plane, new Point(0, 1), subX, subY, out stride);

    private Span<byte> GetPlaneBuffer(PlaneState plane)
        => this.GetPlaneBuffer((Av1Plane)plane.Plane, plane.SubX, plane.SubY, out _);

    private void FilterSuperblockPlane(PlaneState plane, int miRow, int miCol)
    {
        int yRange = Math.Min(plane.ModeInfoRows - (miRow >> plane.SubY), this.superblockMiSize >> plane.SubY);
        int xRange = Math.Min(plane.ModeInfoCols - (miCol >> plane.SubX), this.superblockMiSize >> plane.SubX);
        int planeStartX = (miCol * MiSize) >> plane.SubX;
        int planeStartY = (miRow * MiSize) >> plane.SubY;
        Span<byte> buffer = this.GetPlaneBuffer(plane);

        if (plane.Direction == Av1EdgeDirection.Vertical)
        {
            for (int y = 0; y < yRange; y++)
            {
                int currY = planeStartY + (y * MiSize);
                for (int x = 0; x < xRange;)
                {
                    int currX = planeStartX + (x * MiSize);
                    Av1TransformSize tx = this.FilterEdge(plane, buffer, currX, currY);
                    x += tx.Get4x4WideCount();
                }
            }
        }
        else
        {
            for (int x = 0; x < xRange; x++)
            {
                int currX = planeStartX + (x * MiSize);
                for (int y = 0; y < yRange;)
                {
                    int currY = planeStartY + (y * MiSize);
                    Av1TransformSize tx = this.FilterEdge(plane, buffer, currX, currY);
                    y += tx.Get4x4HighCount();
                }
            }
        }
    }

    private Av1TransformSize FilterEdge(PlaneState plane, Span<byte> buffer, int currX, int currY)
    {
        (Av1EdgeDeblockingParameters parameters, Av1TransformSize tx) = this.GetEdgeParameters(plane, currX, currY);
        if (parameters.FilterLength != 0)
        {
            int offset = (currY * plane.Stride) + currX;
            ApplyFilter(buffer, offset, plane.Stride, plane.Direction, parameters);
        }

        return tx;
    }

    private static void ApplyFilter(Span<byte> buffer, int offset, int stride, Av1EdgeDirection direction, Av1EdgeDeblockingParameters parameters)
    {
        Av1LoopFilterThreshold t = parameters.Threshold;
        if (direction == Av1EdgeDirection.Vertical)
        {
            switch (parameters.FilterLength)
            {
                case 4: Av1LoopFilterPrimitives.LpfVertical4(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 6: Av1LoopFilterPrimitives.LpfVertical6(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 8: Av1LoopFilterPrimitives.LpfVertical8(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 14: Av1LoopFilterPrimitives.LpfVertical14(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
            }
        }
        else
        {
            switch (parameters.FilterLength)
            {
                case 4: Av1LoopFilterPrimitives.LpfHorizontal4(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 6: Av1LoopFilterPrimitives.LpfHorizontal6(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 8: Av1LoopFilterPrimitives.LpfHorizontal8(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
                case 14: Av1LoopFilterPrimitives.LpfHorizontal14(buffer, offset, stride, t.MbLimit, t.Limit, t.HevThreshold); break;
            }
        }
    }

    /// <summary>
    /// Computes the deblocking parameters for the edge crossing pixel (x, y) on the given plane.
    /// Implements section 7.14.2 (edge loop filter process) and 7.14.3 (filter size process) of the
    /// AV1 specification.
    /// </summary>
    private (Av1EdgeDeblockingParameters Parameters, Av1TransformSize TransformSize) GetEdgeParameters(PlaneState plane, int x, int y)
    {
        if (plane.Width <= x || plane.Height <= y)
        {
            return (default, Av1TransformSize.Size4x4);
        }

        // Sub-8x8 chroma: align to bottom-right of the co-located 8x8 luma block.
        int miRow = plane.SubY | ((y << plane.SubY) >> MiSizeLog2);
        int miCol = plane.SubX | ((x << plane.SubX) >> MiSizeLog2);

        if (!this.TryGetModeInfo(miRow, miCol, out Av1BlockModeInfo? mbmi))
        {
            return (default, Av1TransformSize.Size4x4);
        }

        Av1TransformSize ts = this.GetTransformSize(plane, mbmi, miRow, miCol);
        bool isVertical = plane.Direction == Av1EdgeDirection.Vertical;
        int coord = isVertical ? x : y;
        int dimMask = (isVertical ? ts.GetWidth() : ts.GetHeight()) - 1;

        // Filtered edges fall on transform-unit boundaries except the leading frame edge.
        if (coord == 0 || (coord & dimMask) != 0 || plane.FilterLevel == 0)
        {
            return (default, ts);
        }

        int prevMiRow = isVertical ? miRow : miRow - (1 << plane.SubY);
        int prevMiCol = isVertical ? miCol - (1 << plane.SubX) : miCol;
        if (!this.TryGetModeInfo(prevMiRow, prevMiCol, out Av1BlockModeInfo? prevMbmi))
        {
            return (default, ts);
        }

        // Spec section 7.14.2 also requires skipping when both sides have skip_txfm set on a
        // non-PU edge. skip_txfm is only meaningful on inter blocks, so the gating check is
        // omitted for intra-only HEIF streams.
        Av1TransformSize prevTs = this.GetTransformSize(plane, prevMbmi, prevMiRow, prevMiCol);
        int currentLog2 = isVertical ? ts.GetBlockWidthLog2() : ts.GetBlockHeightLog2();
        int prevLog2 = isVertical ? prevTs.GetBlockWidthLog2() : prevTs.GetBlockHeightLog2();
        int dim = Math.Min(currentLog2, prevLog2) - 2;

        int filterLength = plane.Plane != 0
            ? (dim == 0 ? 4 : 6)
            : LumaFilterLengthByDimensionLog2[dim];

        Av1EdgeDeblockingParameters parameters = new((byte)filterLength, this.context.GetThreshold(plane.FilterLevel));
        return (parameters, ts);
    }

    private bool TryGetModeInfo(int miRow, int miCol, out Av1BlockModeInfo mbmi)
    {
        if (miRow < 0 || miCol < 0
            || miRow >= this.frameHeader.ModeInfoRowCount
            || miCol >= this.frameHeader.ModeInfoColumnCount)
        {
            mbmi = null!;
            return false;
        }

        int sbX = miCol >> this.superblockMiSizeLog2;
        int sbY = miRow >> this.superblockMiSizeLog2;
        int miInSbX = miCol - (sbX << this.superblockMiSizeLog2);
        int miInSbY = miRow - (sbY << this.superblockMiSizeLog2);

        Av1BlockModeInfo? result = this.frameInfo.GetModeInfo(new Point(sbX, sbY), new Point(miInSbX, miInSbY));
        mbmi = result!;
        return result is not null;
    }

    private Av1TransformSize GetTransformSize(PlaneState plane, Av1BlockModeInfo mbmi, int miRow, int miCol)
    {
        if (this.frameHeader.LosslessArray[mbmi.SegmentId])
        {
            return Av1TransformSize.Size4x4;
        }

        if (plane.Plane != 0)
        {
            return mbmi.BlockSize.GetMaxUvTransformSize(plane.SubX != 0, plane.SubY != 0);
        }

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

    /// <summary>
    /// Computes the per-block filter level. Implements the adaptive filter strength selection
    /// process of section 7.14.5 of the AV1 specification, restricted to intra-only streams: the
    /// segmentation feature path is omitted, and only the INTRA_FRAME ref delta is applied (the
    /// mode delta is always zero because <c>modeType</c> is 0 for intra modes per section 7.14.4).
    /// </summary>
    private int GetFilterLevel(Av1EdgeDirection direction, int plane)
    {
        ObuLoopFilterParameters lfParams = this.frameHeader.LoopFilterParameters;
        int baseLevel = plane switch
        {
            0 => lfParams.FilterLevel[(int)direction],
            1 => lfParams.FilterLevelU,
            _ => lfParams.FilterLevelV,
        };

        if (!lfParams.ReferenceDeltaModeEnabled)
        {
            return baseLevel;
        }

        int scale = 1 << (baseLevel >> 5);
        int level = baseLevel + (lfParams.ReferenceDeltas[0] * scale);
        return Math.Clamp(level, 0, Av1LoopFilterContext.MaxLoopFilter);
    }

    private readonly record struct Av1EdgeDeblockingParameters(byte FilterLength, Av1LoopFilterThreshold Threshold);

    private readonly record struct PlaneState
    {
        public int Plane { get; init; }

        public Av1EdgeDirection Direction { get; init; }

        public int SubX { get; init; }

        public int SubY { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public int ModeInfoRows { get; init; }

        public int ModeInfoCols { get; init; }

        public int Stride { get; init; }

        public int FilterLevel { get; init; }
    }
}
