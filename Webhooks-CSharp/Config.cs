using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;

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

        [JsonProperty]
        public bool UseTunnel { get; set; }

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

            var apiTokenBytes = WebEncoders.Base64UrlDecode(GraphQLToken);
            var apiTokenBase64 = WebEncoders.Base64UrlEncode(apiTokenBytes);
            if (GraphQLToken != apiTokenBase64) {
                throw new ApplicationException("graphQLToken could not be successfully round-trip decoded and encoded in base64.");
            }
        }
    }
}
