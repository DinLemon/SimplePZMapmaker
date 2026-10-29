using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SimplePZMapmaker.Core.Export;

internal enum NativeMetadataKind { Map, World, StreamedWorld }

/// <summary>
/// Collects only derived metadata, never section projects or rasters. Both world exporters
/// feed this writer one projected section at a time, independently of native cell caching.
/// </summary>
internal sealed class NativeExportMetadata(ZipArchive zip, string slug, NativeMetadataKind kind,
    int cellX, int cellY, int width, int height)
{
    readonly string root = $"{slug}/42/media/maps/{slug}/";
    readonly StringBuilder zones = new("objects = {\n");
    readonly XElement atlas = new("world", new XAttribute("version", "1.0"), new XAttribute("cellSize", 256));
    readonly SpawnPlanner.Plan spawns = new();
    readonly MapThumbnail thumbnail = new(cellX * 256, cellY * 256, width, height);
    readonly HashSet<string> attributions = new(StringComparer.Ordinal);
    long atlasBytes, attributionBytes;

    public void AddSection(MapProject project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var text = ObjectsLua(project, token);
        if (kind != NativeMetadataKind.Map && text.Length + zones.Length > 64_000_000)
            throw new ArgumentException(kind == NativeMetadataKind.World
                ? "World zone descriptions exceed 64 MB of text; split the world into multiple mods."
                : "World zone descriptions exceed 64 MB. Export selected sections.");
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        // World archives historically keep the opening newline of every section.
        var start = first + (kind == NativeMetadataKind.Map ? 2 : 1);
        zones.Append(text.AsSpan(start, last - start));

        var sectionAtlas = NativeMapExporter.WorldMap(project);
        if (kind != NativeMetadataKind.Map)
        {
            atlasBytes += Encoding.UTF8.GetByteCount(sectionAtlas.ToString());
            if (atlasBytes > 64_000_000)
                throw new ArgumentException(kind == NativeMetadataKind.World
                    ? "In-game atlas outlines exceed 64 MB; split the world into multiple mods."
                    : "World atlas descriptions exceed 64 MB. Export selected sections.");
        }
        foreach (var cell in sectionAtlas.Elements("cell")) atlas.Add(new XElement(cell));
        spawns.Add(project.Buildings, project.WorldX * 256, project.WorldY * 256);
        spawns.AddAuthored(project);
        thumbnail.Add(project);
        foreach (var attribution in project.Attribution.Concat(project.Buildings.Concat(project.BuildingLibrary).SelectMany(b => b.Attribution)))
            if (attributions.Add(attribution) && kind == NativeMetadataKind.StreamedWorld)
            {
                attributionBytes += Encoding.UTF8.GetByteCount(attribution);
                if (attributionBytes > 16_000_000)
                    throw new ArgumentException("World attribution descriptions exceed 16 MB. Export selected sections.");
            }
    }

    string ObjectsLua(MapProject project, CancellationToken token)
    {
        if (kind != NativeMetadataKind.Map || project.GameZones.Count > 0)
            return GameZonesExporter.ObjectsLua(project, token);

        // Preserve the original standalone-map Lua whitespace and uncapped terrain fallback.
        var text = new StringBuilder("objects = {\n");
        for (var y = 0; y < project.Height / 8; y++)
        for (var x = 0; x < project.Width / 8; x++)
        {
            if (x == 0) token.ThrowIfCancellationRequested();
            var terrain = project.Terrain[y * 8 * project.Width + x * 8];
            if (terrain == Terrain.Water) continue;
            var type = terrain switch { Terrain.Forest => "Forest", Terrain.Grass => "Vegitation", _ => "TownZone" };
            text.Append(FormattableString.Invariant($" {{ name = \"\", type = \"{type}\", x = {project.WorldX * 256 + x * 8}, y = {project.WorldY * 256 + y * 8}, z = 0, width = 8, height = 8 }},\n"));
        }
        return text.Append("}\n").ToString();
    }

    public void WriteZones()
    {
        zones.Append("}\n");
        Text(root + "objects.lua", zones.ToString());
    }

    public void WriteAtlas()
    {
        var document = kind == NativeMetadataKind.Map
            ? new XDocument(new XDeclaration("1.0", "UTF-8", null), atlas)
            : new XDocument(atlas);
        Text(root + "worldmap.xml", document.ToString());
        Bytes(root + "worldmap.xml.bin", NativeWorldMapBinary.Write(atlas));
    }

    public void WriteSpawns(bool requireSpawn = false)
    {
        if (requireSpawn && spawns.Count == 0) throw new ArgumentException("The world needs at least one dry-land spawn.");
        Text(root + "spawnpoints.lua", spawns.Lua());
        Bytes(root + "thumb.png", thumbnail.Png());
        Text(root + "spawnregions.lua", $"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n");
    }

    public void WriteMapInfo(string name, string description, int zoom) => Text(root + "map.info",
        $"title={name}\nfixed2x=true\ndescription={description}\nzoomX={cellX * 256 + width / 2}\nzoomY={cellY * 256 + height / 2}\nzoomS={zoom}\n");

    public void WriteModInfo(string name, string description, IEnumerable<string> dependencies)
    {
        var required = string.Join(',', dependencies);
        var require = required.Length > 0 ? "require=" + required + "\n" : "";
        var details = kind == NativeMetadataKind.Map
            ? $"description={description}\nversionMin=42.0\n"
            : $"versionMin=42.0\ndescription={description}\n";
        Text(slug + "/42/mod.info", $"name={name}\nid={slug}\n{details}{require}");
    }

    public void WriteElevation(ElevationSurvey? survey)
    {
        if (survey is null) return;
        Text(slug + "/elevation.asc", survey.ToAscii());
        attributions.Add("Elevation: " + survey.Source);
        attributions.Add(survey.License);
    }

    public void WriteAttribution(string header) => Text(slug + "/ATTRIBUTION.txt", string.Join('\n', attributions.Prepend(header)) + "\n");

    public void Text(string path, string value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    public void Bytes(string path, byte[] value)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        stream.Write(value);
    }
}
