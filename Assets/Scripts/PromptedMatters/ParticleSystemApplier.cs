// ParticleSystemApplier.cs
using UnityEngine;

public static class ParticleSystemApplier
{
    public static void Apply(ParticleSystem ps, ParticleProfile p)
    {
        if (ps == null || p == null) return;

        // ===== MAIN =====
        var main = ps.main;
        if (p.main != null)
        {
            if (p.main.duration >= 0f) main.duration = p.main.duration;
            if (p.main.loop.HasValue) main.loop = p.main.loop.Value;

            if (p.main.startLifetime.HasValue)
                main.startLifetime = new ParticleSystem.MinMaxCurve(p.main.startLifetime.Value);

            if (p.main.startSpeed.HasValue)
                main.startSpeed = new ParticleSystem.MinMaxCurve(p.main.startSpeed.Value);

            if (p.main.startSize.HasValue)
            {
                // tiny sizes sometimes get lost if not explicitly set as Constant
                var size = new ParticleSystem.MinMaxCurve(p.main.startSize.Value);
                size.mode = ParticleSystemCurveMode.Constant;
                main.startSize = size;
            }

            if (p.main.startColor != null)
            {
                var c = new Color(p.main.startColor.r, p.main.startColor.g, p.main.startColor.b, p.main.startColor.a);
                main.startColor = new ParticleSystem.MinMaxGradient(c);
            }

            if (p.main.gravityModifier.HasValue)
                main.gravityModifier = new ParticleSystem.MinMaxCurve(p.main.gravityModifier.Value);

            if (!string.IsNullOrEmpty(p.main.simulationSpace))
            {
                switch (p.main.simulationSpace.Trim().ToLowerInvariant())
                {
                    case "world": main.simulationSpace = ParticleSystemSimulationSpace.World; break;
                    case "local": main.simulationSpace = ParticleSystemSimulationSpace.Local; break;
                    // If you later support custom space, you'll need to set main.customSimulationSpace too.
                    default: main.simulationSpace = ParticleSystemSimulationSpace.Local; break;
                }
            }

            if (p.main.maxParticles.HasValue)
                main.maxParticles = Mathf.Max(1, p.main.maxParticles.Value);
        }

        // ===== EMISSION =====
        var emission = ps.emission;
        if (p.emission != null)
        {
            if (p.emission.enabled.HasValue) emission.enabled = p.emission.enabled.Value;

            if (p.emission.rateOverTime.HasValue)
            {
                var r = new ParticleSystem.MinMaxCurve(Mathf.Max(0f, p.emission.rateOverTime.Value));
                r.mode = ParticleSystemCurveMode.Constant;
                emission.rateOverTime = r;
            }

            if (p.emission.bursts != null)
            {
                var bursts = new ParticleSystem.Burst[p.emission.bursts.Count];
                for (int i = 0; i < bursts.Length; i++)
                {
                    var b = p.emission.bursts[i];
                    var time = Mathf.Max(0f, b.time);
                    // Unity burst count is ushort under the hood, clamp to safe range
                    var count = (short)Mathf.Clamp(b.count, 0, 32767);
                    bursts[i] = new ParticleSystem.Burst(time, (short)count);
                }
                // correct overload (no bool)
                emission.SetBursts(bursts); // or emission.SetBursts(bursts, bursts.Length);
            }
        }

        // ===== SHAPE =====
        var shape = ps.shape;
        if (p.shape != null)
        {
            if (p.shape.enabled.HasValue) shape.enabled = p.shape.enabled.Value;

            if (!string.IsNullOrEmpty(p.shape.type))
            {
                switch (p.shape.type)
                {
                    case "Sphere": shape.shapeType = ParticleSystemShapeType.Sphere; break;
                    case "Cone":   shape.shapeType = ParticleSystemShapeType.Cone; break;
                    case "Box":    shape.shapeType = ParticleSystemShapeType.Box; break;
                    default: break; // keep previous
                }
            }

            if (p.shape.radius.HasValue) shape.radius = Mathf.Max(0f, p.shape.radius.Value);
            if (p.shape.angle.HasValue)  shape.angle  = Mathf.Clamp(p.shape.angle.Value, 0f, 90f);
            // You can add more shape params here if your profile supports them (length, arc, position, etc.)
        }

        // ===== COLOR OVER LIFETIME =====
        var col = ps.colorOverLifetime;
        if (p.colorOverLifetime != null)
        {
            if (p.colorOverLifetime.enabled.HasValue) col.enabled = p.colorOverLifetime.enabled.Value;
            if (p.colorOverLifetime.gradient != null)
            {
                var grad = GradientSerialization.ToGradient(p.colorOverLifetime.gradient);
                col.color = new ParticleSystem.MinMaxGradient(grad);
            }
        }

        // ===== SIZE OVER LIFETIME =====
        var sol = ps.sizeOverLifetime;
        if (p.sizeOverLifetime != null)
        {
            if (p.sizeOverLifetime.enabled.HasValue) sol.enabled = p.sizeOverLifetime.enabled.Value;
            if (p.sizeOverLifetime.curve != null)
            {
                var curve = GradientSerialization.ToAnimationCurve(p.sizeOverLifetime.curve);
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }
        }

        // ===== NOISE =====
        var noise = ps.noise;
        if (p.noise != null)
        {
            if (p.noise.enabled.HasValue) noise.enabled = p.noise.enabled.Value;
            if (p.noise.strength.HasValue) noise.strength = p.noise.strength.Value;
            if (p.noise.frequency.HasValue) noise.frequency = Mathf.Max(0.001f, p.noise.frequency.Value);

            // NEW: scrollSpeed and quality mapping
            if (p.noise.scrollSpeed.HasValue) noise.scrollSpeed = p.noise.scrollSpeed.Value;

            if (!string.IsNullOrEmpty(p.noise.quality))
            {
                switch (p.noise.quality.Trim().ToLowerInvariant())
                {
                    case "high":   noise.quality = ParticleSystemNoiseQuality.High; break;
                    case "medium": noise.quality = ParticleSystemNoiseQuality.Medium; break;
                    case "low":    noise.quality = ParticleSystemNoiseQuality.Low; break;
                }
            }
        }

        // ===== RENDERER =====
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        if (pr != null && p.renderer != null)
        {
            if (!string.IsNullOrEmpty(p.renderer.renderMode))
            {
                pr.renderMode = p.renderer.renderMode switch
                {
                    "Stretch" => ParticleSystemRenderMode.Stretch,
                    "Mesh"    => ParticleSystemRenderMode.Mesh,
                    _         => ParticleSystemRenderMode.Billboard
                };
            }

            if (p.renderer.sortingFudge.HasValue)    pr.sortingFudge    = p.renderer.sortingFudge.Value;
            if (p.renderer.minParticleSize.HasValue) pr.minParticleSize = p.renderer.minParticleSize.Value;
            if (p.renderer.maxParticleSize.HasValue) pr.maxParticleSize = p.renderer.maxParticleSize.Value;

            // If your profile exposes material/shader later, wire it here (safely).
        }
    }
}
