// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Heif.Av1.OpenBitstreamUnit;

namespace SixLabors.ImageSharp.Formats.Heif.Av1.LoopRestoration;

/// <summary>
/// Per-restoration-unit selection: which filter (if any) the encoder picked, and the
/// signalled coefficients for that filter. Frame-level
/// <see cref="ObuLoopRestorationItem.Type"/> determines which CDF was used to read
/// <see cref="RestorationType"/>; the filter info is only valid when the type matches.
/// </summary>
internal sealed class Av1RestorationUnitInfo
{
    public ObuRestorationType RestorationType { get; set; } = ObuRestorationType.None;

    public Av1WienerInfo WienerInfo { get; } = new();

    public Av1SgrProjInfo SgrProjInfo { get; } = new();
}
