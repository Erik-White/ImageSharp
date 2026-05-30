// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Cdef;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Quantification;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

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

        bool doLoopRestoration = false;
        bool doUpscale = false;

        Av1LoopFilterDecoder.DecodeFrame(this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer);

        if (doLoopRestoration)
        {
            // LoopRestorationSaveBoundaryLines(false);
        }

        Av1CdefUnitDriver.DecodeFrame(this.configuration, this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer);

        // SuperResolutionUpscaling(doUpscale);
        if (doLoopRestoration && doUpscale)
        {
            // LoopRestorationSaveBoundaryLines(true);
        }

        // DecodeLoopRestoration(doLoopRestoration);
        // PadPicture();
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
