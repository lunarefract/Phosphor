using Newtonsoft.Json;
using PhosphorMP.Parser;
using System.IO;

namespace PhosphorMP
{
    public class SerializableConfig
    {
        [JsonIgnore]
        public static SerializableConfig Singleton { get; private set; }

        public ParserConfig Parser { get; set; } = new ParserConfig();

        private static bool _isLoading = false;

        // Parameterless constructor: for JSON deserializer ONLY
        [JsonConstructor] // optional but explicit, helps clarify intent
        public SerializableConfig() {}
        
        public SerializableConfig(bool loadFromFile)
        {
            if (Singleton == null)
                Singleton = this;

            if (loadFromFile)
                Load();
        }

        public void Save(string filePath = "config.json")
        {
            var json = JsonConvert.SerializeObject(Singleton, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void Load(string filePath = "config.json")
        {
            if (_isLoading) return;
            if (!File.Exists(filePath)) return;
            _isLoading = true;

            try
            {
                string json = File.ReadAllText(filePath);
                SerializableConfig tempConfig = JsonConvert.DeserializeObject<SerializableConfig>(json);

                if (tempConfig == null)
                    return;

                // Copy deserialized properties into Singleton instance
                if (tempConfig.Parser != null)
                    Singleton.Parser = tempConfig.Parser;
            }
            finally
            {
                _isLoading = false;
            }
        }
    }
}