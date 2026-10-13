namespace SimplePZMapmaker.Core;
public static class TerrainEditMask
{
 public static byte[] Decode(MapProject project){if(project.TerrainEdits is null||project.Width is <256 or >2048||project.Height is <256 or >2048)throw new ArgumentException("Маска ручной земли повреждена.");var bytes=checked(project.Width*project.Height/8);if(project.TerrainEdits.Length==0)return new byte[bytes];if(project.TerrainEdits.Length!=((bytes+2)/3)*4)throw new ArgumentException("Размер маски ручной земли не соответствует карте.");try{var mask=Convert.FromBase64String(project.TerrainEdits);if(mask.Length!=bytes||Convert.ToBase64String(mask)!=project.TerrainEdits)throw new ArgumentException("Маска ручной земли повреждена.");return mask;}catch(FormatException e){throw new ArgumentException("Маска ручной земли повреждена.",e);}}
 public static string Encode(byte[] mask)=>mask.All(b=>b==0)?"":Convert.ToBase64String(mask);
 public static bool IsEdited(byte[] mask,int index)=>index>=0&&index/8<mask.Length&&(mask[index/8]&(1<<(index%8)))!=0;
 public static void Mark(MapProject project,int index){if(index<0||index>=project.Width*project.Height)throw new ArgumentException("Клетка земли выходит за карту.");var mask=Decode(project);mask[index/8]|=(byte)(1<<(index%8));project.TerrainEdits=Encode(mask);project.Version=Math.Max(project.Version,6);}
}
