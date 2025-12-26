namespace GalaxyViewer
{
    internal class GalaxyParameters
    {
        public int StarCount { get; set; } = 60000;
        public int ArmCount { get; set; } = 4;
        public float ArmTwist { get; set; } = 5.0f;
        public float ArmSpread { get; set; } = 0.35f;
        public float DiskRadius { get; set; } = 40f;
        public float VerticalThickness { get; set; } = 0.5f;
        public float Noise { get; set; } = 0.25f;
        public float CoreFalloff { get; set; } = 2.0f;
        public float Brightness { get; set; } = 1.0f;
        public int Seed { get; set; } = 12345;

        public GalaxyParameters Clone()
        {
            return (GalaxyParameters)MemberwiseClone();
        }
    }
}
