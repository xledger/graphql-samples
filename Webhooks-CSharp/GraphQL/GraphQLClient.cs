using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            jr.DateParseHandling = DateParseHandling.None;

            var js = new JsonSerializer();
            var respObject = js.Deserialize<JObject>(jr)!;

            foreach (var err in respObject.SelectTokens("$.errors[*]")) {
                var msg = err["message"]!.ToString();
                var errorKind = XledgerGraphQLErrorKind.Other;
                if (msg.StartsWith("Too many requests in the last 5 seconds.")) {
                    errorKind = XledgerGraphQLErrorKind.ShortRateLimitReached;
                } else if (msg.StartsWith("Insufficient credits.")) {
                    errorKind = XledgerGraphQLErrorKind.InsufficientCredits;
                }
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
        InsufficientCredits = 2
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
