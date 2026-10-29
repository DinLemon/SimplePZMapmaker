using System.Diagnostics;
using SimplePZMapmaker.Core;

static class EvidenceValidationCostTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("geographic evidence validation does not multiply format inference by source count",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Version=15;
   // Shared detail makes this a small-memory stress fixture, not a timing test of generation.
   var floor=new BuildingFloor{Edges=Enumerable.Range(0,2000).Select(_=>new BuildingEdge()).ToList()};
   for(var i=0;i<200;i++)p.Buildings.Add(new(){Id="detail-"+i,Design=new(){Floors=[floor]}});
   for(var i=0;i<1000;i++)p.SourceFeatures.Add(new(){Id="evidence-"+i,SourceId="source-"+i,SourceGeometry=[[new(30,50)]],Geometry=[[new(10,10)]]});
   _=p.Version;var baseline=Stopwatch.StartNew();for(var i=0;i<3;i++)_=p.Version;baseline.Stop();
   var elapsed=Stopwatch.StartNew();GeographicEvidence.Validate(p);elapsed.Stop();
   var allowance=Math.Max(2,baseline.Elapsed.TotalSeconds*20);
   if(elapsed.Elapsed.TotalSeconds>allowance)throw new Exception($"Evidence validation took {elapsed.Elapsed.TotalSeconds:F3}s for a {baseline.Elapsed.TotalSeconds:F3}s format baseline; allowance {allowance:F3}s");
   // The expensive format detector still governs independently projected evidence.
   p.SourceFeatures[0].Geometry.Add([new(20,20)]);GeographicEvidence.Validate(p);
   p.SourceFeatures[^1].Geometry[0][0]=new(double.NaN,10);
   try{GeographicEvidence.Validate(p);throw new Exception("Invalid geometry accepted after format inference");}catch(ArgumentException){}
  });
 }
}
