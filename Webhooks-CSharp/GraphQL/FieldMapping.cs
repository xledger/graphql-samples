using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Webhooks.Utils;

namespace Webhooks.GraphQL {
    class FieldMapping {
        internal delegate void SqlParameterSetter(SqliteParameter parameter, object value);
        internal delegate object? GraphQLValueReader(JObject node, string xledgerGraphQLFieldName);

        internal string Name { get; }
        internal GraphQLValueReader ReadValueFromGraphQLNode { get; }
        internal SqlParameterSetter SetSqlParameter { get; }
        internal SqliteType SqliteType { get; }

        internal FieldMapping(
            string name,
            SqliteType sqliteType,
            GraphQLValueReader valueReader,
            SqlParameterSetter paramSetter
        ) {
            Name = name;
            SqliteType = sqliteType;
            ReadValueFromGraphQLNode = valueReader;
            SetSqlParameter = paramSetter;
        }

        public override string ToString() {
            return $"FieldMapping({Name})";
        }

        internal static readonly FieldMapping String = new(
            name: nameof(String),
            sqliteType: SqliteType.Text,
            valueReader: (JObject node, string xledgerGraphQLFieldName) =>
                node[xledgerGraphQLFieldName]?.ToObject<string>(),
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping Boolean = new(
            name: nameof(Boolean),
            sqliteType: SqliteType.Integer,
            valueReader: (JObject node, string xledgerGraphQLFieldName) =>
                node[xledgerGraphQLFieldName]?.ToObject<bool>(),
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping Int = new(
            name: nameof(Int),
            sqliteType: SqliteType.Integer,
            valueReader: (JObject node, string xledgerGraphQLFieldName) =>
                node[xledgerGraphQLFieldName]?.ToObject<int>(),
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping Int64String = new(
            name: nameof(Int64String),
            sqliteType: SqliteType.Integer,
            valueReader: (JObject node, string xledgerGraphQLFieldName) => {
                var s = node[xledgerGraphQLFieldName]?.ToObject<string>();
                if (s is null) { return null; }
                return long.Parse(s, CultureInfo.InvariantCulture);
            },
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping Float = new(
            name: nameof(Float),
            sqliteType: SqliteType.Real,
            valueReader: (JObject node, string xledgerGraphQLFieldName) =>
                node[xledgerGraphQLFieldName]?.ToObject<float>(),
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping MoneyString = new(
            name: nameof(MoneyString),
            sqliteType: SqliteType.Real,
            valueReader: (JObject node, string xledgerGraphQLFieldName) => {
                var s = node[xledgerGraphQLFieldName]?.ToObject<string>();
                if (s is null) { return null; }
                return decimal.Parse(s, CultureInfo.InvariantCulture);
            },
            paramSetter: (p, v) => p.Value = v
        );

        internal static readonly FieldMapping Date = new(
            name: nameof(Date),
            sqliteType: SqliteType.Integer,
            valueReader: (JObject node, string xledgerGraphQLFieldName) => {
                var s = node[xledgerGraphQLFieldName]?.ToObject<string>();
                return s is not null
                    ? System.DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;
            },
            paramSetter: (p, v) => p.Value = Dates.DateToJulian((DateTime)v)
        );

        internal static readonly FieldMapping CET_DateTime = new(
            name: nameof(CET_DateTime),
            sqliteType: SqliteType.Real,
            valueReader: (JObject node, string xledgerGraphQLFieldName) => {
                var s = node[xledgerGraphQLFieldName]?.ToObject<string>();
                if (s is null) {
                    return null;
                }
                var dt = System.DateTime.ParseExact(s, Dates.ISO_8601_DateTimeFormat, CultureInfo.InvariantCulture);
                return TimeZoneInfo.ConvertTimeToUtc(dt, Dates.CETZone);
            },
            paramSetter: (p, v) => p.Value = Dates.DateTimeToJulian((DateTime)v)
        );

        internal static readonly FieldMapping DateTime = new(
            name: nameof(DateTime),
            sqliteType: SqliteType.Real,
            valueReader: (JObject node, string xledgerGraphQLFieldName) => {
                var s = node[xledgerGraphQLFieldName]?.ToObject<string>();
                if (s is null) {
                    return null;
                }
                return System.DateTime.ParseExact(s, Dates.ISO_8601_DateTimeFormat, CultureInfo.InvariantCulture);
            },
            paramSetter: (p, v) => p.Value = Dates.DateTimeToJulian((DateTime)v)
        );
    }
}
