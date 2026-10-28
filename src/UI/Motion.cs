using System.Windows;

namespace Clothesline.UI;

/// <summary>Honours Windows' "Animation effects" switch: when off, state changes apply instantly.</summary>
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
}
