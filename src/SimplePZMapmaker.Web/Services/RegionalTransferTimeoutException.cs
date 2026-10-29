namespace SimplePZMapmaker.Web.Services;

internal sealed class RegionalTransferTimeoutException(string region,long received,long? total,bool stalled,Exception inner)
 :HttpRequestException($"The {region} regional download {(stalled?"stalled without new bytes":"reached its maximum transfer time")}. Received {received:N0}"+(total is {} size?$" / {size:N0}":"")+" bytes. Partial source bytes are preserved; resume the regional download. The current world was preserved.",inner);
