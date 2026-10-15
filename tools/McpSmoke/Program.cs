using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
var endpoint=Environment.GetEnvironmentVariable("PZMAP_TEST_URL")??"http://127.0.0.1:48765";
using var http=new HttpClient{BaseAddress=new Uri(endpoint),Timeout=TimeSpan.FromSeconds(120)};
var response=await http.PostAsJsonAsync("/api/agent/session",MapGenerator.Generate(new(){Kind="blank"}));response.EnsureSuccessStatusCode();using var session=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var token=session.RootElement.GetProperty("token").GetString()!;
try{
 foreach(var protocol in new[]{"2026-07-28","2025-11-25"}){
  await using var client=await McpClient.CreateAsync(new HttpClientTransport(new(){Endpoint=new Uri(endpoint+"/mcp")}),new(){ProtocolVersion=protocol});
  var tools=await client.ListToolsAsync();Check(tools.Any(t=>t.Name=="building_draft_commit")&&tools.Any(t=>t.Name=="models_search"),"MCP tools missing");
  var result=await client.CallToolAsync("project_summary",new Dictionary<string,object?>{{"sessionToken",token}});Check(result.IsError!=true,"Summary failed");
  Console.WriteLine($"PASS MCP {protocol}: discovery, schema and project summary ({tools.Count} tools)");
  await VerifyGameplay(client,http);
  await VerifyCheckedDesign(client,http);
  if(protocol!="2026-07-28")continue;
  var draft=Read(await client.CallToolAsync("building_draft_create",new Dictionary<string,object?>{{"sessionToken",token},{"templateId","village-izba"}}));var id=draft.GetProperty("id").GetString();
  var commit=await client.CallToolAsync("building_draft_commit",new Dictionary<string,object?>{{"sessionToken",token},{"draftId",id},{"expectedRevision",1},{"x",30},{"y",30}});Check(commit.IsError!=true,"Draft commit failed");
  var conflict=await client.CallToolAsync("environment_paint",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"x",5},{"y",5},{"width",4},{"height",4},{"terrain",5}});Check(conflict.IsError==true,"Stale MCP write accepted");
  using var snapshot=JsonDocument.Parse(await http.GetStringAsync("/api/agent/session/"+token));Check(snapshot.RootElement.GetProperty("revision").GetInt64()==2&&snapshot.RootElement.GetProperty("project").GetProperty("buildings").GetArrayLength()==1,"Protocol lost committed map");
  var export=Read(await client.CallToolAsync("project_export",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",2}}));var url=export.GetProperty("downloadUrl").GetString();var zip=await http.GetByteArrayAsync(url);Check(zip.Length>1000&&zip[0]==80&&zip[1]==75,"MCP export not ZIP");
  Console.WriteLine("PASS MCP isolated draft, revision commit/conflict, native export download");
  var full=Read(await client.CallToolAsync("building_draft_create",new Dictionary<string,object?>{{"sessionToken",token},{"levels",32},{"basements",32},{"width",12},{"height",10}}));var fullId=full.GetProperty("id").GetString();var fullDesign=full.GetProperty("building").GetProperty("design");
  Check(fullDesign.GetProperty("minLevel").GetInt32()==-32&&fullDesign.GetProperty("floors").GetArrayLength()==64&&fullDesign.GetProperty("roof").GetString()=="none","MCP did not retain all signed floors");
  foreach(var z in new[]{-32,31}){var scene=Read(await client.CallToolAsync("building_draft_preview",new Dictionary<string,object?>{{"sessionToken",token},{"draftId",fullId},{"level",z}}));Check(scene.GetProperty("minLevel").GetInt32()==z&&scene.GetProperty("maxLevel").GetInt32()==z&&scene.GetProperty("sprites").GetArrayLength()>0,"MCP signed preview failed");}
  var invalidLevel=await client.CallToolAsync("building_draft_preview",new Dictionary<string,object?>{{"sessionToken",token},{"draftId",fullId},{"level",32}});Check(invalidLevel.IsError==true,"MCP accepted z32");
  Check(Read(await client.CallToolAsync("project_summary",new Dictionary<string,object?>{{"sessionToken",token}})).GetProperty("revision").GetInt64()==2,"Isolated vertical draft changed the map revision");
  Console.WriteLine("PASS MCP 64 signed floors, open roof, endpoint previews and invalid z32 rejection");
  var planning=Read(await client.CallToolAsync("neighborhood_plan_create",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",2},{"constraints",new NeighborhoodConstraints{X=80,Y=80,Width=160,Height=160,MaximumBuildings=4,MaximumLevels=1,TemplateIds=["village-izba"],Roads=false}}}));
  var proposal=planning.GetProperty("proposalId").GetString();Check(Read(await client.CallToolAsync("project_summary",new Dictionary<string,object?>{{"sessionToken",token}})).GetProperty("buildings").GetArrayLength()==1,"Proposal mutated source");
  var planned=await client.CallToolAsync("neighborhood_plan_commit",new Dictionary<string,object?>{{"sessionToken",token},{"proposalId",proposal},{"expectedRevision",2}});Check(planned.IsError!=true,"Proposal commit failed");
  var stalePlan=await client.CallToolAsync("neighborhood_plan_commit",new Dictionary<string,object?>{{"sessionToken",token},{"proposalId",proposal},{"expectedRevision",2}});Check(stalePlan.IsError==true,"Stale plan accepted");
  var w=new WorldProject();for(var i=0;i<2;i++){var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=100+i;w.Maps.Add(new(){Project=p});}
  using var worldResponse=await http.PostAsJsonAsync("/api/agent/world/session",w);worldResponse.EnsureSuccessStatusCode();using var worldSession=JsonDocument.Parse(await worldResponse.Content.ReadAsStringAsync());var worldToken=worldSession.RootElement.GetProperty("token").GetString()!;
  try {
   var worldPlan=Read(await client.CallToolAsync("world_plan_create",new Dictionary<string,object?>{{"sessionToken",worldToken},{"expectedRevision",1},{"constraints",new NeighborhoodConstraints{MaximumBuildings=4,MaximumLevels=1,TemplateIds=["village-izba"],Roads=false}}}));
   var worldCommit=await client.CallToolAsync("world_plan_commit",new Dictionary<string,object?>{{"sessionToken",worldToken},{"proposalId",worldPlan.GetProperty("proposalId").GetString()},{"expectedRevision",1}});Check(worldCommit.IsError!=true,"World plan commit failed");
   var summary=Read(await client.CallToolAsync("world_summary",new Dictionary<string,object?>{{"sessionToken",worldToken}}));Check(summary.GetProperty("sections").EnumerateArray().All(m=>m.GetProperty("buildings").GetInt32()==2),"World budget not balanced across sections");
   var b=HousingTemplates.Create("village-izba");b.X=25850;b.Y=25680;
   var cross=await client.CallToolAsync("world_building_put",new Dictionary<string,object?>{{"sessionToken",worldToken},{"expectedRevision",2},{"building",b}});Check(cross.IsError!=true,"Shared building placement failed");
   var authored=Read(await client.CallToolAsync("building_template_read",new Dictionary<string,object?>{{"templateId","village-izba"}}));Check(authored.GetProperty("design").GetProperty("floors").GetArrayLength()==2,"Template authoring data missing");
   var shared=Read(await client.CallToolAsync("world_building_read",new Dictionary<string,object?>{{"sessionToken",worldToken},{"buildingId",b.Id}}));Check(shared.GetProperty("x").GetInt32()==25850,"World building coordinates changed");
   var preview=Read(await client.CallToolAsync("world_section_preview",new Dictionary<string,object?>{{"sessionToken",worldToken},{"memberId",w.Maps[1].Id},{"x",0},{"y",64},{"level",-1}}));Check(preview.GetProperty("sprites").GetArrayLength()>0&&preview.GetProperty("minLevel").GetInt32()==-1,"Shared signed-floor native preview failed");
   var badMove=await client.CallToolAsync("world_section_move",new Dictionary<string,object?>{{"sessionToken",worldToken},{"expectedRevision",3},{"memberId",w.Maps[1].Id},{"cellX",103},{"cellY",100}});Check(badMove.IsError==true,"Move orphaned shared building");
   var worldExport=Read(await client.CallToolAsync("world_export",new Dictionary<string,object?>{{"sessionToken",worldToken},{"expectedRevision",3}}));var archive=await http.GetByteArrayAsync(worldExport.GetProperty("downloadUrl").GetString());Check(archive.Length>1000&&archive[0]==80,"World export failed");
   using var finalWorld=JsonDocument.Parse(await http.GetStringAsync("/api/agent/world/session/"+worldToken));var output=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")!,"work","qa-results");Directory.CreateDirectory(output);await File.WriteAllBytesAsync(Path.Combine(output,"agent-planned-world.zip"),archive);await File.WriteAllTextAsync(Path.Combine(output,"agent-planned-world.pzworld.json"),finalWorld.RootElement.GetProperty("world").GetRawText());
   Console.WriteLine("PASS MCP constrained map/world proposals, atomic stale commits, balanced section budgets, shared building and native world export");
  } finally {using var closeWorld=await http.PostAsJsonAsync("/api/agent/world/session/"+worldToken+"/close",new{});closeWorld.EnsureSuccessStatusCode();}
 }
}finally{using var close=await http.PostAsJsonAsync("/api/agent/session/"+token+"/close",new{});close.EnsureSuccessStatusCode();}
static JsonElement Read(CallToolResult result){Check(result.IsError!=true,"Tool returned error");var text=result.Content.OfType<TextContentBlock>().First().Text;return JsonSerializer.Deserialize<JsonElement>(text,NativeMapExporter.JsonOptions);}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static async Task VerifyGameplay(McpClient client,HttpClient http){
 var p=MapGenerator.Generate(new(){Kind="blank",Name="Agent lighting and zones"});var b=HousingTemplates.Create("village-izba");b.X=20;b.Y=20;p.Buildings.Add(b);
 using var created=await http.PostAsJsonAsync("/api/agent/session",p);created.EnsureSuccessStatusCode();var session=JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());var token=session.GetProperty("token").GetString()!;
 try{
  var catalog=Read(await client.CallToolAsync("gameplay_catalog",new Dictionary<string,object?>()));Check(catalog.GetProperty("zoneTypes").EnumerateArray().Any(x=>x.GetString()=="Farm"),"MCP zone catalog absent");
  var zone=new GameZone{Name="Agent farm",Type="Farm",X=80,Y=80,Width=24,Height=24};var plan=Read(await client.CallToolAsync("gameplay_zones_plan",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"entries",new[]{new{zone}}}}));var id=plan.GetProperty("id").GetString();
  Check(Read(await client.CallToolAsync("gameplay_zones_read",new Dictionary<string,object?>{{"sessionToken",token}})).GetProperty("zones").GetArrayLength()==0,"Zone proposal mutated source");
  var overlap=await client.CallToolAsync("gameplay_zones_plan",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"entries",new[]{new{zone},new{zone=new GameZone{X=81,Y=81,Width=24,Height=24}}}}});Check(overlap.IsError==true,"Overlapping zone batch accepted");
  Check((await client.CallToolAsync("gameplay_zones_plan_commit",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"proposalId",id}})).IsError!=true,"Zone commit failed");
  Check((await client.CallToolAsync("gameplay_zones_plan_commit",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"proposalId",id}})).IsError==true,"Stale zone commit accepted");
  var status=Read(await client.CallToolAsync("lighting_status",new Dictionary<string,object?>()));
  if(status.GetProperty("available").GetBoolean()){
   var field=Read(await client.CallToolAsync("lighting_map_check",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",2},{"x",16},{"y",16},{"size",16},{"level",0},{"skyRgb",new[]{.8,.7,.6}},{"ambient",.8}}));Check(field.GetProperty("provenance").GetProperty("engine").GetString()=="Project Zomboid LightingJNI"&&field.GetProperty("provenance").GetProperty("gameSession").GetBoolean()==false&&field.GetProperty("cells").GetArrayLength()>0,"MCP genuine lighting absent");
   var draft=Read(await client.CallToolAsync("building_draft_create",new Dictionary<string,object?>{{"sessionToken",token},{"templateId","village-izba"}}));var df=Read(await client.CallToolAsync("lighting_draft_check",new Dictionary<string,object?>{{"sessionToken",token},{"draftId",draft.GetProperty("id").GetString()},{"size",16},{"level",-1}}));Check(df.GetProperty("cells").GetArrayLength()>0,"Draft basement lighting absent");
   Check((await client.CallToolAsync("lighting_map_check",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"x",16},{"y",16},{"size",16}})).IsError==true,"Stale native lighting accepted");
   Console.WriteLine("PASS MCP genuine map/draft LightingJNI, signed basement and stale lighting rejection");
  }else Console.WriteLine("SKIP native MCP lighting: installed game library unavailable; tool discovery/status verified");
  Check(Read(await client.CallToolAsync("project_summary",new Dictionary<string,object?>{{"sessionToken",token}})).GetProperty("revision").GetInt64()==2,"Lighting/draft reads mutated revision");
  var world=WorldTestsForSmoke();using var wr=await http.PostAsJsonAsync("/api/agent/world/session",world);wr.EnsureSuccessStatusCode();var wt=JsonSerializer.Deserialize<JsonElement>(await wr.Content.ReadAsStringAsync()).GetProperty("token").GetString()!;
  try{var entry=new{zone=new GameZone{Name="World farm",Type="Farm",X=80,Y=80,Width=24,Height=24},memberId=world.Maps[1].Id};var wp=Read(await client.CallToolAsync("gameplay_zones_plan",new Dictionary<string,object?>{{"sessionToken",wt},{"expectedRevision",1},{"scope","world"},{"entries",new[]{entry}}}));Check((await client.CallToolAsync("gameplay_zones_plan_commit",new Dictionary<string,object?>{{"sessionToken",wt},{"expectedRevision",1},{"proposalId",wp.GetProperty("id").GetString()}})).IsError!=true,"World zone commit failed");
   if(status.GetProperty("available").GetBoolean()){var field=Read(await client.CallToolAsync("lighting_world_check",new Dictionary<string,object?>{{"sessionToken",wt},{"expectedRevision",2},{"memberId",world.Maps[1].Id},{"x",16},{"y",16},{"size",8},{"level",0}}));Check(field.GetProperty("cells").GetArrayLength()==64,"World section lighting incomplete");}
  }finally{using var close=await http.PostAsJsonAsync("/api/agent/world/session/"+wt+"/close",new{});close.EnsureSuccessStatusCode();}
  Console.WriteLine("PASS MCP isolated gameplay zones, overlap/stale rejection and map/world atomic commits");
 }finally{using var close=await http.PostAsJsonAsync("/api/agent/session/"+token+"/close",new{});close.EnsureSuccessStatusCode();}
}
static WorldProject WorldTestsForSmoke(){var w=new WorldProject();for(var i=0;i<2;i++){var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=100+i;w.Maps.Add(new(){Project=p});}return w;}
static async Task VerifyCheckedDesign(McpClient client,HttpClient http){
 var p=MapGenerator.Generate(new(){Kind="blank",Name="Checked design SDK"});var b=new Building{X=8,Y=8};b.Design=BuildingDesigner.Generate(b,1,42,"rect");foreach(var f in b.Design.Floors)f.Objects.Clear();p.Buildings.Add(b);
 using var created=await http.PostAsJsonAsync("/api/agent/session",p);created.EnsureSuccessStatusCode();var token=JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync()).GetProperty("token").GetString()!;
 try{
  var parametric=Read(await client.CallToolAsync("design_parametric_building",new Dictionary<string,object?>{{"options",new{entrances=3,floors=3,apartmentsPerFloor=2,layout="staggered",facade="brick",seed=41,name="SDK apartment block"}}}));Check(parametric.GetProperty("totalApartments").GetInt32()==18,"Counted MCP apartments missing");
  var replacement=NeighborhoodPlanner.Copy(b);foreach(var edge in replacement.Design!.Floors[0].Edges.Where(e=>e.Kind=="door"))edge.Kind="wall";
  var available=Read(await client.CallToolAsync("lighting_status",new Dictionary<string,object?>())).GetProperty("available").GetBoolean();
  var constraints=new{width=64,height=64,maximumIterations=8,maximumChanges=12,requireLighting=available,lightingX=8,lightingY=8,lightingSize=16,minimumLight=0};
  var task=Read(await client.CallToolAsync("design_task_create",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"constraints",constraints},{"replacements",new[]{replacement}}}));var id=task.GetProperty("id").GetString();
  Check((await client.CallToolAsync("design_task_commit",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",1}})).IsError==true,"Unchecked candidate committed");
  var checkedTask=Read(await client.CallToolAsync("design_task_check",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",1}}));Check(!checkedTask.GetProperty("passed").GetBoolean(),"Sealed draft passed");
  Check((await client.CallToolAsync("design_task_repair",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",1}})).IsError==true,"Stale task revision accepted");
  var repaired=Read(await client.CallToolAsync("design_task_repair",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",2}}));Check(repaired.GetProperty("passed").GetBoolean()&&repaired.GetProperty("changes").GetArrayLength()>0,"Bounded repair did not restore access");
  if(available){var last=repaired.GetProperty("iterations").EnumerateArray().Last();Check(last.GetProperty("lightingStatus").GetString()=="checked"&&last.GetProperty("provenance").GetProperty("engine").GetString()=="Project Zomboid LightingJNI","Repair substituted lighting estimate");}
  Check(Read(await client.CallToolAsync("project_summary",new Dictionary<string,object?>{{"sessionToken",token}})).GetProperty("revision").GetInt64()==1,"Candidate mutated source before commit");
  var route=Read(await client.CallToolAsync("design_access_check",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"options",new{width=64,height=64,start=new{x=1,y=1,z=0},end=new{x=63,y=63,z=0}}}}));Check(route.GetProperty("path").GetArrayLength()>0,"MCP route absent");
  Check((await client.CallToolAsync("design_task_commit",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",3}})).IsError!=true,"Checked candidate commit failed");
  Check((await client.CallToolAsync("design_task_commit",new Dictionary<string,object?>{{"sessionToken",token},{"taskId",id},{"expectedTaskRevision",3}})).IsError==true,"Stale source checked candidate committed");
  Console.WriteLine("PASS MCP counted parametric building, isolated design/check/repair, access route, genuine evidence and stale task/source rejection");
 }finally{using var close=await http.PostAsJsonAsync("/api/agent/session/"+token+"/close",new{});close.EnsureSuccessStatusCode();}
}
