// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Pipeline.LoopFilter;

/// <summary>
/// Per-level (limit, blimit, thresh) triplet derived once per frame as specified by section 7.14.4
/// of the AV1 specification.
/// </summary>
internal readonly record struct Av1LoopFilterThreshold(byte Limit, byte MbLimit, byte HevThreshold);
