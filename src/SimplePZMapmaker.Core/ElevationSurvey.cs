using System.Globalization;
using System.Text;
namespace SimplePZMapmaker.Core;
/// <summary>Measured elevation in metres. This survey is an overlay, not fabricated PZ slopes.</summary>
public sealed class ElevationSurvey
{
 public string CoordinateSystem {get;set;}="EPSG:4326";
 public string Source {get;set;}="";
 public string License {get;set;}="";
 public string ImportedUtc {get;set;}=DateTimeOffset.UtcNow.ToString("O");
 public int Columns {get;set;}
 public int Rows {get;set;}
 public double West {get;set;}
 public double South {get;set;}
 public double CellSize {get;set;}
 public double?[] Values {get;set;}=[];
 public static void Validate(ElevationSurvey grid){
  if(grid is null||grid.CoordinateSystem is not("EPSG:4326" or "EPSG:3857")||grid.Columns is <1 or >4096||grid.Rows is <1 or >4096||(long)grid.Columns*grid.Rows>1_000_000||grid.Values is null||grid.Values.Length!=grid.Columns*grid.Rows||!double.IsFinite(grid.West)||!double.IsFinite(grid.South)||!double.IsFinite(grid.CellSize)||grid.CellSize<=0||grid.Values.Any(v=>v.HasValue&&(!double.IsFinite(v.Value)||v is < -12000 or >9000))||!grid.Values.Any(v=>v.HasValue))throw new ArgumentException("Invalid DEM: up to 1 million samples, heights from -12,000 to 9,000 m; EPSG:4326 or EPSG:3857.");
  if(string.IsNullOrWhiteSpace(grid.Source)||grid.Source.Length>1000||grid.License is null||grid.License.Length>1000||!DateTimeOffset.TryParse(grid.ImportedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out _))throw new ArgumentException("Specify the elevation source and license information (up to 1,000 characters).");
  var limit=grid.CoordinateSystem=="EPSG:4326"?180:20037509;var ylimit=grid.CoordinateSystem=="EPSG:4326"?85:20037509;
  if(grid.West< -limit||grid.South< -ylimit||grid.West+grid.Columns*grid.CellSize>limit||grid.South+grid.Rows*grid.CellSize>ylimit)throw new ArgumentException("DEM coordinates do not match the selected system. Reproject the raster to EPSG:4326 or EPSG:3857.");
 }
 public static ElevationSurvey ParseAscii(string text,string coordinateSystem,string source,string license){
  if(text is null||text.Length>25_000_000)throw new ArgumentException("The elevation file exceeds 25 MB.");
  var tokens=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);var header=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);var i=0;
  while(i+1<tokens.Length&&new[]{"ncols","nrows","xllcorner","yllcorner","xllcenter","yllcenter","cellsize","NODATA_value"}.Contains(tokens[i],StringComparer.OrdinalIgnoreCase)){
   if(!double.TryParse(tokens[i+1],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||!header.TryAdd(tokens[i],value))throw new ArgumentException("Invalid ESRI ASCII header or duplicate field.");i+=2;
  }
  double Need(string key)=>header.TryGetValue(key,out var v)?v:throw new ArgumentException("Missing ESRI ASCII field: "+key);
  var nc=Need("ncols");var nr=Need("nrows");var cell=Need("cellsize");if(nc!=Math.Truncate(nc)||nr!=Math.Truncate(nr)||nc is <1 or >4096||nr is <1 or >4096||nc*nr>1_000_000||cell<=0)throw new ArgumentException("DEM dimensions: up to 4,096 samples per side and 1 million samples in total.");
  bool corner=header.ContainsKey("xllcorner")&&header.ContainsKey("yllcorner");bool center=header.ContainsKey("xllcenter")&&header.ContainsKey("yllcenter");if(corner==center||header.ContainsKey("xllcorner")&&header.ContainsKey("xllcenter")||header.ContainsKey("yllcorner")&&header.ContainsKey("yllcenter"))throw new ArgumentException("The DEM must contain matching xllcorner/yllcorner or xllcenter/yllcenter fields.");
  var count=(int)(nc*nr);if(tokens.Length-i!=count)throw new ArgumentException("The number of elevation samples does not match NCOLS x NROWS.");var values=new double?[count];var nodata=header.GetValueOrDefault("NODATA_value",-9999);
  for(var n=0;n<count;n++){if(!double.TryParse(tokens[i+n],NumberStyles.Float,CultureInfo.InvariantCulture,out var v)||!double.IsFinite(v))throw new ArgumentException("The DEM contains a nonnumeric height.");values[n]=v==nodata?null:v;}
  var grid=new ElevationSurvey{CoordinateSystem=coordinateSystem,Source=source,License=license,Columns=(int)nc,Rows=(int)nr,CellSize=cell,West=Need(corner?"xllcorner":"xllcenter")-(center?cell/2:0),South=Need(corner?"yllcorner":"yllcenter")-(center?cell/2:0),Values=values};Validate(grid);return grid;
 }
 public double? Sample(double longitude,double latitude){
  double x=longitude,y=latitude;if(CoordinateSystem=="EPSG:3857"){x=longitude*Math.PI/180*6378137;y=Math.Log(Math.Tan(Math.PI/4+latitude*Math.PI/360))*6378137;}
  // Cell centres, north-first data. No interpolation across missing measurements.
  var column=(int)Math.Floor((x-West)/CellSize+1e-9);var row=Rows-1-(int)Math.Floor((y-South)/CellSize+1e-9);return column>=0&&row>=0&&column<Columns&&row<Rows?Values[row*Columns+column]:null;
 }
 public string ToAscii(){Validate(this);var s=new StringBuilder();string N(double v)=>v.ToString("R",CultureInfo.InvariantCulture);s.Append($"ncols {Columns}\nnrows {Rows}\nxllcorner {N(West)}\nyllcorner {N(South)}\ncellsize {N(CellSize)}\nNODATA_value -32768\n");for(var y=0;y<Rows;y++){for(var x=0;x<Columns;x++){if(x>0)s.Append(' ');s.Append(N(Values[y*Columns+x]??-32768));}s.Append('\n');}return s.ToString();}
}
