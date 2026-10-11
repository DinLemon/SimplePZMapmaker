using SimplePZMapmaker.Core;
using System.Text.Json;

var tests = new (string Name, Action Run)[] {
 ("generation is deterministic", () => {
    var a=MapGenerator.Generate(new() { Size=256,Seed=42 });
    var b=MapGenerator.Generate(new() { Size=256,Seed=42 });
    Check(JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b), "Same seed changed the map");
 }),
 ("town has roads accessible buildings and spawn", () => {
    var p=MapGenerator.Generate(new() { Size=256,Seed=7, Kind="town" });
    Check(p.Terrain.Contains(Terrain.Road),"No roads"); Check(p.Buildings.Count>5,"No buildings");
    ProjectValidator.Validate(p); Check(p.Spawns.Count>0,"No spawn");
 }),
 ("invalid dimensions rejected", () => Reject(() => MapGenerator.Generate(new() {Size=65536}))),
 ("overlapping buildings rejected", () => {
    var p=MapGenerator.Generate(new(){Kind="blank"});
    p.Buildings.Add(new(){X=20,Y=20,Width=12,Height=10}); p.Buildings.Add(new(){X=22,Y=22,Width=12,Height=10});
    Reject(()=>ProjectValidator.Validate(p));
 }),
 ("water spawn rejected", () => {
    var p=MapGenerator.Generate(new(){Kind="blank"});
    var s=p.Spawns[0]; p.Terrain[s.Y*p.Width+s.X]=Terrain.Water; Reject(()=>ProjectValidator.Validate(p));
 }),
 ("corrupt terrain rejected", () => {
    var p=MapGenerator.Generate(new(){Kind="blank"}); p.Terrain=[]; Reject(()=>ProjectValidator.Validate(p));
 })
};
var failures=0;
var all=tests.Concat(GeoTests.Cases()).ToArray();
foreach(var (name,run) in all) { try {run(); Console.WriteLine($"PASS {name}");} catch(Exception e) {failures++;Console.WriteLine($"FAIL {name}: {e.Message}");} }
Console.WriteLine($"{all.Length-failures}/{all.Length} passed"); return failures==0 ? 0:1;
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
