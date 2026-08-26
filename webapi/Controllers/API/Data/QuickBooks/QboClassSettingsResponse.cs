namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>
    /// Everything the profit-center tab needs in one call: whether the company tracks classes at
    /// all, whether it does so per line (which is what a journal entry needs), and the classes
    /// themselves. Bundled because a class list without the preference behind it is misleading, an
    /// empty list reads as "you have no classes" when the real answer is "class tracking is off".
    /// </summary>
    public class QboClassSettingsResponse
    {
        public bool TrackingEnabled { get; set; }
        public bool TrackingPerLine { get; set; }
        public List<QboClassResponse> Classes { get; set; } = new();
    }
}
