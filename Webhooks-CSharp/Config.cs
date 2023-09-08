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
        public bool UseThirdPartyWebhookDevelopmentTunnel { get; set; }

        [JsonProperty(Required = Required.DisallowNull)]
        public required string[] Urls { get; set; }

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

            if (Urls.Length < 1) {
                throw new ApplicationException("At least one valid URL must be specified");
            }
            foreach (var url in Urls) {
                try {
                    var uri = new Uri(url);
                    if (uri.Scheme != "http" && uri.Scheme != "https") {
                        throw new ApplicationException("URL must be a valid HTTP or HTTPS address");
                    }
                } catch (Exception) {
                    throw new ApplicationException("http must be a valid HTTP address");
                }
            }
        }
    }
}
