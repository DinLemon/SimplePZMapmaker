namespace SimplePZMapmaker.Web.Services;
public sealed record DependencyIssue(string Code,string ModId,string[] Chain,string Action);
public sealed record DependencyReport(bool Valid,string[] Requested,string[] ActivationOrder,DependencyIssue[] Issues);
public static class ModDependencies {
 public static DependencyReport Check(IReadOnlyDictionary<string,ModDescriptor> mods,string[] ids){
  if(ids is null||ids.Length>512)throw new ArgumentException("Выберите не более512 модов.");var issues=new List<DependencyIssue>();var done=new HashSet<string>();var chain=new List<string>();var ordered=new List<string>();
  void Visit(string id){
   if(chain.Contains(id)){issues.Add(new("dependency-cycle",id,[..chain,id],"Отключите один из модов этой цепочки или установите совместимые версии без циклической зависимости."));return;}
   if(done.Contains(id))return;
   if(!mods.TryGetValue(id,out var mod)){issues.Add(new("missing-dependency",id,[..chain,id],$"Установите «{id}» через Workshop или добавьте его папку и повторите поиск. Проверьте совместимость с выбранной версией игры."));return;}
   chain.Add(id);foreach(var dependency in mod.Requires)Visit(dependency);chain.RemoveAt(chain.Count-1);done.Add(id);ordered.Add(id);
  }
  foreach(var id in ids.Distinct())Visit(id);return new(issues.Count==0,ids.Distinct().ToArray(),ordered.ToArray(),issues.ToArray());
 }
}
public sealed partial class GameAssetCatalog {
 public DependencyReport CheckModDependencies(string[] ids){lock(sync)return ModDependencies.Check(discoveredMods,ids);}
}
