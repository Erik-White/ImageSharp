// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;

/// <summary>
/// Drives the per-64×64 CDEF apply step for one plane: assembles the working buffer (spec
/// 7.15.2 inputs), runs direction search if needed (spec 7.15.2.2), then iterates the
/// non-skip 8×8 cells and invokes the inner filter (spec 7.15.2.1) to write the filtered
/// pixels back into the frame plane.
/// </summary>
internal static class Av1CdefUnitDriver
{
    private const int UnitMiSize = 16;
    private const int CellSize = Av1CdefPrimitives.CellSize;
    private const int MaxCellsPerUnit = 64;
    private const int WorkingBufferRows = 132; // 128 + 2*VBorder rows of stride.

    /// <summary>
    /// Frame-level CDEF apply. Iterates the 64×64 unit grid over each plane, reads the
    /// per-superblock strength index from <see cref="Av1FrameInfo.GetCdefStrength"/>, looks
    /// up the (primary, secondary) pair from the frame-header strength tables, and invokes
    /// the per-unit driver. Spec 7.15 frame-level driver, simplified for one-shot decode.
    /// </summary>
    public static void DecodeFrame(
        Configuration configuration,
        ObuSequenceHeader sequenceHeader,
        ObuFrameHeader frameHeader,
        Av1FrameInfo frameInfo,
        Av1FrameBuffer<byte> frameBuffer)
    {
        if (!IsCdefActive(sequenceHeader, frameHeader))
        {
            return;
        }

        // Spec 7.15: CDEF reads neighbour samples for each unit's hborder/vborder padding,
        // and those neighbours must be the pre-CDEF samples. Snapshot every plane once at
        // frame start so an already-filtered unit's output never leaks into a later unit's
        // padding (libaom achieves the same with rolling top/bot/col line buffers; a full
        // snapshot is simpler and the only extra cost is one frame-sized clone per plane).
        using PlaneSnapshots snapshots = PlaneSnapshots.Capture(configuration, frameBuffer, sequenceHeader.ColorConfig.IsMonochrome ? 1 : 3);

        FrameContext frame = new(sequenceHeader, frameHeader, frameInfo, frameBuffer, snapshots);
        using IMemoryOwner<ushort> workingOwner = configuration.MemoryAllocator.Allocate<ushort>(
            Av1CdefConstants.BufferStride * WorkingBufferRows,
            AllocationOptions.None);
        using IMemoryOwner<int> directionsOwner = configuration.MemoryAllocator.Allocate<int>(
            MaxCellsPerUnit,
            AllocationOptions.None);
        using IMemoryOwner<int> variancesOwner = configuration.MemoryAllocator.Allocate<int>(
            MaxCellsPerUnit,
            AllocationOptions.None);

        Span<Av1CdefCellPosition> dlist = stackalloc Av1CdefCellPosition[MaxCellsPerUnit];
        UnitBuffers buffers = new(workingOwner.Memory.Span, directionsOwner.Memory.Span, variancesOwner.Memory.Span);

        for (int miRow = 0; miRow < frame.FrameMiRows; miRow += UnitMiSize)
        {
            for (int miCol = 0; miCol < frame.FrameMiCols; miCol += UnitMiSize)
            {
                ProcessUnit(in frame, miRow, miCol, dlist, in buffers);
            }
        }
    }

    /// <summary>
    /// Decodes the strength index for a CDEF unit, splitting the bottom 2 bits as the
    /// secondary index (with the spec's bucket-3-to-4 remap from 7.15.1) and the top bits
    /// as the primary index. Returns the un-shifted (primaryStrength, secondaryStrength)
    /// pair; the caller multiplies by <c>1 &lt;&lt; coeffShift</c>.
    /// </summary>
    private static (int Primary, int Secondary) DecodeStrengths(int packedStrength)
    {
        int primary = packedStrength / Av1CdefConstants.SecondaryStrengthCount;
        int secondary = packedStrength % Av1CdefConstants.SecondaryStrengthCount;
        if (secondary == 3)
        {
            secondary = 4;
        }

        return (primary, secondary);
    }

    private static bool IsCdefActive(ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader)
    {
        if (!sequenceHeader.EnableCdef
            || frameHeader.CodedLossless
            || frameHeader.AllowIntraBlockCopy)
        {
            return false;
        }

        ObuConstraintDirectionalEnhancementFilterParameters cdef = frameHeader.CdefParameters;
        int strengthCount = 1 << cdef.BitCount;
        for (int i = 0; i < strengthCount; i++)
        {
            if (cdef.YStrength[i] != 0 || cdef.UvStrength[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void ProcessUnit(
        in FrameContext frame,
        int miRow,
        int miCol,
        Span<Av1CdefCellPosition> dlist,
        in UnitBuffers buffers)
    {
        if (!TryGetUnitStrengthIndex(in frame, miRow, miCol, out int strengthIndex))
        {
            return;
        }

        int cdefCount = Av1CdefBlockList.Build(
            frame.FrameInfo,
            miRow,
            miCol,
            frame.FrameMiRows,
            frame.FrameMiCols,
            Av1BlockSize.Block64x64,
            dlist);

        if (cdefCount == 0)
        {
            return;
        }

        int unitOriginX = miCol << Av1Constants.ModeInfoSizeLog2;
        int unitOriginY = miRow << Av1Constants.ModeInfoSizeLog2;
        int unitWidth = Math.Min(Av1CdefConstants.BlockSize, frame.FrameWidth - unitOriginX);
        int unitHeight = Math.Min(Av1CdefConstants.BlockSize, frame.FrameHeight - unitOriginY);

        for (int plane = 0; plane < frame.PlaneCount; plane++)
        {
            ApplyToPlane(in frame, plane, strengthIndex, unitOriginX, unitOriginY, unitWidth, unitHeight, dlist, cdefCount, in buffers);
        }
    }

    private static bool TryGetUnitStrengthIndex(in FrameContext frame, int miRow, int miCol, out int strengthIndex)
    {
        int sbCol = (miCol << Av1Constants.ModeInfoSizeLog2) >> frame.SuperblockSizeLog2;
        int sbRow = (miRow << Av1Constants.ModeInfoSizeLog2) >> frame.SuperblockSizeLog2;
        Av1SuperblockInfo sbInfo = frame.FrameInfo.GetSuperblock(new Point(sbCol, sbRow));

        int subUnitIndex = 0;
        if (frame.SuperblockSize == Av1BlockSize.Block128x128)
        {
            int colUnit = (miCol & UnitMiSize) != 0 ? 1 : 0;
            int rowUnit = (miRow & UnitMiSize) != 0 ? 1 : 0;
            subUnitIndex = colUnit + (rowUnit << 1);
        }

        Span<int> sbStrengths = sbInfo.CdefStrength;
        if (subUnitIndex >= sbStrengths.Length)
        {
            strengthIndex = -1;
            return false;
        }

        strengthIndex = sbStrengths[subUnitIndex];
        return strengthIndex >= 0;
    }

    private static void ApplyToPlane(
        in FrameContext frame,
        int plane,
        int strengthIndex,
        int unitOriginX,
        int unitOriginY,
        int unitWidth,
        int unitHeight,
        ReadOnlySpan<Av1CdefCellPosition> dlist,
        int cdefCount,
        in UnitBuffers buffers)
    {
        int packed = plane == 0
            ? frame.CdefParameters.YStrength[strengthIndex]
            : frame.CdefParameters.UvStrength[strengthIndex];
        (int primary, int secondary) = DecodeStrengths(packed);

        if (primary == 0 && secondary == 0)
        {
            return;
        }

        PlaneGeometry geometry = PlaneGeometry.For(in frame, plane, unitOriginX, unitOriginY, unitWidth, unitHeight);
        int adjustedDamping = frame.CdefParameters.Damping + (plane != 0 ? -1 : 0);

        // coeffShift is BitDepth - 8; this decoder is 8-bit only, so it is always 0. A
        // high-bit-depth path would derive it from the sequence header.
        ApplyToUnit(
            geometry,
            dlist,
            cdefCount,
            primary,
            secondary,
            adjustedDamping,
            coeffShift: 0,
            plane,
            in buffers);
    }

    private static void ApplyToUnit(
        PlaneGeometry geometry,
        ReadOnlySpan<Av1CdefCellPosition> dlist,
        int cdefCount,
        int primaryStrength,
        int secondaryStrength,
        int damping,
        int coeffShift,
        int planeIndex,
        in UnitBuffers buffers)
    {
        if (cdefCount == 0 || (primaryStrength == 0 && secondaryStrength == 0))
        {
            return;
        }

        Av1CdefBufferPad.Pad(
            geometry.Source,
            geometry.SourceStride,
            geometry.UnitX,
            geometry.UnitY,
            geometry.UnitWidth,
            geometry.UnitHeight,
            geometry.OriginX,
            geometry.OriginY,
            geometry.Width,
            geometry.Height,
            buffers.Working);

        if (planeIndex == 0)
        {
            PopulateLumaDirections(buffers.Working, dlist, cdefCount, coeffShift, buffers.Directions, buffers.Variances);
        }
        else if (geometry.SubX != geometry.SubY)
        {
            RemapChromaDirections(dlist, cdefCount, buffers.Directions);
        }

        FilterAllCells(geometry, dlist, cdefCount, primaryStrength, secondaryStrength, damping, coeffShift, planeIndex, in buffers);
    }

    private static void PopulateLumaDirections(
        ReadOnlySpan<ushort> workingBuffer,
        ReadOnlySpan<Av1CdefCellPosition> dlist,
        int cdefCount,
        int coeffShift,
        Span<int> directions,
        Span<int> variances)
    {
        int stride = Av1CdefConstants.BufferStride;

        for (int bi = 0; bi < cdefCount; bi++)
        {
            int blockOffset = dlist[bi].WorkingBufferOffset(stride);
            directions[bi] = Av1CdefPrimitives.FindDirection(
                workingBuffer[blockOffset..],
                stride,
                out int variance,
                coeffShift);
            variances[bi] = variance;
        }
    }

    private static void RemapChromaDirections(
        ReadOnlySpan<Av1CdefCellPosition> dlist,
        int cdefCount,
        Span<int> directions)
    {
        // Spec 7.15.2.2 / 7.15.1 Cdef_Uv_Dir: for asymmetric chroma subsampling the luma
        // direction is remapped to the closest direction valid in the rectangular chroma
        // block. The only asymmetric format AV1 permits is 4:2:2 (subX=1, subY=0; the spec's
        // color_config table allows no subX=0/subY=1 case), so the single reachable remap is
        // ChromaConv422.
        ReadOnlySpan<int> remap = Av1CdefConstants.ChromaConv422;
        for (int bi = 0; bi < cdefCount; bi++)
        {
            directions[bi] = remap[directions[bi] & 7];
        }
    }

    private static void FilterAllCells(
        PlaneGeometry geometry,
        ReadOnlySpan<Av1CdefCellPosition> dlist,
        int cdefCount,
        int primaryStrength,
        int secondaryStrength,
        int damping,
        int coeffShift,
        int planeIndex,
        in UnitBuffers buffers)
    {
        int stride = Av1CdefConstants.BufferStride;
        Span<byte> cellDst = stackalloc byte[CellSize * CellSize];

        for (int bi = 0; bi < cdefCount; bi++)
        {
            Av1CdefCellPosition cell = dlist[bi];

            // Spec 7.15.2: luma applies AdjustStrength per cell using the cell's variance
            // proxy from the direction search; chroma reuses the unadjusted primary strength.
            int cellPrimary = planeIndex == 0
                ? Av1CdefPrimitives.AdjustStrength(primaryStrength, buffers.Variances[bi])
                : primaryStrength;
            int direction = cellPrimary != 0 ? buffers.Directions[bi] : 0;

            Av1CdefPrimitives.FilterBlock(
                cellDst,
                CellSize,
                buffers.Working,
                cell.WorkingBufferOffset(stride),
                stride,
                cellPrimary,
                secondaryStrength,
                direction,
                damping,
                damping,
                coeffShift);

            int dstX = geometry.OriginX + geometry.UnitX + cell.PixelX;
            int dstY = geometry.OriginY + geometry.UnitY + cell.PixelY;
            CopyCellToPlane(cellDst, geometry.Buffer, dstX, dstY);
        }
    }

    private static void CopyCellToPlane(ReadOnlySpan<byte> cellDst, Buffer2D<byte> plane, int destX, int destY)
    {
        for (int row = 0; row < CellSize; row++)
        {
            Span<byte> destRow = plane.DangerousGetRowSpan(destY + row);
            cellDst.Slice(row * CellSize, CellSize).CopyTo(destRow.Slice(destX, CellSize));
        }
    }

    private readonly ref struct UnitBuffers
    {
        public UnitBuffers(Span<ushort> working, Span<int> directions, Span<int> variances)
        {
            this.Working = working;
            this.Directions = directions;
            this.Variances = variances;
        }

        public Span<ushort> Working { get; }

        public Span<int> Directions { get; }

        public Span<int> Variances { get; }
    }

    private readonly ref struct FrameContext
    {
        public FrameContext(
            ObuSequenceHeader sequenceHeader,
            ObuFrameHeader frameHeader,
            Av1FrameInfo frameInfo,
            Av1FrameBuffer<byte> frameBuffer,
            PlaneSnapshots snapshots)
        {
            this.FrameInfo = frameInfo;
            this.FrameBuffer = frameBuffer;
            this.Snapshots = snapshots;
            this.CdefParameters = frameHeader.CdefParameters;
            this.ColorConfig = sequenceHeader.ColorConfig;
            this.PlaneCount = sequenceHeader.ColorConfig.IsMonochrome ? 1 : 3;
            this.FrameWidth = frameHeader.FrameSize.FrameWidth;
            this.FrameHeight = frameHeader.FrameSize.FrameHeight;
            this.FrameMiCols = frameHeader.ModeInfoColumnCount;
            this.FrameMiRows = frameHeader.ModeInfoRowCount;
            this.SuperblockSize = sequenceHeader.SuperblockSize;
            this.SuperblockSizeLog2 = sequenceHeader.SuperblockSizeLog2;
        }

        public Av1FrameInfo FrameInfo { get; }

        public Av1FrameBuffer<byte> FrameBuffer { get; }

        public PlaneSnapshots Snapshots { get; }

        public ObuConstraintDirectionalEnhancementFilterParameters CdefParameters { get; }

        public ObuColorConfig ColorConfig { get; }

        public int PlaneCount { get; }

        public int FrameWidth { get; }

        public int FrameHeight { get; }

        public int FrameMiCols { get; }

        public int FrameMiRows { get; }

        public Av1BlockSize SuperblockSize { get; }

        public int SuperblockSizeLog2 { get; }
    }

    private readonly ref struct PlaneGeometry
    {
        private PlaneGeometry(
            Buffer2D<byte> buffer,
            ReadOnlySpan<byte> source,
            int sourceStride,
            int originX,
            int originY,
            int width,
            int height,
            int unitX,
            int unitY,
            int unitWidth,
            int unitHeight,
            int subX,
            int subY)
        {
            this.Buffer = buffer;
            this.Source = source;
            this.SourceStride = sourceStride;
            this.OriginX = originX;
            this.OriginY = originY;
            this.Width = width;
            this.Height = height;
            this.UnitX = unitX;
            this.UnitY = unitY;
            this.UnitWidth = unitWidth;
            this.UnitHeight = unitHeight;
            this.SubX = subX;
            this.SubY = subY;
        }

        public Buffer2D<byte> Buffer { get; }

        public ReadOnlySpan<byte> Source { get; }

        public int SourceStride { get; }

        public int OriginX { get; }

        public int OriginY { get; }

        public int Width { get; }

        public int Height { get; }

        public int UnitX { get; }

        public int UnitY { get; }

        public int UnitWidth { get; }

        public int UnitHeight { get; }

        public int SubX { get; }

        public int SubY { get; }

        public static PlaneGeometry For(in FrameContext frame, int plane, int unitOriginX, int unitOriginY, int unitWidth, int unitHeight)
        {
            int subX = plane > 0 && frame.ColorConfig.SubSamplingX ? 1 : 0;
            int subY = plane > 0 && frame.ColorConfig.SubSamplingY ? 1 : 0;

            Buffer2D<byte> buffer = plane switch
            {
                0 => frame.FrameBuffer.BufferY!,
                1 => frame.FrameBuffer.BufferCb!,
                _ => frame.FrameBuffer.BufferCr!,
            };

            ReadOnlySpan<byte> source = plane switch
            {
                0 => frame.Snapshots.Y.Span,
                1 => frame.Snapshots.U.Span,
                _ => frame.Snapshots.V.Span,
            };

            int sourceStride = plane == 0 ? frame.Snapshots.YStride : frame.Snapshots.UvStride;

            return new PlaneGeometry(
                buffer,
                source,
                sourceStride,
                frame.FrameBuffer.OriginX >> subX,
                frame.FrameBuffer.OriginY >> subY,
                (frame.FrameWidth + subX) >> subX,
                (frame.FrameHeight + subY) >> subY,
                unitOriginX >> subX,
                unitOriginY >> subY,
                unitWidth >> subX,
                unitHeight >> subY,
                subX,
                subY);
        }
    }

    /// <summary>
    /// Frame-start clones of the Y/U/V planes the CDEF padder reads as the source for
    /// neighbour samples. The driver writes filtered output to the live frame buffer; reads
    /// for padding always come from these snapshots so already-filtered units don't leak
    /// into later units' padding.
    /// </summary>
    private sealed class PlaneSnapshots : IDisposable
    {
        private readonly IMemoryOwner<byte> yOwner;
        private readonly IMemoryOwner<byte>? uOwner;
        private readonly IMemoryOwner<byte>? vOwner;

        private PlaneSnapshots(IMemoryOwner<byte> yOwner, IMemoryOwner<byte>? uOwner, IMemoryOwner<byte>? vOwner, int yStride, int uvStride)
        {
            this.yOwner = yOwner;
            this.uOwner = uOwner;
            this.vOwner = vOwner;
            this.YStride = yStride;
            this.UvStride = uvStride;
        }

        public Memory<byte> Y => this.yOwner.Memory;

        // U and V are only present for color frames; PlaneGeometry.For gates these accessors on
        // plane index, which is itself bounded by FrameContext.PlaneCount (1 for monochrome).
        public Memory<byte> U => this.uOwner!.Memory;

        public Memory<byte> V => this.vOwner!.Memory;

        public int YStride { get; }

        public int UvStride { get; }

        public static PlaneSnapshots Capture(Configuration configuration, Av1FrameBuffer<byte> frameBuffer, int planeCount)
        {
            IMemoryOwner<byte> yOwner = ClonePlane(configuration, frameBuffer.BufferY!, out int yStride);
            IMemoryOwner<byte>? uOwner = null;
            IMemoryOwner<byte>? vOwner = null;
            int uvStride = 0;
            if (planeCount > 1)
            {
                uOwner = ClonePlane(configuration, frameBuffer.BufferCb!, out uvStride);
                vOwner = ClonePlane(configuration, frameBuffer.BufferCr!, out _);
            }

            return new PlaneSnapshots(yOwner, uOwner, vOwner, yStride, uvStride);
        }

        public void Dispose()
        {
            this.yOwner.Dispose();
            this.uOwner?.Dispose();
            this.vOwner?.Dispose();
        }

        private static IMemoryOwner<byte> ClonePlane(Configuration configuration, Buffer2D<byte> plane, out int stride)
        {
            stride = plane.Width;
            int height = plane.Height;
            IMemoryOwner<byte> owner = configuration.MemoryAllocator.Allocate<byte>(stride * height, AllocationOptions.None);
            Span<byte> destination = owner.Memory.Span;
            for (int y = 0; y < height; y++)
            {
                plane.DangerousGetRowSpan(y).CopyTo(destination[(y * stride)..]);
            }

            return owner;
        }
    }
}
