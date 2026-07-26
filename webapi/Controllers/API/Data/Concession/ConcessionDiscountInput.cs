namespace webapi.Controllers.API.Data.Concession
{
    // A discount or comp the cashier applied, at either the line or the order level. The server always
    // recomputes the cents from Kind + the referenced config and never trusts a client-sent amount:
    //   'preset'      -> PresetId resolves to a tenant discount preset (percent/amount). No manager PIN.
    //   'percent'     -> Percent (basis points) off. Manual; needs a manager PIN per tenant setting.
    //   'amount'      -> AmountCents off. Manual; needs a manager PIN per tenant setting.
    //   'comp'        -> CompReasonId resolves to a comp reason (full/percent/amount). Always needs a PIN.
    //   'season_pass' -> member perk from settings; requires a verified Season Pass holder (CustomerEmailOrPhone).
    //   'loampass'    -> member perk from settings; requires a verified LoamPass holder (CustomerEmailOrPhone).
    public class ConcessionDiscountInput
    {
        public string Kind { get; set; } = "";
        public Guid? PresetId { get; set; }
        public int? Percent { get; set; }        // basis points: 1500 = 15%
        public int? AmountCents { get; set; }
        public Guid? CompReasonId { get; set; }
        // Member perks verify this customer (email or phone); the resolved customer is snapshotted on the sale.
        public string? CustomerEmailOrPhone { get; set; }

    }
}
