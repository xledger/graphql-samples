using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Webhooks.DB {
    class MigrationFile {
        internal int VersionNumber { get; init; }
        internal string ResourceName { get; init; }
        readonly Lazy<string> sql;

        internal string Sql => sql.Value;

        MigrationFile(int versionNumber, string resourceName) {
            VersionNumber = versionNumber;
            ResourceName = resourceName;
            sql = new(() => {
                var asm = typeof(MigrationFile).Assembly;
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream is null) { return ""; }
                using var sr = new StreamReader(stream);
                return sr.ReadToEnd();
            });
        }

        public override string ToString() {
            return $"MigrationFile({VersionNumber}, \"{ResourceName}\")";
        }

        public static IReadOnlyList<MigrationFile> ReadAll() {
            var files = new List<MigrationFile>();

            var asm = typeof(MigrationFile).Assembly;

            foreach (var resourceName in asm.GetManifestResourceNames()) {
                var match = Regex.Match(resourceName, @"^Webhooks\.DB\.Migrations\.(\d+).+$");
                if (!match.Success) {
                    continue;
                }
                var versionNumber = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                files.Add(new MigrationFile(versionNumber, resourceName));
            }

            files.Sort((a, b) => a.VersionNumber.CompareTo(b.VersionNumber));

            return files;
        }
    }
}
