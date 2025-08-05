using ManagedBass;
using ManagedBass.Midi;
using Vulkan;

namespace PhosphorMP.Audio
{
    public class BassMidiWrapper : IDisposable
    {
        //public bool Initialized { get; private set; }
        public static BassMidiWrapper Singleton { get; private set; }
        public MidiFont[] Soundfonts { get; set; }
        public Limiter SampleLimiter { get; private set; }
        public int Voices { get; set; } = 1024 * 2;
        
        public double CpuUsage => Bass.CPUUsage;

        public int Handle {get; private set;}

        public BassMidiWrapper()
        {
            if (Singleton == null)
            {
                Singleton = this;
            }
            SampleLimiter = new Limiter(48000);
            Init();
            //Open(@"/run/media/memfrag/00AAB9F3AAB9E576/BA Rare ASDF Mode rev 1.1.mid");
            //Play();
            Open();
        }

        ~BassMidiWrapper()
        {
            Dispose();
        }

        public void Init()
        {
            if (!Bass.Init(-1, 48000, DeviceInitFlags.Default))
            {
                throw new Exception($"Failed to initialize Bass! Enum: {Bass.LastError}");
            }

            string sfpath = @"/home/memfrag/Downloads/rjv.sf2";

            if (!File.Exists(sfpath))
            {
                throw new FileNotFoundException($"Failed to initialize Bass! Soundfont path doesn't exist: {sfpath}");
            }
            
            if (BassMidi.StreamSetFonts(Handle, LoadSoundfont(sfpath), 1) == 0)
            {
                throw new Exception($"Failed to set soundfont! Enum: {Bass.LastError}");
            }
            BassMidi.Voices = Voices;
            //Initialized = true;
        }

        public void Open()
        {
            if ((Handle = BassMidi.CreateStream(16, BassFlags.Default | BassFlags.Float | BassFlags.MidiNoteOff1)) == 0) // TODO Decode here later
            {
                throw new Exception($"Failed to create stream! Enum: {Bass.LastError}");
            }
            //Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, 0.05f);
        }

        public void Play()
        {
            if (!Bass.ChannelPlay(Handle))
            {
                throw new Exception($"{Bass.LastError}");
            }
        }

        public void PrepareStream()
        {
            const int sampleRate = 48000;
            const int frameMs = 20;
            const int samplesPerFrame = (sampleRate / 1000) * frameMs; // 960 samples
            const int bytesPerSample = sizeof(short);
            const int frameSize = samplesPerFrame * bytesPerSample;
            
            float[] floatSamples = new float[samplesPerFrame];
            byte[] pcmBuffer = new byte[frameSize];

            while (true)
            {
                int bytesRead = Bass.ChannelGetData(Handle, floatSamples, samplesPerFrame * sizeof(float));
                //if (bytesRead <= 0) break;
                
                int sampleCount = bytesRead / sizeof(float);
                if (sampleCount < samplesPerFrame) Array.Clear(floatSamples, sampleCount, samplesPerFrame - sampleCount);
                SampleLimiter.ProcessSamples(floatSamples);

                for (int i = 0; i < samplesPerFrame; i++)
                {
                    float sample = Math.Clamp(floatSamples[i], -1f, 1f);
                    short pcm = (short)(sample * short.MaxValue);
                    BitConverter.GetBytes(pcm).CopyTo(pcmBuffer, i * bytesPerSample);
                }
                //await transmitStream.WriteAsync(pcmBuffer, 0, frameSize);
            }
        }

        public void Pause()
        {
            if (!Bass.ChannelPause(Handle))
            {
                throw new Exception($"{Bass.LastError}");
            }
        }
        
        public void Stop()
        {
            if (!Bass.ChannelStop(Handle))
            {
                throw new Exception($"{Bass.LastError}");
            }
        }

        // Static API below.
        public static MidiFont[] LoadSoundfont(string soundfontPath = @"gm.sf2")
        {
            var handle = BassMidi.FontInit(soundfontPath, FontInitFlags.Unicode);
            var sf = new[]
            {
                new MidiFont
                {
                    Handle = handle,
                    Preset = -1,
                    Bank = 0
                }
            };
            //BassMidi.Compact = true;
            return sf;
        }
        
        public void SendNoteOn(int channel, byte note, int velocity)
        {
            if (!BassMidi.StreamEvent(Handle, 0, MidiEventType.Note, BitHelper.MakeWord(note, 100)))
            {
                throw new Exception($"Failed to send Note On: {Bass.LastError}");
            }
        }

        public void SendNoteOff(int channel, byte note)
        {
            if (!BassMidi.StreamEvent(Handle, 0, MidiEventType.Note, BitHelper.MakeWord(note, 0)))
            {
                throw new Exception($"Failed to send Note Off: {Bass.LastError}");
            }
        }

        // Dispose of bassmidi here
        public void Dispose()
        {
            Bass.Free();
            Bass.PluginFree(0);
            Bass.PluginFree(Handle);
            GC.SuppressFinalize(this);
        }
    }
}
