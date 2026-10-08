namespace MomentumPorter.Porting;

using MomentumPorter.Games.Gokz;
using MomentumPorter.Games.KreedzClimbing;

/// <summary>
/// Converts maps made for another game into Momentum Mod maps.
/// </summary>
public interface IGameProfile
{
    string Name { get; }

    string Description { get; }

    void Apply(PortContext context);
}

public static class GameProfiles
{
    public static readonly IReadOnlyList<IGameProfile> All = [new KreedzClimbingProfile(), new GokzProfile()];

    public static IGameProfile? Find(string name)
    {
        return All.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
