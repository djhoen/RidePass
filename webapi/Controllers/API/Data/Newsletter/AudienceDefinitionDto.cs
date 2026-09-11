namespace webapi.Controllers.API.Data.Newsletter
{
    public class AudienceDefinitionDto
    {
        /// <summary>everyone | subscribers | customers</summary>
        public string Base { get; set; } = "everyone";
        /// <summary>all (every rule must hold) | any (one is enough)</summary>
        public string Match { get; set; } = "all";
        public List<AudienceRuleDto> Rules { get; set; } = new();
    }
}
