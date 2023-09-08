using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Webhooks.DB {
    static class SqliteExtensions {
        /// <summary>
        /// Like plain .AddWithValue, except adds Convert.DbNull instead of null.
        /// </summary>
        internal static SqliteParameter AddWithValue2(this SqliteParameterCollection @this, string paramName, object? value) {
            return @this.AddWithValue(paramName, value ?? Convert.DBNull);
        }

        /// <summary>
        /// Like plain .AddWithValue, except adds Convert.DbNull instead of null.
        /// </summary>
        internal static SqliteParameter AddWithValue2<T>(this SqliteParameterCollection @this, string paramName, T? value)
            where T : struct
        {
            return @this.AddWithValue(paramName, value ?? Convert.DBNull);
        }
    }
}
