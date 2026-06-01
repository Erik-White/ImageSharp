// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Spec 7.17 loop restoration apply. Runs after CDEF: for each 4x4 block whose plane has a
/// non-NONE restoration type, applies the per-unit Wiener (7.17.4) or self-guided (7.17.2)
/// filter, reading in-stripe samples from the post-CDEF frame and out-of-stripe samples from
/// the pre-CDEF (deblocked) snapshot, and writing into a separate output that is copied back.
/// </summary>
internal static class Av1LoopRestorationDecoder
{
    private const int RestorationUnitOffset = 8;
    private const int FilterBits = Av1RestorationConstants.WienerFilterPrecisionBits;

    public static void DecodeFrame(
        Configuration configuration,
        ObuSequenceHeader sequenceHeader,
        ObuFrameHeader frameHeader,
        Av1FrameInfo frameInfo,
        Av1FrameBuffer<byte> frameBuffer,
        byte[][] preCdefPlanes)
    {
        Av1LoopRestorationGrid? grid = frameInfo.LoopRestorationGrid;
        if (grid == null || !frameHeader.LoopRestorationParameters.UsesLoopRestoration)
        {
            return;
        }

        ObuLoopRestorationParameters lr = frameHeader.LoopRestorationParameters;
        int planeCount = sequenceHeader.ColorConfig.IsMonochrome ? 1 : 3;
        bool subX = sequenceHeader.ColorConfig.SubSamplingX;
        bool subY = sequenceHeader.ColorConfig.SubSamplingY;
        int frameWidth = frameHeader.FrameSize.FrameWidth;
        int frameHeight = frameHeader.FrameSize.FrameHeight;

        for (int plane = 0; plane < planeCount; plane++)
        {
            if (lr.Items[plane].Type == ObuRestorationType.None)
            {
                continue;
            }

            int planeSubX = plane > 0 && subX ? 1 : 0;
            int planeSubY = plane > 0 && subY ? 1 : 0;
            int planeWidth = Av1Math.RoundPowerOf2(frameWidth, planeSubX);
            int planeHeight = Av1Math.RoundPowerOf2(frameHeight, planeSubY);
            int unitSize = lr.Items[plane].Size;
            int unitRows = CountUnitsInFrame(unitSize, planeHeight);
            int unitCols = CountUnitsInFrame(unitSize, planeWidth);

            Buffer2D<byte> destination = plane switch
            {
                0 => frameBuffer.BufferY!,
                1 => frameBuffer.BufferCb!,
                _ => frameBuffer.BufferCr!,
            };
            int originX = frameBuffer.OriginX >> planeSubX;
            int originY = frameBuffer.OriginY >> planeSubY;
            PlaneView view = new(destination, preCdefPlanes[plane], planeWidth, planeHeight, originX, originY);

            ApplyPlane(configuration, grid, plane, view, planeSubY, unitSize, unitRows, unitCols);
        }
    }

    private static void ApplyPlane(
        Configuration configuration,
        Av1LoopRestorationGrid grid,
        int plane,
        in PlaneView view,
        int planeSubY,
        int unitSize,
        int unitRows,
        int unitCols)
    {
        int planeEndX = view.Width - 1;
        int planeEndY = view.Height - 1;

        // Spec 7.17 drives loop restoration over MI_SIZE (4-luma-sample) blocks; the block
        // dimensions in the current plane are MI_SIZE >> subsampling.
        int modeInfoSize = 1 << Av1Constants.ModeInfoSizeLog2;
        int blockSizeY = modeInfoSize >> planeSubY;
        int blockSizeX = blockSizeY;

        // The destination is written from a snapshot of the post-CDEF samples so that a
        // filtered block never feeds the next block's input (spec: LrFrame is a separate copy).
        using IMemoryOwner<byte> postCdefOwner = SnapshotDestination(configuration, view);
        Span<byte> postCdef = postCdefOwner.Memory.Span;

        Span<int> verticalFilter = stackalloc int[Av1RestorationConstants.WienerWin];
        Span<int> horizontalFilter = stackalloc int[Av1RestorationConstants.WienerWin];

        for (int y = 0; y < view.Height; y += blockSizeY)
        {
            int lumaY = y << planeSubY;
            int stripeNum = (lumaY + RestorationUnitOffset) / 64;
            int stripeStartY = (-RestorationUnitOffset + (stripeNum * 64)) >> planeSubY;
            int stripeEndY = stripeStartY + (64 >> planeSubY) - 1;
            int unitRow = Math.Min(unitRows - 1, ((lumaY + RestorationUnitOffset) >> planeSubY) / unitSize);

            int h = Math.Min(blockSizeY, planeEndY - y + 1);
            for (int x = 0; x < view.Width; x += blockSizeX)
            {
                int unitCol = Math.Min(unitCols - 1, x / unitSize);
                Av1RestorationUnitInfo unit = grid.GetUnit(plane, unitRow, unitCol);
                int w = Math.Min(blockSizeX, planeEndX - x + 1);

                if (unit.RestorationType == ObuRestorationType.Weiner)
                {
                    ExpandWienerCoefficients(unit.WienerInfo.VerticalFilter, verticalFilter);
                    ExpandWienerCoefficients(unit.WienerInfo.HorizontalFilter, horizontalFilter);
                    WienerBlock(in view, postCdef, x, y, w, h, stripeStartY, stripeEndY, planeEndX, planeEndY, verticalFilter, horizontalFilter);
                }
                else if (unit.RestorationType == ObuRestorationType.SgrProj)
                {
                    // Spec 7.17.2 self-guided restoration. Not yet implemented; the block keeps
                    // its post-CDEF samples (which is what RESTORE_NONE would also produce).
                    throw new NotImplementedException("AV1 self-guided (SGR) loop restoration apply is not implemented yet.");
                }
            }
        }
    }

    private static void WienerBlock(
        in PlaneView view,
        ReadOnlySpan<byte> postCdef,
        int x,
        int y,
        int w,
        int h,
        int stripeStartY,
        int stripeEndY,
        int planeEndX,
        int planeEndY,
        ReadOnlySpan<int> verticalFilter,
        ReadOnlySpan<int> horizontalFilter)
    {
        // Spec 7.17.4: 8-bit, non-compound. InterRound0 = 3, InterRound1 = 11, FILTER_BITS = 7.
        const int interRound0 = 3;
        const int interRound1 = 11;
        const int bitDepth = 8;
        int offset = 1 << (bitDepth + FilterBits - interRound0 - 1);
        int limit = (1 << (bitDepth + 1 + FilterBits - interRound0)) - 1;

        // Horizontal pass into an (h + 6) x w intermediate.
        int intermediateRows = h + 6;
        int[] intermediate = new int[intermediateRows * w];
        for (int r = 0; r < intermediateRows; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int s = 0;
                for (int t = 0; t < Av1RestorationConstants.WienerWin; t++)
                {
                    int sample = GetSourceSample(in view, postCdef, x + c + t - 3, y + r - 3, stripeStartY, stripeEndY, planeEndX, planeEndY);
                    s += horizontalFilter[t] * sample;
                }

                int v = Av1Math.RoundPowerOf2(s, interRound0);
                intermediate[(r * w) + c] = Av1Math.Clip3(-offset, limit - offset, v);
            }
        }

        // Vertical pass writes the restored samples.
        for (int r = 0; r < h; r++)
        {
            Span<byte> destinationRow = view.Destination.DangerousGetRowSpan(view.OriginY + y + r);
            for (int c = 0; c < w; c++)
            {
                int s = 0;
                for (int t = 0; t < Av1RestorationConstants.WienerWin; t++)
                {
                    s += verticalFilter[t] * intermediate[((r + t) * w) + c];
                }

                int v = Av1Math.RoundPowerOf2(s, interRound1);
                destinationRow[view.OriginX + x + c] = (byte)Av1Math.Clamp(v, 0, byte.MaxValue);
            }
        }
    }

    /// <summary>
    /// Spec 7.17.6 get_source_sample: clamp to the plane extent, then fetch in-stripe samples
    /// from the post-CDEF copy and out-of-stripe samples from the pre-CDEF (deblocked) plane,
    /// limiting the vertical reach to two lines beyond the stripe.
    /// </summary>
    private static int GetSourceSample(
        in PlaneView view,
        ReadOnlySpan<byte> postCdef,
        int x,
        int y,
        int stripeStartY,
        int stripeEndY,
        int planeEndX,
        int planeEndY)
    {
        x = Math.Min(planeEndX, x);
        x = Math.Max(0, x);
        y = Math.Min(planeEndY, y);
        y = Math.Max(0, y);

        if (y < stripeStartY)
        {
            y = Math.Max(stripeStartY - 2, y);
            return view.PreCdef[(y * view.Width) + x];
        }

        if (y > stripeEndY)
        {
            y = Math.Min(stripeEndY + 2, y);
            return view.PreCdef[(y * view.Width) + x];
        }

        return postCdef[(y * view.Width) + x];
    }

    /// <summary>
    /// Spec 7.17.5: expand 3 signalled side taps into the symmetric 7-tap unit-DC-gain filter.
    /// </summary>
    private static void ExpandWienerCoefficients(ReadOnlySpan<int> stored, Span<int> filter)
    {
        // The parser already mirrors the side taps and derives the centre tap into a 7-element
        // array, so copy through directly.
        for (int i = 0; i < Av1RestorationConstants.WienerWin; i++)
        {
            filter[i] = stored[i];
        }
    }

    private static IMemoryOwner<byte> SnapshotDestination(Configuration configuration, in PlaneView view)
    {
        IMemoryOwner<byte> owner = configuration.MemoryAllocator.Allocate<byte>(view.Width * view.Height, AllocationOptions.None);
        Span<byte> destination = owner.Memory.Span;
        for (int y = 0; y < view.Height; y++)
        {
            view.Destination.DangerousGetRowSpan(view.OriginY + y).Slice(view.OriginX, view.Width).CopyTo(destination[(y * view.Width)..]);
        }

        return owner;
    }

    private static int CountUnitsInFrame(int unitSize, int frameSize)
        => Math.Max(1, (frameSize + (unitSize >> 1)) / unitSize);

    /// <summary>
    /// Source plane samples and geometry for one colour plane, expressed in that plane's
    /// own sample coordinates (origin already applied).
    /// </summary>
    private readonly struct PlaneView
    {
        public PlaneView(Buffer2D<byte> destination, byte[] preCdef, int width, int height, int originX, int originY)
        {
            this.Destination = destination;
            this.PreCdef = preCdef;
            this.Width = width;
            this.Height = height;
            this.OriginX = originX;
            this.OriginY = originY;
        }

        public Buffer2D<byte> Destination { get; }

        public byte[] PreCdef { get; }

        public int Width { get; }

        public int Height { get; }

        public int OriginX { get; }

        public int OriginY { get; }
    }
}
