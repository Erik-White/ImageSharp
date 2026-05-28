// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Formats.Heif.Av1;
using SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline;
using SixLabors.ImageSharp.Formats.Heif.Av1.Prediction;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.Palette;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;

internal class Av1TileReader : IAv1TileReader
{
    private static readonly int[] SgrprojXqdMid = [-32, 31];
    private static readonly int[] WienerTapsMid = [3, -7, 15];
    private static readonly int[] Signs = [0, -1, 1];
    private static readonly int[] DcSignContexts = [
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2];

    private static readonly int[][] SkipContexts = [
        [1, 2, 2, 2, 3], [1, 4, 4, 4, 5], [1, 4, 4, 4, 5], [1, 4, 4, 4, 5], [1, 4, 4, 4, 6]];

    private int[][] referenceSgrXqd = [];
    private int[][][] referenceLrWiener = [];
    private readonly Av1ParseAboveNeighbor4x4Context aboveNeighborContext;
    private readonly Av1ParseLeftNeighbor4x4Context leftNeighborContext;
    private int currentQuantizerIndex;
    private readonly int[][] segmentIds = [];
    private readonly int[][] transformUnitCount;
    private readonly int[] firstTransformOffset = new int[2];
    private readonly int[] coefficientIndex = [];
    private readonly Configuration configuration;

    public Av1TileReader(Configuration configuration, ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader)
    {
        this.FrameHeader = frameHeader;
        this.configuration = configuration;
        this.SequenceHeader = sequenceHeader;

        // init_main_frame_ctxt
        this.FrameInfo = new(this.SequenceHeader);
        this.segmentIds = new int[this.FrameHeader.ModeInfoRowCount][];
        for (int y = 0; y < this.FrameHeader.ModeInfoRowCount; y++)
        {
            this.segmentIds[y] = new int[this.FrameHeader.ModeInfoColumnCount];
        }

        // reallocate_parse_context_memory
        // Hard code number of threads to 1 for now.
        int planesCount = sequenceHeader.ColorConfig.PlaneCount;
        int superblockColumnCount =
            Av1Math.AlignPowerOf2(sequenceHeader.MaxFrameWidth, sequenceHeader.SuperblockSizeLog2) >> sequenceHeader.SuperblockSizeLog2;
        int modeInfoWideColumnCount = superblockColumnCount * sequenceHeader.SuperblockModeInfoSize;
        modeInfoWideColumnCount = Av1Math.AlignPowerOf2(modeInfoWideColumnCount, sequenceHeader.SuperblockSizeLog2 - Av1Constants.ModeInfoSizeLog2);
        this.aboveNeighborContext = new Av1ParseAboveNeighbor4x4Context(planesCount, modeInfoWideColumnCount);
        this.leftNeighborContext = new Av1ParseLeftNeighbor4x4Context(planesCount, sequenceHeader.SuperblockModeInfoSize);
        this.transformUnitCount = new int[Av1Constants.MaxPlanes][];
        this.transformUnitCount[0] = new int[this.FrameInfo.ModeInfoCount];
        this.transformUnitCount[1] = new int[this.FrameInfo.ModeInfoCount];
        this.transformUnitCount[2] = new int[this.FrameInfo.ModeInfoCount];
        this.coefficientIndex = new int[Av1Constants.MaxPlanes];
    }

    public ObuFrameHeader FrameHeader { get; }

    public ObuSequenceHeader SequenceHeader { get; }

    public Av1FrameInfo FrameInfo { get; }

    /// <summary>
    /// SVT: parse_tile
    /// </summary>
    public void ReadTile(Span<byte> tileData, int tileNum)
    {
        Av1SymbolDecoder reader = new(this.configuration, tileData, this.FrameHeader.QuantizationParameters.BaseQIndex);
        int tileColumnIndex = tileNum % this.FrameHeader.TilesInfo.TileColumnCount;
        int tileRowIndex = tileNum / this.FrameHeader.TilesInfo.TileColumnCount;

        int modeInfoColumnStart = this.FrameHeader.TilesInfo.TileColumnStartModeInfo[tileColumnIndex];
        int modeInfoColumnEnd = this.FrameHeader.TilesInfo.TileColumnStartModeInfo[tileColumnIndex + 1];
        int modeInfoRowStart = this.FrameHeader.TilesInfo.TileRowStartModeInfo[tileRowIndex];
        int modeInfoRowEnd = this.FrameHeader.TilesInfo.TileRowStartModeInfo[tileRowIndex + 1];
        this.aboveNeighborContext.Clear(this.SequenceHeader, modeInfoColumnStart, modeInfoColumnEnd);
        this.ClearLoopFilterDelta();
        this.currentQuantizerIndex = this.FrameHeader.QuantizationParameters.BaseQIndex;
        int planesCount = this.SequenceHeader.ColorConfig.PlaneCount;

        // Default initialization of Wiener and SGR Filter.
        this.referenceSgrXqd = new int[planesCount][];
        this.referenceLrWiener = new int[planesCount][][];
        for (int plane = 0; plane < planesCount; plane++)
        {
            this.referenceSgrXqd[plane] = new int[2];
            Array.Copy(SgrprojXqdMid, this.referenceSgrXqd[plane], SgrprojXqdMid.Length);
            this.referenceLrWiener[plane] = new int[2][];
            for (int pass = 0; pass < 2; pass++)
            {
                this.referenceLrWiener[plane][pass] = new int[Av1Constants.WienerCoefficientCount];
                Array.Copy(WienerTapsMid, this.referenceLrWiener[plane][pass], WienerTapsMid.Length);
            }
        }

        Av1TileInfo tileInfo = new(tileRowIndex, tileColumnIndex, this.FrameHeader);
        Av1BlockSize superBlockSize = this.SequenceHeader.SuperblockSize;
        int superBlock4x4Size = this.SequenceHeader.SuperblockSize.Get4x4WideCount();
        int superBlockSizeLog2 = this.SequenceHeader.SuperblockSizeLog2;
        for (int row = modeInfoRowStart; row < modeInfoRowEnd; row += superBlock4x4Size)
        {
            int superBlockRow = (row << Av1Constants.ModeInfoSizeLog2) >> superBlockSizeLog2;
            this.leftNeighborContext.Clear(this.SequenceHeader);
            for (int column = modeInfoColumnStart; column < modeInfoColumnEnd; column += superBlock4x4Size)
            {
                int superBlockColumn = (column << Av1Constants.ModeInfoSizeLog2) >> superBlockSizeLog2;
                Point superblockPosition = new(superBlockColumn, superBlockRow);
                Av1SuperblockInfo superblockInfo = this.FrameInfo.GetSuperblock(superblockPosition);

                Point modeInfoPosition = new(column, row);
                this.FrameInfo.ClearCdef(superblockPosition);
                this.firstTransformOffset[0] = 0;
                this.firstTransformOffset[1] = 0;
                this.coefficientIndex[0] = 0;
                this.coefficientIndex[1] = 0;
                this.coefficientIndex[2] = 0;
                this.ReadLoopRestoration(modeInfoPosition, superBlockSize);
                this.ParsePartition(ref reader, modeInfoPosition, superBlockSize, superblockInfo, tileInfo);
            }
        }
    }

    private void ClearLoopFilterDelta()
        => this.FrameInfo.ClearDeltaLoopFilter();

    private void ReadLoopRestoration(Point modeInfoLocation, Av1BlockSize superBlockSize)
    {
        int planesCount = this.SequenceHeader.ColorConfig.PlaneCount;
        for (int plane = 0; plane < planesCount; plane++)
        {
            if (this.FrameHeader.LoopRestorationParameters.Items[plane].Type != ObuRestorationType.None)
            {
                // TODO: Implement.
                throw new NotImplementedException("No loop restoration filter support.");
            }
        }
    }

    /// <summary>
    /// 5.11.4. Decode partition syntax.
    /// </summary>
    private void ParsePartition(ref Av1SymbolDecoder reader, Point modeInfoLocation, Av1BlockSize blockSize, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo)
    {
        int columnIndex = modeInfoLocation.X;
        int rowIndex = modeInfoLocation.Y;
        if (modeInfoLocation.Y >= this.FrameHeader.ModeInfoRowCount || modeInfoLocation.X >= this.FrameHeader.ModeInfoColumnCount)
        {
            return;
        }

        int block4x4Size = blockSize.Get4x4WideCount();
        int halfBlock4x4Size = block4x4Size >> 1;
        int quarterBlock4x4Size = halfBlock4x4Size >> 1;
        bool hasRows = (modeInfoLocation.Y + halfBlock4x4Size) < this.FrameHeader.ModeInfoRowCount;
        bool hasColumns = (modeInfoLocation.X + halfBlock4x4Size) < this.FrameHeader.ModeInfoColumnCount;
        Av1PartitionType partitionType = Av1PartitionType.None;
        if (blockSize >= Av1BlockSize.Block8x8)
        {
            int ctx = this.GetPartitionPlaneContext(modeInfoLocation, blockSize, tileInfo, superblockInfo);
            partitionType = Av1PartitionType.Split;
            if (hasRows && hasColumns)
            {
                partitionType = reader.ReadPartitionType(ctx);
            }
            else if (hasColumns)
            {
                partitionType = reader.ReadSplitOrHorizontal(blockSize, ctx);
            }
            else if (hasRows)
            {
                partitionType = reader.ReadSplitOrVertical(blockSize, ctx);
            }
        }

        Av1BlockSize subSize = partitionType.GetBlockSubSize(blockSize);
        Av1BlockSize splitSize = Av1PartitionType.Split.GetBlockSubSize(blockSize);
        switch (partitionType)
        {
            case Av1PartitionType.Split:
                Point loc1 = new(modeInfoLocation.X + halfBlock4x4Size, modeInfoLocation.Y);
                Point loc2 = new(modeInfoLocation.X, modeInfoLocation.Y + halfBlock4x4Size);
                Point loc3 = new(modeInfoLocation.X + halfBlock4x4Size, modeInfoLocation.Y + halfBlock4x4Size);
                this.ParsePartition(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo);
                this.ParsePartition(ref reader, loc1, subSize, superblockInfo, tileInfo);
                this.ParsePartition(ref reader, loc2, subSize, superblockInfo, tileInfo);
                this.ParsePartition(ref reader, loc3, subSize, superblockInfo, tileInfo);
                break;
            case Av1PartitionType.None:
                this.ParseBlock(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.None);
                break;
            case Av1PartitionType.Horizontal:
                this.ParseBlock(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Horizontal);
                if (hasRows)
                {
                    Point halfLocation = new(columnIndex, rowIndex + halfBlock4x4Size);
                    this.ParseBlock(ref reader, halfLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Horizontal);
                }

                break;
            case Av1PartitionType.Vertical:
                this.ParseBlock(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Vertical);
                if (hasColumns)
                {
                    Point halfLocation = new(columnIndex + halfBlock4x4Size, rowIndex);
                    this.ParseBlock(ref reader, halfLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Vertical);
                }

                break;
            case Av1PartitionType.HorizontalA:
                this.ParseBlock(ref reader, modeInfoLocation, splitSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalA);
                Point locHorA1 = new(columnIndex + halfBlock4x4Size, rowIndex);
                this.ParseBlock(ref reader, locHorA1, splitSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalA);
                Point locHorA2 = new(columnIndex, rowIndex + halfBlock4x4Size);
                this.ParseBlock(ref reader, locHorA2, subSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalA);
                break;
            case Av1PartitionType.HorizontalB:
                this.ParseBlock(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalB);
                Point locHorB1 = new(columnIndex, rowIndex + halfBlock4x4Size);
                this.ParseBlock(ref reader, locHorB1, splitSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalB);
                Point locHorB2 = new(columnIndex + halfBlock4x4Size, rowIndex + halfBlock4x4Size);
                this.ParseBlock(ref reader, locHorB2, splitSize, superblockInfo, tileInfo, Av1PartitionType.HorizontalB);
                break;
            case Av1PartitionType.VerticalA:
                this.ParseBlock(ref reader, modeInfoLocation, splitSize, superblockInfo, tileInfo, Av1PartitionType.VerticalA);
                Point locVertA1 = new(columnIndex, rowIndex + halfBlock4x4Size);
                this.ParseBlock(ref reader, locVertA1, splitSize, superblockInfo, tileInfo, Av1PartitionType.VerticalA);
                Point locVertA2 = new(columnIndex + halfBlock4x4Size, rowIndex);
                this.ParseBlock(ref reader, locVertA2, subSize, superblockInfo, tileInfo, Av1PartitionType.VerticalA);
                break;
            case Av1PartitionType.VerticalB:
                this.ParseBlock(ref reader, modeInfoLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.VerticalB);
                Point locVertB1 = new(columnIndex + halfBlock4x4Size, rowIndex);
                this.ParseBlock(ref reader, locVertB1, splitSize, superblockInfo, tileInfo, Av1PartitionType.VerticalB);
                Point locVertB2 = new(columnIndex + halfBlock4x4Size, rowIndex + halfBlock4x4Size);
                this.ParseBlock(ref reader, locVertB2, splitSize, superblockInfo, tileInfo, Av1PartitionType.VerticalB);
                break;
            case Av1PartitionType.Horizontal4:
                for (int i = 0; i < 4; i++)
                {
                    int currentBlockRow = rowIndex + (i * quarterBlock4x4Size);
                    if (i > 0 && currentBlockRow >= this.FrameHeader.ModeInfoRowCount)
                    {
                        break;
                    }

                    Point currentLocation = new(modeInfoLocation.X, currentBlockRow);
                    this.ParseBlock(ref reader, currentLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Horizontal4);
                }

                break;
            case Av1PartitionType.Vertical4:
                for (int i = 0; i < 4; i++)
                {
                    int currentBlockColumn = columnIndex + (i * quarterBlock4x4Size);
                    if (i > 0 && currentBlockColumn >= this.FrameHeader.ModeInfoColumnCount)
                    {
                        break;
                    }

                    Point currentLocation = new(currentBlockColumn, modeInfoLocation.Y);
                    this.ParseBlock(ref reader, currentLocation, subSize, superblockInfo, tileInfo, Av1PartitionType.Vertical4);
                }

                break;
            default:
                throw new NotImplementedException($"Partition type: {partitionType} is not supported.");
        }

        this.UpdatePartitionContext(new Point(columnIndex, rowIndex), tileInfo, superblockInfo, subSize, blockSize, partitionType);
    }

    private void ParseBlock(ref Av1SymbolDecoder reader, Point modeInfoLocation, Av1BlockSize blockSize, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo, Av1PartitionType partitionType)
    {
        int rowIndex = modeInfoLocation.Y;
        int columnIndex = modeInfoLocation.X;
        int block4x4Width = blockSize.Get4x4WideCount();
        int block4x4Height = blockSize.Get4x4HighCount();
        int planesCount = this.SequenceHeader.ColorConfig.PlaneCount;
        int subX = this.SequenceHeader.ColorConfig.SubSamplingX ? 1 : 0;
        int subY = this.SequenceHeader.ColorConfig.SubSamplingY ? 1 : 0;
        Point superblockLocation = superblockInfo.Position * this.SequenceHeader.SuperblockModeInfoSize;
        Point locationInSuperblock = new Point(modeInfoLocation.X - superblockLocation.X, modeInfoLocation.Y - superblockLocation.Y);
        Av1BlockModeInfo blockModeInfo = new(blockSize, locationInSuperblock)
        {
            PartitionType = partitionType
        };
        blockModeInfo.FirstTransformLocation[0] = this.firstTransformOffset[0];
        blockModeInfo.FirstTransformLocation[1] = this.firstTransformOffset[1];
        bool hasChroma = HasChroma(this.SequenceHeader, modeInfoLocation, blockSize);
        Av1PartitionInfo partitionInfo = new(blockModeInfo, superblockInfo, hasChroma, partitionType);
        partitionInfo.ColumnIndex = columnIndex;
        partitionInfo.RowIndex = rowIndex;
        if (superblockInfo.BlockCount == 0)
        {
            superblockInfo.FirstModeInfoIndex = this.FrameInfo.NextModeInfoIndex;
        }

        superblockInfo.BlockCount++;
        partitionInfo.ComputeBoundaryOffsets(this.configuration, this.SequenceHeader, this.FrameHeader, tileInfo);
        Point superblockOrigin = superblockInfo.Position * this.SequenceHeader.SuperblockModeInfoSize;
        int columnInSuperblock = columnIndex - superblockOrigin.X;
        int rowInSuperblock = rowIndex - superblockOrigin.Y;
        Av1SymbolTrace.WriteMiBlock(rowIndex, columnIndex, (int)blockSize);
        if (partitionInfo.AvailableAbove)
        {
            partitionInfo.AboveModeInfo = this.FrameInfo.GetModeInfoAtMiPosition(new Point(columnIndex, rowIndex - 1));
        }

        if (partitionInfo.AvailableLeft)
        {
            partitionInfo.LeftModeInfo = this.FrameInfo.GetModeInfoAtMiPosition(new Point(columnIndex - 1, rowIndex));
        }

        // libaom set_mi_row_col (av1_common_int.h:1394-1415): chroma reference is
        // anchored to the masked base (row & ~ss_y, col & ~ss_x), then offset by
        // (-1 row, +ss_x col) for above and (+ss_y row, -1 col) for left.
        int chromaBaseRow = rowIndex & ~subY;
        int chromaBaseCol = columnIndex & ~subX;
        if (partitionInfo.AvailableAboveForChroma)
        {
            partitionInfo.AboveModeInfoForChroma = this.FrameInfo.GetModeInfoAtMiPosition(new Point(chromaBaseCol | subX, chromaBaseRow - 1));
        }

        if (partitionInfo.AvailableLeftForChroma)
        {
            partitionInfo.LeftModeInfoForChroma = this.FrameInfo.GetModeInfoAtMiPosition(new Point(chromaBaseCol - 1, chromaBaseRow | subY));
        }

        this.ReadModeInfo(ref reader, partitionInfo, tileInfo);
        Av1PaletteDecoder.ReadPaletteTokens(ref reader, partitionInfo, this.SequenceHeader);
        this.ReadBlockTransformSize(ref reader, modeInfoLocation, partitionInfo, superblockInfo, tileInfo);
        if (partitionInfo.ModeInfo.Skip)
        {
            this.ResetSkipContext(partitionInfo, tileInfo);
        }

        this.Residual(ref reader, partitionInfo, superblockInfo, tileInfo, blockSize);

        // Update the Frame buffer for this ModeInfo.
        this.FrameInfo.UpdateModeInfo(blockModeInfo, superblockInfo);
    }

    /// <summary>
    /// SVT: reset_skip_context
    /// </summary>
    private void ResetSkipContext(Av1PartitionInfo partitionInfo, Av1TileInfo tileInfo)
    {
        int planesCount = this.SequenceHeader.ColorConfig.PlaneCount;
        for (int i = 0; i < planesCount; i++)
        {
            int subX = (i > 0 && this.SequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
            int subY = (i > 0 && this.SequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;
            Av1BlockSize planeBlockSize = partitionInfo.ModeInfo.BlockSize.GetSubsampled(subX, subY);
            DebugGuard.IsTrue(planeBlockSize != Av1BlockSize.Invalid, nameof(planeBlockSize));
            int txsWide = planeBlockSize.GetWidth() >> 2;
            int txsHigh = planeBlockSize.GetHeight() >> 2;
            int aboveOffset = (partitionInfo.ColumnIndex - tileInfo.ModeInfoColumnStart) >> subX;
            int leftOffset = (partitionInfo.RowIndex - partitionInfo.SuperblockInfo.ModeInfoPosition.Y) >> subY;
            this.aboveNeighborContext.ClearContext(i, aboveOffset, txsWide);
            this.leftNeighborContext.ClearContext(i, leftOffset, txsHigh);
        }
    }

    /// <summary>
    /// 5.11.34. Residual syntax.
    /// </summary>
    /// <remarks>SVT: parse_residual</remarks>
    private void Residual(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo, Av1BlockSize blockSize)
    {
        int maxBlocksWide = partitionInfo.GetMaxBlockWide(blockSize, false);
        int maxBlocksHigh = partitionInfo.GetMaxBlockHigh(blockSize, false);
        Av1BlockSize maxUnitSize = Av1BlockSize.Block64x64;
        int modeUnitBlocksWide = maxUnitSize.GetWidth() >> 2;
        int modeUnitBlocksHigh = maxUnitSize.GetHeight() >> 2;
        modeUnitBlocksWide = Math.Min(maxBlocksWide, modeUnitBlocksWide);
        modeUnitBlocksHigh = Math.Min(maxBlocksHigh, modeUnitBlocksHigh);
        int planeCount = this.SequenceHeader.ColorConfig.PlaneCount;
        bool isLossless = this.FrameHeader.LosslessArray[partitionInfo.ModeInfo.SegmentId];
        bool isLosslessBlock = isLossless && (blockSize >= Av1BlockSize.Block64x64) && (blockSize <= Av1BlockSize.Block128x128);
        int subSampling = (this.SequenceHeader.ColorConfig.SubSamplingX ? 1 : 0) + (this.SequenceHeader.ColorConfig.SubSamplingY ? 1 : 0);
        int chromaTransformUnitCount = isLosslessBlock ? ((maxBlocksWide * maxBlocksHigh) >> subSampling) : partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Uv];

        int[] transformInfoIndices = new int[3];
        transformInfoIndices[0] = superblockInfo.TransformInfoIndexY + partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Y];
        transformInfoIndices[1] = superblockInfo.TransformInfoIndexUv + partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Uv];
        transformInfoIndices[2] = transformInfoIndices[1] + chromaTransformUnitCount;
        int forceSplitCount = 0;

        for (int row = 0; row < maxBlocksHigh; row += modeUnitBlocksHigh)
        {
            for (int column = 0; column < maxBlocksWide; column += modeUnitBlocksWide)
            {
                for (int plane = 0; plane < planeCount; ++plane)
                {
                    int totalTransformUnitCount;
                    int transformUnitCount;
                    int subX = (plane > 0 && this.SequenceHeader.ColorConfig.SubSamplingX) ? 1 : 0;
                    int subY = (plane > 0 && this.SequenceHeader.ColorConfig.SubSamplingY) ? 1 : 0;

                    if (plane != 0 && !partitionInfo.IsChroma)
                    {
                        continue;
                    }

                    Span<Av1TransformInfo> transformInfoSpan = (plane == 0) ? superblockInfo.GetTransformInfoY() : superblockInfo.GetTransformInfoUv();
                    if (isLosslessBlock)
                    {
                        // TODO: Implement.
                        int unitHeight = Av1Math.RoundPowerOf2(Math.Min(modeUnitBlocksHigh + row, maxBlocksHigh), 0);
                        int unitWidth = Av1Math.RoundPowerOf2(Math.Min(modeUnitBlocksWide + column, maxBlocksWide), 0);
                        DebugGuard.IsTrue(transformInfoSpan[transformInfoIndices[plane]].Size == Av1TransformSize.Size4x4, "Lossless frame shall have transform units of size 4x4.");
                        transformUnitCount = ((unitWidth - column) * (unitHeight - row)) >> (subX + subY);
                    }
                    else
                    {
                        totalTransformUnitCount = partitionInfo.ModeInfo.TransformUnitsCount[Math.Min(1, plane)];
                        transformUnitCount = this.transformUnitCount[plane][forceSplitCount];

                        DebugGuard.IsFalse(totalTransformUnitCount == 0, nameof(totalTransformUnitCount), string.Empty);
                        DebugGuard.IsTrue(
                            totalTransformUnitCount ==
                                this.transformUnitCount[plane][0] + this.transformUnitCount[plane][1] +
                                this.transformUnitCount[plane][2] + this.transformUnitCount[plane][3],
                            nameof(totalTransformUnitCount),
                            string.Empty);
                    }

                    DebugGuard.IsFalse(transformUnitCount == 0, nameof(transformUnitCount), string.Empty);
                    for (int tu = 0; tu < transformUnitCount; tu++)
                    {
                        Av1TransformInfo transformInfo = transformInfoSpan[transformInfoIndices[plane]];
                        DebugGuard.MustBeLessThanOrEqualTo(transformInfo.OffsetX, maxBlocksWide, nameof(transformInfo));
                        DebugGuard.MustBeLessThanOrEqualTo(transformInfo.OffsetY, maxBlocksHigh, nameof(transformInfo));

                        int coefficientIndex = this.coefficientIndex[plane];
                        int endOfBlock = 0;
                        int blockColumn = transformInfo.OffsetX;
                        int blockRow = transformInfo.OffsetY;
                        int startX = (partitionInfo.ColumnIndex >> subX) + blockColumn;
                        int startY = (partitionInfo.RowIndex >> subY) + blockRow;

                        if (startX >= (this.FrameHeader.ModeInfoColumnCount >> subX) ||
                            startY >= (this.FrameHeader.ModeInfoRowCount >> subY))
                        {
                            return;
                        }

                        if (plane != 0 && partitionInfo.ModeInfo.UseIntraBlockCopy)
                        {
                            // libaom av1_get_tx_type: chroma reuses Y-plane tx_type at the
                            // chroma's scaled-back position. Pre-populate so ComputeTransformType
                            // can read transformInfo.Type instead of deriving from intra mode.
                            transformInfo.Type = LookupYTransformTypeForChroma(partitionInfo, superblockInfo, blockRow, blockColumn, subX, subY);
                        }

                        if (!partitionInfo.ModeInfo.Skip)
                        {
                            endOfBlock = this.ParseTransformBlock(ref reader, partitionInfo, tileInfo, coefficientIndex, transformInfo, plane, blockColumn, blockRow, transformInfo.Size, subX != 0, subY != 0);
                        }

                        if (endOfBlock != 0)
                        {
                            this.coefficientIndex[plane] += endOfBlock + 1;
                            transformInfo.CodeBlockFlag = true;
                        }
                        else
                        {
                            transformInfo.CodeBlockFlag = false;
                        }

                        transformInfoIndices[plane]++;
                    }
                }

                forceSplitCount++;
            }
        }
    }

    public static bool HasChroma(ObuSequenceHeader sequenceHeader, Point modeInfoLocation, Av1BlockSize blockSize)
    {
        int blockWide = blockSize.Get4x4WideCount();
        int blockHigh = blockSize.Get4x4HighCount();
        bool subX = sequenceHeader.ColorConfig.SubSamplingX;
        bool subY = sequenceHeader.ColorConfig.SubSamplingY;
        bool hasChroma = ((modeInfoLocation.Y & 0x01) != 0 || (blockHigh & 0x01) == 0 || !subY) &&
            ((modeInfoLocation.X & 0x01) != 0 || (blockWide & 0x01) == 0 || !subX);
        return hasChroma;
    }

    /// <summary>
    /// 5.11.35. Transform block syntax.
    /// </summary>
    /// <remarks>
    /// The implementation is taken from SVT-AV1 library, which deviates from the code flow in the specification.
    /// </remarks>
    private int ParseTransformBlock(
        ref Av1SymbolDecoder reader,
        Av1PartitionInfo partitionInfo,
        Av1TileInfo tileInfo,
        int coefficientIndex,
        Av1TransformInfo transformInfo,
        int plane,
        int blockColumn,
        int blockRow,
        Av1TransformSize transformSize,
        bool subX,
        bool subY)
    {
        Av1BlockSize planeBlockSize = partitionInfo.ModeInfo.BlockSize.GetSubsampled(subX, subY);
        int transformBlockUnitWideCount = transformSize.Get4x4WideCount();
        int transformBlockUnitHighCount = transformSize.Get4x4HighCount();

        if (partitionInfo.ModeBlockToRightEdge < 0)
        {
            int blocksWide = partitionInfo.GetMaxBlockWide(planeBlockSize, subX);
            transformBlockUnitWideCount = Math.Min(transformBlockUnitWideCount, blocksWide - blockColumn);
        }

        if (partitionInfo.ModeBlockToBottomEdge < 0)
        {
            int blocksHigh = partitionInfo.GetMaxBlockHigh(planeBlockSize, subY);
            transformBlockUnitHighCount = Math.Min(transformBlockUnitHighCount, blocksHigh - blockRow);
        }

        int aboveOffset = ((partitionInfo.ColumnIndex - tileInfo.ModeInfoColumnStart) >> (subX ? 1 : 0)) + blockColumn;
        int leftOffset = ((partitionInfo.RowIndex - partitionInfo.SuperblockInfo.ModeInfoPosition.Y) >> (subY ? 1 : 0)) + blockRow;

        Av1TransformBlockContext transformBlockContext = this.GetTransformBlockContext(transformSize, plane, planeBlockSize, transformBlockUnitHighCount, transformBlockUnitWideCount, aboveOffset, leftOffset);
        return this.ParseCoefficients(ref reader, partitionInfo, blockRow, blockColumn, aboveOffset, leftOffset, plane, subX, subY, transformBlockContext, transformSize, coefficientIndex, transformInfo);
    }

    /// <summary>
    /// 5.11.39. Coefficients syntax.
    /// </summary>
    /// <remarks>
    /// The implementation is taken from SVT-AV1 library, which deviates from the code flow in the specification.
    /// </remarks>
    private int ParseCoefficients(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, int blockRow, int blockColumn, int aboveOffset, int leftOffset, int plane, bool subX, bool subY, Av1TransformBlockContext transformBlockContext, Av1TransformSize transformSize, int coefficientIndex, Av1TransformInfo transformInfo)
    {
        Span<int> coefficientBuffer = partitionInfo.SuperblockInfo.GetCoefficients((Av1Plane)plane)[coefficientIndex..];
        Point blockPosition = new(blockColumn, blockRow);
        bool isLossless = this.FrameHeader.LosslessArray[partitionInfo.ModeInfo.SegmentId];
        Av1BlockSize planeBlockSize = partitionInfo.ModeInfo.BlockSize.GetSubsampled(subX, subY);
        int blocksWide = partitionInfo.GetMaxBlockWide(planeBlockSize, subX);
        int blocksHigh = partitionInfo.GetMaxBlockHigh(planeBlockSize, subY);

        return reader.ReadCoefficients(partitionInfo.ModeInfo, blockPosition, this.aboveNeighborContext.GetContext(plane), this.leftNeighborContext.GetContext(plane), aboveOffset, leftOffset, plane, blocksWide, blocksHigh, transformBlockContext, transformSize, isLossless, this.FrameHeader.UseReducedTransformSet, transformInfo, partitionInfo.ModeBlockToRightEdge, partitionInfo.ModeBlockToBottomEdge, coefficientBuffer);
    }

    private Av1TransformBlockContext GetTransformBlockContext(Av1TransformSize transformSize, int plane, Av1BlockSize planeBlockSize, int transformBlockUnitHighCount, int transformBlockUnitWideCount, int aboveOffset, int leftOffset)
    {
        Av1TransformBlockContext transformBlockContext = new();
        int[] aboveContext = this.aboveNeighborContext.GetContext(plane);
        int[] leftContext = this.leftNeighborContext.GetContext(plane);
        int dcSign = 0;
        int k = 0;
        int mask = (1 << Av1Constants.CoefficientContextBitCount) - 1;

        do
        {
            uint sign = (uint)aboveContext[aboveOffset + k] >> Av1Constants.CoefficientContextBitCount;
            DebugGuard.MustBeLessThanOrEqualTo(sign, 2U, nameof(sign));
            dcSign += Signs[sign];
        }
        while (++k < transformBlockUnitWideCount);

        k = 0;
        do
        {
            uint sign = (uint)leftContext[leftOffset + k] >> Av1Constants.CoefficientContextBitCount;
            DebugGuard.MustBeLessThanOrEqualTo(sign, 2U, nameof(sign));
            dcSign += Signs[sign];
        }
        while (++k < transformBlockUnitHighCount);

        transformBlockContext.DcSignContext = DcSignContexts[dcSign + (Av1Constants.MaxTransformSizeUnit << 1)];

        if (plane == 0)
        {
            if (planeBlockSize == transformSize.ToBlockSize())
            {
                transformBlockContext.SkipContext = 0;
            }
            else
            {
                int top = 0;
                int left = 0;

                k = 0;
                do
                {
                    top |= aboveContext[aboveOffset + k];
                }
                while (++k < transformBlockUnitWideCount);
                top &= mask;

                k = 0;
                do
                {
                    left |= leftContext[leftOffset + k];
                }
                while (++k < transformBlockUnitHighCount);
                left &= mask;

                int max = Math.Min(top | left, 4);
                int min = Math.Min(Math.Min(top, left), 4);

                transformBlockContext.SkipContext = SkipContexts[min][max];
            }
        }
        else
        {
            int contextBase = GetEntropyContext(transformSize, aboveContext.AsSpan(aboveOffset), leftContext.AsSpan(leftOffset));
            int contextOffset = planeBlockSize.GetPelsLog2Count() > transformSize.ToBlockSize().GetPelsLog2Count() ? 10 : 7;
            transformBlockContext.SkipContext = contextBase + contextOffset;
        }

        return transformBlockContext;
    }

    private static int GetEntropyContext(Av1TransformSize transformSize, ReadOnlySpan<int> above, ReadOnlySpan<int> left)
    {
        bool aboveEntropyContext = false;
        bool leftEntropyContext = false;

        switch (transformSize)
        {
            case Av1TransformSize.Size4x4:
                aboveEntropyContext = above[0] != 0;
                leftEntropyContext = left[0] != 0;
                break;
            case Av1TransformSize.Size4x8:
                aboveEntropyContext = above[0] != 0;
                leftEntropyContext = (left[0] | left[1]) != 0; // !!*(const uint16_t*)left;
                break;
            case Av1TransformSize.Size8x4:
                aboveEntropyContext = (above[0] | above[1]) != 0; // !!*(const uint16_t*)above;
                leftEntropyContext = left[0] != 0;
                break;
            case Av1TransformSize.Size8x16:
                aboveEntropyContext = (above[0] | above[1]) != 0; // !!*(const uint16_t*)above;
                leftEntropyContext = (left[0] | left[1] | left[2] | left[3]) != 0; //  !!*(const uint32_t*)left;
                break;
            case Av1TransformSize.Size16x8:
                aboveEntropyContext = (above[0] | above[1] | above[2] | above[3]) != 0; // !!*(const uint32_t*)above;
                leftEntropyContext = (left[0] | left[1]) != 0; // !!*(const uint16_t*)left;
                break;
            case Av1TransformSize.Size16x32:
                aboveEntropyContext = (above[0] | above[1] | above[2] | above[3]) != 0; // !!*(const uint32_t*)above;
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7]) != 0; // !!*(const uint64_t*)left;
                break;
            case Av1TransformSize.Size32x16:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7]) != 0; // !!*(const uint64_t*)above;
                leftEntropyContext = (left[0] | left[1] | left[2] | left[3]) != 0; // !!*(const uint32_t*)left;
                break;
            case Av1TransformSize.Size8x8:
                aboveEntropyContext = (above[0] | above[1]) != 0; // !!*(const uint16_t*)above;
                leftEntropyContext = (left[0] | left[1]) != 0; // !!*(const uint16_t*)left;
                break;
            case Av1TransformSize.Size16x16:
                aboveEntropyContext = (above[0] | above[1] | above[2] | above[3]) != 0; // !!*(const uint32_t*)above;
                leftEntropyContext = (left[0] | left[1] | left[2] | left[3]) != 0; // !!*(const uint32_t*)left;
                break;
            case Av1TransformSize.Size32x32:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7]) != 0; // !!*(const uint64_t*)above;
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7]) != 0; // !!*(const uint64_t*)left;
                break;
            case Av1TransformSize.Size64x64:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7] |
                     above[8] | above[9] | above[10] | above[11] | above[12] | above[13] | above[14] | above[15]) != 0; // !!(*(const uint64_t*)above | *(const uint64_t*)(above + 8));
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7] |
                     left[8] | left[9] | left[10] | left[11] | left[12] | left[13] | left[14] | left[15]) != 0; // !!(*(const uint64_t*)left | *(const uint64_t*)(left + 8));
                break;
            case Av1TransformSize.Size32x64:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7]) != 0; // !!*(const uint64_t*)above;
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7] |
                     left[8] | left[9] | left[10] | left[11] | left[12] | left[13] | left[14] | left[15]) != 0; // !!(*(const uint64_t*)left | *(const uint64_t*)(left + 8));
                break;
            case Av1TransformSize.Size64x32:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7] |
                     above[8] | above[9] | above[10] | above[11] | above[12] | above[13] | above[14] | above[15]) != 0; // !!(*(const uint64_t*)above | *(const uint64_t*)(above + 8));
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7]) != 0; // !!*(const uint64_t*)left;
                break;
            case Av1TransformSize.Size4x16:
                aboveEntropyContext = above[0] != 0;
                leftEntropyContext = (left[0] | left[1] | left[2] | left[3]) != 0; // !!*(const uint32_t*)left;
                break;
            case Av1TransformSize.Size16x4:
                aboveEntropyContext = (above[0] | above[1] | above[2] | above[3]) != 0; // !!*(const uint32_t*)above;
                leftEntropyContext = left[0] != 0;
                break;
            case Av1TransformSize.Size8x32:
                aboveEntropyContext = (above[0] | above[1]) != 0; // !!*(const uint16_t*)above;
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7]) != 0; // !!*(const uint64_t*)left;
                break;
            case Av1TransformSize.Size32x8:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7]) != 0; // !!*(const uint64_t*)above;
                leftEntropyContext = (left[0] | left[1]) != 0; // !!*(const uint16_t*)left;
                break;
            case Av1TransformSize.Size16x64:
                aboveEntropyContext = (above[0] | above[1] | above[2] | above[3]) != 0; // !!*(const uint32_t*)above;
                leftEntropyContext =
                    (left[0] | left[1] | left[2] | left[3] | left[4] | left[5] | left[6] | left[7] |
                     left[8] | left[9] | left[10] | left[11] | left[12] | left[13] | left[14] | left[15]) != 0; // !!(*(const uint64_t*)left | *(const uint64_t*)(left + 8));
                break;
            case Av1TransformSize.Size64x16:
                aboveEntropyContext =
                    (above[0] | above[1] | above[2] | above[3] | above[4] | above[5] | above[6] | above[7] |
                     above[8] | above[9] | above[10] | above[11] | above[12] | above[13] | above[14] | above[15]) != 0; // !!(*(const uint64_t*)above | *(const uint64_t*)(above + 8));
                leftEntropyContext = (left[0] | left[1] | left[2] | left[3]) != 0; // !!*(const uint32_t*)left;
                break;
            default:
                Guard.IsTrue(false, nameof(transformSize), "Invalid transform size.");
                break;
        }

        return (aboveEntropyContext ? 1 : 0) + (leftEntropyContext ? 1 : 0);
    }

    /// <summary>
    /// 5.11.15. TX size syntax.
    /// </summary>
    private Av1TransformSize ReadTransformSize(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo, bool allowSelect)
    {
        Av1BlockModeInfo modeInfo = partitionInfo.ModeInfo;
        if (this.FrameHeader.LosslessArray[modeInfo.SegmentId])
        {
            return Av1TransformSize.Size4x4;
        }

        if (modeInfo.BlockSize > Av1BlockSize.Block4x4 && allowSelect && this.FrameHeader.TransformMode == Av1TransformMode.Select)
        {
            return this.ReadSelectedTransformSize(ref reader, partitionInfo, superblockInfo, tileInfo);
        }

        return modeInfo.BlockSize.GetMaximumTransformSize();
    }

    private Av1TransformSize ReadSelectedTransformSize(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo)
    {
        int context = 0;
        Av1TransformSize maxTransformSize = partitionInfo.ModeInfo.BlockSize.GetMaximumTransformSize();
        int aboveWidth = this.aboveNeighborContext.AboveTransformWidth[partitionInfo.ColumnIndex - tileInfo.ModeInfoColumnStart];
        int above = (aboveWidth >= maxTransformSize.GetWidth()) ? 1 : 0;
        int leftHeight = this.leftNeighborContext.LeftTransformHeight[partitionInfo.RowIndex - superblockInfo.ModeInfoPosition.Y];
        int left = (leftHeight >= maxTransformSize.GetHeight()) ? 1 : 0;
        bool hasAbove = partitionInfo.AvailableAbove;
        bool hasLeft = partitionInfo.AvailableLeft;

        // libaom pred_common.h:355-361: when the neighbor is inter (treats IBC as inter), the
        // tx_size context query uses the neighbor's full block dimension instead of the
        // txfm_context value, which only carries per-leaf state for fragmented var-tx blocks.
        if (hasAbove && partitionInfo.AboveModeInfo?.UseIntraBlockCopy == true)
        {
            above = partitionInfo.AboveModeInfo.BlockSize.GetWidth() >= maxTransformSize.GetWidth() ? 1 : 0;
        }

        if (hasLeft && partitionInfo.LeftModeInfo?.UseIntraBlockCopy == true)
        {
            left = partitionInfo.LeftModeInfo.BlockSize.GetHeight() >= maxTransformSize.GetHeight() ? 1 : 0;
        }

        if (hasAbove && hasLeft)
        {
            context = above + left;
        }
        else if (hasAbove)
        {
            context = above;
        }
        else if (hasLeft)
        {
            context = left;
        }
        else
        {
            context = 0;
        }

        return reader.ReadTransformSize(partitionInfo.ModeInfo.BlockSize, context);
    }

    /// <summary>
    /// Section 5.11.16. Block TX size syntax.
    /// </summary>
    /// <remarks>SVT: read_block_tx_size</remarks>
    private void ReadBlockTransformSize(ref Av1SymbolDecoder reader, Point modeInfoLocation, Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo)
    {
        Av1BlockModeInfo modeInfo = partitionInfo.ModeInfo;
        Av1BlockSize blockSize = modeInfo.BlockSize;
        int block4x4Width = blockSize.Get4x4WideCount();
        int block4x4Height = blockSize.Get4x4HighCount();

        // libaom decodeframe.c:1180: var-tx applies when the block is inter (or intra-block-copy),
        // signals tx size, isn't skipped, isn't lossless, and tx_mode is select.
        bool interBlockTx = modeInfo.UseIntraBlockCopy; // intra frame: only IBC counts as inter for tx purposes.
        bool blockSignalsTxSize = blockSize > Av1BlockSize.Block4x4;
        bool isLossless = this.FrameHeader.LosslessArray[modeInfo.SegmentId];
        if (this.FrameHeader.TransformMode == Av1TransformMode.Select && blockSignalsTxSize &&
            !modeInfo.Skip && interBlockTx && !isLossless)
        {
            Av1TransformSize maxTxSize = blockSize.GetMaximumTransformSize();
            int aboveBaseColumn = modeInfoLocation.X - tileInfo.ModeInfoColumnStart;
            int leftBaseRow = modeInfoLocation.Y - superblockInfo.ModeInfoPosition.Y;
            int blockWide4x4 = blockSize.Get4x4WideCount();
            int blockHigh4x4 = blockSize.Get4x4HighCount();
            int leafStepCols = maxTxSize.Get4x4WideCount();
            int leafStepRows = maxTxSize.Get4x4HighCount();
            List<VarTxLeaf> leaves = [];
            for (int idy = 0; idy < blockHigh4x4; idy += leafStepRows)
            {
                for (int idx = 0; idx < blockWide4x4; idx += leafStepCols)
                {
                    this.ReadVarTxLeaves(ref reader, blockSize, maxTxSize, depth: 0, idy, idx, aboveBaseColumn, leftBaseRow, leaves);
                }
            }

            this.UpdateTransformInfoFromLeaves(partitionInfo, superblockInfo, blockSize, leaves);
            return;
        }

        // libaom decodeframe.c:1192 calls read_tx_size with allow_select_inter=!skip_txfm. Intra
        // blocks ignore this gate (is_inter=0 short-circuits the condition), but IBC blocks fall
        // here only when vartx didn't take them — which means skip=1 — so they must NOT read.
        bool allowSelect = !modeInfo.UseIntraBlockCopy;
        Av1TransformSize transformSize = this.ReadTransformSize(ref reader, partitionInfo, superblockInfo, tileInfo, allowSelect);
        this.aboveNeighborContext.UpdateTransformation(modeInfoLocation, tileInfo, transformSize, blockSize, false);
        this.leftNeighborContext.UpdateTransformation(modeInfoLocation, superblockInfo, transformSize, blockSize, false);
        this.UpdateTransformInfo(partitionInfo, superblockInfo, blockSize, transformSize);
    }

    /// <summary>
    /// Mirrors libaom <c>read_tx_size_vartx</c> (decodeframe.c:1064-1125). Recursively reads
    /// the txfm_partition_cdf split flag at each level (max depth = MAX_VARTX_DEPTH). Each
    /// emitted leaf records its (blkRow, blkCol, txSize) and updates the above/left tx
    /// context per leaf so neighboring leaves see the correct partition context.
    /// </summary>
    private void ReadVarTxLeaves(
        ref Av1SymbolDecoder reader,
        Av1BlockSize blockSize,
        Av1TransformSize txSize,
        int depth,
        int blkRow,
        int blkCol,
        int aboveBaseColumn,
        int leftBaseRow,
        List<VarTxLeaf> leaves)
    {
        int blockHigh4x4 = blockSize.Get4x4HighCount();
        int blockWide4x4 = blockSize.Get4x4WideCount();
        if (blkRow >= blockHigh4x4 || blkCol >= blockWide4x4)
        {
            return;
        }

        DebugGuard.MustBeGreaterThan((int)txSize, (int)Av1TransformSize.Size4x4, nameof(txSize));

        bool isSplit = false;
        if (depth < Av1Constants.MaxVarTransform)
        {
            int aboveTxWide = this.aboveNeighborContext.AboveTransformWidth[aboveBaseColumn + blkCol];
            int leftTxHigh = this.leftNeighborContext.LeftTransformHeight[leftBaseRow + blkRow];
            int ctx = Av1SymbolContextHelper.GetTransformPartitionContext(aboveTxWide, leftTxHigh, blockSize, txSize);
            isSplit = reader.ReadTransformPartitionSplit(ctx);
        }

        if (!isSplit)
        {
            leaves.Add(new VarTxLeaf(blkRow, blkCol, txSize));
            this.UpdateVarTxContext(aboveBaseColumn + blkCol, leftBaseRow + blkRow, txSize, txSize);
            return;
        }

        Av1TransformSize subTxSize = txSize.GetSubSize();
        if (subTxSize == Av1TransformSize.Size4x4)
        {
            // libaom decodeframe.c:1101 emits one tx-size record but coefficient decode iterates
            // (txSize wide / 4x4) x (txSize high / 4x4) TBs at sub-size. Expand to leaf-per-TB so
            // ParseTransformBlock visits every 4x4 sub-block.
            int rows4 = txSize.Get4x4HighCount();
            int cols4 = txSize.Get4x4WideCount();
            for (int row = 0; row < rows4; row++)
            {
                for (int col = 0; col < cols4; col++)
                {
                    leaves.Add(new VarTxLeaf(blkRow + row, blkCol + col, subTxSize));
                }
            }

            this.UpdateVarTxContext(aboveBaseColumn + blkCol, leftBaseRow + blkRow, subTxSize, txSize);
            return;
        }

        int subRows = subTxSize.Get4x4HighCount();
        int subCols = subTxSize.Get4x4WideCount();
        int rows = txSize.Get4x4HighCount();
        int cols = txSize.Get4x4WideCount();
        for (int row = 0; row < rows; row += subRows)
        {
            for (int col = 0; col < cols; col += subCols)
            {
                this.ReadVarTxLeaves(ref reader, blockSize, subTxSize, depth + 1, blkRow + row, blkCol + col, aboveBaseColumn, leftBaseRow, leaves);
            }
        }
    }

    /// <summary>
    /// Mirrors libaom <c>txfm_partition_update</c> (av1_common_int.h:1686): stamps the chosen
    /// leaf size into the above/left tx-context arrays over the txb_size span (in 4x4 units).
    /// </summary>
    private void UpdateVarTxContext(int aboveStartColumn, int leftStartRow, Av1TransformSize leafSize, Av1TransformSize txbSize)
    {
        int txbRows = txbSize.Get4x4HighCount();
        int txbCols = txbSize.Get4x4WideCount();
        int leafW = leafSize.GetWidth();
        int leafH = leafSize.GetHeight();
        Array.Fill(this.aboveNeighborContext.AboveTransformWidth, leafW, aboveStartColumn, txbCols);
        Array.Fill(this.leftNeighborContext.LeftTransformHeight, leafH, leftStartRow, txbRows);
    }

    private void UpdateTransformInfoFromLeaves(Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1BlockSize blockSize, List<VarTxLeaf> leaves)
    {
        int transformInfoYIndex = partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Y];
        int transformInfoUvIndex = partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Uv];
        Span<Av1TransformInfo> lumaTransformInfo = superblockInfo.GetTransformInfoY();
        Span<Av1TransformInfo> chromaTransformInfo = superblockInfo.GetTransformInfoUv();
        bool subX = this.SequenceHeader.ColorConfig.SubSamplingX;
        bool subY = this.SequenceHeader.ColorConfig.SubSamplingY;
        bool isLossLess = this.FrameHeader.LosslessArray[partitionInfo.ModeInfo.SegmentId];
        Av1TransformSize transformSizeUv = isLossLess ? Av1TransformSize.Size4x4 : blockSize.GetMaxUvTransformSize(subX, subY);
        int maxBlockWide = partitionInfo.GetMaxBlockWide(blockSize, false);
        int maxBlockHigh = partitionInfo.GetMaxBlockHigh(blockSize, false);

        // libaom: for IBC blocks (always <= 64x64), the entire block is a single forceSplitCount
        // mode-unit. Place every Y-plane leaf into bucket 0; reset the others.
        int totalLumaTransformUnitCount = leaves.Count;
        for (int i = 0; i < leaves.Count; i++)
        {
            VarTxLeaf leaf = leaves[i];
            lumaTransformInfo[transformInfoYIndex++] = new Av1TransformInfo(leaf.Size, leaf.BlockColumn, leaf.BlockRow);
        }

        this.transformUnitCount[(int)Av1Plane.Y][0] = totalLumaTransformUnitCount;
        this.transformUnitCount[(int)Av1Plane.Y][1] = 0;
        this.transformUnitCount[(int)Av1Plane.Y][2] = 0;
        this.transformUnitCount[(int)Av1Plane.Y][3] = 0;

        int totalChromaTransformUnitCount = 0;
        if (!this.SequenceHeader.ColorConfig.IsMonochrome && partitionInfo.IsChroma)
        {
            int stepCol = transformSizeUv.Get4x4WideCount();
            int stepRow = transformSizeUv.Get4x4HighCount();
            int unitHeight = Av1Math.RoundPowerOf2(maxBlockHigh, subY ? 1 : 0);
            int unitWidth = Av1Math.RoundPowerOf2(maxBlockWide, subX ? 1 : 0);
            for (int blockRow = 0; blockRow < unitHeight; blockRow += stepRow)
            {
                for (int blockColumn = 0; blockColumn < unitWidth; blockColumn += stepCol)
                {
                    chromaTransformInfo[transformInfoUvIndex++] = new Av1TransformInfo(transformSizeUv, blockColumn, blockRow);
                    totalChromaTransformUnitCount++;
                }
            }

            this.transformUnitCount[(int)Av1Plane.U][0] = totalChromaTransformUnitCount;
            this.transformUnitCount[(int)Av1Plane.V][0] = totalChromaTransformUnitCount;
            this.transformUnitCount[(int)Av1Plane.U][1] = 0;
            this.transformUnitCount[(int)Av1Plane.V][1] = 0;
            this.transformUnitCount[(int)Av1Plane.U][2] = 0;
            this.transformUnitCount[(int)Av1Plane.V][2] = 0;
            this.transformUnitCount[(int)Av1Plane.U][3] = 0;
            this.transformUnitCount[(int)Av1Plane.V][3] = 0;
        }

        // V slots are independent storage for V's CodeBlockFlag/Type. Field values are copied
        // by constructing a new instance from each U entry — assigning the reference would
        // alias the V slot to U so that any later writes (e.g. CodeBlockFlag from V's parse)
        // would also corrupt the U slot.
        if (totalChromaTransformUnitCount != 0)
        {
            int originalIndex = transformInfoUvIndex - totalChromaTransformUnitCount;
            for (int i = 0; i < totalChromaTransformUnitCount; i++)
            {
                chromaTransformInfo[transformInfoUvIndex + i] = new Av1TransformInfo(chromaTransformInfo[originalIndex + i]);
            }
        }

        partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Y] = totalLumaTransformUnitCount;
        partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Uv] = totalChromaTransformUnitCount;
        this.firstTransformOffset[(int)Av1PlaneType.Y] += totalLumaTransformUnitCount;
        this.firstTransformOffset[(int)Av1PlaneType.Uv] += totalChromaTransformUnitCount << 1;
    }

    private static Av1TransformType LookupYTransformTypeForChroma(Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, int chromaBlockRow, int chromaBlockColumn, int subX, int subY)
    {
        int yRow = chromaBlockRow << subY;
        int yColumn = chromaBlockColumn << subX;
        int firstYIndex = superblockInfo.TransformInfoIndexY + partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Y];
        int yCount = partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Y];
        Span<Av1TransformInfo> lumaTransformInfo = superblockInfo.GetTransformInfoY();
        for (int i = 0; i < yCount; i++)
        {
            Av1TransformInfo leaf = lumaTransformInfo[firstYIndex + i];
            int leafW = leaf.Size.Get4x4WideCount();
            int leafH = leaf.Size.Get4x4HighCount();
            if (yColumn >= leaf.OffsetX && yColumn < leaf.OffsetX + leafW &&
                yRow >= leaf.OffsetY && yRow < leaf.OffsetY + leafH)
            {
                return leaf.Type;
            }
        }

        return Av1TransformType.DctDct;
    }

    private unsafe void UpdateTransformInfo(Av1PartitionInfo partitionInfo, Av1SuperblockInfo superblockInfo, Av1BlockSize blockSize, Av1TransformSize transformSize)
    {
        int transformInfoYIndex = partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Y];
        int transformInfoUvIndex = partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Uv];
        Span<Av1TransformInfo> lumaTransformInfo = superblockInfo.GetTransformInfoY();
        Span<Av1TransformInfo> chromaTransformInfo = superblockInfo.GetTransformInfoUv();
        int totalLumaTransformUnitCount = 0;
        int totalChromaTransformUnitCount = 0;
        int forceSplitCount = 0;
        bool subX = this.SequenceHeader.ColorConfig.SubSamplingX;
        bool subY = this.SequenceHeader.ColorConfig.SubSamplingY;
        int maxBlockWide = partitionInfo.GetMaxBlockWide(blockSize, false);
        int maxBlockHigh = partitionInfo.GetMaxBlockHigh(blockSize, false);
        int width = 64 >> 2;
        int height = 64 >> 2;
        width = Math.Min(width, maxBlockWide);
        height = Math.Min(height, maxBlockHigh);

        bool isLossLess = this.FrameHeader.LosslessArray[partitionInfo.ModeInfo.SegmentId];
        Av1TransformSize transformSizeUv = isLossLess ? Av1TransformSize.Size4x4 : blockSize.GetMaxUvTransformSize(subX, subY);

        for (int idy = 0; idy < maxBlockHigh; idy += height)
        {
            for (int idx = 0; idx < maxBlockWide; idx += width, forceSplitCount++)
            {
                int lumaTransformUnitCount = 0;
                int chromaTransformUnitCount = 0;

                // Update Luminance Transform Info.
                int stepColumn = transformSize.Get4x4WideCount();
                int stepRow = transformSize.Get4x4HighCount();

                int unitHeight = Av1Math.RoundPowerOf2(Math.Min(height + idy, maxBlockHigh), 0);
                int unitWidth = Av1Math.RoundPowerOf2(Math.Min(width + idx, maxBlockWide), 0);
                for (int blockRow = idy; blockRow < unitHeight; blockRow += stepRow)
                {
                    for (int blockColumn = idx; blockColumn < unitWidth; blockColumn += stepColumn)
                    {
                        lumaTransformInfo[transformInfoYIndex] = new Av1TransformInfo(
                            transformSize, blockColumn, blockRow);
                        transformInfoYIndex++;
                        lumaTransformUnitCount++;
                        totalLumaTransformUnitCount++;
                    }
                }

                this.transformUnitCount[(int)Av1Plane.Y][forceSplitCount] = lumaTransformUnitCount;

                if (this.SequenceHeader.ColorConfig.IsMonochrome || !partitionInfo.IsChroma)
                {
                    continue;
                }

                // Update Chroma Transform Info.
                stepColumn = transformSizeUv.Get4x4WideCount();
                stepRow = transformSizeUv.Get4x4HighCount();

                unitHeight = Av1Math.RoundPowerOf2(Math.Min(height + idx, maxBlockHigh), subY ? 1 : 0);
                unitWidth = Av1Math.RoundPowerOf2(Math.Min(width + idx, maxBlockWide), subX ? 1 : 0);
                for (int blockRow = idy; blockRow < unitHeight; blockRow += stepRow)
                {
                    for (int blockColumn = idx; blockColumn < unitWidth; blockColumn += stepColumn)
                    {
                        chromaTransformInfo[transformInfoUvIndex] = new Av1TransformInfo(
                            transformSizeUv, blockColumn, blockRow);
                        transformInfoUvIndex++;
                        chromaTransformUnitCount++;
                        totalChromaTransformUnitCount++;
                    }
                }

                this.transformUnitCount[(int)Av1Plane.U][forceSplitCount] = chromaTransformUnitCount;
                this.transformUnitCount[(int)Av1Plane.V][forceSplitCount] = chromaTransformUnitCount;
            }
        }

        if (totalChromaTransformUnitCount != 0)
        {
            DebugGuard.IsTrue(
                (transformInfoUvIndex - totalChromaTransformUnitCount) ==
                partitionInfo.ModeInfo.FirstTransformLocation[(int)Av1PlaneType.Uv],
                nameof(totalChromaTransformUnitCount));
            int originalIndex = transformInfoUvIndex - totalChromaTransformUnitCount;
            for (int i = 0; i < totalChromaTransformUnitCount; i++)
            {
                chromaTransformInfo[transformInfoUvIndex + i] = new Av1TransformInfo(chromaTransformInfo[originalIndex + i]);
            }
        }

        partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Y] = totalLumaTransformUnitCount;
        partitionInfo.ModeInfo.TransformUnitsCount[(int)Av1PlaneType.Uv] = totalChromaTransformUnitCount;

        this.firstTransformOffset[(int)Av1PlaneType.Y] += totalLumaTransformUnitCount;
        this.firstTransformOffset[(int)Av1PlaneType.Uv] += totalChromaTransformUnitCount << 1;
    }

    /// <summary>
    /// 5.11.6. Mode info syntax.
    /// </summary>
    private void ReadModeInfo(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, Av1TileInfo tileInfo)
    {
        DebugGuard.IsTrue(this.FrameHeader.FrameType is ObuFrameType.KeyFrame or ObuFrameType.IntraOnlyFrame, "Only INTRA frames supported.");
        this.ReadIntraFrameModeInfo(ref reader, partitionInfo, tileInfo);
    }

    /// <summary>
    /// 5.11.7. Intra frame mode info syntax.
    /// </summary>
    private void ReadIntraFrameModeInfo(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo, Av1TileInfo tileInfo)
    {
        if (this.FrameHeader.SegmentationParameters.SegmentIdPrecedesSkip)
        {
            this.IntraSegmentId(ref reader, partitionInfo);
        }

        // this.skipMode = false;
        partitionInfo.ModeInfo.Skip = this.ReadSkip(ref reader, partitionInfo);
        if (!this.FrameHeader.SegmentationParameters.SegmentIdPrecedesSkip)
        {
            this.IntraSegmentId(ref reader, partitionInfo);
        }

        this.ReadCdef(ref reader, partitionInfo);

        if (this.FrameHeader.DeltaQParameters.IsPresent)
        {
            this.ReadDeltaQuantizerIndex(ref reader, partitionInfo);
            this.ReadDeltaLoopFilter(ref reader, partitionInfo);
        }

        partitionInfo.ReferenceFrame[0] = 0; // IntraFrame;
        partitionInfo.ReferenceFrame[1] = -1; // None;
        partitionInfo.ModeInfo.SetPaletteSizes(0, 0);
        partitionInfo.ModeInfo.UseIntraBlockCopy = false;
        if (this.AllowIntraBlockCopy())
        {
            partitionInfo.ModeInfo.UseIntraBlockCopy = reader.ReadUseIntraBlockCopy();
        }

        if (partitionInfo.ModeInfo.UseIntraBlockCopy)
        {
            partitionInfo.ModeInfo.YMode = Av1PredictionMode.DC;
            partitionInfo.ModeInfo.UvMode = Av1PredictionMode.DC;
            Av1MotionVectorReader.AssignIntraBlockCopyMotionVector(
                ref reader,
                partitionInfo,
                tileInfo,
                this.SequenceHeader.SuperblockModeInfoSize,
                this.FrameInfo.GetModeInfoAtMiPosition);
        }
        else
        {
            // this.IsInter = false;
            partitionInfo.ModeInfo.YMode = reader.ReadYMode(partitionInfo.AboveModeInfo, partitionInfo.LeftModeInfo);

            // 5.11.42.Intra angle info luma syntax.
            partitionInfo.ModeInfo.AngleDelta[(int)Av1PlaneType.Y] = IntraAngleInfo(ref reader, partitionInfo.ModeInfo.YMode, partitionInfo.ModeInfo.BlockSize);
            if (partitionInfo.IsChroma && !this.SequenceHeader.ColorConfig.IsMonochrome)
            {
                partitionInfo.ModeInfo.UvMode = reader.ReadIntraModeUv(partitionInfo.ModeInfo.YMode, this.IsChromaForLumaAllowed(partitionInfo));
                if (partitionInfo.ModeInfo.UvMode == Av1PredictionMode.UvChromaFromLuma)
                {
                    ReadChromaFromLumaAlphas(ref reader, partitionInfo.ModeInfo);
                }

                // 5.11.43.Intra angle info chroma syntax.
                partitionInfo.ModeInfo.AngleDelta[(int)Av1PlaneType.Uv] = IntraAngleInfo(ref reader, partitionInfo.ModeInfo.UvMode, partitionInfo.ModeInfo.BlockSize);
            }
            else
            {
                partitionInfo.ModeInfo.UvMode = Av1PredictionMode.DC;
            }

            if (partitionInfo.ModeInfo.BlockSize >= Av1BlockSize.Block8x8 &&
                partitionInfo.ModeInfo.BlockSize.GetWidth() <= 64 &&
                partitionInfo.ModeInfo.BlockSize.GetHeight() <= 64 &&
                this.FrameHeader.AllowScreenContentTools)
            {
                Av1PaletteDecoder.ReadPaletteModeInfo(ref reader, partitionInfo, this.SequenceHeader);
            }

            this.FilterIntraModeInfo(ref reader, partitionInfo);
        }
    }

    private bool AllowIntraBlockCopy()
        => (this.FrameHeader.FrameType is ObuFrameType.KeyFrame or ObuFrameType.IntraOnlyFrame) &&
            (this.SequenceHeader.ForceScreenContentTools > 0) &&
            this.FrameHeader.AllowIntraBlockCopy;

    private bool IsChromaForLumaAllowed(Av1PartitionInfo partitionInfo)
    {
        if (this.FrameHeader.LosslessArray[partitionInfo.ModeInfo.SegmentId])
        {
            // In lossless, CfL is available when the partition size is equal to the
            // transform size.
            bool subX = this.SequenceHeader.ColorConfig.SubSamplingX;
            bool subY = this.SequenceHeader.ColorConfig.SubSamplingY;
            Av1BlockSize planeBlockSize = partitionInfo.ModeInfo.BlockSize.GetSubsampled(subX, subY);
            return planeBlockSize == Av1BlockSize.Block4x4;
        }

        // Spec: CfL is available to luma partitions lesser than or equal to 32x32
        return partitionInfo.ModeInfo.BlockSize.GetWidth() <= 32 && partitionInfo.ModeInfo.BlockSize.GetHeight() <= 32;
    }

    private void FilterIntraModeInfo(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        partitionInfo.ModeInfo.FilterIntraModeInfo.UseFilterIntra = false;
        if (this.SequenceHeader.EnableFilterIntra &&
            partitionInfo.ModeInfo.YMode == Av1PredictionMode.DC &&
            partitionInfo.ModeInfo.GetPaletteSize(Av1PlaneType.Y) == 0 &&
            Math.Max(partitionInfo.ModeInfo.BlockSize.GetWidth(), partitionInfo.ModeInfo.BlockSize.GetHeight()) <= 32)
        {
            Av1FilterIntraMode filterIntraMode = reader.ReadFilterUltraMode(partitionInfo.ModeInfo.BlockSize);
            if (filterIntraMode != Av1FilterIntraMode.AllFilterIntraModes)
            {
                partitionInfo.ModeInfo.FilterIntraModeInfo.UseFilterIntra = true;
                partitionInfo.ModeInfo.FilterIntraModeInfo.Mode = filterIntraMode;
            }
        }
    }

    /// <summary>
    /// 5.11.45. Read CFL alphas syntax.
    /// </summary>
    private static void ReadChromaFromLumaAlphas(ref Av1SymbolDecoder reader, Av1BlockModeInfo modeInfo)
    {
        int jointSignPlus1 = reader.ReadChromFromLumaSign() + 1;
        int index = 0;
        if (jointSignPlus1 >= 3)
        {
            index = reader.ReadChromaFromLumaAlphaU(jointSignPlus1) << Av1Constants.ChromaFromLumaAlphabetSizeLog2;
        }

        if (jointSignPlus1 % 3 != 0)
        {
            index += reader.ReadChromaFromLumaAlphaV(jointSignPlus1);
        }

        modeInfo.ChromaFromLumaAlphaSign = jointSignPlus1 - 1;
        modeInfo.ChromaFromLumaAlphaIndex = index;
    }

    /// <summary>
    /// 5.11.42. and 5.11.43.
    /// </summary>
    private static int IntraAngleInfo(ref Av1SymbolDecoder reader, Av1PredictionMode mode, Av1BlockSize blockSize)
    {
        int angleDelta = 0;
        if (blockSize >= Av1BlockSize.Block8x8 && IsDirectionalMode(mode))
        {
            int symbol = reader.ReadAngleDelta(mode);
            angleDelta = symbol - Av1Constants.MaxAngleDelta;
        }

        return angleDelta;
    }

    private static bool IsDirectionalMode(Av1PredictionMode mode)
        => mode is >= Av1PredictionMode.Vertical and <= Av1PredictionMode.Directional67Degrees;

    /// <summary>
    /// 5.11.8. Intra segment ID syntax.
    /// </summary>
    private void IntraSegmentId(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        if (this.FrameHeader.SegmentationParameters.Enabled)
        {
            this.ReadSegmentId(ref reader, partitionInfo);
        }

        int blockWidth4x4 = partitionInfo.ModeInfo.BlockSize.Get4x4WideCount();
        int blockHeight4x4 = partitionInfo.ModeInfo.BlockSize.Get4x4HighCount();
        int modeInfoCountX = Math.Min(this.FrameHeader.ModeInfoColumnCount - partitionInfo.ColumnIndex, blockWidth4x4);
        int modeInfoCountY = Math.Min(this.FrameHeader.ModeInfoRowCount - partitionInfo.RowIndex, blockHeight4x4);
        int segmentId = partitionInfo.ModeInfo.SegmentId;
        for (int y = 0; y < modeInfoCountY; y++)
        {
            int[] segmentRow = this.segmentIds[partitionInfo.RowIndex + y];
            for (int x = 0; x < modeInfoCountX; x++)
            {
                segmentRow[partitionInfo.ColumnIndex + x] = segmentId;
            }
        }
    }

    /// <summary>
    /// 5.11.9. Read segment ID syntax.
    /// </summary>
    private void ReadSegmentId(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        int predictor;
        int prevUL = -1;
        int prevU = -1;
        int prevL = -1;
        int columnIndex = partitionInfo.ColumnIndex;
        int rowIndex = partitionInfo.RowIndex;
        if (partitionInfo.AvailableAbove && partitionInfo.AvailableLeft)
        {
            prevUL = Av1SymbolContextHelper.GetSegmentId(partitionInfo, this.FrameHeader, this.segmentIds, rowIndex - 1, columnIndex - 1);
        }

        if (partitionInfo.AvailableAbove)
        {
            prevU = Av1SymbolContextHelper.GetSegmentId(partitionInfo, this.FrameHeader, this.segmentIds, rowIndex - 1, columnIndex);
        }

        if (partitionInfo.AvailableLeft)
        {
            prevU = Av1SymbolContextHelper.GetSegmentId(partitionInfo, this.FrameHeader, this.segmentIds, rowIndex, columnIndex - 1);
        }

        if (prevU == -1)
        {
            predictor = prevL == -1 ? 0 : prevL;
        }
        else if (prevL == -1)
        {
            predictor = prevU;
        }
        else
        {
            predictor = prevU == prevUL ? prevU : prevL;
        }

        if (partitionInfo.ModeInfo.Skip)
        {
            partitionInfo.ModeInfo.SegmentId = predictor;
        }
        else
        {
            int ctx = prevUL < 0 ? 0 /* Edge cases */
                : prevUL == prevU && prevUL == prevL ? 2
                : prevUL == prevU || prevUL == prevL || prevU == prevL ? 1 : 0;
            int lastActiveSegmentId = this.FrameHeader.SegmentationParameters.LastActiveSegmentId;
            partitionInfo.ModeInfo.SegmentId = Av1SymbolContextHelper.NegativeDeinterleave(reader.ReadSegmentId(ctx), predictor, lastActiveSegmentId + 1);
        }
    }

    /// <summary>
    /// 5.11.56. Read CDEF syntax.
    /// </summary>
    /// <remarks>SVT: read_cdef</remarks>
    private void ReadCdef(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        if (partitionInfo.ModeInfo.Skip || this.FrameHeader.CodedLossless || !this.SequenceHeader.EnableCdef || this.FrameHeader.AllowIntraBlockCopy)
        {
            return;
        }

        int cdefSize4 = Av1BlockSize.Block64x64.Get4x4WideCount();
        int row = partitionInfo.RowIndex & cdefSize4;
        int col = partitionInfo.ColumnIndex & cdefSize4;
        int index = this.SequenceHeader.SuperblockSize == Av1BlockSize.Block128x128 ? Math.Max(1, col) + (Math.Max(1, row) << 1) : 0;
        if (partitionInfo.CdefStrength[index] == -1)
        {
            int cdfStrength = reader.ReadCdfStrength(this.FrameHeader.CdefParameters.BitCount);
            partitionInfo.CdefStrength[index] = cdfStrength;

            // Populate to nearby 64x64s if needed based on h4 & w4
            if (this.SequenceHeader.SuperblockSize == Av1BlockSize.Block128x128)
            {
                int w4 = partitionInfo.ModeInfo.BlockSize.Get4x4WideCount();
                int h4 = partitionInfo.ModeInfo.BlockSize.Get4x4HighCount();
                for (int i = row; i < row + h4; i += cdefSize4)
                {
                    for (int j = col; j < col + w4; j += cdefSize4)
                    {
                        partitionInfo.CdefStrength[Math.Max(1, j & cdefSize4) + (Math.Max(1, i & cdefSize4) << 1)] = cdfStrength;
                    }
                }
            }
        }
    }

    private void ReadDeltaLoopFilter(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        Av1BlockSize superBlockSize = this.SequenceHeader.Use128x128Superblock ? Av1BlockSize.Block128x128 : Av1BlockSize.Block64x64;
        if (this.FrameHeader.DeltaLoopFilterParameters.IsPresent ||
            (partitionInfo.ModeInfo.BlockSize == superBlockSize && partitionInfo.ModeInfo.Skip))
        {
            return;
        }

        if (this.FrameHeader.DeltaLoopFilterParameters.IsPresent)
        {
            int frameLoopFilterCount = 1;
            if (this.FrameHeader.DeltaLoopFilterParameters.IsMulti)
            {
                frameLoopFilterCount = this.SequenceHeader.ColorConfig.PlaneCount > 1 ? Av1Constants.FrameLoopFilterCount : Av1Constants.FrameLoopFilterCount - 2;
            }

            Span<int> currentDeltaLoopFilter = partitionInfo.SuperblockInfo.SuperblockDeltaLoopFilter;
            for (int i = 0; i < frameLoopFilterCount; i++)
            {
                int reducedDeltaLoopFilterLevel = reader.ReadDeltaLoopFilter();
                int deltaLoopFilterResolution = this.FrameHeader.DeltaLoopFilterParameters.Resolution;
                currentDeltaLoopFilter[i] = Av1Math.Clip3(-Av1Constants.MaxLoopFilter, Av1Constants.MaxLoopFilter, currentDeltaLoopFilter[i] + (reducedDeltaLoopFilterLevel << deltaLoopFilterResolution));
            }
        }
    }

    private bool ReadSkip(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        int segmentId = partitionInfo.ModeInfo.SegmentId;
        if (this.FrameHeader.SegmentationParameters.SegmentIdPrecedesSkip &&
            this.FrameHeader.SegmentationParameters.IsFeatureActive(segmentId, ObuSegmentationLevelFeature.Skip))
        {
            return true;
        }
        else
        {
            int aboveSkip = partitionInfo.AboveModeInfo != null && partitionInfo.AboveModeInfo.Skip ? 1 : 0;
            int leftSkip = partitionInfo.LeftModeInfo != null && partitionInfo.LeftModeInfo.Skip ? 1 : 0;
            return reader.ReadSkip(aboveSkip + leftSkip);
        }
    }

    /// <summary>
    /// SVT: read_delta_qindex
    /// </summary>
    private void ReadDeltaQuantizerIndex(ref Av1SymbolDecoder reader, Av1PartitionInfo partitionInfo)
    {
        Av1BlockSize superBlockSize = this.SequenceHeader.Use128x128Superblock ? Av1BlockSize.Block128x128 : Av1BlockSize.Block64x64;
        if (!this.FrameHeader.DeltaQParameters.IsPresent ||
            (partitionInfo.ModeInfo.BlockSize == superBlockSize && partitionInfo.ModeInfo.Skip))
        {
            return;
        }

        // libaom decodemv.c read_delta_qindex: only the SB top-left block reads the delta.
        int superblockMaskInModeInfoUnits = (this.SequenceHeader.Use128x128Superblock ? 32 : 16) - 1;
        if ((partitionInfo.ColumnIndex & superblockMaskInModeInfoUnits) != 0 ||
            (partitionInfo.RowIndex & superblockMaskInModeInfoUnits) != 0)
        {
            partitionInfo.SuperblockInfo.SuperblockDeltaQ = this.currentQuantizerIndex;
            return;
        }

        if (partitionInfo.ModeInfo.BlockSize != this.SequenceHeader.SuperblockSize || !partitionInfo.ModeInfo.Skip)
        {
            int reducedDeltaQuantizerIndex = reader.ReadDeltaQuantizerIndex();
            int deltaQuantizerResolution = this.FrameHeader.DeltaQParameters.Resolution;
            this.currentQuantizerIndex = Av1Math.Clip3(1, 255, this.currentQuantizerIndex + (reducedDeltaQuantizerIndex << deltaQuantizerResolution));
            partitionInfo.SuperblockInfo.SuperblockDeltaQ = this.currentQuantizerIndex;
        }
    }

    /*
    private static bool IsChroma(int rowIndex, int columnIndex, Av1BlockModeInfo blockMode, bool subSamplingX, bool subSamplingY)
    {
        int block4x4Width = blockMode.BlockSize.Get4x4WideCount();
        int block4x4Height = blockMode.BlockSize.Get4x4HighCount();
        bool xPos = (columnIndex & 0x1) > 0 || (block4x4Width & 0x1) > 0 || !subSamplingX;
        bool yPos = (rowIndex & 0x1) > 0 || (block4x4Height & 0x1) > 0 || !subSamplingY;
        return xPos && yPos;
    }*/

    /// <summary>
    /// SVT: partition_plane_context
    /// </summary>
    private int GetPartitionPlaneContext(Point location, Av1BlockSize blockSize, Av1TileInfo tileInfo, Av1SuperblockInfo superblockInfo)
    {
        // Maximum partition point is 8x8. Offset the log value occordingly.
        int aboveCtx = this.aboveNeighborContext.AbovePartitionWidth[location.X - tileInfo.ModeInfoColumnStart];
        int leftCtx = this.leftNeighborContext.LeftPartitionHeight[(location.Y - superblockInfo.ModeInfoPosition.Y) & Av1PartitionContext.Mask];
        int blockSizeLog = blockSize.Get4x4WidthLog2() - Av1BlockSize.Block8x8.Get4x4WidthLog2();
        int above = (aboveCtx >> blockSizeLog) & 0x1;
        int left = (leftCtx >> blockSizeLog) & 0x1;
        DebugGuard.IsTrue(blockSize.Get4x4WidthLog2() == blockSize.Get4x4HeightLog2(), "Blocks should be square.");
        DebugGuard.MustBeGreaterThanOrEqualTo(blockSizeLog, 0, nameof(blockSizeLog));
        return ((left << 1) + above) + (blockSizeLog * Av1Constants.PartitionProbabilitySet);
    }

    private void UpdatePartitionContext(Point modeInfoLocation, Av1TileInfo tileLoc, Av1SuperblockInfo superblockInfo, Av1BlockSize subSize, Av1BlockSize blockSize, Av1PartitionType partition)
    {
        if (blockSize >= Av1BlockSize.Block8x8)
        {
            int hbs = blockSize.Get4x4WideCount() / 2;
            Av1BlockSize blockSize2 = Av1PartitionType.Split.GetBlockSubSize(blockSize);
            switch (partition)
            {
                case Av1PartitionType.Split:
                    if (blockSize != Av1BlockSize.Block8x8)
                    {
                        break;
                    }

                    goto PARTITIONS;
                case Av1PartitionType.None:
                case Av1PartitionType.Horizontal:
                case Av1PartitionType.Vertical:
                case Av1PartitionType.Horizontal4:
                case Av1PartitionType.Vertical4:
                    PARTITIONS:
                    this.aboveNeighborContext.UpdatePartition(modeInfoLocation, tileLoc, subSize, blockSize);
                    this.leftNeighborContext.UpdatePartition(modeInfoLocation, superblockInfo, subSize, blockSize);
                    break;
                case Av1PartitionType.HorizontalA:
                    this.aboveNeighborContext.UpdatePartition(modeInfoLocation, tileLoc, blockSize2, subSize);
                    this.leftNeighborContext.UpdatePartition(modeInfoLocation, superblockInfo, blockSize2, subSize);
                    Point locHorizontalA = new(modeInfoLocation.X, modeInfoLocation.Y + hbs);
                    this.aboveNeighborContext.UpdatePartition(locHorizontalA, tileLoc, subSize, subSize);
                    this.leftNeighborContext.UpdatePartition(locHorizontalA, superblockInfo, subSize, subSize);
                    break;
                case Av1PartitionType.HorizontalB:
                    this.aboveNeighborContext.UpdatePartition(modeInfoLocation, tileLoc, subSize, subSize);
                    this.leftNeighborContext.UpdatePartition(modeInfoLocation, superblockInfo, subSize, subSize);
                    Point locHorizontalB = new(modeInfoLocation.X, modeInfoLocation.Y + hbs);
                    this.aboveNeighborContext.UpdatePartition(locHorizontalB, tileLoc, blockSize2, subSize);
                    this.leftNeighborContext.UpdatePartition(locHorizontalB, superblockInfo, blockSize2, subSize);
                    break;
                case Av1PartitionType.VerticalA:
                    this.aboveNeighborContext.UpdatePartition(modeInfoLocation, tileLoc, blockSize2, subSize);
                    this.leftNeighborContext.UpdatePartition(modeInfoLocation, superblockInfo, blockSize2, subSize);
                    Point locVerticalA = new(modeInfoLocation.X + hbs, modeInfoLocation.Y);
                    this.aboveNeighborContext.UpdatePartition(locVerticalA, tileLoc, subSize, subSize);
                    this.leftNeighborContext.UpdatePartition(locVerticalA, superblockInfo, subSize, subSize);
                    break;
                case Av1PartitionType.VerticalB:
                    this.aboveNeighborContext.UpdatePartition(modeInfoLocation, tileLoc, subSize, subSize);
                    this.leftNeighborContext.UpdatePartition(modeInfoLocation, superblockInfo, subSize, subSize);
                    Point locVerticalB = new(modeInfoLocation.X + hbs, modeInfoLocation.Y);
                    this.aboveNeighborContext.UpdatePartition(locVerticalB, tileLoc, blockSize2, subSize);
                    this.leftNeighborContext.UpdatePartition(locVerticalB, superblockInfo, blockSize2, subSize);
                    break;
                default:
                    throw new InvalidImageContentException($"Unknown partition type: {partition}");
            }
        }
    }

    private readonly record struct VarTxLeaf(int BlockRow, int BlockColumn, Av1TransformSize Size);
}
