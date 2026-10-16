using SimplePZMapmaker.Core;
public static class RotatedProjectionTests
{
    public static IEnumerable<(string,Action)> Cases() {
        yield return("rotated geographic reference round-trips, aligns a skewed street and leaves unrotated worlds unchanged",()=>{
            var bounds=new GeoBounds{West=45.8817,East=45.9437,South=55.5354,North=55.5790};
            var flat=new WorldGeoReference{Bounds=bounds,MetresPerTile=1,OriginCellX=200,OriginCellY=200};
            var turned=new WorldGeoReference{Bounds=bounds,MetresPerTile=1,OriginCellX=200,OriginCellY=200,RotationDegrees=-11.22};
            // Unrotated: the original north-west-anchored formula, bit for bit.
            var p0=new GeographicProjection(flat,204,208);var cos=Math.Cos((bounds.North+bounds.South)/2*Math.PI/180);var q=(45.919,55.553);var a=p0.Project(q);
            Check(a==((q.Item1-bounds.West)*cos*111320-(204-200)*256,(bounds.North-q.Item2)*111320-(208-200)*256),"Unrotated projection changed");
            foreach(var (cx,cy) in new[]{(200,200),(204,208),(212,216)}){var p=new GeographicProjection(turned,cx,cy);
                foreach(var (x,y) in new[]{(0.0,0.0),(511.5,17.25),(1023.0,1023.0)}){var g=p.Geographic(x,y);var back=p.Project((g.Longitude,g.Latitude));Check(Math.Abs(back.X-x)<1e-6&&Math.Abs(back.Y-y)<1e-6,"Rotated projection does not round-trip");}}
            // A street at bearing 11.22 degrees east of north becomes a grid column after rotation by -11.22.
            var origin=new GeographicProjection(turned,200,200);var lat0=55.55;var step=200.0/111320;var start=(45.91,lat0);
            var end=(start.Item1+step*Math.Sin(11.22*Math.PI/180)/cos,lat0+step*Math.Cos(11.22*Math.PI/180));var s0=origin.Project(start);var s1=origin.Project(end);
            Check(Math.Abs(s1.X-s0.X)<0.01&&Math.Abs(s0.Y-s1.Y-200)<0.01,$"Street was not aligned: {s0} -> {s1}");
            // The whole rotated bounds land inside the world, starting at the origin cell.
            var size=GeographicProjection.RequiredTiles(turned);var corners=new[]{(bounds.West,bounds.North),(bounds.East,bounds.North),(bounds.West,bounds.South),(bounds.East,bounds.South)}.Select(origin.Project).ToArray();
            Check(Math.Abs(corners.Min(c=>c.X))<1e-6&&Math.Abs(corners.Min(c=>c.Y))<1e-6&&corners.Max(c=>c.X)<=size.Width&&corners.Max(c=>c.Y)<=size.Height,"Rotated bounds are not anchored to the origin cell");
            // A section's source box covers all four of its corners.
            var section=new GeographicProjection(turned,204,204);var box=section.SectionBounds(1024,1024);
            foreach(var (x,y) in new[]{(0.0,0.0),(1024.0,0.0),(0.0,1024.0),(1024.0,1024.0)}){var g=section.Geographic(x,y);Check(g.Longitude>=box.West-1e-12&&g.Longitude<=box.East+1e-12&&g.Latitude>=box.South-1e-12&&g.Latitude<=box.North+1e-12,"Section source box misses a corner");}
            Reject(()=>WorldGeoReference.Validate(new(){Bounds=bounds,RotationDegrees=46}));Reject(()=>WorldGeoReference.Validate(new(){Bounds=bounds,RotationDegrees=double.NaN}));
            var world=new WorldProject{GeoReference=turned};Check(world.Version==7,"A rotated reference must raise the world format");Check(new WorldProject{GeoReference=flat}.Version==1,"An unrotated reference must not raise the world format");
        });
        yield return("rotated import places a skewed real house as a grid-aligned rectangle with its real outline kept",()=>{
            var bounds=new GeoBounds{West=45.910,East=45.930,South=55.548,North=55.558};
            Building Import(double rotation){
                var reference=new WorldGeoReference{Bounds=bounds,MetresPerTile=1,OriginCellX=200,OriginCellY=200,RotationDegrees=rotation};
                var size=GeographicProjection.RequiredTiles(reference);var cellX=200+size.Width/2/256;var cellY=200+size.Height/2/256;var projection=new GeographicProjection(reference,cellX,cellY);
                // A 16 x 10 m house whose long side runs at bearing 90 - rotation degrees, centred off the tile grid lines near section tile (128, 128).
                var cos=Math.Cos((bounds.North+bounds.South)/2*Math.PI/180);var angle=-rotation*Math.PI/180;var centre=projection.Geographic(128.4,128.3);
                double[] At(double east,double south){var e=east*Math.Cos(angle)-south*Math.Sin(angle);var n=-(east*Math.Sin(angle)+south*Math.Cos(angle));return[centre.Longitude+e/(cos*111320),centre.Latitude+n/111320];}
                var ring=new[]{At(-8,-5),At(8,-5),At(8,5),At(-8,5),At(-8,-5)};
                var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="osm-way-1",properties=new Dictionary<string,string>{{"building","house"}},geometry=new{type="Polygon",coordinates=new[]{ring}}}}});
                var blank=MapGenerator.Generate(new(){Kind="blank",Size=256});blank.WorldX=cellX;blank.WorldY=cellY;
                var p=GeographicImporter.Import(json,blank,projection.SectionBounds(256,256),projection:projection).Project;
                var source=p.SourceFeatures.Single(f=>f.SourceId=="geo-osm-way-1");Check(Math.Abs(source.SourceGeometry[0][0].Longitude-ring[0][0])<1e-12&&Math.Abs(source.SourceGeometry[0][0].Latitude-ring[0][1])<1e-12,"Real source coordinates were not kept");
                return p.Buildings.Single();
            }
            var control=Import(0);var turned=Import(-11.22);
            Check(turned.X==control.X&&turned.Y==control.Y&&turned.Width==control.Width&&turned.Height==control.Height&&turned.FootprintOrigin==control.FootprintOrigin,$"Skewed house under a rotated reference differs from a north-aligned one: {turned.X},{turned.Y} {turned.Width}x{turned.Height} {turned.FootprintOrigin} vs {control.X},{control.Y} {control.Width}x{control.Height} {control.FootprintOrigin}");
        });
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
