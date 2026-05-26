// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Buffers.Binary;
using SixLabors.ImageSharp.Common.Helpers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Heif;

/// <summary>
/// Decoder for a grid of several <see cref="HeifItem"/> into a single image.
/// </summary>
internal class GridHeifItemDecoder<TPixel> : IHeifItemDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly IList<HeifItem> items;
    private readonly IList<HeifItemLink> itemLinks;
    private readonly IDictionary<uint, IMemoryOwner<byte>> buffers;
    private readonly Func<Heif4CharCode, IHeifItemDecoder<TPixel>?> tileDecoderFactory;

    public GridHeifItemDecoder(Configuration configuration, IList<HeifItem> items, IList<HeifItemLink> itemLinks, IDictionary<uint, IMemoryOwner<byte>> buffers)
        : this(configuration, items, itemLinks, buffers, HeifCompressionFactory.GetDecoder<TPixel>)
    {
    }

    internal GridHeifItemDecoder(Configuration configuration, IList<HeifItem> items, IList<HeifItemLink> itemLinks, IDictionary<uint, IMemoryOwner<byte>> buffers, Func<Heif4CharCode, IHeifItemDecoder<TPixel>?> tileDecoderFactory)
    {
        this.configuration = configuration;
        this.items = items;
        this.itemLinks = itemLinks;
        this.buffers = buffers;
        this.tileDecoderFactory = tileDecoderFactory;
    }

    /// <summary>
    /// Gets the item type this decoder decodes, which is <see cref="Heif4CharCode.Grid"/>.
    /// </summary>
    public Heif4CharCode Type => Heif4CharCode.Grid;

    /// <summary>
    /// Gets the compression method this doceder uses.
    /// </summary>
    public HeifCompressionMethod CompressionMethod { get; private set; }

    /// <summary>
    /// Decode the grid item, compositing tile sub-images into a single output image.
    /// </summary>
    /// <remarks>
    /// Grid item header per ISO/IEC 23008-12 section 6.6.2.3.2:
    /// <code>
    /// unsigned int(8) version = 0;
    /// unsigned int(8) flags;
    /// unsigned int(8) rows_minus_one;
    /// unsigned int(8) columns_minus_one;
    /// unsigned int(FieldLength) output_width;
    /// unsigned int(FieldLength) output_height;
    /// </code>
    /// where <c>FieldLength</c> is 16 if <c>(flags &amp; 1) == 0</c>, else 32.
    /// </remarks>
    public Image<TPixel> DecodeItemData(Configuration configuration, HeifItem gridItem, Span<byte> data)
    {
        ParseGridHeader(data, out int rows, out int columns, out int outputWidth, out int outputHeight);

        HeifItemLink link = this.itemLinks.First(l => l.SourceId == gridItem.Id && l.Type == Heif4CharCode.Dimg);
        List<uint> tileIds = link.DestinationIds;
        int expectedTiles = rows * columns;
        if (tileIds.Count < expectedTiles)
        {
            throw new ImageFormatException($"Grid item {gridItem.Id} expects {expectedTiles} tiles ({rows}x{columns}) but only {tileIds.Count} were referenced.");
        }

        using DisposableList<Image<TPixel>> gridTiles = new(expectedTiles);
        for (int i = 0; i < expectedTiles; i++)
        {
            uint id = tileIds[i];
            HeifItem? tileItem = this.items.FirstOrDefault(item => item.Id == id);
            if (tileItem == null)
            {
                throw new ImageFormatException($"Grid tile item {id} not found.");
            }

            IHeifItemDecoder<TPixel>? tileDecoder = this.tileDecoderFactory(tileItem.Type);
            if (tileDecoder == null)
            {
                throw new ImageFormatException($"No decoder available for tile item type '{tileItem.Type}'.");
            }

            this.CompressionMethod = tileDecoder.CompressionMethod;
            IMemoryOwner<byte> itemMemory = this.buffers[tileItem.Id];
            gridTiles.Add(tileDecoder.DecodeItemData(this.configuration, tileItem, itemMemory.GetSpan()));
        }

        // All tiles are required by spec to have identical dimensions.
        int tileWidth = gridTiles[0].Width;
        int tileHeight = gridTiles[0].Height;

        Image<TPixel> result = new(this.configuration, outputWidth, outputHeight);
        try
        {
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    Image<TPixel> tile = gridTiles[(row * columns) + col];
                    int x = col * tileWidth;
                    int y = row * tileHeight;

                    // The output image may be smaller than rows*tileHeight by columns*tileWidth;
                    // tiles overflowing the output extent are clipped.
                    int copyWidth = Math.Min(tileWidth, outputWidth - x);
                    int copyHeight = Math.Min(tileHeight, outputHeight - y);
                    if (copyWidth <= 0 || copyHeight <= 0)
                    {
                        continue;
                    }

                    CopyTileRegion(tile, result, x, y, copyWidth, copyHeight);
                }
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    private static void ParseGridHeader(Span<byte> data, out int rows, out int columns, out int outputWidth, out int outputHeight)
    {
        if (data.Length < 8)
        {
            throw new ImageFormatException("Grid item data is too short to contain a header.");
        }

        byte version = data[0];
        if (version != 0)
        {
            throw new ImageFormatException($"Unsupported grid item header version {version}.");
        }

        byte flags = data[1];
        rows = data[2] + 1;
        columns = data[3] + 1;
        bool largeFields = (flags & 0x01) != 0;
        if (largeFields)
        {
            if (data.Length < 12)
            {
                throw new ImageFormatException("Grid item data is too short for 32-bit dimension fields.");
            }

            outputWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            outputHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        }
        else
        {
            outputWidth = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
            outputHeight = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        }
    }

    private static void CopyTileRegion(Image<TPixel> tile, Image<TPixel> destination, int destX, int destY, int width, int height)
    {
        Buffer2D<TPixel> tileBuffer = tile.Frames.RootFrame.PixelBuffer;
        Buffer2D<TPixel> destBuffer = destination.Frames.RootFrame.PixelBuffer;
        for (int y = 0; y < height; y++)
        {
            Span<TPixel> source = tileBuffer.DangerousGetRowSpan(y).Slice(0, width);
            Span<TPixel> target = destBuffer.DangerousGetRowSpan(destY + y).Slice(destX, width);
            source.CopyTo(target);
        }
    }
}
