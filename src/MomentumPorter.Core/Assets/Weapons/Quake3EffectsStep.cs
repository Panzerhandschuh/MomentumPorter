namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;

/// <summary>
/// Gives Quake 3's projectile weapons Quake 3's explosions and trails (see <see cref="Quake3Effects"/>) in place of
/// Momentum's, and the plasma gun its plasma ball in place of a rocket.
/// </summary>
public sealed class Quake3EffectsStep : IPortStep
{
    public string Name => "Quake 3 effects";

    public async Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        if (state.GameInfo!.Engine != ContentEngine.Quake3)
            return;

        string weaponId = state.Weapon!.Id;
        if (weaponId is "railgun" or "machinegun" or "shotgun" or "lightning")
        {
            context.Report.Warn("Momentum draws this weapon's hits with its own bullet impacts; Quake 3's impact flashes, marks and rail rings aren't drawn");
            return;
        }

        if (!state.Options.Particles || Quake3Effects.ParticlesFor(weaponId) is not { } particles)
            return;

        try
        {
            await Quake3Effects.WriteAsync(state.Content!, state.Momentum!, state.Sink!, context.WorkDirectory, context.Log, context.Cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PortStepException or IOException)
        {
            context.Report.Warn($"Quake 3's explosions and trails couldn't be made, Momentum's are kept: {ex.Message}");
            return;
        }

        foreach ((string key, string particle) in particles)
            state.Particles[key] = particle;

        context.Report.Change("Quake 3 explosions and trails");
        context.Report.Warn($"The pack replaces {Quake3Effects.ManifestPath} (Momentum's plus {Quake3Effects.ParticleFile}), since particle files only load through it: other packs that replace it hide each other, and Momentum's updates to it are hidden until the pack is rebuilt");

        if (Quake3Effects.HasSpriteProjectile(weaponId) && state.Options.Projectile)
        {
            string studioMdl = string.IsNullOrWhiteSpace(state.Options.StudioMdlPath) ? state.Momentum!.StudioMdlPath : state.Options.StudioMdlPath;
            var compiler = new StudioMdlCompiler(studioMdl, Path.Combine(context.WorkDirectory, "studiomdl_effects"), context.Log);
            try
            {
                state.ProjectileModel = await Quake3Effects.WriteInvisibleProjectileAsync(compiler, state.Sink!, context.Cancellation).ConfigureAwait(false);
            }
            catch (PortStepException ex)
            {
                context.Report.Warn($"The plasma ball's projectile model couldn't be made, so Momentum's rocket model is drawn under it: {ex.Message}");
            }
        }
    }
}
