// GradientSerialization.cs
using UnityEngine;

public static class GradientSerialization
{
    public static Gradient ToGradient(ParticleProfile.GradientData data)
    {
        var g = new Gradient();
        if (data == null || data.keys == null || data.keys.Count == 0)
        {
            g.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            return g;
        }

        var cks = new GradientColorKey[data.keys.Count];
        var aks = new GradientAlphaKey[data.keys.Count];
        for (int i = 0; i < data.keys.Count; i++)
        {
            var k = data.keys[i];
            var col = new Color(k.r, k.g, k.b, 1f);
            cks[i] = new GradientColorKey(col, Mathf.Clamp01(k.t));
            aks[i] = new GradientAlphaKey(Mathf.Clamp01(k.a), Mathf.Clamp01(k.t));
        }
        g.SetKeys(cks, aks);
        return g;
    }

    public static AnimationCurve ToAnimationCurve(ParticleProfile.CurveData data)
    {
        if (data == null || data.keys == null || data.keys.Count == 0)
            return AnimationCurve.Linear(0f, 1f, 1f, 1f);

        var keys = new Keyframe[data.keys.Count];
        for (int i = 0; i < data.keys.Count; i++)
        {
            var k = data.keys[i];
            keys[i] = new Keyframe(Mathf.Clamp01(k.t), k.v);
        }
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }
        return curve;
    }

    private static class AnimationUtility
    {
        public enum TangentMode { Auto }
        public static void SetKeyLeftTangentMode(AnimationCurve c, int i, TangentMode m) { }
        public static void SetKeyRightTangentMode(AnimationCurve c, int i, TangentMode m) { }
    }
}
