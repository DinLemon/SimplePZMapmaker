namespace SimplePZMapmaker.Core;
public sealed record LinearPoint(double X,double Y);
public sealed class LinearFeature {
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Kind {get;set;}="road";
 public double Width {get;set;}=4;
 public int Level {get;set;}
 public string Access {get;set;}="none";
 public bool GuardRails {get;set;}
 public int SupportSpacing {get;set;}
 public string? GroupId {get;set;}
 public string Interpolation {get;set;}="polyline";
 public List<LinearPoint> Points {get;set;}=[];
 public List<SourceOutlinePoint> SourcePath {get;set;}=[];
 /// <summary>Optional floor artwork owned by this deck, in the same coordinates as Points and at Level.</summary>
 public List<DeckArtworkCell> DeckArtwork {get;set;}=[];
 public string? SourceId {get;set;}
 public Dictionary<string,string> SourceTags {get;set;}=[];
 public bool Modified {get;set;}
 public List<LinearUnderlayCell> Underlay {get;set;}=[];
 public List<string> DecorationIds {get;set;}=[];
 public List<WorldDecoration> GeneratedDecorations {get;set;}=[];
 public Dictionary<string,string> AssetSources {get;set;}=[];
}
public sealed record DeckArtworkCell(int X,int Y,string Tile);
public sealed record LinearUnderlayCell(int Index,Terrain Terrain,string? Surface,bool Edited,Terrain RenderedTerrain,string? RenderedSurface,bool RenderedEdited);
