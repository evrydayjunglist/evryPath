using DamageType = GameServer.Core.World.DamageType;
using HitPresentation = GameServer.Core.Protocol.Instance.HitPresentation;

/// <summary>
/// One of a variety's TakeHit clips: its index in the rig's animation list and its length in seconds
/// (data/monster-takehit.json "indexes" and "lengths"). The client picks the clip itself for a 0x1af stun; the server
/// only needs to know the variety has one.
/// </summary>
readonly record struct HitAnimation(int Index, double Seconds);

/// <summary>
/// What a hit on a monster presents in S→C 0x1af besides its angle: the flags and the type code
/// (<see cref="HitPresentation"/>) and the stun it carries, if the hit stunned (<see cref="HitReaction.TryStun"/>).
/// </summary>
readonly record struct MonsterHitEffects(bool Spell, bool Critical, DamageType Type, double? StunSeconds)
{
    public ushort Flags => HitPresentation.Flags(Spell, Critical, stunned: StunSeconds is not null);
    public byte Code => HitPresentation.Code(Type);
    public ushort StunMs => (ushort)Math.Clamp(Math.Round((StunSeconds ?? 0) * 1000), 0, ushort.MaxValue);

    public override string ToString() =>
        $"{(Spell ? "spell" : "attack")}{(Critical ? ", critical" : "")}, {Type}{(StunSeconds is { } s ? $", stun {s:0.###} s" : "")}";
}

/// <summary>
/// A monster's stun from a hit: PoE's rule, a chance of 200 × damage / maximum life (percent) that counts only above
/// 20%, so a hit of half its life always stuns and a scratch never does, for 350 ms grown by the attacker's stun
/// duration. The stun goes out in the hit's 0x1af (flags 0x400 with the duration): the client interrupts the monster's
/// current and pending actions and plays its TakeHit for that long — or rejects it by its own immunity checks, which the
/// server mirrors (cannot be stunned, already stunned). On the server the stun stops the monster's walk, holds its AI
/// (<c>BusyUntil</c>) and cancels a swing that started before it (<see cref="InterruptsSwing"/>), so the monster's
/// attack does not land while the client shows it reeling. Monster stun recovery modifiers are not modelled.
/// </summary>
static class HitReaction
{
    public const double BaseStunSeconds = 0.35;
    public const double ChanceCutoff = 20;

    /// <summary>The stun chance in percent: 200 × damage / maximum life, capped at 100, nothing at or below the cutoff.</summary>
    public static double StunChance(int damage, uint maxLife)
    {
        if (damage <= 0 || maxLife == 0) return 0;
        double chance = Math.Min(100, 200.0 * damage / maxLife);
        return chance <= ChanceCutoff ? 0 : chance;
    }

    /// <summary>The stun's length: the base grown by the attacker's <c>stun_duration_+%</c>, to the millisecond.</summary>
    public static double StunSeconds(int durationPct)
    {
        double seconds = Math.Round(BaseStunSeconds * (1 + durationPct / 100.0), 3);
        return seconds > 0 ? Math.Min(seconds, 60) : 0;
    }

    /// <summary>
    /// Rolls this hit's stun and, when it stuns, claims the monster: the stun's length in seconds, or null when the hit
    /// does not stun it. Mutates the monster (its timers and walk), so call it under the Monsters lock, with the clock
    /// its AI uses (<c>ProjectileWorldTime</c>).
    /// </summary>
    public static double? TryStun(SpawnedMonster m, int damage, double now, Random random, int durationPct = 0)
    {
        if (m.Life == 0 || m.Dead || m.HitAnimations.Count == 0) return null;   // no TakeHit clip: the client has nothing to play
        if (m.Mods.Effects.CannotBeStunned) return null;   // Unwavering
        if (now < m.StunnedUntil) return null;   // the client rejects a stun on a stunned actor
        double chance = StunChance(damage, m.MaxLife);
        if (chance <= 0 || random.NextDouble() * 100 >= chance) return null;
        double seconds = StunSeconds(durationPct);
        if (seconds <= 0) return null;
        m.StunnedAt = now;
        m.StunnedUntil = now + seconds;
        m.BusyUntil = Math.Max(m.BusyUntil, m.StunnedUntil);
        // The client ends the monster's walk with the stun: stop the server's estimate where it is, and let the AI order
        // a fresh chase afterwards.
        var (x, y) = m.Walk.Position(now);
        m.Walk.Order(x, y, now);
        m.LastTarget = null;
        return seconds;
    }

    /// <summary>
    /// Whether a swing that started at <paramref name="swingStartedAt"/> is cancelled at its contact time
    /// <paramref name="now"/>: a stun landed since it started, or the monster is still stunned.
    /// </summary>
    public static bool InterruptsSwing(SpawnedMonster m, double swingStartedAt, double now) =>
        m.StunnedAt >= swingStartedAt || now < m.StunnedUntil;
}
