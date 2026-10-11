namespace SimplePZMapmaker.Core;
public static class ProjectValidator
{
    public static void Validate(MapProject p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if(p.Version!=1) Fail("Версия проекта не поддерживается.");
        if(p.Width is <256 or >1024 || p.Height is <256 or >1024 || p.Width%256!=0 || p.Height%256!=0) Fail("Размер карты: от 256 до 1024, кратный 256.");
        if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>80 || p.Name.Any(char.IsControl)) Fail("Название: от 1 до 80 символов без переносов строк.");
        if(p.WorldX is <0 or >1000 || p.WorldY is <0 or >1000) Fail("Координаты мира: от 0 до 1000 ячеек.");
        if(p.Terrain is null || p.Terrain.Length!=p.Width*p.Height || p.Terrain.Any(t=>!Enum.IsDefined(t))) Fail("Слой земли повреждён.");
        if(p.Zombies is null || p.Zombies.Length!=p.Width*p.Height/64 || p.Zombies.Any(z=>z is <0 or >255)) Fail("Слой зомби повреждён.");
        if(p.Buildings is null || p.Buildings.Count>4000 || p.Spawns is null || p.Spawns.Count is <1 or >100 || p.Attribution is null || p.Attribution.Count>30 || p.Attribution.Any(a=>a is null || a.Length>1000)) Fail("Объекты проекта повреждены.");
        var occupied=new HashSet<int>(); var ids=new HashSet<string>();
        foreach(var b in p.Buildings)
        {
            if(b is null || b.Kind is not ("house" or "shop" or "warehouse") || string.IsNullOrEmpty(b.Id) || !ids.Add(b.Id) || b.Name is null || b.Name.Length>100) Fail("Неизвестное или повреждённое здание.");
            if(b.Width is <6 or >64 || b.Height is <6 or >64 || b.X<1 || b.Y<1 || b.X>p.Width-b.Width-1 || b.Y>p.Height-b.Height-1 || b.X/256!=(b.X+b.Width)/256 || b.Y/256!=(b.Y+b.Height)/256) Fail("Здание выходит за границу карты или пересекает границу ячейки 256×256. Переместите его.");
            for(var y=b.Y;y<=b.Y+b.Height;y++) for(var x=b.X;x<=b.X+b.Width;x++)
            {var i=y*p.Width+x; if(!occupied.Add(i)) Fail("Стены или помещения зданий пересекаются."); if(x<b.X+b.Width&&y<b.Y+b.Height&&p.Terrain[i]==Terrain.Water) Fail("Здание стоит на воде.");}
        }
        foreach(var s in p.Spawns)
        {
            if(s is null || s.X<0 || s.Y<0 || s.X>=p.Width || s.Y>=p.Height) Fail("Точка появления выходит за границу карты.");
            if(p.Terrain[s.Y*p.Width+s.X]==Terrain.Water) Fail("Точка появления находится в воде.");
            if(p.Buildings.Any(b=>s.X>=b.X && s.X<=b.X+b.Width && s.Y>=b.Y && s.Y<=b.Y+b.Height)) Fail("Поставьте точку появления снаружи здания.");
        }
        if(p.Bounds is not null) ValidateBounds(p.Bounds);
    }
    public static void ValidateBounds(GeoBounds b)
    {
        if(b is null)throw new ArgumentException("Не задан выбранный участок. Укажите координаты границ.");
        if(!double.IsFinite(b.South)||!double.IsFinite(b.North)||!double.IsFinite(b.West)||!double.IsFinite(b.East)|| b.South< -85 || b.North>85 || b.West< -180 || b.East>180 || b.South>=b.North || b.West>=b.East || b.North-b.South>0.08 || b.East-b.West>0.12) Fail("Выберите участок до 0,08° по широте и 0,12° по долготе, без пересечения линии перемены дат.");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message)=>throw new ArgumentException(message);
}
