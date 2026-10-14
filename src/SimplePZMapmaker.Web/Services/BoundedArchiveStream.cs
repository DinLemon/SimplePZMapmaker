namespace SimplePZMapmaker.Web.Services;
/// <summary>Bounds the final seekable archive during writes, not after filling the disk.</summary>
public sealed class BoundedArchiveStream(Stream inner,long maximum):Stream
{
 void Check(long end){if(end<0||end>maximum)throw new ArgumentException("The native archive exceeds 256 MB. Export selected sections separately.");}
 public override bool CanRead=>inner.CanRead;public override bool CanWrite=>inner.CanWrite;public override bool CanSeek=>inner.CanSeek;
 public override long Length=>inner.Length;public override long Position{get=>inner.Position;set{Check(value);inner.Position=value;}}
 public override void Write(byte[] buffer,int offset,int count){Check(checked(Position+count));inner.Write(buffer,offset,count);}
 public override void Write(ReadOnlySpan<byte> buffer){Check(checked(Position+buffer.Length));inner.Write(buffer);}
 public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){Check(checked(Position+buffer.Length));return inner.WriteAsync(buffer,token);}
 public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
 public override long Seek(long offset,SeekOrigin origin){var next=origin switch{SeekOrigin.Begin=>offset,SeekOrigin.Current=>checked(Position+offset),SeekOrigin.End=>checked(Length+offset),_=>throw new ArgumentException("Invalid archive seek.")};Check(next);return inner.Seek(offset,origin);}
 public override void SetLength(long value){Check(value);inner.SetLength(value);}
 public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken token)=>inner.FlushAsync(token);
}
