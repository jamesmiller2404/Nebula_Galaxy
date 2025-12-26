using System;
using OpenTK.Mathematics;

namespace GalaxyViewer
{
    internal class Camera
    {
        public float Yaw { get; private set; } = 0f;
        public float Pitch { get; private set; } = -0.35f;
        public float Distance { get; private set; } = 140f;
        public Vector3 Target { get; set; } = Vector3.Zero;

        public void Rotate(float deltaYaw, float deltaPitch)
        {
            Yaw += deltaYaw;
            Pitch = Math.Clamp(Pitch + deltaPitch, -1.45f, 1.45f);
        }

        public void Zoom(float delta)
        {
            Distance = Math.Clamp(Distance + delta, 10f, 400f);
        }

        public Matrix4 GetViewMatrix()
        {
            float cosPitch = MathF.Cos(Pitch);
            var forward = new Vector3(
                cosPitch * MathF.Cos(Yaw),
                MathF.Sin(Pitch),
                cosPitch * MathF.Sin(Yaw));

            Vector3 position = Target + forward * Distance;
            return Matrix4.LookAt(position, Target, Vector3.UnitY);
        }

        public Matrix4 GetProjectionMatrix(float aspectRatio)
        {
            float safeAspect = Math.Max(0.1f, aspectRatio);
            return Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), safeAspect, 0.1f, 1000f);
        }
    }
}
