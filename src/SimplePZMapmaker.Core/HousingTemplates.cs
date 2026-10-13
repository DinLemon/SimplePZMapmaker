namespace SimplePZMapmaker.Core;

/// <summary>Original, hand-drawn adaptations of researched Russian housing types.</summary>
public static class HousingTemplates
{
 public sealed record Descriptor(string Id,string Name,string Category,string Description,int Width,int Height,int Levels,int Basements,IReadOnlyList<string> References);
 private const string IzbaReference="https://ar.culture.ru/ru/article/krestyanskiy-byt-russkaya-izba-i-podvore";
 private const string MuseumReference="https://www.semenkovo.ru/ru/monuments/dom-ue-pudovoy";
 private const string BrickReference="https://www.russianrealty.ru/useful/serii_domov/43740/";
 private const string SettlementReference="https://www.photobuildings.com/projects/171/";
 private const string PanelReference="https://photobuildings.com/projects/622/";
 private const string TowerReference="https://photobuildings.com/projects/164/?lang=ru";
 public static IReadOnlyList<Descriptor> Catalog {get;}=Array.AsReadOnly(new Descriptor[]{
  new("village-izba","Деревенская изба с сенями","village","Деревянный дом: жилая изба, кухня, спальня, сени, кладовая, крыльцо и погреб. Двускатная крыша.",16,14,1,1,Array.AsReadOnly(new[]{IzbaReference,MuseumReference})),
  new("village-veranda","Сельский дом с верандой","village","Дом с боковой деревянной верандой и выступающим крыльцом; кухня, спальня и гостиная образуют отдельный жилой объём.",20,16,1,0,Array.AsReadOnly(new[]{MuseumReference,IzbaReference})),
  new("settlement-two-storey","Поселковый двухэтажный дом","settlement","Четыре квартиры на этаже вокруг центральной лестницы; отдельные прихожие, кухни, спальни и санузлы. Восемь квартир всего.",30,24,2,0,Array.AsReadOnly(new[]{SettlementReference})),
  new("urban-brick-five","Кирпичная пятиэтажка","urban","Два независимых подъезда, по четыре двухкомнатные квартиры на площадке, регулярные окна и балконы. Авторская адаптация кирпичного секционного жилья.",48,26,5,0,Array.AsReadOnly(new[]{BrickReference})),
  new("urban-panel-five","Панельная пятиэтажка","urban","Два независимых подъезда, компактные однокомнатные квартиры с прихожими, серые фасады, балконы и плоская крыша. Другая планировка секций.",48,24,5,0,Array.AsReadOnly(new[]{PanelReference})),
  new("urban-panel-nine","Панельная девятиэтажная башня","urban","Один лестничный узел, четыре квартиры на этаже, подсобное помещение, балконы и плоская крыша. Лифт не моделируется.",32,30,9,0,Array.AsReadOnly(new[]{TowerReference,PanelReference}))
 });

 public static Building Create(string id)
 {
  var descriptor=Catalog.SingleOrDefault(d=>d.Id==id)??throw new ArgumentException("Неизвестный готовый шаблон здания.",nameof(id));
  var b=new Building{X=8,Y=8,Width=descriptor.Width,Height=descriptor.Height,Name=descriptor.Name,Kind=descriptor.Category=="village"?"house":"apartments",Modified=true,LevelsOrigin="manual",SourceKind="original-housing-template",Attribution=["Original housing layout by SimplePZMapmaker; architectural adaptation, not a replica of a standard project.",..descriptor.References]};
  b.Design=new(){MinLevel=-descriptor.Basements,Shape=descriptor.Category=="village"?"custom":"rect",Style=id switch{"village-izba"=>"cottage","village-veranda"=>"modern","urban-panel-five" or "urban-panel-nine"=>"modern",_=>"brick"},Roof=id is "village-izba" or "settlement-two-storey"?"gabled":id=="village-veranda"?"shed":"flat"};
  b.Design.Material=VisualStyles.All[b.Design.Style].Material;
  switch(id){
   case "village-izba":Izba(b);break;
   case "village-veranda":Veranda(b);break;
   case "settlement-two-storey":Settlement(b);break;
   case "urban-brick-five":BrickFive(b);break;
   case "urban-panel-five":PanelFive(b);break;
   case "urban-panel-nine":PanelNine(b);break;
  }
  for(var z=0;z<b.Design.Floors.Count;z++){
   BuildingDesigner.Furnish(b,z,new Random(20261012+z));
   var f=b.Design.Floors[z];f.Objects.RemoveAll(o=>o.Kind!="stairs"&&f.Rooms[BuildingDesigner.Cell(b,f,o.X,o.Y).ToString()]=="hall");
   if(id=="urban-panel-five")AddCombinedLivingBeds(b,z);
  }
  BuildingDesigner.Validate(b);return b;
 }

 private static void Izba(Building b)
 {
  var cellar=new Plan(b,-1);var passage=cellar.Room("hall",0,8,12,4);var store=cellar.Room("storage",0,0,12,8);cellar.Join(passage,store);cellar.Stairs(2,9,"w");cellar.Finish();
  var p=new Plan(b,0);var living=p.Room("livingroom",0,0,8,8);var kitchen=p.Room("kitchen",8,0,4,8);var bedroom=p.Room("bedroom",12,0,4,8);var seni=p.Room("hall",0,8,12,4);var pantry=p.Room("storage",12,8,4,4);var porch=p.Room("balcony",4,12,4,2);
  p.Join(seni,living);p.Join(seni,kitchen);p.Join(kitchen,bedroom);p.Join(seni,pantry);p.Join(seni,porch);p.Entrance(6,14,"n");p.Finish();
 }
 private static void Veranda(Building b)
 {
  var p=new Plan(b,0);var kitchen=p.Room("kitchen",0,0,6,6);var bedroom=p.Room("bedroom",6,0,8,6);var living=p.Room("livingroom",0,6,10,6);var bathroom=p.Room("bathroom",10,6,4,3);var hall=p.Room("hall",10,9,4,3);var veranda=p.Room("hall",14,3,6,9);var porch=p.Room("balcony",14,12,4,4);
  p.Join(kitchen,living);p.Join(bedroom,living);p.Join(living,hall);p.Join(hall,bathroom);p.Join(hall,veranda);p.Join(veranda,porch);p.Entrance(16,16,"n");p.Finish();
  // The annex uses the wooden vanilla palette, while the main volume stays plaster.
  foreach(var e in b.Design!.Floors[0].Edges.Where(e=>e.X>=14)){var offset=e.Kind=="door"?10:e.Kind=="window"?8:0;e.WallTile="walls_exterior_house_01_"+(48+offset+(e.Edge=="n"?1:0));}
 }
 private static void Settlement(Building b)
 {
  for(var z=0;z<2;z++){
   var p=new Plan(b,z);var core=p.Room("hall",12,0,6,24);
   SettlementFlat(p,0,0,12,12,true,core);SettlementFlat(p,18,0,12,12,false,core);SettlementFlat(p,0,12,12,12,true,core);SettlementFlat(p,18,12,12,12,false,core);
   if(z==0){p.Stairs(14,3);p.Entrance(15,0,"n");}p.Finish();
  }
 }
 private static void SettlementFlat(Plan p,int x,int y,int width,int height,bool left,char core)
 {
  var inner=left?x+width-2:x;var body=left?x:x+2;var hall=p.Room("hall",inner,y,2,height);var living=p.Room("livingroom",body,y,width-2,6);
  var outer=left?body:body+5;var near=left?body+5:body;var kitchen=p.Room("kitchen",outer,y+6,5,6);var bedroom=p.Room("bedroom",near,y+6,5,3);var bath=p.Room("bathroom",near,y+9,5,3);
  p.Join(core,hall);p.Join(hall,living);p.Join(living,kitchen);p.Join(hall,bedroom);p.Join(hall,bath);
 }
 private static void BrickFive(Building b)
 {
  for(var z=0;z<5;z++){
   var p=new Plan(b,z);var balcony=p.Code("balcony");
   foreach(var section in new[]{0,24}){
    var core=p.Room("hall",section+10,2,4,22);
    BrickFlat(p,section,2,true,false,core,balcony);BrickFlat(p,section+14,2,false,false,core,balcony);
    BrickFlat(p,section,13,true,true,core,balcony);BrickFlat(p,section+14,13,false,true,core,balcony);
    if(z<4)p.Stairs(section+11,5);if(z==0)p.Entrance(section+12,2,"n");
   }p.Finish();
  }
 }
 private static void BrickFlat(Plan p,int x,int y,bool left,bool south,char core,char balcony)
 {
  var inner=left?x+5:x;var outer=left?x:x+5;var bodyY=south?y+3:y;
  var living=p.Room("livingroom",inner,bodyY,5,8);var kitchen=p.Room("kitchen",outer,bodyY,5,4);var bath=p.Room("bathroom",outer,bodyY+4,5,4);var bedroom=p.Room("bedroom",x,south?y:y+8,10,3);
  p.Join(core,living);p.Join(living,kitchen);p.Join(living,bath);p.Join(living,bedroom);
  p.Fill(balcony,inner+1,south?24:0,4,2);p.Join(living,balcony);
 }
 private static void PanelFive(Building b)
 {
  for(var z=0;z<5;z++){
   var p=new Plan(b,z);var balcony=p.Code("balcony");
   foreach(var section in new[]{0,24}){
    var core=p.Room("hall",section+9,2,6,20);
    PanelFlat(p,section,2,true,false,core,balcony);PanelFlat(p,section+15,2,false,false,core,balcony);
    PanelFlat(p,section,12,true,true,core,balcony);PanelFlat(p,section+15,12,false,true,core,balcony);
    if(z<4)p.Stairs(section+11,4);if(z==0)p.Entrance(section+12,2,"n");
   }p.Finish();GreyFacade(b.Design!.Floors[z]);
  }
 }
 private static void PanelFlat(Plan p,int x,int y,bool left,bool south,char core,char balcony)
 {
  var inner=left?x+6:x;var outer=left?x:x+3;
  var living=p.Room("livingroom",outer,y,6,10);var hall=p.Room("hall",inner,y,3,3);var kitchen=p.Room("kitchen",inner,y+3,3,4);var bath=p.Room("bathroom",inner,y+7,3,3);
  p.Join(core,hall);p.Join(hall,living);p.Join(hall,kitchen);p.Join(kitchen,bath);p.Fill(balcony,outer+1,south?22:0,4,2);p.Join(living,balcony);
 }
 private static void PanelNine(Building b)
 {
  for(var z=0;z<9;z++){
   var p=new Plan(b,z);var core=p.Room("hall",12,2,8,26);var utility=p.Room("storage",16,11,4,4,true);p.Join(core,utility);
   TowerFlat(p,0,2,true,false,core);TowerFlat(p,20,2,false,false,core);TowerFlat(p,0,15,true,true,core);TowerFlat(p,20,15,false,true,core);
   if(z<8)p.Stairs(14,5);if(z==0)p.Entrance(15,2,"n");p.Finish();GreyFacade(b.Design!.Floors[z]);
  }
 }
 private static void TowerFlat(Plan p,int x,int y,bool left,bool south,char core)
 {
  var inner=left?x+9:x;var body=left?x:x+3;var near=left?body+5:body;var outer=left?body:body+4;
  var hall=p.Room("hall",inner,y,3,13);var living=p.Room("livingroom",body,y,9,6);var bed=p.Room("bedroom",outer,y+6,5,7);var kitchen=p.Room("kitchen",near,y+6,4,4);var bath=p.Room("bathroom",near,y+10,4,3);
  p.Join(core,hall);p.Join(hall,living);p.Join(living,bed);p.Join(hall,kitchen);p.Join(hall,bath);var balcony=p.Room("balcony",body+2,south?28:0,4,2);p.Join(south?bed:living,balcony);
 }
 private static void GreyFacade(BuildingFloor f)
 {
  // Vanilla grey rendered wall palette; style "modern" supplies the matching interior furniture.
  foreach(var e in f.Edges){var offset=e.Kind=="door"?10:e.Kind=="window"?8:0;e.WallTile="walls_exterior_house_02_"+(64+offset+(e.Edge=="n"?1:0));}
 }
 private static void AddCombinedLivingBeds(Building b,int z)
 {
  var f=b.Design!.Floors[z];
  foreach(var code in f.Rooms.Where(r=>r.Value=="livingroom").Select(r=>r.Key[0])){
   for(var y=0;y<b.Height-1;y++)for(var x=0;x<b.Width;x++){
    if(BuildingDesigner.Cell(b,f,x,y)!=code||BuildingDesigner.Cell(b,f,x,y+1)!=code)continue;
    var bed=new BuildingObject{X=x,Y=y,Kind="bed",Style=b.Design.Style};f.Objects.Add(bed);
    try{BuildingDesigner.Validate(b);goto Placed;}catch(ArgumentException){f.Objects.Remove(bed);}
   }Placed:;
  }
 }

 /// <summary>Rectangles and explicitly selected room adjacencies; never calls Generate.</summary>
 private sealed class Plan
 {
  private readonly Building building;private readonly int level;private readonly char[] cells;private readonly BuildingFloor floor=new();private readonly List<(char A,char B)> joins=[];private readonly List<(int X,int Y,string Edge)> entrances=[];
  internal Plan(Building b,int z){building=b;level=z;cells=Enumerable.Repeat('.',b.Width*b.Height).ToArray();}
  internal char Code(string name){if(floor.Rooms.Count==35)throw new InvalidOperationException("Шаблон превышает 35 кодов комнат.");var code="0123456789abcdefghijklmnopqrstuvwxyz"[floor.Rooms.Count];floor.Rooms.Add(code.ToString(),name);return code;}
  internal char Room(string name,int x,int y,int w,int h,bool replace=false){var code=Code(name);Fill(code,x,y,w,h,replace);return code;}
  internal void Fill(char code,int x,int y,int w,int h,bool replace=false){for(var yy=y;yy<y+h;yy++)for(var xx=x;xx<x+w;xx++){var i=yy*building.Width+xx;if(!replace&&cells[i]!='.')throw new InvalidOperationException("Пересечение комнат шаблона.");cells[i]=code;}}
  internal void Join(char a,char b)=>joins.Add((a,b));
  internal void Entrance(int x,int y,string edge)=>entrances.Add((x,y,edge));
  internal void Stairs(int x,int y,string direction="n")=>floor.Objects.Add(new(){Kind="stairs",X=x,Y=y,Direction=direction});
  internal void Finish(){
   floor.Cells=new(cells);floor.Edges=BuildingDesigner.AutoEdges(building,floor,level);
   foreach(var e in floor.Edges){var a=BuildingDesigner.Cell(building,floor,e.X,e.Y);var b=e.Edge=="n"?BuildingDesigner.Cell(building,floor,e.X,e.Y-1):BuildingDesigner.Cell(building,floor,e.X-1,e.Y);var outer=a=='.'||b=='.';var room=a=='.'?b:a;
    e.Kind=outer&&level>=0&&floor.Rooms[room.ToString()]!="balcony"&&(e.Edge=="n"?e.X:e.Y)%4==2?"window":"wall";
   }
   foreach(var (a,b) in joins){
    var candidates=floor.Edges.Where(e=>{var first=BuildingDesigner.Cell(building,floor,e.X,e.Y);var second=e.Edge=="n"?BuildingDesigner.Cell(building,floor,e.X,e.Y-1):BuildingDesigner.Cell(building,floor,e.X-1,e.Y);return first==a&&second==b||first==b&&second==a;}).ToArray();
    // Each apartment has its own room code, so its balcony adjacency selects only that apartment's door.
    if(candidates.Length==0)throw new InvalidOperationException($"Нет общей стены комнат {a}/{b}.");
    var chosen=candidates[candidates.Length/2];chosen.Kind="door";
   }
   foreach(var entrance in entrances){var e=floor.Edges.Single(e=>e.X==entrance.X&&e.Y==entrance.Y&&e.Edge==entrance.Edge);e.Kind="door";}
   building.Design!.Floors.Add(floor);
  }
 }
}
