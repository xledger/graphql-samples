﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}