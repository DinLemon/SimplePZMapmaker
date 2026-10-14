namespace SimplePZMapmaker.Core;

/// <summary>Build 42 signed world levels: the upper bound is inclusive.</summary>
public static class ZLevels
{
 public const int Min=-32;
 public const int Max=31;
 public const int AboveGroundCount=32;
 public const int BasementCount=32;
 public const int Total=64;
 public const int NativeBias=32;
 public static bool Contains(int level)=>level>=Min&&level<=Max;
}
