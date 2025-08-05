using System;

namespace PhosphorMP.Audio
{
    public class Limiter
    {
        public Limiter(double sampleRate = 48000)
        {
            SampleRate = sampleRate;
            falloff = SampleRate / 3;
        }

        public double SampleRate { get; }
        public double Strength { get; set; } = 1;

        public bool ReduceHighPitch = false;

        private double loudnessL = 1;
        private double loudnessR = 1;
        private double velocityR = 0;
        private double velocityL = 0;
        private double attack = 100;
        private double falloff = 48000 / 3;
        private double minThresh = 0.4;
        private double velocityThresh = 1;

        public void ProcessSamples(float[] buffer)
        {
            int length = buffer.Length;

            if (length % 2 != 0) throw new ArgumentException("Buffer length must be a multiple of 2");

            for (int i = 0; i < length; i += 2)
            {
                double l = Math.Abs(buffer[i]);
                double r = Math.Abs(buffer[i + 1]);

                if (loudnessL > l)
                    loudnessL = (loudnessL * falloff + l) / (falloff + 1);
                else
                    loudnessL = (loudnessL * attack + l) / (attack + 1);

                if (loudnessR > r)
                    loudnessR = (loudnessR * falloff + r) / (falloff + 1);
                else
                    loudnessR = (loudnessR * attack + r) / (attack + 1);

                if (loudnessL < minThresh) loudnessL = minThresh;
                if (loudnessR < minThresh) loudnessR = minThresh;

                l = buffer[i] / (loudnessL * Strength + 2 * (1 - Strength)) / 2;
                r = buffer[i + 1] / (loudnessR * Strength + 2 * (1 - Strength)) / 2;

                if (i != 0)
                {
                    double dl = Math.Abs(buffer[i] - (float)l);
                    double dr = Math.Abs(buffer[i + 1] - (float)r);

                    if (velocityL > dl)
                        velocityL = (velocityL * falloff + dl) / (falloff + 1);
                    else
                        velocityL = (velocityL * attack + dl) / (attack + 1);

                    if (velocityR > dr)
                        velocityR = (velocityR * falloff + dr) / (falloff + 1);
                    else
                        velocityR = (velocityR * attack + dr) / (attack + 1);
                }

                if (ReduceHighPitch)
                {
                    if (velocityL > velocityThresh)
                        l = l / velocityL * velocityThresh;
                    if (velocityR > velocityThresh)
                        r = r / velocityR * velocityThresh;
                }

                l *= 0.75;
                r *= 0.75;

                // Clamping the samples to the range [-1.0, 1.0]
                l = Math.Clamp(l, -1.0, 1.0);
                r = Math.Clamp(r, -1.0, 1.0);

                buffer[i] = (float)l;
                buffer[i + 1] = (float)r;
            }
        }
    }
}
