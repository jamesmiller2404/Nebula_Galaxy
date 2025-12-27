using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Mathematics;

namespace GalaxyViewer
{
    internal readonly struct Star
    {
        public Vector3 Position { get; }
        public float Intensity { get; }
        public byte ColorIndex { get; }

        public Star(Vector3 position, float intensity, byte colorIndex)
        {
            Position = position;
            Intensity = intensity;
            ColorIndex = colorIndex;
        }
    }

    internal class GalaxyGenerator
    {
        public List<Star> Generate(GalaxyParameters parameters, CancellationToken token)
        {
            var random = new Random(parameters.Seed);
            int haloStarCount = Math.Max(1000, parameters.StarCount / 8);
            var stars = new List<Star>(parameters.StarCount + parameters.BulgeStarCount + haloStarCount);
            float diskRadius = Math.Max(1f, parameters.DiskRadius);
            float haloRadius = diskRadius * 1.35f;

            for (int i = 0; i < parameters.StarCount; i++)
            {
                token.ThrowIfCancellationRequested();

                int arm = Math.Max(1, parameters.ArmCount);
                int armIndex = random.Next(arm);
                // Bias toward the core so density gently decreases outward
                float baseRadius = diskRadius * MathF.Pow((float)random.NextDouble(), 1.6f);
                float armAngle = armIndex * MathF.Tau / arm;
                float twist = parameters.ArmTwist * (baseRadius / diskRadius);
                float angleNoise = (float)(NextGaussian(random) * parameters.ArmSpread);
                float angle = armAngle + twist + angleNoise;

                // Allow some spill beyond the nominal disk to soften the edge
                float radialNoise = (float)(NextGaussian(random) * parameters.Noise * diskRadius * 0.25f);
                float radius = Math.Clamp(baseRadius + radialNoise, 0.05f, haloRadius);

                float x = radius * MathF.Cos(angle);
                float y = radius * MathF.Sin(angle);
                float thickness = parameters.VerticalThickness;
                float z = (float)(NextGaussian(random) * thickness);

                float radial01 = radius / diskRadius;
                float coreFalloff = MathF.Pow(MathF.Max(0f, 1f - radial01), parameters.CoreFalloff);
                float haloT = Clamp((radius - diskRadius) / MathF.Max(0.0001f, haloRadius - diskRadius), 0f, 1f);
                float envelope = MathF.Pow(1f - haloT, 2.5f);

                float intensity = parameters.Brightness * coreFalloff * envelope;
                intensity += (float)(random.NextDouble() * 0.04 - 0.02);
                intensity = Math.Clamp(intensity, 0.003f, float.MaxValue);

                float quantized = MathF.Pow(intensity, 0.7f);
                quantized += (float)(random.NextDouble() - 0.5) * (1f / 255f);
                byte colorIndex = (byte)Math.Clamp((int)MathF.Round(quantized * 255f), 0, 255);

                stars.Add(new Star(new Vector3(x, z, y), intensity, colorIndex));
            }

            // Generate bulge stars
            var bulgeRandom = new Random(parameters.Seed + 1);
            float bulgeRadius = Math.Max(0.1f, parameters.BulgeRadius);
            float r_min = 0.1f;
            float inv_r_min = 1f / r_min;
            float inv_r_max = 1f / bulgeRadius;
            float inv_r_range = inv_r_min - inv_r_max;

            for (int i = 0; i < parameters.BulgeStarCount; i++)
            {
                token.ThrowIfCancellationRequested();

                // Sample radius using inverse transform for power-law density
                float u = (float)bulgeRandom.NextDouble();
                float inv_r = inv_r_min - u * inv_r_range;
                float radius = 1f / inv_r;
                radius = Math.Clamp(radius, r_min, bulgeRadius);

                // Uniform angle
                float angle = (float)(bulgeRandom.NextDouble() * MathF.Tau);

                // Vertical position with bulge-specific thickness
                float sigma_z = parameters.VerticalThickness * parameters.BulgeVerticalScale;
                float z = (float)(NextGaussian(bulgeRandom) * sigma_z);

                float x = radius * MathF.Cos(angle);
                float y = radius * MathF.Sin(angle);

                // Intensity with bulge falloff
                float radial01 = radius / bulgeRadius;
                float intensity = parameters.BulgeBrightness * MathF.Pow(1f - radial01, parameters.BulgeFalloff);
                intensity += (float)(bulgeRandom.NextDouble() * 0.08 - 0.04);
                intensity = Math.Clamp(intensity, 0.05f, float.MaxValue);

                // Slightly hotter colors for bulge (shift towards blue)
                float quantized = MathF.Pow(intensity, 0.6f);
                quantized += (float)(bulgeRandom.NextDouble() - 0.5) * (1f / 255f);
                byte colorIndex = (byte)Math.Clamp((int)MathF.Round(quantized * 255f), 0, 255);

                stars.Add(new Star(new Vector3(x, z, y), intensity, colorIndex));
            }

            // Generate a sparse halo to diffuse the outer edge
            var haloRandom = new Random(parameters.Seed + 2);
            float haloOuterRadius = haloRadius * 1.2f;
            for (int i = 0; i < haloStarCount; i++)
            {
                token.ThrowIfCancellationRequested();

                float u = (float)haloRandom.NextDouble();
                float radius = Lerp(diskRadius * 0.85f, haloOuterRadius, MathF.Pow(u, 0.6f));
                radius += (float)(NextGaussian(haloRandom) * parameters.Noise * diskRadius * 0.1f);
                radius = Math.Clamp(radius, diskRadius * 0.75f, haloOuterRadius);

                float angle = (float)(haloRandom.NextDouble() * MathF.Tau);
                float x = radius * MathF.Cos(angle);
                float y = radius * MathF.Sin(angle);
                float z = (float)(NextGaussian(haloRandom) * parameters.VerticalThickness * 1.2f);

                float haloFade = Clamp((radius - diskRadius) / MathF.Max(0.001f, haloOuterRadius - diskRadius), 0f, 1f);
                float haloIntensity = parameters.Brightness * 0.35f * MathF.Pow(1f - haloFade, 3f);
                haloIntensity += (float)(haloRandom.NextDouble() * 0.02 - 0.01f);
                haloIntensity = Math.Clamp(haloIntensity, 0.0015f, 0.2f);

                float quantizedHalo = MathF.Pow(haloIntensity, 0.65f);
                quantizedHalo += (float)(haloRandom.NextDouble() - 0.5f) * (1f / 255f);
                byte haloColorIndex = (byte)Math.Clamp((int)MathF.Round(quantizedHalo * 255f), 0, 255);

                stars.Add(new Star(new Vector3(x, z, y), haloIntensity, haloColorIndex));
            }

            return stars;
        }

        private static double NextGaussian(Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
