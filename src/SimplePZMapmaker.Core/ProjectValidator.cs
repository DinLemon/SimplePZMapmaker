namespace SimplePZMapmaker.Core;
public static class ProjectValidator
{
    public static void Validate(MapProject p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if(p.Version is not(1 or 2 or 3 or 4 or 5 or 6)) Fail("Версия проекта не поддерживается.");
        ResourceDependencies.Validate(p.RequiredMods,p.AssetSources);
        if(p.StaticModels is null||p.StaticModels.Count>10000)Fail("Слой статичных моделей повреждён.");
        if(p.Version<6&&(p.RequiredMods.Count>0||p.AssetSources.Count>0||p.StaticModels.Count>0||p.Buildings.Concat(p.BuildingLibrary).Any(b=>b?.RequiredMods.Count>0||b?.AssetSources.Count>0||b?.Design?.Floors.Any(f=>f.StaticModels.Count>0)==true)))Fail("Моды и статичные модели требуют формат проекта версии6.");
        if(p.Width is <256 or >2048 || p.Height is <256 or >2048 || p.Width%256!=0 || p.Height%256!=0) Fail("Размер карты: от 256 до 2048, кратный 256.");
        if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>80 || p.Name.Any(char.IsControl)) Fail("Название: от 1 до 80 символов без переносов строк.");
        if(p.WorldX is <0 or >1000 || p.WorldY is <0 or >1000) Fail("Координаты мира: от 0 до 1000 ячеек.");
        if(p.Terrain is null || p.Terrain.Length!=p.Width*p.Height || p.Terrain.Any(t=>!Enum.IsDefined(t))) Fail("Слой земли повреждён.");
        TerrainEditMask.Decode(p);if(p.Version<6&&p.TerrainEdits.Length>0)Fail("Ручная маска земли требует формат проекта версии6.");
        if(p.Zombies is null || p.Zombies.Length!=p.Width*p.Height/64 || p.Zombies.Any(z=>z is <0 or >255)) Fail("Слой зомби повреждён.");
        if(p.WorldSection&&p.Version<6)Fail("Участок мира требует формат проекта версии6.");
        if(p.Buildings is null || p.Buildings.Count>4000 || p.Spawns is null || p.Spawns.Count<(p.WorldSection?0:1)||p.Spawns.Count>100 || p.Attribution is null || p.Attribution.Count>30 || p.Attribution.Any(a=>a is null || a.Length>1000)) Fail("Объекты проекта повреждены.");
        var occupied=new HashSet<int>(); var ids=new HashSet<string>();
        if(p.SurfaceRegions is null||p.SurfaceRegions.Count>128||p.SurfaceRegions.Any(r=>r is null||r.Theme is null||!EnvironmentStyles.Names.ContainsKey(r.Theme)||r.X<0||r.Y<0||r.Width<1||r.Height<1||(long)r.X+r.Width>p.Width||(long)r.Y+r.Height>p.Height))Fail("Участок оформления выходит за карту или повреждён (предел128 участков).");
        if(p.Version<5&&p.SurfaceRegions.Count>0)Fail("Оформление отдельных участков требует формат проекта версии5.");
        if(p.SurfaceTheme is null||!EnvironmentStyles.Names.ContainsKey(p.SurfaceTheme)||p.SurfaceTiles is null||p.SurfaceTiles.Count>100000||p.SurfaceTiles.Any(v=>v.Key<0||v.Key>=p.Terrain.Length||!VisualStyles.TileId(v.Value))||p.Decorations is null||p.Decorations.Count>10000)Fail("Игровые поверхности или декор повреждены.");
        if(p.BuildingLibrary is null||p.BuildingLibrary.Count>64)Fail("Библиотека зданий повреждена или содержит больше 64 шаблонов.");
        if(p.Version==1&&(p.BuildingLibrary.Count>0||p.Buildings.Any(b=>b?.Design is not null)))Fail("Этажи и шаблоны зданий требуют формат проекта версии 2.");
        if(p.Version<3&&p.Buildings.Concat(p.BuildingLibrary).Any(b=>b?.Design?.MinLevel!=0&&b?.Design is not null))Fail("Подвалы требуют формат проекта версии 3.");
        if(p.Version<4&&(p.SurfaceTheme!="legacy"||p.SurfaceTiles.Count>0||p.Decorations.Count>0||p.Buildings.Concat(p.BuildingLibrary).Any(UsesAssets)))Fail("Оформление и игровые ресурсы требуют формат проекта версии 4.");
        foreach(var template in p.BuildingLibrary){if(template is null||template.Name is null||template.Name.Length>100||template.Id is null||template.Id.Length>100)Fail("Шаблон здания повреждён.");BuildingDesigner.Validate(template);}
        if(p.Buildings.Concat(p.BuildingLibrary).Where(b=>b?.Design?.Floors is not null).Sum(b=>(long)b.Design!.Floors.Sum(f=>f?.Cells?.Length??0))>20_000_000)Fail("Слишком много клеток в этажах зданий: предел 20 миллионов.");
        foreach(var b in p.Buildings)
        {
            if(b is null || !BuildingDesigner.Kinds.Contains(b.Kind) || string.IsNullOrEmpty(b.Id) || !ids.Add(b.Id) || b.Name is null || b.Name.Length>100||b.SourceId?.Length>100||b.SourceKind?.Length>100||b.SourceLevels?.Length>100||b.SourceBasements?.Length>100||b.SourceRoof?.Length>100||b.LevelsOrigin is not("inferred" or "osm-levels" or "osm-height" or "manual")) Fail("Неизвестное или повреждённое здание.");
            if(b.Width is <6 or >64 || b.Height is <6 or >64 || b.X<1 || b.Y<1 || b.X>p.Width-b.Width-1 || b.Y>p.Height-b.Height-1) Fail("Здание выходит за границу участка карты. Переместите его или расширьте участок.");
            BuildingDesigner.Validate(b);
            foreach(var index in BuildingDesigner.Occupied(b)){var x=b.X+index%(b.Width+1);var y=b.Y+index/(b.Width+1);if(!occupied.Add(y*p.Width+x))Fail("Стены или помещения зданий пересекаются.");}
            for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(BuildingDesigner.Occupies(b,x,y)&&p.Terrain[(b.Y+y)*p.Width+b.X+x]==Terrain.Water)Fail("Здание стоит на воде.");
        }
        foreach(var s in p.Spawns)
        {
            if(s is null || s.X<0 || s.Y<0 || s.X>=p.Width || s.Y>=p.Height) Fail("Точка появления выходит за границу карты.");
            if(p.Terrain[s.Y*p.Width+s.X]==Terrain.Water) Fail("Точка появления находится в воде.");
            if(occupied.Contains(s.Y*p.Width+s.X)) Fail("Поставьте точку появления снаружи здания.");
        }
        var decorationIds=new HashSet<string>();var decorationCells=new HashSet<int>();foreach(var d in p.Decorations){if(d is null||string.IsNullOrEmpty(d.Id)||d.Id.Length>100||!decorationIds.Add(d.Id))Fail("Декор местности повреждён.");VisualStyles.ValidateParts(d.Asset,d.Parts);foreach(var part in d.Parts){var x=d.X+part.Dx;var y=d.Y+part.Dy;if(x<0||y<0||x>=p.Width||y>=p.Height||p.Terrain[y*p.Width+x]==Terrain.Water||occupied.Contains(y*p.Width+x)||p.Spawns.Any(s=>s.X==x&&s.Y==y)||!decorationCells.Add(y*p.Width+x))Fail("Декор выходит за карту, пересекается с объектом, водой или точкой появления.");}}
        var modelIds=new HashSet<string>();foreach(var model in p.StaticModels){StaticModelPlacement.Validate(model,p.Width,p.Height,0,0);if(!modelIds.Add(model.Id)||p.Terrain[model.Y*p.Width+model.X]==Terrain.Water||occupied.Contains(model.Y*p.Width+model.X)||p.Spawns.Any(s=>s.X==model.X&&s.Y==model.Y))Fail("Модель окружения пересекается с водой, зданием или точкой появления.");}
        if(p.Bounds is not null) ValidateBounds(p.Bounds);
        if(p.MetresPerTile is double metres && (!double.IsFinite(metres)||metres<=0||metres>100000))Fail("Масштаб геоданных повреждён.");
        if(p.ImportSummary is { } summary && (summary.SourceFeatures is <0 or >30000||summary.SourceBuildings is <0 or >30000||summary.PlacedBuildings is <0 or >4000||summary.SkipReasons is null||summary.SkipReasons.Count>20||summary.SkipReasons.Any(s=>s.Key.Length>100||s.Value is <0 or >30000)))Fail("Отчёт импорта повреждён.");
        if(p.ImportSummary is { } report&&(report.Notes is null||report.Notes.Count>20||report.Notes.Any(s=>s is null||s.Length>1000)))Fail("Примечания импорта повреждены.");
    }
    public static bool UsesAssets(Building? b)=>b?.Design is {} d&&(d.Style!=""||d.RoofTileset is not null||d.Floors?.Any(f=>f is not null&&(f.FloorTile is not null||f.FloorTiles?.Count>0||f.Edges?.Any(e=>e?.WallTile is not null||e?.OpeningTile is not null)==true||f.Objects?.Any(o=>o is not null&&(o.Kind=="asset"||o.Style!=""||o.Asset is not null||o.Parts?.Count>0))==true))==true);
    public static void ValidateBounds(GeoBounds b)
    {
        if(b is null)throw new ArgumentException("Не задан выбранный участок. Укажите координаты границ.");
        if(!double.IsFinite(b.South)||!double.IsFinite(b.North)||!double.IsFinite(b.West)||!double.IsFinite(b.East)|| b.South< -85 || b.North>85 || b.West< -180 || b.East>180 || b.South>=b.North || b.West>=b.East || b.North-b.South>0.08 || b.East-b.West>0.12) Fail("Выберите участок до 0,08° по широте и 0,12° по долготе, без пересечения линии перемены дат.");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message)=>throw new ArgumentException(message);
}
