using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public class HitReactionTests
{
    static SpawnedMonster Monster(uint maxLife = 100, int clips = 1) =>
        new("Rhoa@1", new WalkEstimator(0, 0, 30, null, 2000, 2000), aggroRange: 60, stopRange: 3, maxLife: maxLife)
        {
            HitAnimations = [.. Enumerable.Range(0, clips).Select(i => new HitAnimation(7 + i, 0.5))],
        };

    static double? Stun(SpawnedMonster m, int damage, double now, int seed = 1, int durationPct = 0) =>
        HitReaction.TryStun(m, damage, now, new Random(seed), durationPct);

    [Fact]
    public void The_stun_chance_is_200_times_the_damage_over_max_life_above_a_fifth()
    {
        Assert.Equal(0, HitReaction.StunChance(10, 100));    // exactly 20%: nothing
        Assert.Equal(22, HitReaction.StunChance(11, 100));
        Assert.Equal(100, HitReaction.StunChance(50, 100));
        Assert.Equal(100, HitReaction.StunChance(90, 100));
        Assert.Equal(0, HitReaction.StunChance(0, 100));
        Assert.Equal(0, HitReaction.StunChance(5, 0));
    }

    [Fact]
    public void The_stun_lasts_350_ms_grown_by_the_attackers_stun_duration()
    {
        Assert.Equal(0.35, HitReaction.StunSeconds(0));
        Assert.Equal(0.7, HitReaction.StunSeconds(100));
        Assert.Equal(0.42, HitReaction.StunSeconds(20));
        Assert.Equal(0, HitReaction.StunSeconds(-100));
    }

    [Fact]
    public void A_hit_of_half_the_max_life_always_stuns()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var m = Monster();
            Assert.Equal(0.35, Stun(m, 50, now: 10, seed));
            Assert.Equal(10, m.StunnedAt);
            Assert.Equal(10.35, m.StunnedUntil);
        }
    }

    [Fact]
    public void A_hit_of_a_tenth_of_the_max_life_never_stuns()
    {
        for (int seed = 0; seed < 50; seed++) Assert.Null(Stun(Monster(1000), 100, now: 10, seed));
    }

    [Fact]
    public void A_hit_between_rolls_its_chance()
    {
        int stuns = 0;
        for (int seed = 0; seed < 200; seed++) if (Stun(Monster(), 25, now: 10, seed) is not null) stuns++;   // 50%
        Assert.InRange(stuns, 70, 130);
    }

    [Fact]
    public void A_stunned_monster_is_not_stunned_again_until_it_recovers()
    {
        var m = Monster();
        Assert.NotNull(Stun(m, 50, now: 10));
        Assert.Null(Stun(m, 50, now: 10.3));
        Assert.NotNull(Stun(m, 50, now: 10.35));
        Assert.Equal(10.35, m.StunnedAt);
    }

    [Fact]
    public void The_attackers_stun_duration_lengthens_the_stun()
    {
        var m = Monster();
        Assert.Equal(0.7, Stun(m, 50, now: 10, durationPct: 100));
        Assert.Equal(10.7, m.StunnedUntil);
    }

    [Fact]
    public void A_dead_monster_or_one_without_a_TakeHit_clip_is_not_stunned()
    {
        var dead = Monster();
        dead.Life = 0;
        Assert.Null(Stun(dead, 50, now: 0));
        var marked = Monster();
        marked.Dead = true;
        Assert.Null(Stun(marked, 50, now: 0));
        Assert.Null(Stun(Monster(clips: 0), 50, now: 0));
    }

    [Fact]
    public void A_stun_stops_the_monsters_walk_and_holds_its_AI_for_the_duration()
    {
        var m = Monster();
        m.Walk.Order(300, 0, now: 0);
        m.LastTarget = (300, 0);
        m.BusyUntil = 0;
        var before = m.Walk.Position(2);
        Assert.NotEqual((0, 0), before);
        Assert.NotNull(Stun(m, 50, now: 2));
        Assert.Equal(before, m.Walk.Position(5));   // stopped where the stun landed
        Assert.Equal(2.35, m.BusyUntil);
        Assert.Null(m.LastTarget);
        // A longer hold (an emerge, a cast) is kept.
        var busy = Monster();
        busy.BusyUntil = 30;
        Assert.NotNull(Stun(busy, 50, now: 2));
        Assert.Equal(30, busy.BusyUntil);
    }

    [Fact]
    public void A_stun_since_the_swing_started_cancels_its_contact()
    {
        var m = Monster();
        Assert.False(HitReaction.InterruptsSwing(m, swingStartedAt: 10, now: 10.6));
        Assert.NotNull(Stun(m, 50, now: 10.2));
        Assert.True(HitReaction.InterruptsSwing(m, swingStartedAt: 10, now: 10.6));   // even after it recovered at 10.55
        Assert.False(HitReaction.InterruptsSwing(m, swingStartedAt: 10.3, now: 10.6));   // a swing after the stun lands
        // A swing started while a stun still holds is cancelled too.
        var held = Monster();
        Assert.NotNull(Stun(held, 50, now: 9.9));
        Assert.True(HitReaction.InterruptsSwing(held, swingStartedAt: 10, now: 10.2));
        Assert.False(HitReaction.InterruptsSwing(held, swingStartedAt: 10, now: 10.4));
    }
}
