namespace MomentumPorter.Games.KreedzClimbing;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// Kreedz Climbing Mod (Source 2013).
/// </summary>
public sealed partial class KreedzClimbingProfile : IGameProfile
{
    // Spawnflags shared by KZ's teleport entities
    private const int KzKeepAngles = 32;
    private const int KzKeepVelocity = 64;
    private const int KzOnlyAllowedPlayers = 512;
    private const int KzAllPlayersByDefault = 4096;

    public string Name => "kzmod";

    public string Description => "Kreedz Climbing Mod";

    public void Apply(PortContext context)
    {
        var allowList = new AllowList();

        ConvertBhopTeleports(context);
        ConvertFuncBrushes(context, allowList);
        ConvertTeleports(context, allowList);
        ConvertAllowListInputs(context, allowList);
        ConvertTonemap(context);
        ConvertSoundscapes(context);
    }

    private static readonly Dictionary<string, string> PlayerInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AddAllowedPlayer"] = "Enable",
        ["RemoveAllowedPlayer"] = "Disable",
        ["TogglePlayer"] = "Toggle",
    };

    private static readonly HashSet<string> TeamInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddAllowedTeam",
        "RemoveAllowedTeam",
        "ToggleTeam",
        "ResetTeam",
        "AllowForSeekers",
        "AllowForRunners",
        "RemoveAllowForSeekers",
        "RemoveAllowForRunners",
    };

    private static void ConvertAllowListInputs(PortContext context, AllowList allowList)
    {
        context.RewriteOutputs(
            (_, output) =>
            {
                List<bool> startsEnabled = allowList.Match(output.target);
                if (startsEnabled.Count == 0)
                    return output;

                if (PlayerInputs.TryGetValue(output.action, out string? input))
                {
                    output.action = input;
                }
                else if (output.action.Equals("ResetPlayer", StringComparison.OrdinalIgnoreCase))
                {
                    // Back to the entity's default for the player
                    output.action = startsEnabled[0] ? "Enable" : "Disable";
                    if (startsEnabled.Distinct().Count() > 1)
                        context.Report.Warn($"ResetPlayer sent to entities with different defaults ({output.target})");
                }
                else
                {
                    if (TeamInputs.Contains(output.action))
                        context.Report.Warn($"{output.action} (multiplayer teams) isn't converted");

                    return output;
                }

                context.Report.Change("per-player KZ inputs -> Enable/Disable/Toggle");
                return output;
            }
        );
    }

    /// <summary>
    /// KZ entities that are only active for the players allowed through inputs.
    /// Momentum is single player, so being allowed becomes being enabled.
    /// </summary>
    private sealed class AllowList
    {
        private readonly Dictionary<string, List<bool>> _startsEnabled = new(StringComparer.OrdinalIgnoreCase);

        public void Add(Entity entity, bool startsEnabled)
        {
            if (entity.Name is not { Length: > 0 } name)
                return;

            if (!_startsEnabled.TryGetValue(name, out List<bool>? values))
                _startsEnabled[name] = values = [];

            values.Add(startsEnabled);
        }

        // Whether each matching entity starts enabled. Target names ending in * match by prefix, like the engine.
        public List<bool> Match(string target)
        {
            if (target.EndsWith('*'))
            {
                string prefix = target[..^1];
                return _startsEnabled
                    .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Value)
                    .ToList();
            }

            return _startsEnabled.TryGetValue(target, out List<bool>? values) ? values : [];
        }
    }
}
