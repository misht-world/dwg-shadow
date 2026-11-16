using System;

namespace DwgShadow.Core
{
    /// <summary>Position of the sun in the sky.</summary>
    public struct SunState
    {
        /// <summary>Sun azimuth, degrees from north clockwise (0 = north, 90 = east, 180 = south).</summary>
        public double AzimuthDeg;

        /// <summary>Sun altitude above the horizon including refraction, degrees.</summary>
        public double AltitudeDeg;

        public override string ToString() => $"azimuth {AzimuthDeg:0.0}°, altitude {AltitudeDeg:0.0}°";
    }

    /// <summary>
    /// Sun position by the NOAA algorithm (accuracy about 0.01–0.1° for 1800–2100).
    /// Works offline.
    /// </summary>
    public static class SunPosition
    {
        const double Deg = Math.PI / 180.0;

        /// <param name="latitudeDeg">Latitude, degrees (north +).</param>
        /// <param name="longitudeDeg">Longitude, degrees (east +).</param>
        /// <param name="localTime">Local standard time (Kind is ignored).</param>
        /// <param name="utcOffsetHours">Time zone offset from UTC, hours (Moscow = +3).</param>
        public static SunState Compute(double latitudeDeg, double longitudeDeg, DateTime localTime, double utcOffsetHours)
        {
            DateTime utc = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified).AddHours(-utcOffsetHours);

            double jd = JulianDay(utc);
            double t = (jd - 2451545.0) / 36525.0; // Julian centuries since J2000

            double l0 = Normalize360(280.46646 + t * (36000.76983 + t * 0.0003032));
            double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

            double c = Math.Sin(m * Deg) * (1.914602 - t * (0.004817 + 0.000014 * t))
                     + Math.Sin(2 * m * Deg) * (0.019993 - 0.000101 * t)
                     + Math.Sin(3 * m * Deg) * 0.000289;

            double trueLong = l0 + c;
            double omega = 125.04 - 1934.136 * t;
            double lambda = trueLong - 0.00569 - 0.00478 * Math.Sin(omega * Deg);

            double eps0 = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
            double eps = eps0 + 0.00256 * Math.Cos(omega * Deg);

            double decl = Math.Asin(Math.Sin(eps * Deg) * Math.Sin(lambda * Deg)); // radians

            double y = Math.Tan(eps * Deg / 2);
            y *= y;
            double eqTimeMin = 4.0 / Deg * (
                  y * Math.Sin(2 * l0 * Deg)
                - 2 * e * Math.Sin(m * Deg)
                + 4 * e * y * Math.Sin(m * Deg) * Math.Cos(2 * l0 * Deg)
                - 0.5 * y * y * Math.Sin(4 * l0 * Deg)
                - 1.25 * e * e * Math.Sin(2 * m * Deg));

            double utcMinutes = utc.TimeOfDay.TotalMinutes;
            double trueSolarTime = utcMinutes + eqTimeMin + 4.0 * longitudeDeg;
            trueSolarTime = ((trueSolarTime % 1440) + 1440) % 1440;

            double hourAngle = trueSolarTime / 4.0 - 180.0; // degrees
            double lat = latitudeDeg * Deg;
            double ha = hourAngle * Deg;

            double cosZen = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
            cosZen = Math.Max(-1.0, Math.Min(1.0, cosZen));
            double zenith = Math.Acos(cosZen) / Deg;
            double elevation = 90.0 - zenith;
            elevation += Refraction(elevation);

            // Azimuth from north, clockwise
            double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat)) / Deg + 180.0;
            az = Normalize360(az);

            return new SunState { AzimuthDeg = az, AltitudeDeg = elevation };
        }

        /// <summary>Atmospheric refraction (altitude correction), degrees. NOAA formula.</summary>
        static double Refraction(double elevationDeg)
        {
            if (elevationDeg > 85.0) return 0.0;
            double te = Math.Tan(elevationDeg * Deg);
            double arcsec;
            if (elevationDeg > 5.0)
                arcsec = 58.1 / te - 0.07 / (te * te * te) + 0.000086 / Math.Pow(te, 5);
            else if (elevationDeg > -0.575)
                arcsec = 1735.0 + elevationDeg * (-518.2 + elevationDeg * (103.4 + elevationDeg * (-12.79 + elevationDeg * 0.711)));
            else
                arcsec = -20.774 / te;
            return arcsec / 3600.0;
        }

        static double JulianDay(DateTime utc)
        {
            int year = utc.Year, month = utc.Month;
            double day = utc.Day + utc.TimeOfDay.TotalDays;
            if (month <= 2) { year -= 1; month += 12; }
            int a = year / 100;
            int b = 2 - a + a / 4;
            return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + b - 1524.5;
        }

        static double Normalize360(double a) => ((a % 360.0) + 360.0) % 360.0;
    }
}
