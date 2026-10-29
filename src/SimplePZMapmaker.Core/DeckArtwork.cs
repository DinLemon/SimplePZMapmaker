namespace SimplePZMapmaker.Core;

/// <summary>Visual floor layers cannot outlive, escape, or open holes in their structural deck.</summary>
public static class DeckArtwork
{
 static readonly HashSet<string> structural=[InfrastructureGeometry.DeckTile,"carpentry_02_96","carpentry_02_97","carpentry_02_98","fencing_01_1","fencing_01_2","walls_exterior_house_01_0","walls_exterior_house_01_1","industry_railroad_01_0","industry_railroad_01_2","industry_railroad_01_3","industry_railroad_01_5"];
 public static bool ReservedTile(string id)=>structural.Contains(id);
 public static void Validate(LinearFeature f){
  if(f.DeckArtwork is null||f.DeckArtwork.Count>100000||f.DeckArtwork.Any(c=>c is null||c.X<0||c.Y<0||c.X>258048||c.Y>258048||!VisualStyles.TileId(c.Tile)||ReservedTile(c.Tile))||f.DeckArtwork.Select(c=>(c.X,c.Y)).Distinct().Count()!=f.DeckArtwork.Count)throw new ArgumentException("Invalid deck artwork: use unique floor cells and valid non-structural tile IDs (limit: 100,000).");
  if(f.DeckArtwork.Count>0&&!InfrastructureGeometry.HasDeck(f))throw new ArgumentException("Deck artwork requires a structural bridge or elevated deck.");
 }
 public static void ValidateCoverage(LinearFeature f,Func<int,int,bool> contains){foreach(var c in f.DeckArtwork)if(!contains(c.X,c.Y))throw new ArgumentException("Deck artwork extends outside its owning deck. Clear or reposition the artwork before moving the path.");}
 public static bool MapHasArtwork(MapProject p)=>p.LinearFeatures?.Any(f=>f?.DeckArtwork?.Count>0)==true;
 public static bool WorldHasArtwork(WorldProject w)=>w.LinearFeatures?.Any(f=>f?.DeckArtwork?.Count>0)==true||w.Maps?.Any(m=>m?.Project is {} p&&MapHasArtwork(p))==true;
}
