// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// <auto-generated />
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;

namespace SixLabors.ImageSharp;

/// <summary>
/// Extension methods for the <see cref="ImageMetadata"/> and <see cref="ImageFrameMetadata"/> types.
/// </summary>
public static class ImageMetadataExtensions
{
    /// <summary>
    /// Gets the <see cref="BmpMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="BmpMetadata"/>
    /// </returns>
    public static BmpMetadata GetBmpMetadata(this ImageMetadata source) => source.GetFormatMetadata(BmpFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="BmpMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetBmpMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="BmpMetadata"/></returns>
    public static BmpMetadata CloneBmpMetadata(this ImageMetadata source) => source.CloneFormatMetadata(BmpFormat.Instance);

    /// <summary>
    /// Gets the <see cref="GifMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="GifMetadata"/>
    /// </returns>
    public static GifMetadata GetGifMetadata(this ImageMetadata source) => source.GetFormatMetadata(GifFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="GifMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetGifMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="GifMetadata"/></returns>
    public static GifMetadata CloneGifMetadata(this ImageMetadata source) => source.CloneFormatMetadata(GifFormat.Instance);

    /// <summary>
    /// Gets the <see cref="JpegMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="JpegMetadata"/>
    /// </returns>
    public static JpegMetadata GetJpegMetadata(this ImageMetadata source) => source.GetFormatMetadata(JpegFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="JpegMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetJpegMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="JpegMetadata"/></returns>
    public static JpegMetadata CloneJpegMetadata(this ImageMetadata source) => source.CloneFormatMetadata(JpegFormat.Instance);

    /// <summary>
    /// Gets the <see cref="PbmMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="PbmMetadata"/>
    /// </returns>
    public static PbmMetadata GetPbmMetadata(this ImageMetadata source) => source.GetFormatMetadata(PbmFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="PbmMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetPbmMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="PbmMetadata"/></returns>
    public static PbmMetadata ClonePbmMetadata(this ImageMetadata source) => source.CloneFormatMetadata(PbmFormat.Instance);

    /// <summary>
    /// Gets the <see cref="PngMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="PngMetadata"/>
    /// </returns>
    public static PngMetadata GetPngMetadata(this ImageMetadata source) => source.GetFormatMetadata(PngFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="PngMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetPngMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="PngMetadata"/></returns>
    public static PngMetadata ClonePngMetadata(this ImageMetadata source) => source.CloneFormatMetadata(PngFormat.Instance);

    /// <summary>
    /// Gets the <see cref="QoiMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="QoiMetadata"/>
    /// </returns>
    public static QoiMetadata GetQoiMetadata(this ImageMetadata source) => source.GetFormatMetadata(QoiFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="QoiMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetQoiMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="QoiMetadata"/></returns>
    public static QoiMetadata CloneQoiMetadata(this ImageMetadata source) => source.CloneFormatMetadata(QoiFormat.Instance);

    /// <summary>
    /// Gets the <see cref="TgaMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="TgaMetadata"/>
    /// </returns>
    public static TgaMetadata GetTgaMetadata(this ImageMetadata source) => source.GetFormatMetadata(TgaFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="TgaMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetTgaMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="TgaMetadata"/></returns>
    public static TgaMetadata CloneTgaMetadata(this ImageMetadata source) => source.CloneFormatMetadata(TgaFormat.Instance);

    /// <summary>
    /// Gets the <see cref="TiffMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="TiffMetadata"/>
    /// </returns>
    public static TiffMetadata GetTiffMetadata(this ImageMetadata source) => source.GetFormatMetadata(TiffFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="TiffMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetTiffMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="TiffMetadata"/></returns>
    public static TiffMetadata CloneTiffMetadata(this ImageMetadata source) => source.CloneFormatMetadata(TiffFormat.Instance);

    /// <summary>
    /// Gets the <see cref="WebpMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>
    /// The <see cref="WebpMetadata"/>
    /// </returns>
    public static WebpMetadata GetWebpMetadata(this ImageMetadata source) => source.GetFormatMetadata(WebpFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="WebpMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetWebpMetadata(ImageMetadata)"/>
    /// </summary>
    /// <param name="source">The image metadata.</param>
    /// <returns>The new <see cref="WebpMetadata"/></returns>
    public static WebpMetadata CloneWebpMetadata(this ImageMetadata source) => source.CloneFormatMetadata(WebpFormat.Instance);


    /// <summary>
    /// Gets the <see cref="GifFrameMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>
    /// The <see cref="GifFrameMetadata"/>
    /// </returns>
    public static GifFrameMetadata GetGifMetadata(this ImageFrameMetadata source) => source.GetFormatMetadata(GifFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="GifMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetGifMetadata(ImageFrameMetadata)"/>
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>The new <see cref="GifFrameMetadata"/></returns>
    public static GifFrameMetadata CloneGifMetadata(this ImageFrameMetadata source) => source.CloneFormatMetadata(GifFormat.Instance);

    /// <summary>
    /// Gets the <see cref="PngFrameMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>
    /// The <see cref="PngFrameMetadata"/>
    /// </returns>
    public static PngFrameMetadata GetPngMetadata(this ImageFrameMetadata source) => source.GetFormatMetadata(PngFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="PngMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetPngMetadata(ImageFrameMetadata)"/>
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>The new <see cref="PngFrameMetadata"/></returns>
    public static PngFrameMetadata ClonePngMetadata(this ImageFrameMetadata source) => source.CloneFormatMetadata(PngFormat.Instance);

    /// <summary>
    /// Gets the <see cref="TiffFrameMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>
    /// The <see cref="TiffFrameMetadata"/>
    /// </returns>
    public static TiffFrameMetadata GetTiffMetadata(this ImageFrameMetadata source) => source.GetFormatMetadata(TiffFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="TiffMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetTiffMetadata(ImageFrameMetadata)"/>
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>The new <see cref="TiffFrameMetadata"/></returns>
    public static TiffFrameMetadata CloneTiffMetadata(this ImageFrameMetadata source) => source.CloneFormatMetadata(TiffFormat.Instance);

    /// <summary>
    /// Gets the <see cref="WebpFrameMetadata"/> from <paramref name="source"/>.<br/>
    /// If none is found, an instance is created either by conversion from the decoded image format metadata
    /// or the requested format default constructor.
    /// This instance will be added to the metadata for future requests.
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>
    /// The <see cref="WebpFrameMetadata"/>
    /// </returns>
    public static WebpFrameMetadata GetWebpMetadata(this ImageFrameMetadata source) => source.GetFormatMetadata(WebpFormat.Instance);

    /// <summary>
    /// Creates a new cloned instance of <see cref="WebpMetadata"/> from the <paramref name="source"/>.
    /// The instance is created via <see cref="GetWebpMetadata(ImageFrameMetadata)"/>
    /// </summary>
    /// <param name="source">The image frame metadata.</param>
    /// <returns>The new <see cref="WebpFrameMetadata"/></returns>
    public static WebpFrameMetadata CloneWebpMetadata(this ImageFrameMetadata source) => source.CloneFormatMetadata(WebpFormat.Instance);
}
