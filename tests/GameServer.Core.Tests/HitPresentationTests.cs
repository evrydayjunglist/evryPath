using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Core.Tests;

public class HitPresentationTests
{
    [Fact]
    public void A_weapon_hit_carries_blood_particles_and_the_main_hand()
    {
        Assert.Equal(0x4201, HitPresentation.Flags());
    }

    [Fact]
    public void A_spell_hit_sets_its_spell_bit_instead_of_the_weapon()
    {
        Assert.Equal(0x4081, HitPresentation.Flags(spell: true));
    }

    [Fact]
    public void Critical_block_and_stun_are_their_own_bits()
    {
        Assert.Equal(0x4221, HitPresentation.Flags(critical: true));
        Assert.Equal(0x4241, HitPresentation.Flags(blocked: true));
        Assert.Equal(0x4601, HitPresentation.Flags(stunned: true));
        Assert.Equal(0x46e1, HitPresentation.Flags(critical: true, blocked: true, stunned: true, spell: true) | HitPresentation.MainHand);
    }

    [Fact]
    public void The_code_is_the_client_damage_type_order_not_the_servers()
    {
        Assert.Equal(0, HitPresentation.Code(DamageType.Physical));
        Assert.Equal(1, HitPresentation.Code(DamageType.Cold));
        Assert.Equal(2, HitPresentation.Code(DamageType.Fire));
        Assert.Equal(3, HitPresentation.Code(DamageType.Lightning));
        Assert.Equal(4, HitPresentation.Code(DamageType.Chaos));
        Assert.Equal(0x12, HitPresentation.Code(DamageType.Fire, variation: 2));
    }

    [Fact]
    public void The_dominant_type_is_the_one_with_the_most_average_damage()
    {
        Assert.Equal(DamageType.Physical, HitPresentation.DominantType(DamagePacket.Empty));
        var hit = DamagePacket.Of(DamageType.Physical, 10, 20).Add(DamageType.Fire, new DamageRange(30, 40));
        Assert.Equal(DamageType.Fire, HitPresentation.DominantType(hit));
        var chaos = DamagePacket.Of(DamageType.Lightning, 1, 60).Add(DamageType.Chaos, new DamageRange(32, 32));
        Assert.Equal(DamageType.Chaos, HitPresentation.DominantType(chaos));
    }
}
