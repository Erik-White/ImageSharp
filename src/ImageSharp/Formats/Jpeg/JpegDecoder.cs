// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Jpeg;

/// <summary>
/// Decoder for generating an image out of a jpeg encoded stream.
/// </summary>
public sealed class JpegDecoder : SpecializedImageDecoder<JpegDecoderOptions>
{
    /// <inheritdoc/>
    protected internal override IImageInfo Identify(DecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(stream, nameof(stream));

        using JpegDecoderCore decoder = new(new() { GeneralOptions = options });
        return decoder.Identify(options.Configuration, stream, cancellationToken);
    }

    /// <inheritdoc/>
    protected internal override Image<TPixel> Decode<TPixel>(JpegDecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(stream, nameof(stream));

        using JpegDecoderCore decoder = new(options);
        Image<TPixel> image = decoder.Decode<TPixel>(options.GeneralOptions.Configuration, stream, cancellationToken);

        if (options.ResizeMode != JpegDecoderResizeMode.IdctOnly)
        {
            Resize(options.GeneralOptions, image);
        }

        return image;
    }

    /// <inheritdoc/>
    protected internal override Image Decode(JpegDecoderOptions options, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgb24>(options, stream, cancellationToken);

    /// <inheritdoc/>
    protected internal override JpegDecoderOptions CreateDefaultSpecializedOptions(DecoderOptions options)
        => new() { GeneralOptions = options };
}
