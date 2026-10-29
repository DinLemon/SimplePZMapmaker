namespace SimplePZMapmaker.Core;

public enum GeographicSurveySourceKind { Osm, Cadastre, YandexSuppliedVectors, GooglePanoramas, YandexPanoramas, OwnImages, Mapillary, AvitoManualObservations, LocalSurvey, HistoricalSuppliedMaps }
public enum GeographicSurveyAccess { ExistingOsmPipeline, BoundedWms, SuppliedVectors, OfficialConfiguredOrExternal, SuppliedImages, SuppliedObservations }
public enum GeographicSurveyEvidence { Observed, Inferred }
public enum GeographicSurveyCrs { Wgs84, Epsg3857 }
public enum GeographicSurveyKind { Generic, Parcel, Building }

public sealed record GeographicSurveyCapability(GeographicSurveySourceKind Source, GeographicSurveyAccess Access,
 bool CanFetch, bool CanNormalizeVectors, bool ReferenceOnly, string Limitation);

/// <summary>Capabilities implemented by this application. Provider names do not imply an available API.</summary>
public static class GeographicSurveySources
{
 public static IReadOnlyList<GeographicSurveyCapability> All {get;}=Array.AsReadOnly(new[]{
  new GeographicSurveyCapability(GeographicSurveySourceKind.Osm,GeographicSurveyAccess.ExistingOsmPipeline,true,true,false,"Existing OSM PBF/JSON/cache pipeline is the baseline; attribution required."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.Cadastre,GeographicSurveyAccess.BoundedWms,true,true,false,"NSPD sampled GetFeatureInfo is not a complete parcel/building enumeration. Availability, TLS trust and schema must be verified; supplied GeoJSON works offline."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.YandexSuppliedVectors,GeographicSurveyAccess.SuppliedVectors,false,true,false,"Import only vectors supplied with attribution and permission; no undocumented cartographic scraping."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.GooglePanoramas,GeographicSurveyAccess.OfficialConfiguredOrExternal,false,false,true,"No panorama fetch adapter here. Use an independently configured official provider or licensed external references; retain camera/date/attribution."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.YandexPanoramas,GeographicSurveyAccess.OfficialConfiguredOrExternal,false,false,true,"No panorama fetch adapter here. Official configured access or external references only; no scraping."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.OwnImages,GeographicSurveyAccess.SuppliedImages,false,false,true,"Use existing visual-reference image intake, georeferencing and dates; images stay outside exported mods."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.Mapillary,GeographicSurveyAccess.OfficialConfiguredOrExternal,true,false,true,"Existing visual-reference search/download adapter uses the official API when PZMAP_MAPILLARY_TOKEN is configured. Access and coverage are confirmed only by successful requests; images stay outside exported mods."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.AvitoManualObservations,GeographicSurveyAccess.SuppliedObservations,false,true,false,"Attributed manual/supplied GeoJSON observations only; inferred house interiors/material statistics are not per-house observations."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.LocalSurvey,GeographicSurveyAccess.SuppliedVectors,false,true,false,"Supplied survey GeoJSON with stable IDs, attribution, dates and explicit inference; applying corrections must protect manual overrides."),
  new GeographicSurveyCapability(GeographicSurveySourceKind.HistoricalSuppliedMaps,GeographicSurveyAccess.SuppliedImages,false,true,true,"Supplied georeferenced images through visual references; independently supplied digitized GeoJSON may be normalized. No bundled unlicensed imagery.")
 });
}

public sealed record GeographicSurveyContext
{
 public GeographicSurveySourceKind Source {get;init;}=GeographicSurveySourceKind.LocalSurvey;
 public string Attribution {get;init;}="";
 public string? Author {get;init;}
 public string? ExpectedRegionCode {get;init;}
 public string? SourceReference {get;init;}
 public DateTimeOffset? SourceDate {get;init;}
 public DateTimeOffset? RetrievedAt {get;init;}
 public string? License {get;init;}
 public GeographicSurveyCrs InputCrs {get;init;}=GeographicSurveyCrs.Wgs84;
 public GeographicSurveyEvidence Evidence {get;init;}=GeographicSurveyEvidence.Observed;
 public GeographicSurveyKind Kind {get;init;}=GeographicSurveyKind.Generic;
}

public sealed record GeographicSurveyResult(string GeoJson,int Features,IReadOnlyList<string> Diagnostics);

/// <summary>Spherical Web Mercator, x=easting/y=northing, longitude/latitude output. No axis guessing.</summary>
public static class GeographicSurveyProjection
{
 public const double Radius=6378137;
 public const double MaximumLatitude=85.0511287798066;
 public const double MaximumCoordinate=Math.PI*Radius;
 public static (double X,double Y) ToWebMercator(double longitude,double latitude)
 {
  if(!double.IsFinite(longitude)||!double.IsFinite(latitude)||Math.Abs(longitude)>180||Math.Abs(latitude)>MaximumLatitude)
   throw new ArgumentException("Coordinates must be finite WGS84 longitude and Web-Mercator-compatible latitude.");
  return (Radius*longitude*Math.PI/180,Radius*Math.Log(Math.Tan(Math.PI/4+latitude*Math.PI/360)));
 }
 public static (double Longitude,double Latitude) FromWebMercator(double x,double y)
 {
  if(!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)>MaximumCoordinate+1e-7||Math.Abs(y)>MaximumCoordinate+1e-7)
   throw new ArgumentException("EPSG:3857 coordinates exceed the supported world extent.");
  return (x/Radius*180/Math.PI,(2*Math.Atan(Math.Exp(y/Radius))-Math.PI/2)*180/Math.PI);
 }
 public static (double West,double South,double East,double North) ProjectBounds(GeoBounds bounds)
 {
  ArgumentNullException.ThrowIfNull(bounds);
  if(bounds.West>=bounds.East||bounds.South>=bounds.North)throw new ArgumentException("Bounds require west < east and south < north; antimeridian wrapping is unsupported.");
  var sw=ToWebMercator(bounds.West,bounds.South);var ne=ToWebMercator(bounds.East,bounds.North);
  return(sw.X,sw.Y,ne.X,ne.Y);
 }
}
