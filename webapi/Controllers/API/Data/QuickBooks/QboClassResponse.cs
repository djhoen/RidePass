namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>One Class from the tenant's QuickBooks company, for the profit-center dropdowns.</summary>
    public class QboClassResponse
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        /// <summary>"Parent:Child" when classes are nested; the same as Name when they aren't.</summary>
        public string FullyQualifiedName { get; set; } = null!;
    }
}
