using System.Numerics;
using HitSpark.Models;

namespace HitSpark.Capture;

/// <summary>Damage school reported by the game, used for palette tinting.</summary>
public enum DamageKind : byte
{
    None = 0,
    Physical = 1,
    Magical = 2,
    Unique = 3,
}

/// <summary>
/// Everything the renderer needs to put one piece of text on screen. Produced by the game
/// hooks and by the test source; plain data so it can sit in a ring buffer without boxing.
/// </summary>
public struct SpawnRequest
{
    public Category Category;
    public DamageKind DamageKind;

    /// <summary>Who the event involves; drives the "other players" visibility and size.</summary>
    public Involvement Involvement;
    /// <summary>Size multiplier on top of the style (1 for your own numbers).</summary>
    public float SizeScale;

    /// <summary>When false the primary line is <see cref="Word"/> instead of a number.</summary>
    public bool HasNumber;
    public long Value;
    /// <summary>Primary text for word-only events ("Miss", "Evade"...).</summary>
    public string? Word;
    /// <summary>Unit appended after the number (" MP", " EXP").</summary>
    public string? Unit;
    /// <summary>Secondary line: action, status or item name.</summary>
    public string? Name;
    public uint IconId;

    /// <summary>The actor the text is about; resolved to a screen position every frame.</summary>
    public uint ActorEntityId;
    public ushort ActorIndex;

    /// <summary>Merge key for aggregation (category, target, action).</summary>
    public ulong AggregateActionKey;
    public ulong AggregateTargetKey;

    /// <summary>Preview-canvas spawns carry fixed screen anchors instead of actors.</summary>
    public bool UseScreenAnchor;
    public Vector2 ScreenActor;
    public Vector2 ScreenSelf;

    public const ushort NoActor = 0xFFFF;
}
