using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;
/// <summary>Bounded static ASCII X/OBJ mesh reader. No scripts, animation or external references are evaluated.</summary>
public static class StaticModelGeometry
{
 public sealed record Mesh(float[] Positions,float[] Uvs,int[] Indices);
 private static readonly Regex Numbers=new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",RegexOptions.Compiled,TimeSpan.FromSeconds(2));
 public static Mesh[] ReadX(string text)
 {
  if(text.Length>32*1024*1024||!text.StartsWith("xof ",StringComparison.Ordinal)||!text[..Math.Min(16,text.Length)].Contains("txt",StringComparison.Ordinal))throw new ArgumentException("Only static ASCII DirectX .X files are supported.");
  var nodes=ScriptBlocks.Read(text);var meshes=new List<Mesh>();
  foreach(var block in nodes.Where(n=>n.Kind=="Mesh"&&!n.Template)){
   var childStart=nodes.Where(n=>n.Parent==block).Select(n=>n.Start).DefaultIfEmpty(block.End).Min();var vals=Values(text[(block.BodyStart)..childStart]);var at=0;
   int I(){if(at>=vals.Length||vals[at]!=Math.Truncate(vals[at])||vals[at]<0||vals[at]>1000000)throw new ArgumentException("Invalid .X index.");return (int)vals[at++];}
   float F(){if(at>=vals.Length||!double.IsFinite(vals[at])||Math.Abs(vals[at])>1e8)throw new ArgumentException("Invalid .X vertex.");return (float)vals[at++];}
   var count=I();if(count is <3 or >200000)throw new ArgumentException("Too many .X vertices.");var positions=new float[count*3];for(var i=0;i<positions.Length;i++)positions[i]=F();var faces=I();if(faces>200000)throw new ArgumentException("Too many .X faces.");var indices=new List<int>();
   for(var f=0;f<faces;f++){var size=I();if(size is <3 or >64)throw new ArgumentException("Invalid .X face.");var polygon=new int[size];for(var j=0;j<size;j++){polygon[j]=I();if(polygon[j]>=count)throw new ArgumentException("The .X vertex index is outside the mesh.");}for(var j=1;j<size-1;j++){indices.Add(polygon[0]);indices.Add(polygon[j]);indices.Add(polygon[j+1]);}}
   var uv=new float[count*2];var uvNode=nodes.FirstOrDefault(n=>n.Parent==block&&n.Kind=="MeshTextureCoords");if(uvNode is not null){var values=Values(text[uvNode.BodyStart..uvNode.End]);if(values.Length!=count*2+1||values[0]!=count)throw new ArgumentException("Invalid .X UV mesh.");for(var i=0;i<uv.Length;i++)uv[i]=(float)values[i+1];}
   var matrix=Matrix4x4.Identity;for(var parent=block.Parent;parent is not null;parent=parent.Parent){var transform=nodes.FirstOrDefault(n=>n.Parent==parent&&n.Kind=="FrameTransformMatrix");if(transform is null)continue;var v=Values(text[transform.BodyStart..transform.End]);if(v.Length!=16)throw new ArgumentException("Invalid .X matrix.");var m=v.Select(x=>(float)x).ToArray();matrix*=new Matrix4x4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);}
   for(var i=0;i<count;i++){var p=Vector3.Transform(new(positions[i*3],positions[i*3+1],positions[i*3+2]),matrix);positions[i*3]=p.X;positions[i*3+1]=p.Y;positions[i*3+2]=p.Z;}
   meshes.Add(new(positions,uv,indices.ToArray()));if(meshes.Sum(m=>m.Positions.Length)>900000||meshes.Sum(m=>m.Indices.Length)>1800000)throw new ArgumentException("The model is too large for preview.");
  }
  if(meshes.Count==0)throw new ArgumentException("No static Mesh was found in the .X file.");return meshes.ToArray();
 }
 public static Mesh[] ReadObj(string text)
 {
  if(text.Length>32*1024*1024)throw new ArgumentException("The OBJ file is too large.");var vertices=new List<float[]>();var tex=new List<float[]>();var positions=new List<float>();var uv=new List<float>();var indices=new List<int>();
  foreach(var line in text.Split('\n')){var fields=line.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(fields.Length==0||fields[0].StartsWith('#'))continue;
   float V(string v){if(!float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!float.IsFinite(n)||Math.Abs(n)>1e8)throw new ArgumentException("Invalid OBJ vertex.");return n;}
   if(fields[0]=="v"&&fields.Length>=4)vertices.Add([V(fields[1]),V(fields[2]),V(fields[3])]);else if(fields[0]=="vt"&&fields.Length>=3)tex.Add([V(fields[1]),V(fields[2])]);else if(fields[0]=="f"){
    if(fields.Length is <4 or >65)throw new ArgumentException("Invalid OBJ face.");var begin=positions.Count/3;
    foreach(var field in fields.Skip(1)){var parts=field.Split('/');int Index(string s,int count){if(!int.TryParse(s,CultureInfo.InvariantCulture,out var i)||i==0)throw new ArgumentException("Invalid OBJ index.");i=i<0?count+i:i-1;if(i<0||i>=count)throw new ArgumentException("The OBJ index is outside the mesh.");return i;}
     positions.AddRange(vertices[Index(parts[0],vertices.Count)]);uv.AddRange(parts.Length>1&&parts[1].Length>0?tex[Index(parts[1],tex.Count)]:[0f,0f]);}
    for(var i=1;i<fields.Length-2;i++){indices.Add(begin);indices.Add(begin+i);indices.Add(begin+i+1);}
   }
   if(vertices.Count>200000||positions.Count>900000||indices.Count>1800000)throw new ArgumentException("The OBJ file is too large for preview.");
  }
  if(indices.Count==0)throw new ArgumentException("The OBJ file contains no faces.");return [new(positions.ToArray(),uv.ToArray(),indices.ToArray())];
 }
 private static double[] Values(string value){var values=new List<double>();foreach(Match match in Numbers.Matches(value)){if(values.Count>=2400000)throw new ArgumentException("Too many numbers in the model mesh.");var number=double.Parse(match.Value,CultureInfo.InvariantCulture);if(!double.IsFinite(number)||Math.Abs(number)>1e8)throw new ArgumentException("Invalid number in the model mesh.");values.Add(number);}return values.ToArray();}
}
public static class ScriptBlocks
{
 public sealed class Block(string kind,string name,int start,int bodyStart,int end,Block? parent,bool template)
 { public string Kind {get;}=kind;public string Name {get;}=name;public int Start {get;}=start;public int BodyStart {get;}=bodyStart;public int End {get;set;}=end;public Block? Parent {get;}=parent;public bool Template {get;}=template; }
 public static List<Block> Read(string source)
 {
  if(source.Length>32*1024*1024)throw new ArgumentException("The declaration file is too large.");
  var tokens=Regex.Matches(source,@"//[^\r\n]*|/\*[\s\S]*?\*/|\""(?:\\.|[^\""\\])*\""|[A-Za-z_][A-Za-z0-9_.\-/]*|[{}]",RegexOptions.None,TimeSpan.FromSeconds(5)).Where(m=>!m.Value.StartsWith("//")&&!m.Value.StartsWith("/*")).ToArray();
  var result=new List<Block>();var stack=new Stack<int>();
  for(var i=0;i<tokens.Length;i++){var token=tokens[i];if(token.Value=="{"){var previous=i>0?tokens[i-1].Value:"";var before=i>1?tokens[i-2].Value:"";var named=before is "module" or "model" or "item" or "attachment" or "Frame" or "Mesh" or "MeshTextureCoords" or "Material";var kind=named?before:previous;var start=named?tokens[i-2].Index:tokens[Math.Max(0,i-1)].Index;var isTemplate=(i>2&&tokens[i-3].Value=="template")||before=="template"||(stack.Count>0&&result[stack.Peek()].Template);result.Add(new(kind,named?previous:"",start,token.Index+1,-1,stack.Count>0?result[stack.Peek()]:null,isTemplate));stack.Push(result.Count-1);}else if(token.Value=="}"){if(stack.Count==0)throw new ArgumentException("Unmatched declaration brace.");result[stack.Pop()].End=token.Index;}if(stack.Count>64||result.Count>100000)throw new ArgumentException("The declaration file is too complex.");}
  if(stack.Count!=0)throw new ArgumentException("Unclosed declaration.");return result;
 }
 public static Dictionary<string,string> Properties(string source,Block block,IEnumerable<Block> nodes)
 {
  var body=source[block.BodyStart..block.End];foreach(var child in nodes.Where(n=>n.Parent==block).OrderByDescending(n=>n.Start)){var start=child.Start-block.BodyStart;body=body.Remove(start,child.End+1-child.Start);}
  return Regex.Matches(body,@"(?:^|[,\r\n])\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*([^,\r\n]+)",RegexOptions.None,TimeSpan.FromSeconds(2)).GroupBy(m=>m.Groups[1].Value,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Last().Groups[2].Value.Trim().Trim('"'),StringComparer.OrdinalIgnoreCase);
 }
}
