namespace SimplePZMapmaker.Core;
public static class JunctionTemplates
{
 public static readonly string[] Names=["crossroads","t-junction","roundabout","overpass","underpass","stacked-crossing"];
 public sealed class Request {
  public string Kind {get;set;}="overpass";
  public string GroupId {get;set;}=Guid.NewGuid().ToString("N");
  public double X {get;set;}=128;
  public double Y {get;set;}=128;
  public double Span {get;set;}=48;
  public double Width {get;set;}=6;
  public int Level {get;set;}=1;
 }
 public static List<LinearFeature> Create(Request r){
  if(r is null||!Names.Contains(r.Kind)||string.IsNullOrWhiteSpace(r.GroupId)||r.GroupId.Length>60||!double.IsFinite(r.X)||!double.IsFinite(r.Y)||!double.IsFinite(r.Span)||r.Span is <16 or >256||!double.IsFinite(r.Width)||r.Width is <2 or >16||r.Span<r.Width*4||r.Level is <1 or >31||r.Kind=="stacked-crossing"&&r.Level>=31)throw new ArgumentException("Choose a junction template, finite center, span 16–256, width 2–16, and upper floor 1–31 (stacked: at most 30).");
  var half=r.Span/2;var result=new List<LinearFeature>();
  void Add(string name,List<LinearPoint> points,int z=0){result.Add(new(){Id=$"{r.GroupId}-{name}",GroupId=r.GroupId,Kind=z==0?"road":"bridge",Width=r.Width,Points=points,Level=z,Access=z==0?"none":"stairs",GuardRails=z!=0,SupportSpacing=z>0?12:0,Modified=true});}
  switch(r.Kind){
   case "roundabout":var radius=r.Span/4;var ring=Enumerable.Range(0,33).Select(i=>new LinearPoint(r.X+radius*Math.Cos(i*Math.PI/16),r.Y+radius*Math.Sin(i*Math.PI/16))).ToList();Add("ring",ring);Add("west",[new(r.X-half,r.Y),new(r.X-radius,r.Y)]);Add("east",[new(r.X+radius,r.Y),new(r.X+half,r.Y)]);Add("north",[new(r.X,r.Y-half),new(r.X,r.Y-radius)]);Add("south",[new(r.X,r.Y+radius),new(r.X,r.Y+half)]);break;
   case "t-junction":Add("east-west",[new(r.X-half,r.Y),new(r.X+half,r.Y)]);Add("south",[new(r.X,r.Y),new(r.X,r.Y+half)]);break;
   default:Add("east-west",[new(r.X-half,r.Y),new(r.X+half,r.Y)],r.Kind=="underpass"?-r.Level:r.Kind is "overpass" or "stacked-crossing"?r.Level:0);Add("north-south",[new(r.X,r.Y-half),new(r.X,r.Y+half)],r.Kind=="stacked-crossing"?r.Level+1:0);if(r.Kind=="stacked-crossing")result[^1].SupportSpacing=0;break;
  }
  return result;
 }
 public static MapProject Apply(MapProject p,Request r)=>LinearFeatureEditor.PutGroup(p,r.GroupId,Create(r));
 public static string[] Warnings(Request r)=>r.Kind is "overpass" or "underpass" or "stacked-crossing"?["Access uses native pedestrian stairs. These templates do not generate vehicle ramp approaches, so their decks do not connect vehicle traffic between floors."]:[];
}
