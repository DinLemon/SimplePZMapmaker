using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public static class ModelRules {
 public static void Building(Building b,StaticModelCatalog models){foreach(var model in b.Design?.Floors.SelectMany(f=>f.StaticModels)??[])models.Validate(model);}
}
