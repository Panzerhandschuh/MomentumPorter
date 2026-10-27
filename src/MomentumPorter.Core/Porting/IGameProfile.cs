namespace MomentumPorter.Porting;

using MomentumPorter.Games.CounterStrikeSource;
using MomentumPorter.Games.Gokz;
using MomentumPorter.Games.KreedzClimbing;

/// <summary>
/// Converts maps made for another game into Momentum Mod maps.
/// </summary>
public interface IGameProfile
{
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// The map's assets have to be packed into it from the game (bspPack). Only Kreedz Climbing's are: Momentum
    /// mounts the other games, so their maps keep whatever their authors packed.
    /// </summary>
    bool PacksAssets => false;

    /// <summary>
    /// The game Momentum mounts that the map's assets come from, which the submission form asks for under required
    /// games, or null when there's none.
    /// </summary>
    string? MountedGame => null;

    /// <summary>
    /// In the game the map came from, a trigger_gravity's change stayed with the player after leaving the trigger, as
    /// in every Source game before CS:GO (and GoldSrc). Momentum reverts it unless the trigger has <c>persist 1</c>,
    /// so ports from those games get it (Lumper's entity rules and the review checklist say the same).
    /// </summary>
    bool GravityPersists => true;

    /// <summary>
    /// The profile writes a zone file from the map's timer entities, unless <see cref="PortContext.ConvertZones"/> is off.
    /// Only GOKZ's so far; the other games' maps are zoned in game.
    /// </summary>
    bool WritesZones => false;

    void Apply(PortContext context);
}

public static class GameProfiles
{
    public static readonly IReadOnlyList<IGameProfile> All = [new KreedzClimbingProfile(), new GokzProfile(), new CounterStrikeSourceProfile()];

    public static IGameProfile? Find(string name)
    {
        return All.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
