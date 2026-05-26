// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Formats.Heif;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Tests.Formats.Heif;

[Trait("Format", "Heif")]
public class GridHeifItemDecoderTests
{
    [Fact]
    public void DecodeItemDataCompositesTilesIntoFullOutput()
    {
        // 2x2 grid of 4x4 solid-color tiles into a 8x8 output.
        Rgb24[] colors =
        [
            new(255, 0, 0),
            new(0, 255, 0),
            new(0, 0, 255),
            new(255, 255, 0),
        ];

        Span<byte> header =
        [
            0x00, 0x00, // version=0, flags=0 (16-bit dimensions)
            0x01, 0x01, // rows_minus_one=1, columns_minus_one=1
            0x00, 0x08, 0x00, 0x08, // output_width=8, output_height=8
        ];

        using Image<Rgb24> result = DecodeGrid(4, 4, header, colors);

        Assert.Equal(8, result.Width);
        Assert.Equal(8, result.Height);
        AssertTileFill(result, 0, 0, 4, 4, colors[0]);
        AssertTileFill(result, 4, 0, 4, 4, colors[1]);
        AssertTileFill(result, 0, 4, 4, 4, colors[2]);
        AssertTileFill(result, 4, 4, 4, 4, colors[3]);
    }

    [Fact]
    public void DecodeItemDataClipsTilesToDeclaredOutput()
    {
        // 2x2 grid of 4x4 tiles, but declared output is 6x6 — tile bottom and right edges are clipped.
        Rgb24[] colors =
        [
            new(255, 0, 0),
            new(0, 255, 0),
            new(0, 0, 255),
            new(255, 255, 0),
        ];

        Span<byte> header =
        [
            0x00, 0x00,
            0x01, 0x01,
            0x00, 0x06, 0x00, 0x06,
        ];

        using Image<Rgb24> result = DecodeGrid(4, 4, header, colors);

        Assert.Equal(6, result.Width);
        Assert.Equal(6, result.Height);
        AssertTileFill(result, 0, 0, 4, 4, colors[0]);
        AssertTileFill(result, 4, 0, 2, 4, colors[1]);
        AssertTileFill(result, 0, 4, 4, 2, colors[2]);
        AssertTileFill(result, 4, 4, 2, 2, colors[3]);
    }

    [Fact]
    public void DecodeItemDataParsesLargeFieldHeader()
    {
        // flags & 1 == 1 means 32-bit output width/height.
        Rgb24[] colors = [new(10, 20, 30)];
        Span<byte> header =
        [
            0x00, 0x01, // version=0, flags=1
            0x00, 0x00, // 1x1 grid
            0x00, 0x00, 0x00, 0x04,
            0x00, 0x00, 0x00, 0x04,
        ];

        using Image<Rgb24> result = DecodeGrid(4, 4, header, colors);

        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        AssertTileFill(result, 0, 0, 4, 4, colors[0]);
    }

    [Fact]
    public void DecodeItemDataThrowsWhenTooFewTilesReferenced()
    {
        // Header declares 2x2 (4 tiles) but we only supply 3.
        Rgb24[] colors =
        [
            new(255, 0, 0),
            new(0, 255, 0),
            new(0, 0, 255),
        ];

        Span<byte> header =
        [
            0x00, 0x00,
            0x01, 0x01,
            0x00, 0x08, 0x00, 0x08,
        ];

        byte[] headerArray = header.ToArray();
        Assert.Throws<ImageFormatException>(() => DecodeGrid(4, 4, headerArray, colors).Dispose());
    }

    [Fact]
    public void DecodeItemDataThrowsOnUnsupportedHeaderVersion()
    {
        Rgb24[] colors = [new(0, 0, 0)];
        Span<byte> header =
        [
            0x01, 0x00, // version=1
            0x00, 0x00,
            0x00, 0x04, 0x00, 0x04,
        ];

        byte[] headerArray = header.ToArray();
        Assert.Throws<ImageFormatException>(() => DecodeGrid(4, 4, headerArray, colors).Dispose());
    }

    private static Image<Rgb24> DecodeGrid(int tileWidth, int tileHeight, Span<byte> headerBytes, Rgb24[] tileColors)
    {
        const uint gridId = 1;
        HeifItem gridItem = new(Heif4CharCode.Grid, gridId);
        List<HeifItem> items = [gridItem];
        Dictionary<uint, IMemoryOwner<byte>> buffers = [];
        HeifItemLink dimgLink = new(Heif4CharCode.Dimg, gridId);

        for (int i = 0; i < tileColors.Length; i++)
        {
            uint tileId = (uint)(100 + i);
            items.Add(new HeifItem(Heif4CharCode.Av01, tileId));
            dimgLink.DestinationIds.Add(tileId);
            buffers[tileId] = Configuration.Default.MemoryAllocator.Allocate<byte>(1);
        }

        try
        {
            FakeTileDecoder factory = new(tileWidth, tileHeight, tileColors);
            GridHeifItemDecoder<Rgb24> gridDecoder = new(
                Configuration.Default,
                items,
                [dimgLink],
                buffers,
                _ => factory);

            return gridDecoder.DecodeItemData(Configuration.Default, gridItem, headerBytes);
        }
        finally
        {
            foreach (IMemoryOwner<byte> owner in buffers.Values)
            {
                owner.Dispose();
            }
        }
    }

    private static void AssertTileFill(Image<Rgb24> image, int x, int y, int width, int height, Rgb24 expected)
    {
        for (int row = 0; row < height; row++)
        {
            Span<Rgb24> rowSpan = image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y + row);
            for (int col = 0; col < width; col++)
            {
                Assert.Equal(expected, rowSpan[x + col]);
            }
        }
    }

    private sealed class FakeTileDecoder : IHeifItemDecoder<Rgb24>
    {
        private readonly int width;
        private readonly int height;
        private readonly Rgb24[] colors;
        private int index;

        public FakeTileDecoder(int width, int height, Rgb24[] colors)
        {
            this.width = width;
            this.height = height;
            this.colors = colors;
        }

        public Heif4CharCode Type => Heif4CharCode.Av01;

        public HeifCompressionMethod CompressionMethod => HeifCompressionMethod.Av1;

        public Image<Rgb24> DecodeItemData(Configuration configuration, HeifItem item, Span<byte> data)
        {
            Rgb24 color = this.colors[this.index++];
            Image<Rgb24> tile = new(configuration, this.width, this.height);
            tile.Frames.RootFrame.PixelBuffer.DangerousGetSingleSpan().Fill(color);
            return tile;
        }
    }
}
