namespace SimplePZMapmaker.Core;
public static partial class HousingTemplates
{
 private const string FarmReference="https://www.semenkovo.ru/ru/taxonomy/term/3";
 private const string ClassicReference="https://www.culture.ru/materials/258275/arkhitektura-xx-veka-ot-moderna-do-formalizma";
 private const string CornerReference="https://photobuildings.com/projects/2228/";
 private static IEnumerable<Descriptor> AdditionalDescriptors()=>[
  new("village-barn","Сельский амбар с навесом","village","Хозяйственная постройка: зерновой зал, кладовая, мастерская и открытая площадка под навесом. Авторская планировка по образцам русского деревянного хозяйства.",24,18,1,0,[FarmReference]),
  new("village-workshop","Сельский гараж и мастерская","village","Небольшая пристройка с широким рабочим залом, кладовой и кабинетом. Односкатная крыша; самостоятельный вход.",18,14,1,0,[FarmReference]),
  new("settlement-shop","Поселковый магазин","settlement","Торговый зал, кабинет, кладовая, задний склад и санузел. Один этаж; планировка магазина создана для игры, без копирования типового проекта.",24,20,1,0,[SettlementReference]),
  new("settlement-school","Поселковая двухэтажная школа","settlement","Два этажа с четырьмя классами, учительской, санузлами и подсобными комнатами вокруг центрального коридора и лестницы. Авторская адаптация общественного здания.",30,32,2,0,[ClassicReference]),
  new("urban-classic-three","Кирпичный трёхэтажный дом","urban","Компактный дом с двумя просторными квартирами на этаже и центральной лестницей. Кирпичные стены и скатная крыша по мотивам городского жилья середины XX века.",36,28,3,0,[ClassicReference]),
  new("urban-corner-four","Г-образный городской дом","urban","Четыре этажа, три квартиры на уровне и общий лестничный узел. Два крыла образуют двор; авторская адаптация углового кирпичного корпуса.",40,36,4,0,[CornerReference,ClassicReference])
 ];
 private static void AdditionalPlan(Building b,string id)
 {
  switch(id){
   case "village-barn":Barn(b);break;case "village-workshop":RuralWorkshop(b);break;
   case "settlement-shop":Shop(b);break;case "settlement-school":School(b);break;
   case "urban-classic-three":ClassicThree(b);break;case "urban-corner-four":CornerFour(b);break;
   default:RegionalPlan(b,id);break;
  }
 }
 private static void Barn(Building b)
 {
  b.Kind="warehouse";b.Design!.Style="cottage";b.Design.Material="wood";b.Design.Roof="gabled";
  var p=new Plan(b,0);var barn=p.Room("warehouse",0,0,18,14);var storage=p.Room("storage",18,0,6,10);var workshop=p.Room("garage",18,10,6,4);var canopy=p.Room("balcony",4,14,10,4);
  p.Join(barn,storage);p.Join(barn,workshop);p.Join(barn,canopy);p.Entrance(8,0,"n");p.Finish();
 }
 private static void RuralWorkshop(Building b)
 {
  b.Kind="garage";b.Design!.Style="cottage";b.Design.Material="wood";b.Design.Roof="shed";
  var p=new Plan(b,0);var work=p.Room("garage",0,0,12,14);var store=p.Room("storage",12,0,6,8);var office=p.Room("office",12,8,6,6);
  p.Join(work,store);p.Join(work,office);p.Entrance(6,0,"n");p.Finish();
 }
 private static void Shop(Building b)
 {
  b.Kind="shop";b.Design!.Roof="flat";
  var p=new Plan(b,0);var retail=p.Room("conveniencestore",0,0,18,12);var office=p.Room("office",18,0,6,8);var store=p.Room("storage",18,8,6,4);var warehouse=p.Room("warehouse",0,12,18,8);var bathroom=p.Room("bathroom",18,12,6,8);
  p.Join(retail,office);p.Join(retail,store);p.Join(retail,warehouse);p.Join(warehouse,bathroom);p.Entrance(9,0,"n");p.Finish();
 }
 private static void School(Building b)
 {
  b.Kind="school";b.Design!.Roof="gabled";
  for(var z=0;z<2;z++){var p=new Plan(b,z);var core=p.Room("hall",12,0,6,32);
   for(var row=0;row<2;row++)foreach(var x in new[]{0,18})p.Join(core,p.Room("classroom",x,row*10,12,10));
   p.Join(core,p.Room("office",0,20,12,6));p.Join(core,p.Room("bathroom",18,20,12,6));p.Join(core,p.Room("storage",0,26,12,6));p.Join(core,p.Room("hall",18,26,12,6));
   if(z==0){p.Stairs(14,3);p.Entrance(15,0,"n");}p.Finish();
  }
 }
 private static void ClassicThree(Building b)
 {
  b.Design!.Roof="gabled";
  for(var z=0;z<3;z++){var p=new Plan(b,z);var core=p.Room("hall",15,0,6,28);ClassicFlat(p,0,true,core);ClassicFlat(p,21,false,core);if(z<2)p.Stairs(17,3);if(z==0)p.Entrance(18,0,"n");p.Finish();}
 }
 private static void ClassicFlat(Plan p,int x,bool left,char core)
 {
  var inner=left?x+12:x;var near=left?x+6:x+3;var outer=left?x:x+9;var body=left?x:x+3;
  var hall=p.Room("hall",inner,0,3,28);var living=p.Room("livingroom",body,0,12,12);var kitchen=p.Room("kitchen",outer,12,6,8);var bedroom=p.Room("bedroom",near,12,6,8);var bathroom=p.Room("bathroom",outer,20,6,8);var bedroom2=p.Room("bedroom",near,20,6,8);
  p.Join(core,hall);p.Join(hall,living);p.Join(living,kitchen);p.Join(hall,bedroom);p.Join(hall,bedroom2);p.Join(bedroom2,bathroom);
 }
 private static void CornerFour(Building b)
 {
  b.Design!.Shape="custom";b.Design.Roof="flat";
  for(var z=0;z<4;z++){var p=new Plan(b,z);var core=p.Room("hall",12,0,4,36);
   var upper=p.Room("livingroom",0,0,12,10);var kitchen=p.Room("kitchen",0,10,6,6);var bedroom=p.Room("bedroom",6,10,6,6);p.Join(core,upper);p.Join(upper,kitchen);p.Join(core,bedroom);
   var hall=p.Room("hall",16,0,3,16);var living=p.Room("livingroom",19,0,21,8);var kitchen2=p.Room("kitchen",19,8,7,8);var bathroom=p.Room("bathroom",26,8,7,8);var bedroom2=p.Room("bedroom",33,8,7,8);p.Join(core,hall);p.Join(hall,living);p.Join(hall,kitchen2);p.Join(kitchen2,bathroom);p.Join(living,bedroom2);
   var lower=p.Room("livingroom",0,16,12,10);var kitchen3=p.Room("kitchen",0,26,6,10);var bedroom3=p.Room("bedroom",6,26,6,10);p.Join(core,lower);p.Join(lower,kitchen3);p.Join(core,bedroom3);
   if(z<3)p.Stairs(13,3);if(z==0)p.Entrance(14,0,"n");p.Finish();
  }
 }
}
