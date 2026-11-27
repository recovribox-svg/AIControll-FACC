using UnityEngine;

namespace CompanionAI_Pathfinder.UI
{
    /// <summary>
    /// ★ v0.3.70 features review (issue #114): the settings panel follows UnityModManager's UI scale. UMM scales its window by
    /// the skin's base font (Font.CreateDynamicFontFromOSFont at Scale(13)) and its own styles' sizes; this panel set fixed
    /// font sizes (12/14/18), fixed rich-text sizes (&lt;size=11&gt;) and fixed widths and heights, so at a large UI scale
    /// (4K) most of it stayed unreadably small while UMM's own buttons grew. Every size here goes through UMM's
    /// <c>UnityModManager.UI.Scale</c> (1 when UMM's UI is not up).
    /// </summary>
    internal static class UiScale
    {
        /// <summary>UMM's current UI scale factor.</summary>
        internal static float Factor
        {
            get
            {
                try { return UnityModManagerNet.UnityModManager.UI.Scale(1f); }
                catch { return 1f; }
            }
        }

        /// <summary>A size in pixels at UMM's scale.</summary>
        internal static float Px(float value) => value * Factor;

        /// <summary>A font size at UMM's scale (at least 1).</summary>
        internal static int Font(int size) => Mathf.Max(1, Mathf.RoundToInt(size * Factor));

        internal static GUILayoutOption W(float width) => GUILayout.Width(Px(width));
        internal static GUILayoutOption H(float height) => GUILayout.Height(Px(height));
        internal static void Space(float pixels) => GUILayout.Space(Px(pixels));

        /// <summary>A rich-text size tag at UMM's scale ("&lt;size=N&gt;").</summary>
        internal static string Size(int size) => "<size=" + Font(size) + ">";
    }
}
