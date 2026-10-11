using SimplePZMapmaker.Web.Services;
using System.Net;
public static class UpstreamTests
{
    public static IEnumerable<(string,Action)> Cases(){foreach(var lengthKnown in new[]{false,true})yield return ($"OSM download bounded before buffering (Content-Length={lengthKnown})",()=>{
        var stream=new ProbeStream();var directory=Path.Combine(Directory.GetCurrentDirectory(),"work","upstream-tests",Guid.NewGuid().ToString("N"));
        using var client=new OsmClient(directory,new FakeHandler(stream,lengthKnown));
        try{client.Fetch(new(){West=30,East=30.001,South=50,North=50.001},CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Oversized response accepted");}
        catch(ArgumentException){if(stream.ReadBytes>16_008_192)throw new Exception($"Buffered {stream.ReadBytes} bytes before limit");}
    });}
    private sealed class FakeHandler(ProbeStream stream,bool lengthKnown):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){var content=new StreamContent(stream);if(lengthKnown)content.Headers.ContentLength=20_000_000;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});}
    }
    private sealed class ProbeStream:Stream {
        public long ReadBytes {get;private set;}
        public override int Read(byte[] buffer,int offset,int count){var n=(int)Math.Min(count,20_000_000-ReadBytes);Array.Fill(buffer,(byte)' ',offset,n);ReadBytes+=n;return n;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>20_000_000;
        public override long Position {get=>ReadBytes;set=>throw new NotSupportedException();}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
