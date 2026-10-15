using System.IO.Compression;
namespace SimplePZMapmaker.Core.Export;

public sealed record NativeExportProgress(string Stage,string? SectionId,int SectionsCompleted,int SectionsTotal,int CellsCompleted,int CellsTotal,int ReusedSections);
/// <summary>A cache stores only native cell data. Metadata and world context are always written afresh.</summary>
public interface INativeSectionCache
{
 bool Write(MapProject project,ZipArchive destination,string root,CancellationToken token,Action<int>? cellCompleted,Action<ZipArchive> compose);
}
public sealed class NativeExportContext(INativeSectionCache? cache=null,Action<NativeExportProgress>? progress=null)
{
 public INativeSectionCache? Cache {get;}=cache;
 int sections,cells,reused,totalSections,totalCells;string? section;
 public void Begin(int sectionCount,int cellCount){totalSections=sectionCount;totalCells=cellCount;Report("starting");}
 public void Section(string id){section=id;Report("section");}
 public void Cell(){cells++;Report("cells");}
 public void Reused(){reused++;}
 public void SectionDone(){sections++;Report("section-completed");}
 public void Complete()=>Report("completed");
 void Report(string stage)=>progress?.Invoke(new(stage,section,sections,totalSections,cells,totalCells,reused));
}
