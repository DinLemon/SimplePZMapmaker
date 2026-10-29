namespace SimplePZMapmaker.Web.Services;

// A completed regional transfer failed locally. Switching to online providers would
// hide the actual problem and may needlessly download the same geographic data again.
internal sealed class RegionalProcessingException(string message,Exception inner,bool retained=false):ArgumentException(message,inner)
{
 public bool Retained {get;}=retained;
 internal static string Detail(Exception error)=>new(error.Message.Where(c=>!char.IsControl(c)).Take(384).ToArray());
}
