namespace SimplePZMapmaker.Web.Services;

public sealed record OsmImportTimeouts
{
 public TimeSpan TransferMaximum {get;init;}=TimeSpan.FromHours(4);
 public TimeSpan TransferIdle {get;init;}=TimeSpan.FromMinutes(5);
 public TimeSpan ProcessingMaximum {get;init;}=TimeSpan.FromMinutes(60);
 public TimeSpan SourceMaximum=>TransferMaximum+ProcessingMaximum+TimeSpan.FromMinutes(5);
 internal void Validate(){if(new[]{TransferMaximum,TransferIdle,ProcessingMaximum}.Any(t=>t<=TimeSpan.Zero||t>TimeSpan.FromDays(1)))throw new ArgumentException("Invalid geographic import time budget.");}
}
