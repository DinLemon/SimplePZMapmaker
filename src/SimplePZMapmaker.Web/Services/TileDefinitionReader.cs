using System.Text;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;

// Independent reader of the documented tdef 0/1 layout; no game/mod code is executed.
public static class TileDefinitionReader
{
 public sealed record Tile(string Id,Dictionary<string,string> Properties);
 public static IReadOnlyList<Tile> Read(Stream stream)
 {
  if(!stream.CanSeek||stream.Length>32*1024*1024)throw new ArgumentException("Справочник тайлов слишком велик.");
  try {
   using var r=new BinaryReader(stream,Encoding.Latin1,true);var signature=r.ReadBytes(4);if(signature.Length!=4)throw new EndOfStreamException();var version=0;
   if(signature.SequenceEqual("tdef"u8.ToArray()))version=Bound(r.ReadInt32(),0,1);else stream.Position=0;
   var count=Bound(r.ReadInt32(),0,4096);var result=new List<Tile>();var names=new HashSet<string>();
   string Line(){var bytes=new List<byte>();for(var i=0;i<4096;i++){var b=r.ReadByte();if(b==10)return Encoding.Latin1.GetString(bytes.ToArray()).TrimEnd('\r');bytes.Add(b);}throw new ArgumentException("Строка справочника тайлов слишком длинная.");}
   for(var i=0;i<count;i++){
    var name=Line();Line();if(!Regex.IsMatch(name,@"^[A-Za-z0-9_]{1,160}$")||!names.Add(name))throw new ArgumentException("Неверное или повторное имя набора тайлов.");
    var columns=Bound(r.ReadInt32(),1,1024);var rows=Bound(r.ReadInt32(),1,1024);if(version>0)Bound(r.ReadInt32(),0,int.MaxValue);
    var slots=checked(columns*rows);if(slots>100000)throw new ArgumentException("Набор тайлов слишком велик.");var entries=Bound(r.ReadInt32(),0,slots);
    if(result.Count+slots>200000)throw new ArgumentException("Слишком много тайлов.");
    for(var j=0;j<slots;j++){var p=new Dictionary<string,string>(StringComparer.Ordinal);if(j<entries){var n=Bound(r.ReadInt32(),0,256);for(var k=0;k<n;k++){var key=Line();var value=Line();if(key.Length==0||!p.TryAdd(key,value))throw new ArgumentException("Повторное или пустое свойство тайла.");}}result.Add(new(name+"_"+j,p));}
   }
   if(stream.Position!=stream.Length)throw new ArgumentException("Лишние данные справочника тайлов.");return result;
  } catch(Exception e) when(e is IOException or OverflowException){throw new ArgumentException("Повреждён или обрезан справочник тайлов.",e);}
 }
 public static IReadOnlyList<Tile> ReadText(string file)
 {
  if(new FileInfo(file).Length>16*1024*1024)throw new ArgumentException("Текстовый справочник тайлов слишком велик.");
  var result=new List<Tile>();foreach(Match m in Regex.Matches(File.ReadAllText(file),@"// ([a-zA-Z0-9_]+_\d+)\r?\n\s*tile\s*\{([^}]*)\}",RegexOptions.None,TimeSpan.FromSeconds(8))){var props=new Dictionary<string,string>();foreach(var line in m.Groups[2].Value.Split('\n')){var split=line.IndexOf('=');if(split>=0)props[line[..split].Trim()]=line[(split+1)..].Trim();}result.Add(new(m.Groups[1].Value,props));if(result.Count>200000)throw new ArgumentException("Слишком много тайлов.");}return result;
 }
 private static int Bound(int n,int min,int max)=>n>=min&&n<=max?n:throw new ArgumentException("Недопустимый размер данных тайлов.");
}
