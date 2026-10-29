using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SimplePZMapmaker.Core;

public static partial class GeographicImporter
{
 /// <summary>Legacy GeoJSON ID mapping. Even an ID beginning with geo- is a raw ID here.</summary>
 public static string CanonicalGeoJsonSourceId(string rawId)
 {
  ArgumentNullException.ThrowIfNull(rawId);
  return "geo-"+(rawId.Length>75?Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawId))):rawId);
 }

 /// <summary>An explicit canonical identity is used verbatim, never prefixed, trimmed or hashed.</summary>
 internal static string? ExplicitSourceIdentity(IReadOnlyDictionary<string,string> tags)
 {
  if(!tags.TryGetValue("pz:source_id",out var identity))return null;
  if(string.IsNullOrWhiteSpace(identity)||identity.Length>100||identity.Any(char.IsControl))
   throw new ArgumentException("pz:source_id must be a nonempty canonical source identity of at most 100 characters without controls.");
  return identity;
 }

 internal static string? ExplicitGeoJsonSourceIdentity(JsonObject? properties)
 {
  if(properties is null||!properties.TryGetPropertyValue("pz:source_id",out var node))return null;
  if(node is not JsonValue value||!value.TryGetValue<string>(out var identity))
   throw new ArgumentException("pz:source_id must be a canonical source identity string.");
  return ExplicitSourceIdentity(new Dictionary<string,string>{{"pz:source_id",identity}});
 }
}
