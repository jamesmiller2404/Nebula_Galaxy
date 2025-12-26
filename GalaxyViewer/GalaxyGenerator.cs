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

        public Star(Vector3 position, float intensity)
        {
            Position = position;
            Intensity = intensity;
        }
    }

    internal class GalaxyGenerator
    {
        public List<Star> Generate(GalaxyParameters parameters, CancellationToken token)
        {
            var random = new Random(parameters.Seed);
            var stars = new List<Star>(parameters.StarCount);
            float diskRadius = Math.Max(1f, parameters.DiskRadius);

            for (int i = 0; i < parameters.StarCount; i++)
            {
                token.ThrowIfCancellationRequested();

                int arm = Math.Max(1, parameters.ArmCount);
                int armIndex = random.Next(arm);
                float baseRadius = (float)Math.Pow(random.NextDouble(), 0.6) * diskRadius;
                float armAngle = armIndex * MathF.Tau / arm;
                float twist = parameters.ArmTwist * (baseRadius / diskRadius);
                float angleNoise = (float)(NextGaussian(random) * parameters.ArmSpread);
                float angle = armAngle + twist + angleNoise;

                float radialNoise = (float)(NextGaussian(random) * parameters.Noise * diskRadius * 0.15f);
                float radius = Math.Clamp(baseRadius + radialNoise, 0.1f, diskRadius);

                float x = radius * MathF.Cos(angle);
                float y = radius * MathF.Sin(angle);
                float thickness = parameters.VerticalThickness * (1f - radius / diskRadius);
                float z = (float)(NextGaussian(random) * thickness);

                float radial01 = Math.Clamp(radius / diskRadius, 0f, 1f);
                float intensity = parameters.Brightness * MathF.Pow(1f - radial01, parameters.CoreFalloff);
                intensity += (float)(random.NextDouble() * 0.08 - 0.04);
                intensity = Math.Clamp(intensity, 0.05f, 1f);

                stars.Add(new Star(new Vector3(x, z, y), intensity));
            }

            return stars;
        }

        private static double NextGaussian(Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
