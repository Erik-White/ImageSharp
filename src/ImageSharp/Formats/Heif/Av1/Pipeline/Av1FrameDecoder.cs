// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;
using SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.Quantification;
using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling;
using SixLabors.ImageSharp.Formats.Heif.Av1.Transform;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline;

internal class Av1FrameDecoder : IAv1FrameDecoder
{
    private readonly ObuSequenceHeader sequenceHeader;
    private readonly ObuFrameHeader frameHeader;
    private readonly Av1FrameInfo frameInfo;
    private readonly Av1FrameBuffer<byte> frameBuffer;
    private readonly Av1InverseQuantizer inverseQuantizer;
    private readonly Av1DeQuantizationContext deQuants;
    private readonly Av1BlockDecoder blockDecoder;

    public Av1FrameDecoder(Configuration configuration, ObuSequenceHeader sequenceHeader, ObuFrameHeader frameHeader, Av1FrameInfo frameInfo, Av1FrameBuffer<byte> frameBuffer)
    {
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
        for (int column = 0; column < this.frameHeader.TilesInfo.TileColumnCount; column++)
        {
            this.DecodeFrameTiles(column);
        }

        bool doLoopRestoration = false;
        bool doUpscale = false;

        Av1LoopFilterDecoder.DecodeFrame(this.sequenceHeader, this.frameHeader, this.frameInfo, this.frameBuffer);

        if (doLoopRestoration)
        {
            // LoopRestorationSaveBoundaryLines(false);
        }

        // DecodeCdef();
        // SuperResolutionUpscaling(doUpscale);
        if (doLoopRestoration && doUpscale)
        {
            // LoopRestorationSaveBoundaryLines(true);
        }

        // DecodeLoopRestoration(doLoopRestoration);
        // PadPicture();
    }

    /// <summary>
    /// SVT: decode_tile
    /// </summary>
    private void DecodeFrameTiles(int tileColumn)
    {
        int tileRowCount = this.frameHeader.TilesInfo.TileRowCount;
        int tileCount = tileRowCount * this.frameHeader.TilesInfo.TileColumnCount;
        for (int row = 0; row < tileRowCount; row++)
        {
            int superblockRowTileStart = this.frameHeader.TilesInfo.TileRowStartModeInfo[row] << Av1Constants.ModeInfoSizeLog2 >>
                this.sequenceHeader.SuperblockSizeLog2;
            int superblockRow = row + superblockRowTileStart;

            int modeInfoRow = superblockRow << this.sequenceHeader.SuperblockSizeLog2 >> Av1Constants.ModeInfoSizeLog2;

            // EbColorConfig* color_config = &dec_mod_ctxt->seq_header->color_config;
            // svt_cfl_init(&dec_mod_ctxt->cfl_ctx, color_config);
            this.DecodeTileRow(row, tileColumn, modeInfoRow, superblockRow);
        }
    }

    /// <summary>
    /// SVT: decode_tile_row
    /// </summary>
    private void DecodeTileRow(int tileRow, int tileColumn, int modeInfoRow, int superblockRow)
    {
        int superblockModeInfoSizeLog2 = this.sequenceHeader.SuperblockSizeLog2 - Av1Constants.ModeInfoSizeLog2;
        int superblockRowTileStart = this.frameHeader.TilesInfo.TileRowStartModeInfo[tileRow] << Av1Constants.ModeInfoSizeLog2 >>
            this.sequenceHeader.SuperblockSizeLog2;

        int superblockRowInTile = superblockRow - superblockRowTileStart;

        ObuTileGroupHeader tileInfo = this.frameHeader.TilesInfo;
        for (int modeInfoColumn = tileInfo.TileColumnStartModeInfo[tileColumn]; modeInfoColumn < tileInfo.TileColumnStartModeInfo[tileColumn + 1];
             modeInfoColumn += this.sequenceHeader.SuperblockModeInfoSize)
        {
            int superblockColumn = modeInfoColumn << Av1Constants.ModeInfoSizeLog2 >> this.sequenceHeader.SuperblockSizeLog2;

            Av1SuperblockInfo superblockInfo = this.frameInfo.GetSuperblock(new Point(superblockColumn, superblockRow));

            Point modeInfoPosition = new(modeInfoColumn, modeInfoRow);
            this.DecodeSuperblock(modeInfoPosition, superblockInfo, new Av1TileInfo(tileRow, tileColumn, this.frameHeader));
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
