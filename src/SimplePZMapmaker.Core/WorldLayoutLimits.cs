namespace SimplePZMapmaker.Core;

/// <summary>Bounded section-streamed layout/import budgets and legacy in-memory assembly allowance.</summary>
public static class WorldLayoutLimits
{
 public const int NormalSections=128;
 public const int NormalImportCells=768;
 public const int MaxSections=65536;
 public const int MaxWorldCells=65536;
 public const int InMemoryCells=8192;
 public const int MaxResidentCells=256;
 public const int CellSize=256;
 public const double MaximumMetresPerTile=20;
 // Terrain is a byte raster. Zombie density is one Int32 per 8 x 8 tiles.
 // The aggregate is a disk/work estimate, not peak RAM. Section assembly retains only
 // the current raster or a bounded seam-neighbor window; metadata has a separate cost.
 // This base estimate excludes object graphs, source geometry, temporary copies and JSON.
 public const long TerrainBytesPerCell=CellSize*CellSize;
 public const long BaseRasterBytesPerCell=TerrainBytesPerCell+(CellSize/8)*(CellSize/8)*sizeof(int);
 public static long EstimateBaseRasterBytes(int cells){if(cells is <1 or >MaxWorldCells)throw new ArgumentException("Invalid world cell count.");return cells*BaseRasterBytesPerCell;}

 /// <summary>Source intake can cover a world at the coarsest supported scale, never the globe.
 /// Layout planning still checks actual scale, rotation, section padding and coordinates.
 /// Online requests retain a separate small-tile and request-count budget.</summary>
 public static void ValidateSourceBounds(GeoBounds b){
  if(b is null||!double.IsFinite(b.South)||!double.IsFinite(b.North)||!double.IsFinite(b.West)||!double.IsFinite(b.East)||b.South< -85||b.North>85||b.West< -180||b.East>180||b.South>=b.North||b.West>=b.East)throw new ArgumentException("Specify valid geographic bounds without crossing the date line.");
  var columns=Math.Ceiling((b.East-b.West)*Math.Cos((b.North+b.South)/2*Math.PI/180)*GeographicProjection.MetresPerDegree/(CellSize*MaximumMetresPerTile));
  var rows=Math.Ceiling((b.North-b.South)*GeographicProjection.MetresPerDegree/(CellSize*MaximumMetresPerTile));
  if(columns*rows>MaxWorldCells)throw new ArgumentException($"The geographic source area exceeds the bounded {MaxWorldCells.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)}-cell world footprint even at the coarsest supported scale.");
 }
}
