namespace MomentumPorter.Games.GoldSrc;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

/// <summary>
/// The bhop block fix, baked into the map. GoldSrc bhop maps make their blocks func_doors (or func_buttons) that drop
/// when the player touches them, so whoever doesn't jump off in time falls into the teleport below. Momentum plays
/// these as func_bhops instead: blocks that stay put and teleport the player where that teleport would have once
/// they've stood on them too long.
/// </summary>
/// <remarks>
/// Replaces the engine's block fix (mom_blockfix.cpp, and the entity tools exporting its blocks to Stripper configs),
/// which only finds teleports the block itself dips into as it opens. GoldSrc blocks mostly drop their own height
/// above a pit with the teleport at the bottom, so this follows the player down instead: a block converts when the
/// player falls into a teleport from somewhere on it, without landing on something solid first.
/// </remarks>
public static class BhopBlocks
{
    // Source door spawnflags (func_door.h)
    private const int SF_DOOR_PTOUCH = 1024;
    private const int SF_DOOR_LOCKED = 2048;

    // Source button spawnflags (func_button.h)
    private const int SF_BUTTON_DONTMOVE = 1;
    private const int SF_BUTTON_TOUCH_ACTIVATES = 256;
    private const int SF_BUTTON_LOCKED = 2048;

    // A button that's given no lip stops 4 units short (CBaseButton::Spawn)
    private const float DefaultButtonLip = 4f;

    // func_bhop spawnflags: reset the landing count when it activates, and when the player lands on anything else
    // that isn't a func_bhop. With the landing limit off, it teleports a player who stays on it too long.
    private const int FuncBhopFlags = 1 | 2;

    // MASK_PLAYERSOLID: CONTENTS_SOLID | CONTENTS_WINDOW | CONTENTS_GRATE | CONTENTS_MOVEABLE | CONTENTS_PLAYERCLIP | CONTENTS_MONSTER
    private const int PlayerSolidContents = 0x1 | 0x2 | 0x8 | 0x4000 | 0x10000 | 0x2000000;

    // Brush entities that are always solid, which the player can land on below a block
    private static readonly HashSet<string> SolidClasses = new(StringComparer.OrdinalIgnoreCase) { "func_wall", "func_brush", "func_detail" };

    // The columns the player can fall down from a block are sampled at most this far apart, and at most this many
    // along each side
    private const float SampleSpacing = 8f;
    private const int MaxSamples = 16;

    // A floor level with a teleport's top doesn't catch the player above it: they touch the teleport as they land.
    // Converted brush bounds are only float-accurate (a GoldSrc floor flush with its teleport can end up at
    // -127.99999 over one at -128), so level is within this.
    private const float LevelTolerance = 0.1f;

    // Keys a block keeps as a func_bhop: what it is and how it's drawn
    private static readonly HashSet<string> KeptKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "classname", "model", "origin", "angles", "rendermode", "renderamt", "rendercolor", "renderfx", "disableshadows",
        "disablereceiveshadows",
    };

    private readonly record struct Box(Vector3 Mins, Vector3 Maxs)
    {
        public bool Contains(float x, float y) => x >= Mins.X && x <= Maxs.X && y >= Mins.Y && y <= Maxs.Y;

        public bool OverlapsColumn(Box column) =>
            Mins.X < column.Maxs.X && Maxs.X > column.Mins.X && Mins.Y < column.Maxs.Y && Maxs.Y > column.Mins.Y;

        public Box Offset(Vector3 offset) => new(Mins + offset, Maxs + offset);
    }

    private sealed record Teleport(Entity Entity, List<Box> Brushes);

    // A solid brush, in the model it belongs to placed at its entity's origin
    private readonly record struct Solid(Box Bounds, int Brush, Vector3 Origin);

    /// <summary>A dropping block and the teleport the player falls into from it.</summary>
    public sealed record Block(Entity Entity, Entity Teleport);

    /// <summary>
    /// The map's dropping blocks that lead into a teleport, which <see cref="Convert"/> would turn into func_bhops.
    /// </summary>
    public static List<Block> Find(PortContext context)
    {
        BSP bsp = context.Bsp;
        List<Entity> entities = context.Entities;
        var blocks = new List<Block>();

        List<Teleport> teleports = entities
            .Where(x => x.ClassName is "trigger_teleport" or "trigger_momentum_teleport" && !string.IsNullOrEmpty(x["target"]) && x.ModelNumber > 0)
            .Select(x => new Teleport(x, GetBrushes(bsp, x.ModelNumber, x.Origin, contents: null).Select(b => b.Bounds).ToList()))
            .Where(x => x.Brushes.Count > 0)
            .ToList();

        if (teleports.Count == 0)
            return blocks;

        List<Solid>? solids = null;
        foreach (Entity entity in entities)
        {
            if (GetDroppingBlock(bsp, entity) is not Box block)
                continue;

            if (solids is null)
            {
                solids = GetBrushes(bsp, 0, Vector3.Zero, PlayerSolidContents);
                foreach (Entity solid in entities.Where(x => SolidClasses.Contains(x.ClassName) && x.ModelNumber > 0))
                    solids.AddRange(GetBrushes(bsp, solid.ModelNumber, solid.Origin, PlayerSolidContents));
            }

            if (FindTeleportBelow(bsp, block, teleports, solids) is Entity teleport)
                blocks.Add(new Block(entity, teleport));
        }

        return blocks;
    }

    /// <summary>
    /// Turns the map's dropping blocks into func_bhops. Returns how many it converted.
    /// </summary>
    public static int Convert(PortContext context)
    {
        List<Block> blocks = Find(context);
        foreach (Block block in blocks)
            ConvertToBhop(context, block.Entity, block.Teleport);

        if (blocks.Count > 0)
            context.Report.Change("dropping func_door/func_button bhop blocks -> func_bhop", blocks.Count);

        return blocks.Count;
    }

    // A door or button that moves straight down when the player touches it (CBaseDoor::Spawn, CBaseButton::Spawn):
    // its bounds where it starts
    private static Box? GetDroppingBlock(BSP bsp, Entity entity)
    {
        int flags = entity.GetInt("spawnflags", 0);
        float lip = entity.GetFloat("lip", 0);
        switch (entity.ClassName)
        {
            case "func_door":
                if ((flags & SF_DOOR_PTOUCH) == 0 || (flags & SF_DOOR_LOCKED) != 0)
                    return null;
                break;
            case "func_button":
                if ((flags & SF_BUTTON_TOUCH_ACTIVATES) == 0 || (flags & (SF_BUTTON_DONTMOVE | SF_BUTTON_LOCKED)) != 0)
                    return null;
                if (lip == 0)
                    lip = DefaultButtonLip;
                break;
            default:
                return null;
        }

        int model = entity.ModelNumber;
        if (model <= 0 || model >= bsp.Models.Count || entity.Angles != Vector3.Zero)
            return null;

        Vector3 direction = GetMoveDirection(entity);
        if (MathF.Abs(direction.X) > 0.001f || MathF.Abs(direction.Y) > 0.001f || direction.Z >= 0)
            return null;

        // The engine's bounds are the model's grown by a unit on each side, which Spawn takes back off
        Model bounds = bsp.Models[model];
        Vector3 size = bounds.Maximums - bounds.Minimums;
        float drop = MathF.Abs(direction.Z * size.Z) - lip;
        if (drop <= 0)
            return null;

        return new Box(bounds.Minimums, bounds.Maximums).Offset(entity.Origin);
    }

    // "movedir" holds angles, the direction is their forward vector (AngleVectors)
    private static Vector3 GetMoveDirection(Entity entity)
    {
        Vector4 angles = entity.GetVector("movedir");
        float pitch = angles.X * MathF.PI / 180f;
        float yaw = angles.Y * MathF.PI / 180f;
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
    }

    // Drops the player from columns all over the top of the block. In each, the first teleport they fall into
    // before anything solid catches them. The teleport most of the block leads to wins, then the higher one.
    private static Entity? FindTeleportBelow(BSP bsp, Box block, List<Teleport> teleports, List<Solid> solids)
    {
        float top = block.Maxs.Z;
        List<(Teleport Teleport, List<Box> Brushes)> nearTeleports = teleports
            .Select(x => (x, x.Brushes.Where(b => b.OverlapsColumn(block) && b.Mins.Z < top).ToList()))
            .Where(x => x.Item2.Count > 0)
            .ToList();

        if (nearTeleports.Count == 0)
            return null;

        List<Solid> nearSolids = solids.Where(x => x.Bounds.OverlapsColumn(block) && x.Bounds.Maxs.Z < top).ToList();
        var columns = new Dictionary<Teleport, (int Count, float Top)>();

        Vector3 size = block.Maxs - block.Mins;
        int samplesX = Math.Clamp((int)MathF.Ceiling(size.X / SampleSpacing), 1, MaxSamples);
        int samplesY = Math.Clamp((int)MathF.Ceiling(size.Y / SampleSpacing), 1, MaxSamples);
        for (int i = 0; i < samplesX; i++)
        {
            float x = block.Mins.X + (i + 0.5f) * size.X / samplesX;
            for (int j = 0; j < samplesY; j++)
            {
                float y = block.Mins.Y + (j + 0.5f) * size.Y / samplesY;

                Teleport? reached = null;
                float teleportTop = float.MinValue;
                foreach ((Teleport teleport, List<Box> brushes) in nearTeleports)
                {
                    foreach (Box brush in brushes.Where(b => b.Contains(x, y)))
                    {
                        float brushTop = MathF.Min(brush.Maxs.Z, top);
                        if (brushTop > teleportTop)
                        {
                            reached = teleport;
                            teleportTop = brushTop;
                        }
                    }
                }

                // The block is out of the way once it's dropped, but anything solid between it and the teleport isn't
                if (reached is null || nearSolids.Any(s => CatchesAbove(bsp, s, x, y, teleportTop)))
                    continue;

                (int count, float best) = columns.GetValueOrDefault(reached);
                columns[reached] = (count + 1, MathF.Max(best, teleportTop));
            }
        }

        return columns
            .OrderByDescending(x => x.Value.Count)
            .ThenByDescending(x => x.Value.Top)
            .Select(x => x.Key.Entity)
            .FirstOrDefault();
    }

    // Whether the player falling down the column at (x, y) lands on a solid before they're down to a teleport's top.
    // Sloped brushes are checked at their height in the column, not their bounds' top.
    private static bool CatchesAbove(BSP bsp, Solid solid, float x, float y, float teleportTop)
    {
        if (!solid.Bounds.Contains(x, y) || solid.Bounds.Maxs.Z <= teleportTop + LevelTolerance)
            return false;

        return BrushGeometry.GetColumnSpan(bsp, solid.Brush, x - solid.Origin.X, y - solid.Origin.Y) is (_, float top)
            && top + solid.Origin.Z > teleportTop + LevelTolerance;
    }

    // What the engine's block fix gave the func_bhop (CMomentumBhopBlockFixSystem::FindTeleportAndConvert)
    private static void ConvertToBhop(PortContext context, Entity entity, Entity teleport)
    {
        if (entity.connections.Count > 0 || !string.IsNullOrEmpty(entity["targetname"]))
            context.Report.Warn("bhop block had a name or outputs, which its func_bhop doesn't keep");

        foreach (string key in entity.Keys.Where(x => !KeptKeys.Contains(x)).ToList())
            entity.Remove(key);
        entity.connections.Clear();

        entity.ClassName = "func_bhop";
        entity["target"] = teleport["target"];
        entity.Set("ProgressTeleport", 0);
        // trigger_teleport turns the player to the destination's angles unless UseLandmarkAngles is 0
        entity.Set("UseDestinationAngles", teleport.GetInt("UseLandmarkAngles", -1) != 0 ? 1 : 0);
        // TeleportVelocityMode_t::Reset
        entity.Set("ResetVelocityOnTeleport", teleport.GetInt("VelocityMode", 0) == 1 ? 1 : 0);
        entity.Set("spawnflags", FuncBhopFlags);
        entity.Set("LandingLimit", -1);
    }

    private static List<Solid> GetBrushes(BSP bsp, int model, Vector3 origin, int? contents)
    {
        var brushes = new List<Solid>();
        if (model < 0 || model >= bsp.Models.Count)
            return brushes;

        foreach ((int brush, _) in BrushGeometry.GetBrushes(bsp, model))
        {
            if (contents is int mask && (bsp.Brushes[brush].Contents & mask) == 0)
                continue;

            if (BrushGeometry.GetBrushBounds(bsp, brush) is (Vector3 mins, Vector3 maxs))
                brushes.Add(new Solid(new Box(mins, maxs).Offset(origin), brush, origin));
        }

        return brushes;
    }
}
