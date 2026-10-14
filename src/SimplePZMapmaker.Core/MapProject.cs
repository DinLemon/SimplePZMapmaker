namespace SimplePZMapmaker.Core;

public enum Terrain : byte { Grass, Forest, Road, Water, Sand, Dirt, Pavement }
public sealed class MapProject
{
    private int version=4;
    public int Version { get=>AuthoringFormats.MapHasInfrastructure(this)&&version<11?11:AuthoringFormats.MapHasExtendedLevels(this)&&version<10?10:AuthoringFormats.MapHasNewAuthoring(this)&&version<9?9:ResourcePalettes?.Count>0&&version<8?8:version==6&&(StaticModels?.Any(m=>m?.Profile!="pickup")==true||(Buildings??[]).Concat(BuildingLibrary??[]).Any(b=>b?.Design?.Floors?.Any(f=>f?.StaticModels?.Any(m=>m?.Profile!="pickup")==true)==true))?7:version;set=>version=value; }
    public bool WorldSection {get;set;}
    public string Name { get; set; }="New map";
    public int Width { get; set; }=256;
    public int Height { get; set; }=256;
    public int Seed { get; set; }=42;
    public int WorldX { get; set; }=100;
    public int WorldY { get; set; }=100;
    public Terrain[] Terrain { get; set; }=[];
    public string TerrainEdits {get;set;}="";
    public int[] Zombies { get; set; }=[];
    public List<Building> Buildings { get; set; }=[];
    public List<Building> BuildingLibrary {get;set;}=[];
    public List<ResourcePalette> ResourcePalettes {get;set;}=[];
    public List<LinearFeature> LinearFeatures {get;set;}=[];
    public List<GameZone> GameZones {get;set;}=[];
    public string SurfaceTheme {get;set;}="legacy";
    public List<SurfaceRegion> SurfaceRegions {get;set;}=[];
    public Dictionary<int,string> SurfaceTiles {get;set;}=[];
    public List<WorldDecoration> Decorations {get;set;}=[];
    public List<StaticModelPlacement> StaticModels {get;set;}=[];
    public List<string> RequiredMods {get;set;}=[];
    public Dictionary<string,string> AssetSources {get;set;}=[];
    public List<SpawnPoint> Spawns { get; set; }=[];
    public List<string> Attribution { get; set; }=[];
    public GeoBounds? Bounds { get; set; }
    public double? MetresPerTile { get; set; }
    public ImportSummary? ImportSummary {get;set;}
}
public sealed class ImportSummary
{
    public int SourceFeatures {get;set;}
    public int SourceBuildings {get;set;}
    public int PlacedBuildings {get;set;}
    public Dictionary<string,int> SkipReasons {get;set;}=[];
    public ImportQualitySummary? Quality {get;set;}
    public List<SkippedImportOutline> SkippedOutlines {get;set;}=[];
    public List<string> Notes {get;set;}=[];
}
public sealed class Building
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Kind { get; set; }="house";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }=12;
    public int Height { get; set; }=10;
    public string Name { get; set; }="House";
    public BuildingDesign? Design {get;set;}
    public string? SourceId {get;set;}
    public string? SourceKind {get;set;}
    public string? SourceLevels {get;set;}
    public string? SourceBasements {get;set;}
    public string? SourceRoof {get;set;}
    public string FootprintOrigin {get;set;}="authored";
    public List<List<SourceOutlinePoint>> SourceOutline {get;set;}=[];
    public bool SourceOutlineSimplified {get;set;}
    public List<string> Attribution {get;set;}=[];
    public List<string> RequiredMods {get;set;}=[];
    public Dictionary<string,string> AssetSources {get;set;}=[];
    public string LevelsOrigin {get;set;}="inferred";
    public bool Modified {get;set;}
}
public sealed class BuildingDesign {
    public int MinLevel {get;set;}
    public string Material {get;set;}="wood";
    public string Roof {get;set;}="flat";
    public string Shape {get;set;}="rect";
    public int Variant {get;set;}
    public string Style {get;set;}="";
    public string? RoofTileset {get;set;}
    public List<BuildingFloor> Floors {get;set;}=[];
}
public sealed class BuildingFloor {
    public string Cells {get;set;}="";
    public Dictionary<string,string> Rooms {get;set;}=[];
    public List<BuildingEdge> Edges {get;set;}=[];
    public List<BuildingObject> Objects {get;set;}=[];
    public string? FloorTile {get;set;}
    public Dictionary<int,string> FloorTiles {get;set;}=[];
    public List<StaticModelPlacement> StaticModels {get;set;}=[];
}
public sealed class BuildingEdge {
    public int X {get;set;} public int Y {get;set;}
    public string Edge {get;set;}="n";
    public string Kind {get;set;}="wall";
    public bool Edited {get;set;}
    public string? WallTile {get;set;}
    public string? OpeningTile {get;set;}
}
public sealed class BuildingObject {
    public int X {get;set;} public int Y {get;set;}
    public string Kind {get;set;}="storage";
    public string Direction {get;set;}="n";
    public string Style {get;set;}="";
    public string? Asset {get;set;}
    public List<BuildingObjects.Part> Parts {get;set;}=[];
}
public sealed class WorldDecoration {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public int X {get;set;} public int Y {get;set;}
    public string Asset {get;set;}="";
    public List<BuildingObjects.Part> Parts {get;set;}=[];
}
public sealed class SurfaceRegion {
    public int X {get;set;} public int Y {get;set;}
    public int Width {get;set;} public int Height {get;set;}
    public string Theme {get;set;}="temperate";
}
public sealed class SpawnPoint { public int X { get; set; } public int Y { get; set; } }
public sealed class GeoBounds { public double South { get; set; } public double West { get; set; } public double North { get; set; } public double East { get; set; } }
public sealed class GenerationOptions
{
    public int Size { get; set; }=256;
    public int Seed { get; set; }=42;
    public string Kind { get; set; }="town";
    public int ForestDensity { get; set; }=35;
    public int BuildingDensity { get; set; }=55;
    public int ZombieDensity { get; set; }=90;
    public string Name { get; set; }="New map";
    public string SurfaceTheme {get;set;}="legacy";
}
