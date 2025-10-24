// ParticleProfile.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ParticleProfile
{
    public string add_mode; // none | replace_mesh | append
    public string target;   // mesh | new | named:<string>

    public Main main;
    public Emission emission;
    public Shape shape;
    public ColorOverLifetime colorOverLifetime;
    public SizeOverLifetime sizeOverLifetime;
    public Noise noise;
    public RendererSettings renderer;

    [Serializable] public class Main
    {
        public float duration = -1f;
        public bool? loop;
        public float? startLifetime;
        public float? startSpeed;
        public float? startSize;
        public ColorRGBA startColor;
        public float? gravityModifier;
        public string simulationSpace; // "Local" | "World"
        public int? maxParticles;
    }

    [Serializable] public class Emission
    {
        public bool? enabled;
        public float? rateOverTime;
        public List<Burst> bursts;
        [Serializable] public class Burst { public float time; public int count; }
    }

    [Serializable] public class Shape
    {
        public bool? enabled;
        public string type; // Sphere|Cone|Box|Mesh|MeshRenderer|SkinnedMeshRenderer
        public float? radius;
        public float? angle;
    }

    [Serializable] public class ColorOverLifetime
    {
        public bool? enabled;
        public GradientData gradient;
    }

    [Serializable] public class SizeOverLifetime
    {
        public bool? enabled;
        public CurveData curve;
    }

    [Serializable] public class Noise
    {
        public bool? enabled;
        public float? strength;
        public float? frequency;
    }

    [Serializable] public class RendererSettings
    {
        public string renderMode; // Billboard|Stretch|Mesh
        public float? sortingFudge;
        public float? minParticleSize;
        public float? maxParticleSize;
    }

    [Serializable] public class ColorRGBA { public float r, g, b, a = 1f; }

    [Serializable] public class GradientData
    {
        public List<GradientKey> keys;
        [Serializable] public class GradientKey { public float t, r, g, b, a = 1f; }
    }

    [Serializable] public class CurveData
    {
        public List<CurveKey> keys;
        [Serializable] public class CurveKey { public float t; public float v; }
    }
}
