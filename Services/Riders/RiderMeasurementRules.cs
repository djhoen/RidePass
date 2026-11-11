namespace Services.Riders
{
    /// <summary>
    /// Pure rules for a rider's height and weight (Script0317): stored as whole inches and whole pounds,
    /// entered as feet + inches and pounds. Used by online lesson checkout, the Gate Sale, the kiosk and
    /// the rider's profile, so every surface accepts and refuses the same numbers.
    /// </summary>
    public static class RiderMeasurementRules
    {
        public const int MinHeightIn = 24;   // 2 ft 0 in
        public const int MaxHeightIn = 96;   // 8 ft 0 in
        public const int MinWeightLb = 20;
        public const int MaxWeightLb = 450;

        /// <summary>tenant.kiosk_height_weight_mode values.</summary>
        public const string ModeOff = "off";
        public const string ModeOptional = "optional";
        public const string ModeRequired = "required";

        public static bool IsValidMode(string? mode) => mode is ModeOff or ModeOptional or ModeRequired;

        /// <summary>The stored mode, or off for anything unknown.</summary>
        public static string NormalizeMode(string? mode) => IsValidMode(mode) ? mode! : ModeOff;

        /// <summary>Feet + inches to total inches. Null when feet is missing; inches must be 0-11.</summary>
        public static int? ToInches(int? feet, int? inches)
        {
            if (feet is null) return null;
            var i = inches ?? 0;
            if (feet < 0 || i < 0 || i > 11) return null;
            return feet.Value * 12 + i;
        }

        /// <summary>Total inches to feet + inches (5 ft 7 in for 67).</summary>
        public static (int Feet, int Inches) FromInches(int totalInches) =>
            (totalInches / 12, totalInches % 12);

        public static bool IsValidHeight(int heightIn) => heightIn is >= MinHeightIn and <= MaxHeightIn;

        public static bool IsValidWeight(int weightLb) => weightLb is >= MinWeightLb and <= MaxWeightLb;

        /// <summary>
        /// Null when acceptable. Both or neither (a height without a weight can't size a bike); when
        /// <paramref name="required"/>, both. <paramref name="who"/> names the rider in the message
        /// ("Sam's"), or "your" when null.
        /// </summary>
        public static string? Validate(int? heightIn, int? weightLb, bool required, string? who = null)
        {
            var whose = string.IsNullOrWhiteSpace(who) ? "your" : $"{who.Trim()}'s";
            if (heightIn is null && weightLb is null)
                return required ? $"Enter {whose} height and weight so the shop can size the bike." : null;
            if (heightIn is null || weightLb is null)
                return $"Enter both {whose} height and weight.";
            if (!IsValidHeight(heightIn.Value))
                return $"Height must be between {Format(MinHeightIn)} and {Format(MaxHeightIn)}.";
            if (!IsValidWeight(weightLb.Value))
                return $"Weight must be between {MinWeightLb} and {MaxWeightLb} lb.";
            return null;
        }

        /// <summary>"5 ft 7 in".</summary>
        public static string Format(int heightIn)
        {
            var (ft, inch) = FromInches(heightIn);
            return $"{ft} ft {inch} in";
        }

        /// <summary>"5 ft 7 in, 150 lb", or null when either is unknown.</summary>
        public static string? Format(int? heightIn, int? weightLb) =>
            heightIn is int h && weightLb is int w ? $"{Format(h)}, {w} lb" : null;
    }
}
