using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class FenceLineTests
{
    static FenceStyle Sheet=>new(){Id="sheet",W="fencing_01_40",N="fencing_01_41",NW="fencing_01_42",Post="fencing_01_43"};
    static FenceStyle Forged=>new(){Id="forged",W="pilna_fence_01_25",N="pilna_fence_01_26",WAlt="pilna_fence_01_28",NAlt="pilna_fence_01_29",AltEvery=3};
    static GateStyle Wicket=>new(){Id="wicket",Width=1,ClosedW=["fixtures_doors_fences_01_12"],ClosedN=["fixtures_doors_fences_01_13"],OpenW=["fixtures_doors_fences_01_14"],OpenN=["fixtures_doors_fences_01_15"]};
    static GateStyle Double=>new(){Id="double",Width=2,ClosedW=["fixtures_doors_fences_01_60","fixtures_doors_fences_01_61"],ClosedN=["fixtures_doors_fences_01_62","fixtures_doors_fences_01_63"]};
    static FenceLine Yard(params FenceGate[] gates)=>new(){Id="yard",Style="sheet",Points=[new(10,10),new(16,10),new(16,14),new(10,14),new(10,10)],Gates=[..gates]};
    static Dictionary<(int,int),List<string>> Cells(FenceLines.Composition c)=>c.Pieces.GroupBy(p=>(p.X,p.Y)).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Tile).OrderBy(t=>t,StringComparer.Ordinal).ToList());
    public static IEnumerable<(string,Action)> Cases() {
        yield return("fence lines put N and W edges on the right cells, merge corners and keep gates in line order",()=>{
            var c=FenceLines.Compose([Yard(new(){At=2,Style="double"},new(){At=7,Style="wicket",Open=true}),new(){Id="lane",Style="forged",Points=[new(20,10),new(27,10)]}],[Sheet,Forged],[Wicket,Double],64,64);var cells=Cells(c);
            // Top run (10,10)->(16,10): N edges of cells (10..15,10); the double gate covers edges 2-3, so cells 12 and 13.
            Check(cells[(10,10)].SequenceEqual(["fencing_01_42"]),"Corner cell did not merge into NW");Check(cells[(11,10)].SequenceEqual(["fencing_01_41"]),"Top run lost its N edge");
            Check(cells[(12,10)].SequenceEqual(["fixtures_doors_fences_01_62"])&&cells[(13,10)].SequenceEqual(["fixtures_doors_fences_01_63"]),"Double gate pieces are not in line order");
            // Right run (16,10)->(16,14): W edges of cells (16,10..13); the wicket is edge 7 = the second edge of that run, opened.
            Check(cells[(16,11)].SequenceEqual(["fixtures_doors_fences_01_14"])&&cells[(16,10)].SequenceEqual(["fencing_01_40"]),"Wicket is not on its edge");
            // Bottom run (16,14)->(10,14): N edges of cells (10..15,14); left run back up: W edges of (10,10..13).
            Check(Enumerable.Range(10,6).All(x=>cells[(x,14)].Contains("fencing_01_41"))&&Enumerable.Range(11,3).All(y=>cells[(10,y)].SequenceEqual(["fencing_01_40"])),"Closing runs are incomplete");
            Check(c.Pieces.Single(p=>p.Tile=="fixtures_doors_fences_01_62").Flags==0&&c.Pieces.First(p=>p.Tile=="fencing_01_41").Flags==2&&c.Pieces.First(p=>p.Tile=="fencing_01_40").Flags==4,"Navigation edge bits are wrong");
            // Forged sections with a pillar every third edge.
            Check(Enumerable.Range(20,7).Select(x=>cells[(x,10)].Single()).SequenceEqual(["pilna_fence_01_26","pilna_fence_01_26","pilna_fence_01_29","pilna_fence_01_26","pilna_fence_01_26","pilna_fence_01_29","pilna_fence_01_26"]),"Alternating pillar pattern is wrong");
            Check(c.Skipped.Count==0&&!c.Pieces.Any(p=>p.Tile=="fencing_01_43"),"Posts appeared without a post spacing");
        });
        yield return("gates opening to the left reverse their pieces, posts follow the spacing and blocked cells are skipped and counted",()=>{
            var line=new FenceLine{Id="street",Style="sheet",PostSpacing=3,Points=[new(0,5),new(9,5)],Gates=[new(){At=4,Style="double",OpensTo="left"}]};
            var c=FenceLines.Compose([line],[Sheet],[Double],64,64,blocked:(x,y)=>x==8?"building":null);var cells=Cells(c);
            Check(cells[(4,5)].Contains("fixtures_doors_fences_01_63")&&cells[(5,5)].Contains("fixtures_doors_fences_01_62"),"Left-opening gate was not mirrored");
            Check(new[]{0,3,6,9}.All(x=>cells.TryGetValue((x,5),out var t)&&t.Contains("fencing_01_43")),"Posts are not every third vertex and at the end");
            Check(!cells.ContainsKey((8,5))&&c.Skipped["building"]==1,"An edge on a building was not skipped and counted");
        });
        yield return("fence lines validate grid runs, styles and gate placement",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank"});p.FenceStyles=[Sheet];p.GateStyles=[Wicket,Double];
            p.Fences=[Yard(new FenceGate{At=0,Style="wicket"})];ProjectValidator.Validate(p);Check(p.Version==12,"A fence layer must raise the map format");
            var copy=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;Check(JsonSerializer.Serialize(copy.Fences)==JsonSerializer.Serialize(p.Fences),"Fences did not survive JSON");
            void Bad(Action<MapProject> change){var q=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;change(q);Reject(()=>ProjectValidator.Validate(q));}
            Bad(q=>q.Fences[0].Points=[new(1,1),new(4,5)]);Bad(q=>q.Fences[0].Style="missing");Bad(q=>q.Fences[0].Gates=[new(){At=5,Style="double"}]);
            Bad(q=>q.Fences[0].Gates=[new(){At=0,Style="wicket"},new(){At=0,Style="wicket"}]);Bad(q=>q.Fences[0].Gates=[new(){At=0,Style="wicket",OpensTo="up"}]);
            Bad(q=>q.Fences[0].Gates=[new(){At=0,Style="double",Open=true}]);Bad(q=>q.FenceStyles[0].W="not a tile");Bad(q=>q.Fences.Add(Yard()));
        });
        yield return("a fence shares a cell with a decoration and both reach the native lot",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank"});p.FenceStyles=[Sheet];p.GateStyles=[Wicket];p.Fences=[Yard(new FenceGate{At=1,Style="wicket"})];
            p.Decorations=[new(){Id="bush",X=11,Y=10,Asset="vegetation_ornamental_01_0",Parts=[new(0,0,"vegetation_ornamental_01_0")]}];ProjectValidator.Validate(p);
            var header=Lotheader(NativeMapExporter.Export(p),p.WorldX,p.WorldY);
            foreach(var tile in new[]{"fencing_01_40","fencing_01_41","fencing_01_42","fixtures_doors_fences_01_13","vegetation_ornamental_01_0"})Check(header.Contains(tile),$"{tile} missing from the lot header");
        });
        yield return("a world fence crossing a section seam stays continuous in both sections",()=>{
            MapProject Section(int x){var s=MapGenerator.Generate(new(){Kind="blank"});s.WorldX=x;s.WorldY=100;s.WorldSection=true;s.Version=6;if(x!=100)s.Spawns=[];return s;}
            var world=new WorldProject{Name="Seam",Maps=[new(){Project=Section(100)},new(){Project=Section(101)}],FenceStyles=[Sheet],Fences=[new(){Id="across",Style="sheet",Points=[new(100*256+250,100*256+20),new(101*256+6,100*256+20)]}]};
            WorldAssembler.Validate(world);Check(world.Version==7,"World fences must raise the world format");
            var left=WorldAssembler.ProjectSection(world,world.Maps[0]);var right=WorldAssembler.ProjectSection(world,world.Maps[1]);
            var a=FenceLines.Compose(left.Fences,left.FenceStyles,left.GateStyles,256,256).Pieces;var b=FenceLines.Compose(right.Fences,right.FenceStyles,right.GateStyles,256,256).Pieces;
            Check(a.Select(p=>p.X).OrderBy(x=>x).SequenceEqual(Enumerable.Range(250,6))&&b.Select(p=>p.X).OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,6))&&a.Concat(b).All(p=>p.Y==20&&p.Tile=="fencing_01_41"),"Fence is not continuous across the seam");
            using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(world)));
            foreach(var cell in new[]{"100_100","101_100"}){var entry=zip.Entries.Single(e=>e.Name==$"{cell}.lotheader");using var r=new StreamReader(entry.Open(),Encoding.Latin1);Check(r.ReadToEnd().Contains("fencing_01_41"),$"Cell {cell} lost its part of the fence");}
            world.Maps[1].Project.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_44"}];Reject(()=>WorldAssembler.Validate(world));
        });
    }
    static string Lotheader(byte[] archive,int x,int y){using var zip=new ZipArchive(new MemoryStream(archive));var entry=zip.Entries.Single(e=>e.Name==$"{x}_{y}.lotheader");using var r=new StreamReader(entry.Open(),Encoding.Latin1);return r.ReadToEnd();}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
