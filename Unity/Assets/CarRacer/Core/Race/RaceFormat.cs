namespace CarRacer.Core.Race
{
    /// <summary>How race positions and times read on screen.</summary>
    public static class RaceFormat
    {
        /// <summary>1 → "1st", 2 → "2nd", 11 → "11th", 22 → "22nd".</summary>
        public static string Ordinal(int n)
        {
            int lastTwo = n % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return n + "th";
            }

            switch (n % 10)
            {
                case 1:
                    return n + "st";
                case 2:
                    return n + "nd";
                case 3:
                    return n + "rd";
                default:
                    return n + "th";
            }
        }

        /// <summary>A clock in whole seconds as minutes:seconds, e.g. 65400 ms → "1:05".</summary>
        public static string Clock(long ms)
        {
            if (ms < 0)
            {
                ms = 0;
            }

            return $"{ms / 60000}:{ms / 1000 % 60:00}";
        }

        /// <summary>Race time as minutes:seconds.hundredths, e.g. 83456 ms → "1:23.45".</summary>
        public static string Time(long ms)
        {
            if (ms < 0)
            {
                ms = 0;
            }

            return $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000 / 10:00}";
        }
    }
}
