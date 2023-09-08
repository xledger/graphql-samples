namespace Webhooks.Utils {
    static class Dates {
        const double OADateEpochInJulian = 2415018.5;

        internal static DateTime JulianToDateTime(double julianDate) {
            return DateTime.FromOADate(julianDate - OADateEpochInJulian);
        }

        internal static double DateTimeToJulian(DateTime dateTime) {
            return dateTime.ToOADate() + OADateEpochInJulian;
        }

        internal static int DateToJulian(DateTime dateTime) {
            return (int)Math.Floor(dateTime.ToOADate() + OADateEpochInJulian);
        }

        internal static readonly TimeZoneInfo CETZone =
            TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

        internal const string ISO_8601_DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

        internal static DateTime UtcToCET(DateTime dt) =>
            TimeZoneInfo.ConvertTimeFromUtc(dt, CETZone);
    }
}
