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
internal static class Av1LoopFilterDecoder
{
    private const int MiSize = 4;
    private const int MiSizeLog2 = Av1Constants.ModeInfoSizeLog2;

    // Maps tx_size dim_log2 (0..4 for 4x4 .. 64x64) to outer-edge filter length for luma.
    private static readonly byte[] LumaFilterLengthByDimensionLog2 = [4, 8, 14, 14, 14];

    public static void DecodeFrame(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
    {
        ObuLoopFilterParameters lfParams = frameHeader.LoopFilterParameters;
        if (lfParams.FilterLevel[0] == 0 && lfParams.FilterLevel[1] == 0
            && lfParams.FilterLevelU == 0 && lfParams.FilterLevelV == 0)
        {
            return;
        }

        FrameContext ctx = FrameContext.Create(sequenceHeader, frameHeader, frameInfo, frameBuffer);

        int superblockSizeLog2 = sequenceHeader.SuperblockSizeLog2;
        int frameWidthInSuperblocks = Av1Math.DivideLog2Ceiling(frameHeader.FrameSize.FrameWidth, superblockSizeLog2);
        int frameHeightInSuperblocks = Av1Math.DivideLog2Ceiling(frameHeader.FrameSize.FrameHeight, superblockSizeLog2);
        int planeCount = sequenceHeader.ColorConfig.IsMonochrome ? 1 : Av1Constants.MaxPlanes;

        PlaneState[] verticalStates = new PlaneState[planeCount];
        PlaneState[] horizontalStates = new PlaneState[planeCount];
        for (int plane = 0; plane < planeCount; plane++)
        {
            verticalStates[plane] = CreatePlaneState(in ctx, plane, Av1EdgeDirection.Vertical);
            horizontalStates[plane] = CreatePlaneState(in ctx, plane, Av1EdgeDirection.Horizontal);
        }

        // Spec 7.14.1: the loop filter is applied on all vertical boundaries followed by all
        // horizontal boundaries. A vertical wide filter at a superblock's left boundary writes
        // samples back into the previous superblock's columns, so interleaving the two passes per
        // superblock (vert+horz before advancing) would let a horizontal pass read columns a later
        // vertical pass has yet to update. We tile the frame-wide passes by superblock-row; within
        // a row every column is filtered vertically, then every column horizontally.
        for (int sbY = 0; sbY < frameHeightInSuperblocks; sbY++)
        {
            int miRow = sbY << ctx.SuperblockMiSizeLog2;
            for (int plane = 0; plane < planeCount; plane++)
            {
                for (int sbX = 0; sbX < frameWidthInSuperblocks; sbX++)
                {
                    FilterSuperblockPlane(in ctx, verticalStates[plane], miRow, sbX << ctx.SuperblockMiSizeLog2);
                }

                for (int sbX = 0; sbX < frameWidthInSuperblocks; sbX++)
                {
                    FilterSuperblockPlane(in ctx, horizontalStates[plane], miRow, sbX << ctx.SuperblockMiSizeLog2);
                }
            }
        }
    }

    private static PlaneState CreatePlaneState(in FrameContext ctx, int plane, Av1EdgeDirection direction)
    {
        bool isChroma = plane > 0;
        int subX = (isChroma && ctx.SequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
        int subY = (isChroma && ctx.SequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;
        GetPlaneBuffer(in ctx, (Av1Plane)plane, subX, subY, out int stride);

        return new PlaneState
        {
            Plane = plane,
            Direction = direction,
            SubX = subX,
            SubY = subY,
            Width = (ctx.FrameHeader.FrameSize.FrameWidth + ((1 << subX) - 1)) >> subX,
            Height = (ctx.FrameHeader.FrameSize.FrameHeight + ((1 << subY) - 1)) >> subY,
            ModeInfoRows = (ctx.FrameHeader.ModeInfoRowCount + ((1 << subY) - 1)) >> subY,
            ModeInfoCols = (ctx.FrameHeader.ModeInfoColumnCount + ((1 << subX) - 1)) >> subX,
            Stride = stride,
            FilterLevel = GetFilterLevel(ctx.FrameHeader.LoopFilterParameters, direction, plane),
        };
    }

    // DeriveBlockPointer returns a span offset one row before the requested location; passing
    // y=1 cancels that out so the returned span starts at the plane origin.
    private static Span<byte> GetPlaneBuffer(in FrameContext ctx, Av1Plane plane, int subX, int subY, out int stride)
        => ctx.FrameBuffer.DeriveBlockPointer(plane, new Point(0, 1), subX, subY, out stride);

    private static Span<byte> GetPlaneBuffer(in FrameContext ctx, PlaneState plane)
        => GetPlaneBuffer(in ctx, (Av1Plane)plane.Plane, plane.SubX, plane.SubY, out _);

    private static void FilterSuperblockPlane(in FrameContext ctx, PlaneState plane, int miRow, int miCol)
    {
        int yRange = Math.Min(plane.ModeInfoRows - (miRow >> plane.SubY), ctx.SuperblockMiSize >> plane.SubY);
        int xRange = Math.Min(plane.ModeInfoCols - (miCol >> plane.SubX), ctx.SuperblockMiSize >> plane.SubX);
        int planeStartX = (miCol * MiSize) >> plane.SubX;
        int planeStartY = (miRow * MiSize) >> plane.SubY;
        Span<byte> buffer = GetPlaneBuffer(in ctx, plane);

        if (plane.Direction == Av1EdgeDirection.Vertical)
        {
            for (int y = 0; y < yRange; y++)
            {
                int currY = planeStartY + (y * MiSize);
                for (int x = 0; x < xRange;)
                {
                    int currX = planeStartX + (x * MiSize);
                    Av1TransformSize tx = FilterEdge(in ctx, plane, buffer, currX, currY);
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
                    Av1TransformSize tx = FilterEdge(in ctx, plane, buffer, currX, currY);
                    y += tx.Get4x4HighCount();
                }
            }
        }
    }

    private static Av1TransformSize FilterEdge(in FrameContext ctx, PlaneState plane, Span<byte> buffer, int currX, int currY)
    {
        (Av1EdgeDeblockingParameters parameters, Av1TransformSize tx) = GetEdgeParameters(in ctx, plane, currX, currY);
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
    private static (Av1EdgeDeblockingParameters Parameters, Av1TransformSize TransformSize) GetEdgeParameters(in FrameContext ctx, PlaneState plane, int x, int y)
    {
        if (plane.Width <= x || plane.Height <= y)
        {
            return (default, Av1TransformSize.Size4x4);
        }

        // Sub-8x8 chroma: align to bottom-right of the co-located 8x8 luma block.
        int miRow = plane.SubY | ((y << plane.SubY) >> MiSizeLog2);
        int miCol = plane.SubX | ((x << plane.SubX) >> MiSizeLog2);

        if (!TryGetModeInfo(in ctx, miRow, miCol, out Av1BlockModeInfo? mbmi))
        {
            return (default, Av1TransformSize.Size4x4);
        }

        Av1TransformSize ts = GetTransformSize(in ctx, plane, mbmi, miRow, miCol);
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
        if (!TryGetModeInfo(in ctx, prevMiRow, prevMiCol, out Av1BlockModeInfo? prevMbmi))
        {
            return (default, ts);
        }

        // Spec section 7.14.2 also requires skipping when both sides have skip_txfm set on a
        // non-PU edge. skip_txfm is only meaningful on inter blocks, so the gating check is
        // omitted for intra-only HEIF streams.
        Av1TransformSize prevTs = GetTransformSize(in ctx, plane, prevMbmi, prevMiRow, prevMiCol);
        int currentLog2 = isVertical ? ts.GetBlockWidthLog2() : ts.GetBlockHeightLog2();
        int prevLog2 = isVertical ? prevTs.GetBlockWidthLog2() : prevTs.GetBlockHeightLog2();
        int dim = Math.Min(currentLog2, prevLog2) - 2;

        int filterLength = plane.Plane != 0
            ? (dim == 0 ? 4 : 6)
            : LumaFilterLengthByDimensionLog2[dim];

        Av1EdgeDeblockingParameters parameters = new((byte)filterLength, ctx.Context.GetThreshold(plane.FilterLevel));
        return (parameters, ts);
    }

    private static bool TryGetModeInfo(in FrameContext ctx, int miRow, int miCol, out Av1BlockModeInfo mbmi)
    {
        if (miRow < 0 || miCol < 0
            || miRow >= ctx.FrameHeader.ModeInfoRowCount
            || miCol >= ctx.FrameHeader.ModeInfoColumnCount)
        {
            mbmi = null!;
            return false;
        }

        int sbX = miCol >> ctx.SuperblockMiSizeLog2;
        int sbY = miRow >> ctx.SuperblockMiSizeLog2;
        int miInSbX = miCol - (sbX << ctx.SuperblockMiSizeLog2);
        int miInSbY = miRow - (sbY << ctx.SuperblockMiSizeLog2);

        Av1BlockModeInfo? result = ctx.FrameInfo.GetModeInfo(new Point(sbX, sbY), new Point(miInSbX, miInSbY));
        mbmi = result!;
        return result is not null;
    }

    private static Av1TransformSize GetTransformSize(in FrameContext ctx, PlaneState plane, Av1BlockModeInfo mbmi, int miRow, int miCol)
    {
        if (ctx.FrameHeader.LosslessArray[mbmi.SegmentId])
        {
            return Av1TransformSize.Size4x4;
        }

        if (plane.Plane != 0)
        {
            return mbmi.BlockSize.GetMaxUvTransformSize(plane.SubX != 0, plane.SubY != 0);
        }

        int sbX = miCol >> ctx.SuperblockMiSizeLog2;
        int sbY = miRow >> ctx.SuperblockMiSizeLog2;
        Av1SuperblockInfo sbInfo = ctx.FrameInfo.GetSuperblock(new Point(sbX, sbY));
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
    private static int GetFilterLevel(ObuLoopFilterParameters lfParams, Av1EdgeDirection direction, int plane)
    {
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

    private readonly record struct FrameContext(
        ObuSequenceHeader SequenceHeader,
        ObuFrameHeader FrameHeader,
        Av1FrameInfo FrameInfo,
        Av1FrameBuffer<byte> FrameBuffer,
        Av1LoopFilterContext Context,
        int SuperblockMiSize,
        int SuperblockMiSizeLog2)
    {
        public static FrameContext Create(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
        {
            int superblockMiSizeLog2 = sequenceHeader.SuperblockSizeLog2 - MiSizeLog2;
            return new FrameContext(
                sequenceHeader,
                frameHeader,
                frameInfo,
                frameBuffer,
                new Av1LoopFilterContext(frameHeader.LoopFilterParameters),
                1 << superblockMiSizeLog2,
                superblockMiSizeLog2);
        }
    }

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
