// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Tiling.MotionVector;

/// <summary>
/// Selects the entropy-coding context for motion-vector reads/writes. libaom
/// keeps <c>nmvc</c> (inter MVs) and <c>ndvc</c> (IBC displacement vectors) as
/// separate adaptive instances so IBC decode does not pollute inter-MV stats.
/// See libaom <c>decodemv.c:read_mv</c>.
/// </summary>
internal enum Av1MotionVectorContext
{
    Inter = 0,
    IntraBlockCopy = 1,
}
