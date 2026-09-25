using System.Numerics;
using System.Runtime.CompilerServices;
using HitSpark.Capture;

namespace HitSpark.Rendering;

/// <summary>Inline text storage so an instance never owns a heap string for its number.</summary>
[InlineArray(64)]
public struct TextBuffer
{
    private char element;
}

/// <summary>Recent screen positions for the ghost trail.</summary>
[InlineArray(FlyInstance.MaxGhosts)]
public struct GhostTrail
{
    private Vector2 element;
}

/// <summary>
/// One live piece of combat text. Lives in a fixed pooled array and is reused, so the
/// renderer allocates nothing after startup.
/// </summary>
public struct FlyInstance
{
    public const int MaxGhosts = 6;

    public bool Active;
    public CompiledStyle Style;

    // Timing (seconds on the renderer clock)
    public double SpawnTime;
    public double LastHit;
    /// <summary>Aggregated instances hold their motion until this time, then play out.</summary>
    public double ReleaseTime;
    public double PunchStart;
    public float HoldAge;

    // Per-instance randomness, fixed at spawn so motion is stable frame to frame
    public float Dir;
    public Vector2 Jitter;
    public float Rand;
    public float Tilt;
    public int Slot;
    public int AnchorKey;

    // Anchor
    public uint ActorEntityId;
    public ushort ActorIndex;
    public bool UseScreenAnchor;
    public Vector2 ScreenActor;
    public Vector2 ScreenSelf;
    public Vector3 LastWorld;
    public bool HasWorld;

    // Content
    public bool HasNumber;
    public long Value;
    public double Displayed;
    public long Shown;
    public TextBuffer Text;
    public int TextLength;
    public string? Word;
    public string? Unit;
    public string? Name;
    public uint IconId;
    /// <summary>The word or name needs glyphs the style's Latin-only font doesn't have.</summary>
    public bool WordFallback;
    public bool NameFallback;
    public DamageKind DamageKind;
    /// <summary>Size multiplier for other players' numbers; 1 for your own.</summary>
    public float SizeScale;

    // Aggregation
    public ulong AggregateKey;
    public int Hits;

    // Effects
    public GhostTrail Ghosts;
    public int GhostHead;
    public int GhostFilled;
    public double GhostLastSample;
    public bool SparksPending;
    /// <summary>Anchor resolution has been written to the log (event recorder only).</summary>
    public bool Traced;
}

/// <summary>A spark from a crit burst. Pooled like the text instances.</summary>
public struct Particle
{
    public bool Active;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Age;
    public float Life;
    public float Size;
    public uint Color;
}
