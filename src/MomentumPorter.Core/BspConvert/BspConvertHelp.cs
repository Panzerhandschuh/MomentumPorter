namespace MomentumPorter.BspConvert;

/// <summary>
/// Help text for the BSPConvert options, from BSPConvert's README, shared by <see cref="BspConvertOptions"/>'s
/// <c>[PortOption]</c> attributes and the CLI's options so they stay the same.
/// </summary>
public static class BspConvertHelp
{
    // General
    public const string NoPak = "Export materials (and GoldSrc sounds and models) into folders instead of embedding them in the BSP.";
    public const string Compress = "LZMA compress the converted BSP.";
    public const string Prefix = "Prefix for the converted BSP's file name.";
    public const string Output = "Output game directory for converted BSP/materials (e.g. Momentum Mod's momentum folder).";
    public const string Maps = "Convert only the named BSP(s) from an archive instead of every BSP it contains. Comma-separated map names without extension (e.g. --maps pgrocket,pgplasma). Case-insensitive.";
    public const string Overwrite = "Replace files that are already in the output folder (BSPConvert's own behaviour). Otherwise the conversion stops before copying anything.";

    // Quake 3
    public const string Subdiv = "Displacement subdivisions [2-4].";
    public const string NoToolDisps = "Skip converting patches with tool textures to displacements.";
    public const string PatchDisps = "Convert patches to displacements instead of primitive meshes (curved collision, no per-vertex UV/lightmap, not entity-attachable).";
    public const string MinDmg = "Minimum damage for trigger_hurt to respawn player.";
    public const string LavaTriggers = "Duplicate lava brushes into trigger_hurt volumes that kill/respawn the player on contact.";
    public const string NoZones = "Ignore timer zone triggers (no zone file is written).";
    public const string OffModeEnts = "For maps with different cpm/vq3 entities, choose which entities to use when played in non-defrag modes (cpm or vq3).";
    public const string Scale = "Uniformly scale the map's geometry and entity positions by this factor (e.g. 2 doubles the world size). Mover speed/wait times, jump pad and teleport velocities, and other gameplay-tuning values are not scaled.";
    public const string FogObb = "Use legacy obb_volumefog entities instead of the default Fog shader. Handles arbitrary brush shapes but can flicker on thin volumes.";
    public const string FogMinHeight = "Minimum vertical height (units) for converted fog volumes. Only used with --fogobb.";
    public const string NoFogOverlay = "Don't draw fog shader visible stages (e.g. scrolling clouds) as an overlay face; fog brush faces are just dropped.";
    public const string NoEnvMap = "Disable envmap (specular cubemap) shader conversion. Useful for maps where Source's cubemap poorly emulates Quake 3's spheremap effect.";
    public const string ClampOverbright = "Apply Quake 3's hue-preserving overbright clamp to lightmaps, flattening over-bright highlights toward white.";
    public const string Q3Content = "When the Q3Content folder next to Momentum Porter is empty, extract the Quake 3 install's baseq3 pak files into it first (found through Steam, about 650 MB, once), so the textures, shaders and sounds maps share with the base game convert.";

    // Animated textures
    public const string NoAnim = "Disable baking multi-pass / animated shaders (scrolling liquids, layered effects, animMaps) into animated flipbook textures.";
    public const string AnimBudget = "Target max size (MB) of each baked animated VTF. Resolution adapts down as the seamless loop needs more frames.";
    public const string AnimMaxRes = "Sharpness ceiling (px) for baked animated textures.";
    public const string AnimFps = "Playback fps (smoothness) of baked animated textures.";
    public const string AnimMaxFrames = "Hard cap on baked frame count. Longer loops are sped up to fit while staying seamless.";
    public const string AnimAlpha = "Translucency [0-1] for baked liquids. 1 derives per-texel translucency from each texture's alpha/luminance; below 1 uses a flat constant alpha.";

    // GoldSrc
    public const string Wads = "Comma-separated folders to search (recursively) for the WAD files the map takes its textures from, its sky images (gfx/env), and the sounds, sprites and models its entities use, e.g. your Half-Life install or a folder of community WADs. The input's own folder and a default Steam Half-Life install are always searched.";
    public const string Studiomdl = "Path to the studiomdl.exe that compiles the models entities use (in Momentum Mod's bin/win64 folder). Found next to the output game folder or in a default Steam Momentum Mod install if not given. Models aren't converted if it isn't found.";
    public const string GoldSrcHulls = "Copy the player clip hulls of this GoldSrc BSP into the input BSP(s) instead of converting them. For a Strata BSP recompiled from a converted map (e.g. given new visuals and recompiled with VBSP), so GoldSrc-hull game modes collide exactly as in the GoldSrc map. Run it on the finished BSP, after VVIS and VRAD.";
}
