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
var all=tests.Concat(GeoTests.Cases()).Concat(ExportTests.Cases()).Concat(RegressionTests.Cases()).Concat(UpstreamTests.Cases()).Concat(PilnaTests.Cases()).Concat(RecoveryTests.Cases()).Concat(BuildingTests.Cases()).Concat(AssetTests.Cases()).Concat(VisualWorkshopTests.Cases()).Concat(HousingTemplateTests.Cases()).Concat(WorldTests.Cases()).Concat(AgentSessionTests.Cases()).Concat(ModAssetTests.Cases()).Concat(StaticModelTests.Cases()).Concat(CrossCellTests.Cases()).Concat(OsmWorldTests.Cases()).Concat(NeighborhoodTests.Cases()).Concat(RecoveryVersionTests.Cases()).Concat(WorldGeographyTests.Cases()).Concat(ArchitectureTests.Cases()).Concat(PlanningSessionTests.Cases()).Concat(IntegrationQualityTests.Cases()).Concat(TerrariumTests.Cases()).Concat(NeighborhoodParcelTests.Cases()).Concat(ImportQualityTests.Cases()).Concat(NativeViewportTests.Cases()).Concat(MapSceneStoreTests.Cases()).Concat(SimplePZMapmaker.Tests.PaletteTests.Cases()).Concat(SimplePZMapmaker.Tests.WorldMapBinaryTests.Cases()).Concat(LinearGeographyTests.Cases()).Concat(GameplayAuthoringTests.Cases()).Concat(NativeLightingGeometryTests.Cases()).Concat(AuthoringIntegrationTests.Cases()).Concat(ZLevelTests.Cases()).Concat(NativeVerticalRangeTests.Cases()).Concat(ZFormatTests.Cases()).Concat(SectionWorkspaceTests.Cases()).Concat(StreamingExportTests.Cases()).Concat(BuildingModuleTests.Cases()).Concat(InfrastructureTests.Cases()).Concat(ZonePlanningTests.Cases()).Concat(WorldInfrastructureTests.Cases()).Concat(ParametricBuildingTests.Cases()).Concat(StreetEnsembleTests.Cases()).Concat(ExportJobTests.Cases()).Concat(WalkabilityTests.Cases()).Concat(CheckedDesignTests.Cases()).Concat(InstallationDiscoveryTests.Cases()).Concat(RoofSurfaceTests.Cases()).Concat(SettlementGenerationTests.Cases()).Concat(GenerationStudioTests.Cases()).Concat(GenerationQualityTests.Cases()).ToArray();
foreach(var (name,run) in all) { try {run(); Console.WriteLine($"PASS {name}");} catch(Exception e) {failures++;Console.WriteLine($"FAIL {name}: {e.Message}");} }
Console.WriteLine($"{all.Length-failures}/{all.Length} passed"); return failures==0 ? 0:1;
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
