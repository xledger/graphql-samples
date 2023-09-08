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
        public required string DbPath { get; set; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string LogPath { get; set; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string GraphQLToken { get; set; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string GraphQLEndpoint { get; set; }

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

        public void Validate() {
            if (string.IsNullOrWhiteSpace(DbPath)) {
                throw new ApplicationException("dbPath cannot be blank");
            }

            if (Path.GetExtension(DbPath) != ".db") {
                throw new ApplicationException("dbPath must have a .db extension.");
            }

            if (string.IsNullOrWhiteSpace(GraphQLToken)) {
                throw new ApplicationException("graphQLToken cannot be blank");
            }
        }
        
    }
}
