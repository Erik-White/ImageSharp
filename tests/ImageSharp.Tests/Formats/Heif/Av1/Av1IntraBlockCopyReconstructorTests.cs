// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1IntraBlockCopyReconstructorTests
{
    [Fact]
    public void CopySingleRowReplicatesSourceBytes()
    {
        using Buffer2D<byte> plane = Configuration.Default.MemoryAllocator.Allocate2D<byte>(16, 4, AllocationOptions.Clean);
        Span<byte> srcRow = plane.DangerousGetRowSpan(0);
        for (int i = 0; i < 8; i++)
        {
            srcRow[i] = (byte)(i + 1);
        }

        Av1IntraBlockCopyReconstructor.Copy(plane, srcX: 0, srcY: 0, dstX: 8, dstY: 0, width: 8, height: 1);

        Span<byte> dstRow = plane.DangerousGetRowSpan(0);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal((byte)(i + 1), dstRow[8 + i]);
        }
    }

    [Fact]
    public void CopyMultiRowCopiesEachRowIndependently()
    {
        using Buffer2D<byte> plane = Configuration.Default.MemoryAllocator.Allocate2D<byte>(8, 8, AllocationOptions.Clean);
        for (int y = 0; y < 4; y++)
        {
            Span<byte> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < 4; x++)
            {
                row[x] = (byte)((y * 4) + x + 1);
            }
        }

        Av1IntraBlockCopyReconstructor.Copy(plane, srcX: 0, srcY: 0, dstX: 4, dstY: 4, width: 4, height: 4);

        for (int y = 0; y < 4; y++)
        {
            Span<byte> dstRow = plane.DangerousGetRowSpan(4 + y);
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal((byte)((y * 4) + x + 1), dstRow[4 + x]);
            }
        }
    }

    [Fact]
    public void CopyAboveLeftAdjacentDoesNotCorruptSourceMidCopy()
    {
        // Wavefront-safe case: source rectangle ends one row above and at most aligned-with destination start.
        // Destination rows are written top-down; their writes must not overwrite source rows that are still being read.
        using Buffer2D<byte> plane = Configuration.Default.MemoryAllocator.Allocate2D<byte>(8, 8, AllocationOptions.Clean);
        for (int y = 0; y < 4; y++)
        {
            Span<byte> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < 4; x++)
            {
                row[x] = 0xAA;
            }
        }

        Av1IntraBlockCopyReconstructor.Copy(plane, srcX: 0, srcY: 0, dstX: 0, dstY: 4, width: 4, height: 4);

        for (int y = 4; y < 8; y++)
        {
            Span<byte> dstRow = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal((byte)0xAA, dstRow[x]);
            }
        }
    }

    [Fact]
    public void CopyDoesNotTouchPixelsOutsideRectangle()
    {
        using Buffer2D<byte> plane = Configuration.Default.MemoryAllocator.Allocate2D<byte>(16, 8, AllocationOptions.Clean);
        for (int y = 0; y < 8; y++)
        {
            plane.DangerousGetRowSpan(y).Fill(0x55);
        }

        // Stamp a distinct source pattern.
        for (int y = 0; y < 4; y++)
        {
            Span<byte> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < 4; x++)
            {
                row[x] = 0xEE;
            }
        }

        Av1IntraBlockCopyReconstructor.Copy(plane, srcX: 0, srcY: 0, dstX: 8, dstY: 4, width: 4, height: 4);

        // Pixels outside the destination rectangle keep their original 0x55 fill (or 0xEE source fill).
        for (int y = 4; y < 8; y++)
        {
            Span<byte> row = plane.DangerousGetRowSpan(y);
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal((byte)0x55, row[x]);
            }

            for (int x = 12; x < 16; x++)
            {
                Assert.Equal((byte)0x55, row[x]);
            }
        }
    }
}
