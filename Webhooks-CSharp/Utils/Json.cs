using Newtonsoft.Json;

namespace Webhooks.Utils {
    static class Json {
        static readonly JsonSerializerSettings SerializerSettings = new() {
            DateParseHandling = DateParseHandling.None,
        };

        static readonly JsonSerializer Serializer = JsonSerializer.Create(SerializerSettings);

        internal static T? Deserialize<T>(string s) => Serializer.Deserialize<T>(new JsonTextReader(new StringReader(s)));
        internal static T? Deserialize<T>(JsonTextReader tr) => Serializer.Deserialize<T>(tr);
    }
}
