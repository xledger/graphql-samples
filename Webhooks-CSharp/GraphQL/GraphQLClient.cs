using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Webhooks.Utils;

namespace Webhooks.GraphQL {
    class GraphQLClient {
        static readonly HttpClient http = new HttpClient();

        /// <summary>
        /// The Xledger API token
        /// </summary>
        internal string Token { get; }

        /// <summary>
        /// GraphQL API endpoint to use, e.g., "https://www.xledger.net/graphql"
        /// </summary>
        internal Uri Endpoint { get; }

        internal GraphQLClient(string token, Uri graphqlEndpoint) {
            Token = token;
            Endpoint = graphqlEndpoint;
        }

        internal async Task<JObject> QueryAsync(string query, Dictionary<string, object?>? variables, CancellationToken tok) {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            Debug.Assert(req.Headers.TryAddWithoutValidation("Authorization", $"token {Token}"));

            var payload = new { query, variables };
            var payloadJson = JsonConvert.SerializeObject(payload);
            req.Content = new StringContent(payloadJson, Encoding.UTF8);

            using var resp = await http.SendAsync(req, tok);
            using var stream = await resp.Content.ReadAsStreamAsync(tok);
            using var sr = new StreamReader(stream);
            using var jr = new JsonTextReader(sr);

            var respObject = Json.Deserialize<JObject>(jr)!;

            foreach (var err in respObject.SelectTokens("$.errors[*]")) {
                var code = err["code"]?.ToString();

                var errorKind = code switch {
                    "BAD_REQUEST.INSUFFICIENT_CREDITS" => XledgerGraphQLErrorKind.InsufficientCredits,
                    "BAD_REQUEST.BURST_RATE_LIMIT_REACHED" => XledgerGraphQLErrorKind.ShortRateLimitReached,
                    "BAD_REQUEST.CONCURRENCY_LIMIT_REACHED" => XledgerGraphQLErrorKind.ConcurrencyLimitReached,
                    _ => XledgerGraphQLErrorKind.Other
                };

                var msg = err["message"]!.ToString();
                var ex = new XledgerGraphQLException(errorKind, msg);
                if (err["extensions"] is JObject extensions
                    && extensions.ToObject<Dictionary<string, object>>() is Dictionary<string, object> extensionData) {
                    foreach ((var key, var value) in extensionData) {
                        ex.Data[key] = value;
                    }
                }
                throw ex;
            }

            return respObject;
        }
    }

    enum XledgerGraphQLErrorKind {
        Other = 0,
        ShortRateLimitReached = 1,
        InsufficientCredits = 2,
        ConcurrencyLimitReached = 3
    }

    class XledgerGraphQLException : ApplicationException {
        internal XledgerGraphQLErrorKind ErrorKind { get; }

        internal XledgerGraphQLException(
            XledgerGraphQLErrorKind errorKind,
            string message) : base(message) {
            ErrorKind = errorKind;
        }
    }
}
