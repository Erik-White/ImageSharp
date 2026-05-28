// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Formats.Heif.Av1.Entropy;

/// <summary>
/// Diagnostic per-symbol entropy-decode trace. Pairs with libaom's <c>AOM_SYM_TRACE</c>
/// hooks in <c>aom_dsp/bitreader.h</c> (<c>g_aom_sym_trace_fp</c>): when both decoders
/// are run with their respective env vars set, the two traces can be diffed line-for-line
/// to locate the first divergent symbol. This has repeatedly been the fastest way to
/// diagnose monochrome / IBC / palette parser divergences during the HEIC port; expect
/// it to stay until AV1 (and later HEVC) decode is fully aligned with the reference.
/// <para>
/// Enabled by setting <c>IMAGESHARP_SYM_TRACE</c> (entropy stream) and/or
/// <c>IMAGESHARP_IBC_TRACE</c> (IBC DV records) to writable file paths. When the env
/// var is absent <see cref="Enabled"/> returns false on a single static read, so call
/// sites add no measurable overhead to the production decode path.
/// </para>
/// </summary>
internal static class Av1SymbolTrace
{
    private static StreamWriter? writer;
    private static StreamWriter? ibcWriter;
    private static int idx;
    private static bool initialized;
    private static bool ibcInitialized;
    private static readonly object Sync = new();
    private static readonly object IbcSync = new();

    public static bool Enabled => EnsureInitialized();

    public static bool IbcEnabled => EnsureIbcInitialized();

    public static void WriteIbcDv(int row, int col, int bsize, int refRow, int refCol, int mvRow, int mvCol)
    {
        if (!EnsureIbcInitialized())
        {
            return;
        }

        lock (IbcSync)
        {
            ibcWriter!.WriteLine($"IBC_DV r={row} c={col} bsize={bsize} ref=({refRow},{refCol}) mv=({mvRow},{mvCol})");
        }
    }

    public static void WriteSymbol(int position, int nsymbs, int cdf0, int cdf1, int value, string caller = "")
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lock (Sync)
        {
            writer!.WriteLine($"#{idx++} pos={position} nsym={nsymbs} cdf0={cdf0} cdf1={cdf1} => {value} @{caller}");
        }
    }

    public static void WriteLiteral(int position, int bits, int value, string caller = "")
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lock (Sync)
        {
            writer!.WriteLine($"#{idx++} pos={position} lit{bits} value={value} @{caller}");
        }
    }

    public static void WriteMiBlock(int row, int col, int bsize)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lock (Sync)
        {
            writer!.WriteLine($"MI_BLOCK #{idx} r={row} c={col} bsize={bsize}");
        }
    }

    public static void WriteNote(string note)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        lock (Sync)
        {
            writer!.WriteLine(note);
        }
    }

    public static void Flush()
    {
        lock (Sync)
        {
            writer?.Flush();
        }

        lock (IbcSync)
        {
            ibcWriter?.Flush();
        }
    }

    private static bool EnsureInitialized()
    {
        if (initialized)
        {
            return writer != null;
        }

        lock (Sync)
        {
            if (initialized)
            {
                return writer != null;
            }

            string? path = Environment.GetEnvironmentVariable("IMAGESHARP_SYM_TRACE");
            if (!string.IsNullOrEmpty(path))
            {
                writer = new StreamWriter(path) { AutoFlush = true };
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        writer?.Flush();
                        writer?.Dispose();
                    }
                    catch
                    {
                        // best-effort flush
                    }
                };
            }

            initialized = true;
            return writer != null;
        }
    }

    private static bool EnsureIbcInitialized()
    {
        if (ibcInitialized)
        {
            return ibcWriter != null;
        }

        lock (IbcSync)
        {
            if (ibcInitialized)
            {
                return ibcWriter != null;
            }

            string? path = Environment.GetEnvironmentVariable("IMAGESHARP_IBC_TRACE");
            if (!string.IsNullOrEmpty(path))
            {
                ibcWriter = new StreamWriter(path) { AutoFlush = true };
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        ibcWriter?.Flush();
                        ibcWriter?.Dispose();
                    }
                    catch
                    {
                        // best-effort flush
                    }
                };
            }

            ibcInitialized = true;
            return ibcWriter != null;
        }
    }
}
