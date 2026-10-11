namespace SimplePZMapmaker.Core;

public enum Terrain : byte { Grass, Forest, Road, Water, Sand, Dirt, Pavement }
public sealed class MapProject
{
    public int Version { get; set; }=1;
    public string Name { get; set; }="Новая карта";
    public int Width { get; set; }=256;
    public int Height { get; set; }=256;
    public int Seed { get; set; }=42;
    public int WorldX { get; set; }=100;
    public int WorldY { get; set; }=100;
    public Terrain[] Terrain { get; set; }=[];
    public int[] Zombies { get; set; }=[];
    public List<Building> Buildings { get; set; }=[];
    public List<SpawnPoint> Spawns { get; set; }=[];
    public List<string> Attribution { get; set; }=[];
    public GeoBounds? Bounds { get; set; }
}
public sealed class Building
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Kind { get; set; }="house";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }=12;
    public int Height { get; set; }=10;
    public string Name { get; set; }="Дом";
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
    public string Name { get; set; }="Новая карта";
}
