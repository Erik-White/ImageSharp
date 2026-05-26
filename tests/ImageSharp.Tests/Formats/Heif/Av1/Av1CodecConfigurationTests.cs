// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1;

namespace SixLabors.ImageSharp.Tests.Formats.Heif.Av1;

[Trait("Format", "Avif")]
public class Av1CodecConfigurationTests
{
    [Fact]
    public void ParsesFixedFieldsForCommonAvifConfiguration()
    {
        // Header bytes for an 8-bit 4:2:0 profile-0 AVIF (Irvine_CA.avif typical layout):
        //   marker=1, version=1, seq_profile=0, seq_level_idx_0=8, seq_tier_0=0,
        //   high_bitdepth=0, twelve_bit=0, monochrome=0,
        //   chroma_subsampling_x=1, chroma_subsampling_y=1, chroma_sample_position=0,
        //   reserved=0, initial_presentation_delay_present=0, reserved=0
        // Encoded as bytes: 0x81, 0x08, 0x0C, 0x00
        Span<byte> header = [0x81, 0x08, 0x0C, 0x00];
        Av1CodecConfiguration config = new(header);

        Assert.Equal(1, config.Marker);
        Assert.Equal(1, config.Version);
        Assert.Equal(0, config.SeqProfile);
        Assert.Equal(8, config.SeqLevelIdx0);
        Assert.False(config.MonoChrome);
        Assert.True(config.ChromaSubsamplingX);
        Assert.True(config.ChromaSubsamplingY);
        Assert.False(config.InitialPresentationDelayPresent);
    }
}
