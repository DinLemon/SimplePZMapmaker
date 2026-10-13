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
  if(protocol!="2026-07-28")continue;
  var draft=Read(await client.CallToolAsync("building_draft_create",new Dictionary<string,object?>{{"sessionToken",token},{"templateId","village-izba"}}));var id=draft.GetProperty("id").GetString();
  var commit=await client.CallToolAsync("building_draft_commit",new Dictionary<string,object?>{{"sessionToken",token},{"draftId",id},{"expectedRevision",1},{"x",30},{"y",30}});Check(commit.IsError!=true,"Draft commit failed");
  var conflict=await client.CallToolAsync("environment_paint",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",1},{"x",5},{"y",5},{"width",4},{"height",4},{"terrain",5}});Check(conflict.IsError==true,"Stale MCP write accepted");
  using var snapshot=JsonDocument.Parse(await http.GetStringAsync("/api/agent/session/"+token));Check(snapshot.RootElement.GetProperty("revision").GetInt64()==2&&snapshot.RootElement.GetProperty("project").GetProperty("buildings").GetArrayLength()==1,"Protocol lost committed map");
  var export=Read(await client.CallToolAsync("project_export",new Dictionary<string,object?>{{"sessionToken",token},{"expectedRevision",2}}));var url=export.GetProperty("downloadUrl").GetString();var zip=await http.GetByteArrayAsync(url);Check(zip.Length>1000&&zip[0]==80&&zip[1]==75,"MCP export not ZIP");
  Console.WriteLine("PASS MCP isolated draft, revision commit/conflict, native export download");
 }
}finally{using var close=await http.PostAsJsonAsync("/api/agent/session/"+token+"/close",new{});close.EnsureSuccessStatusCode();}
static JsonElement Read(CallToolResult result){Check(result.IsError!=true,"Tool returned error");var text=result.Content.OfType<TextContentBlock>().First().Text;return JsonSerializer.Deserialize<JsonElement>(text,NativeMapExporter.JsonOptions);}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
