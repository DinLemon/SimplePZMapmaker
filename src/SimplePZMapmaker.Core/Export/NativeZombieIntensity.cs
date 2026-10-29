namespace SimplePZMapmaker.Core.Export;

/// <summary>Export policy, not an editor raster migration or a game ABI upper bound.
/// A bounded sample of installed B42 Muldraugh lotheader tails used 0..10.
/// Challenge maps differ; see docs/TRANSFER-B42-VALIDATION.md.</summary>
public static class NativeZombieIntensity
{
    public const int EditorMaximum = 255;
    public const int NativeMaximum = 10;
    public const string ExportNotice = "Zombie density: editable/source raster remains 0..255; native lotheader intensity is scaled to 0..10 (positive values rounded up, zero preserved). This is an export policy based on sampled B42 vanilla data, not a guarantee against client hangs. Re-export into a separate NEW save; existing saves retain cached population data.";

    public static byte FromEditor(int density)
    {
        if (density is < 0 or > EditorMaximum) throw new ArgumentOutOfRangeException(nameof(density), "Editable zombie density must be 0..255.");
        return (byte)((density * NativeMaximum + EditorMaximum - 1) / EditorMaximum);
    }
}
