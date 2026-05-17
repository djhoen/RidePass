namespace webapi.Helpers
{
    public static class WaiverPolicy
    {
        /// <summary>
        /// True when the user has a birthdate on file and is under 18 in UTC. Legacy users
        /// without a birthdate are treated as adults so existing accounts keep working;
        /// the next signup or counter visit will collect a DOB.
        /// </summary>
        public static bool IsMinor(DateTime? birthdate)
        {
            if (!birthdate.HasValue) return false;
            var today = DateTime.UtcNow.Date;
            var age = today.Year - birthdate.Value.Year;
            if (birthdate.Value.Date > today.AddYears(-age)) age--;
            return age < 18;
        }
    }
}
