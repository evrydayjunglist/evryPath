using GameServer.Core.World;

namespace GameServer.Core.Protocol.Instance;

/// <summary>
/// The flags and the type code of S→C 0x1af "action hit" (<see cref="InstanceProtocol.ActionHit"/>), as the client's
/// damage-package consumers read them (static decode of 3.29.3b, research/poe-rea-reference:
/// Agents/docs/re/action-hit-packet-3.29.3b.md). The flags select the target's presentation of the hit — blood,
/// critical and block layers, the stun or block-recovery interruption with its duration, knockback — and the code
/// selects the damage particle type. The low bits 0x0002..0x0010, 0x1000, 0x2000 and 0x8000 are not named; an
/// evaded or no-damage encoding is not established, so a hit that does not land sends nothing.
/// </summary>
public static class HitPresentation
{
    /// <summary>Blood (or energy-shield hit) presentation; also skips an extra elemental impact sound.</summary>
    public const ushort Blood = 0x0001;
    /// <summary>Critical blood and sound layers; DamageHitEffects reaction 1 (over stun).</summary>
    public const ushort Critical = 0x0020;
    /// <summary>The target's block callback and presentation; with <see cref="Stun"/> a Block (row 46) recovery.</summary>
    public const ushort Blocked = 0x0040;
    /// <summary>A spell's hit (the spell-critical weapon-effect branch).</summary>
    public const ushort Spell = 0x0080;
    /// <summary>Off-hand weapon effect and stat selection.</summary>
    public const ushort Offhand = 0x0100;
    /// <summary>Main-hand weapon effect and stat selection.</summary>
    public const ushort MainHand = 0x0200;
    /// <summary>
    /// A stun: the packet carries a u16 duration in milliseconds, and the client interrupts the target's current and
    /// pending actions and plays its directional TakeHit for that long (Actor 0x141e96500 → 0x141e95e60, the routine
    /// 0x1d1 uses), unless its own immunity and avoidance checks reject it.
    /// </summary>
    public const ushort Stun = 0x0400;
    /// <summary>Knockback: the client moves the target along the hit angle (Actor::OnKnockback 0x141e96790).</summary>
    public const ushort Knockback = 0x0800;
    /// <summary>The generic damage particles (DamageParticleEffects by the code's type and variation).</summary>
    public const ushort Particles = 0x4000;

    /// <summary>
    /// The flags of a hit that landed. Every hit carries blood and particles; a weapon hit (an attack, by the player or a
    /// monster) is a main-hand hit, a spell's hit sets its spell bit instead.
    /// </summary>
    public static ushort Flags(bool spell = false, bool critical = false, bool blocked = false, bool stunned = false)
    {
        ushort flags = Blood | Particles;
        flags |= spell ? Spell : MainHand;
        if (critical) flags |= Critical;
        if (blocked) flags |= Blocked;
        if (stunned) flags |= Stun;
        return flags;
    }

    /// <summary>
    /// The hit's code byte: the low three bits are the damage particle type (image table 0x1435d2110: Physical 0, Cold 1,
    /// Fire 2, Lightning 3, Chaos 4; not the server's <see cref="DamageType"/> order), the upper bits a variation.
    /// </summary>
    public static byte Code(DamageType type, int variation = 0) => (byte)((variation & 0x1f) << 3 | type switch
    {
        DamageType.Cold => 1,
        DamageType.Fire => 2,
        DamageType.Lightning => 3,
        DamageType.Chaos => 4,
        _ => 0,
    });

    /// <summary>
    /// The type a hit presents: the one with the most average damage in the packet, physical when the packet is empty.
    /// The packet carries a single presentation type, not its components.
    /// </summary>
    public static DamageType DominantType(DamagePacket packet)
    {
        var best = DamageType.Physical;
        double most = 0;
        foreach (var (type, range) in packet.Components)
            if (range.Average > most) (best, most) = (type, range.Average);
        return best;
    }
}
