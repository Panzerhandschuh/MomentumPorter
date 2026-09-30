namespace MomentumPorter.Porting;

using MomentumPorter.Games.KreedzClimbing;

/// <summary>
/// Converts maps made for another game into Momentum Mod maps.
/// </summary>
internal interface IGameProfile
{
    string Name { get; }

    string Description { get; }

    void Apply(PortContext context);
}

internal static class GameProfiles
{
    public static readonly IReadOnlyList<IGameProfile> All = [new KreedzClimbingProfile()];

    public static IGameProfile? Find(string name)
    {
        return All.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
