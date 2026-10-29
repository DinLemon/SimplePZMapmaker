namespace SimplePZMapmaker.Core;

/// <summary>
/// Native B42 road access. Points name inclusive end cells of the entire approach/deck/descent.
/// Each storey uses exactly 20 native slope cells, with segment 20 still on the lower Z.
/// Geometry is planned in the feature's coordinate frame before any section clipping.
/// </summary>
public static class RoadRampGeometry
{
 // Direction/index convention adapted from KnoxMap generator/structures.py (MIT).
 // Copyright (c) 2026 spytheeuclidean-a11y. See docs/licenses/KNOXMAP-MIT.txt.
 public const int SegmentsPerStorey=20;
 public sealed record RampPlan(InfrastructureGeometry.Geometry Geometry,HashSet<(int X,int Y)> Footprint,HashSet<(int X,int Y)> Approaches,HashSet<(int X,int Y)> Deck);

 /// <summary>Only the 80 real slope records, excluding padding and the unrelated full-height block.</summary>
 public static bool IsRampTile(string id)=>id.StartsWith("ramps_01_",StringComparison.Ordinal)&&int.TryParse(id.AsSpan(9),out var index)&&index is >=0 and <=19 or >=24 and <=43 or >=48 and <=67 or >=72 and <=91&&id==$"ramps_01_{index}";

 public static void ValidateProfile(LinearFeature f)
 {
  if(f.Access!="ramps")throw new ArgumentException("Road ramp planning requires Access='ramps'.");
  if(f.Kind is not("road" or "bridge")||f.Level is not(1 or 2)||f.Interpolation!="polyline"||f.Points is not {Count:2}||f.Points.Any(p=>p is null||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>258048||Math.Abs(p.Y)>258048||p.X!=Math.Floor(p.X)||p.Y!=Math.Floor(p.Y))||!double.IsFinite(f.Width)||f.Width<1||f.Width>16||f.Width!=Math.Floor(f.Width))throw new ArgumentException("Native road ramps require a straight two-point road/bridge polyline with integer coordinates, positive level 1 or 2 and integer full width 1..16.");
  var a=f.Points[0];var b=f.Points[1];
  if((a.X==b.X)==(a.Y==b.Y))throw new ArgumentException("Native road ramps require one cardinal straight run; diagonal and coincident endpoints are unsupported.");
  var run=Math.Abs(Math.Floor(b.X)-Math.Floor(a.X))+Math.Abs(Math.Floor(b.Y)-Math.Floor(a.Y))+1;
  if(run<2*SegmentsPerStorey*f.Level+2)throw new ArgumentException("Native road ramps need 20 cells per storey at each end and at least two flat deck cells (inclusive endpoint cells).");
  if(run*f.Width>LinearFeatureEditor.MaximumCells)throw new ArgumentException("Native road ramp footprint exceeds the 100,000 managed tile budget.");
  if(f.DeckArtwork is not {Count:0})throw new ArgumentException("Deck artwork is unsupported on native road ramps; clear DeckArtwork before selecting ramp access.");
 }

 /// <summary>Common local/world planner. Optional ground sampling rejects wet approaches and keeps piers off underlying roads.</summary>
 public static RampPlan Plan(LinearFeature feature,Func<int,int,Terrain>? readGround=null)
 {
  ValidateProfile(feature);var a=feature.Points[0];var b=feature.Points[1];
  var sx=(int)Math.Floor(a.X);var sy=(int)Math.Floor(a.Y);var ex=(int)Math.Floor(b.X);var ey=(int)Math.Floor(b.Y);
  var dx=Math.Sign(ex-sx);var dy=Math.Sign(ey-sy);var length=Math.Abs(ex-sx)+Math.Abs(ey-sy)+1;var width=(int)feature.Width;var approach=SegmentsPerStorey*feature.Level;
  var up=dx>0?72:dx<0?48:dy>0?24:0;var down=dx>0?48:dx<0?72:dy>0?0:24;
  var tiles=new List<InfrastructureGeometry.Tile>();var footprint=new HashSet<(int X,int Y)>();var approaches=new HashSet<(int X,int Y)>();var deck=new HashSet<(int X,int Y)>();
  void Add(int x,int y,int z,string id,byte flags=0){if(tiles.Count>=InfrastructureGeometry.MaximumTiles)throw new ArgumentException("Native road ramps exceed the 250,000 structural tile budget.");tiles.Add(new(x,y,z,id,flags));}
  for(var at=0;at<length;at++)for(var lane=0;lane<width;lane++){
   var cross=lane-width/2;var x=sx+dx*at+(dx==0?cross:0);var y=sy+dy*at+(dy==0?cross:0);footprint.Add((x,y));
   if(at<approach||at>=length-approach){
    approaches.Add((x,y));if(readGround?.Invoke(x,y)==Terrain.Water)throw new ArgumentException("Native road ramp approaches require dry unoccupied ground at every storey.");
    var run=at<approach?at:length-1-at;Add(x,y,run/SegmentsPerStorey,$"ramps_01_{(at<approach?up:down)+run%SegmentsPerStorey}");
   }else{
    deck.Add((x,y));Add(x,y,feature.Level,InfrastructureGeometry.DeckTile);
    // Only lateral edges of the flat deck receive fences; never cap an approach/deck join.
    if(feature.GuardRails){if(dx!=0){if(lane==0)Add(x,y,feature.Level,"fencing_01_1",2);if(lane==width-1)Add(x,y+1,feature.Level,"fencing_01_1",2);}else{if(lane==0)Add(x,y,feature.Level,"fencing_01_2",4);if(lane==width-1)Add(x+1,y,feature.Level,"fencing_01_2",4);}}
    if(feature.SupportSpacing>0&&lane==0&&(dx!=0?x:y)%feature.SupportSpacing==0&&readGround?.Invoke(x,y) is not(Terrain.Road or Terrain.Pavement))for(var z=0;z<feature.Level;z++){Add(x,y,z,"walls_exterior_house_01_0",4);Add(x,y,z,"walls_exterior_house_01_1",2);}
   }
  }
  return new(new(tiles,[]){RampApproaches=approaches},footprint,approaches,deck);
 }
}
