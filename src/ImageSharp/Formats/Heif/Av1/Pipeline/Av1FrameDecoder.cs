// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;
using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Quantification;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline;

internal class Av1FrameDecoder : IAv1FrameDecoder
{
    private readonly Configuration configuration;
    private readonly ObuSequenceHeader sequenceHeader;
    private readonly ObuFrameHeader frameHeader;
    private readonly Av1FrameInfo frameInfo;
    private readonly Av1FrameBuffer<byte> frameBuffer;
    private readonly Av1InverseQuantizer inverseQuantizer;
    private readonly Av1DeQuantizationContext deQuants;
    private readonly Av1BlockDecoder blockDecoder;

    public Av1FrameDecoder(Configuration configuration, ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
    {
        this.configuration = configuration;
        this.sequenceHeader = sequenceHeader;
        this.frameHeader = frameHeader;
        this.frameInfo = frameInfo;
        this.frameBuffer = frameBuffer;
        this.inverseQuantizer = new(sequenceHeader, frameHeader);
        this.deQuants = new(sequenceHeader, frameHeader);
        this.blockDecoder = new(configuration, this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer, this.inverseQuantizer);
    }

    public void DecodeFrame()
    {
        ObuTileGroupHeader tilesInfo = this.frameHeader.TilesInfo;
        for (int tileRow = 0; tileRow < tilesInfo.TileRowCount; tileRow++)
        {
            for (int tileColumn = 0; tileColumn < tilesInfo.TileColumnCount; tileColumn++)
            {
                this.DecodeTile(tileRow, tileColumn);
            }
        }

        Av1LoopFilterDecoder.DecodeFrame(this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer);

        // Loop restoration's get_source_sample (spec 7.17.6) reads out-of-stripe samples from
        // the deblocked-but-pre-CDEF frame, so snapshot the planes before CDEF overwrites them.
        bool doLoopRestoration = this.frameHeader.LoopRestorationParameters.UsesLoopRestoration &&
            !this.frameHeader.AllLossless &&
            !this.frameHeader.AllowIntraBlockCopy;
        byte[][]? preCdefPlanes = doLoopRestoration ? this.SnapshotPlanes() : null;

        Av1CdefUnitDriver.DecodeFrame(this.configuration, this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer);

        if (doLoopRestoration)
        {
            Av1LoopRestorationDecoder.DecodeFrame(this.configuration, this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer, preCdefPlanes!);
        }

        // SuperResolutionUpscaling not yet implemented.
        // PadPicture();
    }

    /// <summary>
    /// Clones the reconstructed (deblocked, pre-CDEF) plane samples into plane-local arrays
    /// for loop restoration's out-of-stripe source fetch (spec 7.17.6 UpscaledCurrFrame).
    /// </summary>
    private byte[][] SnapshotPlanes()
    {
        int planeCount = this.sequenceHeader.ColorConfig.IsMonochrome ? 1 : 3;
        bool subX = this.sequenceHeader.ColorConfig.SubSamplingX;
        bool subY = this.sequenceHeader.ColorConfig.SubSamplingY;
        int frameWidth = this.frameHeader.FrameSize.FrameWidth;
        int frameHeight = this.frameHeader.FrameSize.FrameHeight;

        byte[][] planes = new byte[3][];
        for (int plane = 0; plane < planeCount; plane++)
        {
            int planeSubX = plane > 0 && subX ? 1 : 0;
            int planeSubY = plane > 0 && subY ? 1 : 0;
            int planeWidth = Av1Math.RoundPowerOf2(frameWidth, planeSubX);
            int planeHeight = Av1Math.RoundPowerOf2(frameHeight, planeSubY);
            Buffer2D<byte> buffer = plane switch
            {
                0 => this.frameBuffer.BufferY!,
                1 => this.frameBuffer.BufferCb!,
                _ => this.frameBuffer.BufferCr!,
            };
            int originX = this.frameBuffer.OriginX >> planeSubX;
            int originY = this.frameBuffer.OriginY >> planeSubY;
            byte[] snapshot = new byte[planeWidth * planeHeight];
            for (int y = 0; y < planeHeight; y++)
            {
                buffer.DangerousGetRowSpan(originY + y).Slice(originX, planeWidth).CopyTo(snapshot.AsSpan(y * planeWidth, planeWidth));
            }

            planes[plane] = snapshot;
        }

        return planes;
    }

    /// <summary>
    /// SVT: decode_tile / decode_tile_row.
    /// Mirrors Av1TileReader.ReadTile so reconstruction visits the same superblock grid the parser populated.
    /// </summary>
    private void DecodeTile(int tileRow, int tileColumn)
    {
        ObuTileGroupHeader tilesInfo = this.frameHeader.TilesInfo;
        int superblockModeInfoSize = this.sequenceHeader.SuperblockModeInfoSize;
        int modeInfoRowStart = tilesInfo.TileRowStartModeInfo[tileRow];
        int modeInfoRowEnd = tilesInfo.TileRowStartModeInfo[tileRow + 1];
        int modeInfoColumnStart = tilesInfo.TileColumnStartModeInfo[tileColumn];
        int modeInfoColumnEnd = tilesInfo.TileColumnStartModeInfo[tileColumn + 1];
        Av1TileInfo tileInfo = new(tileRow, tileColumn, this.frameHeader);

        for (int modeInfoRow = modeInfoRowStart; modeInfoRow < modeInfoRowEnd; modeInfoRow += superblockModeInfoSize)
        {
            int superblockRow = modeInfoRow << Av1Constants.ModeInfoSizeLog2 >> this.sequenceHeader.SuperblockSizeLog2;
            for (int modeInfoColumn = modeInfoColumnStart; modeInfoColumn < modeInfoColumnEnd; modeInfoColumn += superblockModeInfoSize)
            {
                int superblockColumn = modeInfoColumn << Av1Constants.ModeInfoSizeLog2 >> this.sequenceHeader.SuperblockSizeLog2;
                Av1SuperblockInfo superblockInfo = this.frameInfo.GetSuperblock(new Point(superblockColumn, superblockRow));
                Point modeInfoPosition = new(modeInfoColumn, modeInfoRow);
                this.DecodeSuperblock(modeInfoPosition, superblockInfo, tileInfo);
            }
        }
    }

    /// <summary>
    /// SVT: svt_aom_decode_super_block
    /// </summary>
    public void DecodeSuperblock(Point modeInfoPosition, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo)
    {
        this.blockDecoder.UpdateSuperblock(superblockInfo);
        this.inverseQuantizer.UpdateDequant(this.deQuants, superblockInfo);
        this.DecodePartition(modeInfoPosition, superblockInfo, tileInfo);
    }

    /// <summary>
    /// SVT: decode_partition
    /// </summary>
    private void DecodePartition(Point modeInfoPosition, Av1SuperblockInfo superblockInfo, Av1TileInfo tileInfo)
    {
        for (int i = 0; i < superblockInfo.BlockCount; i++)
        {
            Av1BlockModeInfo modeInfo = this.frameInfo.GetModeInfoByIndex(superblockInfo.FirstModeInfoIndex + i);
            Point subPosition = modeInfo.PositionInSuperblock;
            Av1BlockSize subSize = modeInfo.BlockSize;
            Point globalPosition = new(modeInfoPosition.X + subPosition.X, modeInfoPosition.Y + subPosition.Y);
            this.blockDecoder.DecodeBlock(modeInfo, globalPosition, subSize, superblockInfo, tileInfo);
        }
    }
}
