namespace Services.LoamPassMx
{
    /// <summary>A linked LoamMx (LoamPassMx) rider account, returned after code verification.</summary>
    public class LoamPassAccount
    {
        public string AccountId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string DisplayName { get; set; } = "";
    }
}
