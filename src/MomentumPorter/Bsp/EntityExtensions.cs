namespace MomentumPorter.Bsp;

using System.Globalization;
using LibBSP;

internal static class EntityExtensions
{
    public static void Set(this Entity entity, string key, int value)
    {
        entity[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    public static void Set(this Entity entity, string key, float value)
    {
        entity[key] = value.ToString(CultureInfo.InvariantCulture);
    }
}
