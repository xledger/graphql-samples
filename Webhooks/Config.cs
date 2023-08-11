using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webhooks {
    class Config {
        [JsonProperty(Required = Required.DisallowNull)]
        public required string DbPath { get; init; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string LogPath { get; init; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string GraphQLToken { get; init; }

        public static async Task<Config> FromJsonFile(string path) {
            var jsonSettings = new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Error
            };
            var json = await File.ReadAllTextAsync(path);
            if (JsonConvert.DeserializeObject<Config>(json, jsonSettings) is not Config config) {
                throw new ArgumentException("Could not read config file.");
            }

            return config;
        }
        
    }
}
