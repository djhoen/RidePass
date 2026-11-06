using Services.Helpers.Interfaces;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <summary>
    /// Four statements, one per concern, for the bike shop "Today" sheet: the day's rentals, their
    /// lines, their package links, and the events they hang off. Never N+1: the follow-up
    /// statements take the day's rental ids as one array parameter.
    ///
    /// Every join re-asserts tenant_id rather than trusting the FK chain (one predicate per table
    /// is the rule in this codebase). shop_rental_line carries no tenant_id of its own, so lines
    /// are scoped through their rental.
    /// </summary>
    public class ShopDayRepository : IShopDayRepository
    {
        private readonly IDbHelper _db;
        public ShopDayRepository(IDbHelper db) => _db = db;

        public async Task<ShopDayRaw> GetDay(Guid tenantId, DateTime dayStartUtc, DateTime dayEndUtc, bool includeCancelled)
        {
            // Day membership, five ways:
            //   1. the rental window overlaps the day (half-open, the same overlap every
            //      availability check uses);
            //   2. the gear came back during the day, even if the window ended earlier (a late
            //      return belongs on the day it was actually taken in);
            //   3. the booking was taken during the day for a later start: the day's
            //      reservations, which is what the "reserved" phase and the Reserved tile count;
            //   4. overdue return: checked out, due back before the day began, still not in. It
            //      stays on every day's sheet until it comes back, so Returns due keeps chasing it;
            //   5. overdue pickup (a no-show): paid, never checked out, window already over. It
            //      still holds a reservation, so Pickups due keeps it in view until staff resolve it.
            // ShopDayMath flags 4 and 5 with overdue = true.
            // failed / abandoned never held capacity and never show; cancelled is opt-in.
            const string rentalSql = @"
                SELECT r.id AS Id, r.renter_user_id AS RenterUserId, r.renter_name AS RenterName,
                       r.renter_email AS RenterEmail, r.renter_phone AS RenterPhone,
                       r.starts_at AS StartsAt, r.ends_at AS EndsAt, r.status AS Status,
                       r.checked_out_at AS CheckedOutAt, r.returned_at AS ReturnedAt,
                       r.order_number AS OrderNumber, r.sold_by_user_id AS SoldByUserId,
                       r.payment_method AS PaymentMethod, r.event_id AS EventId,
                       r.total_cents AS TotalCents, r.deposit_cents AS DepositCents,
                       r.created_at AS CreatedAt
                FROM shop_rental r
                WHERE r.tenant_id = @tenantId
                  AND r.status NOT IN ('failed','abandoned')
                  AND (@includeCancelled OR r.status <> 'cancelled')
                  AND (
                        (r.starts_at < @dayEnd AND r.ends_at > @dayStart)
                     OR (r.returned_at >= @dayStart AND r.returned_at < @dayEnd)
                     OR (r.created_at >= @dayStart AND r.created_at < @dayEnd AND r.starts_at >= @dayEnd)
                     OR (r.status = 'out' AND r.ends_at < @dayStart)
                     OR (r.status = 'paid' AND r.checked_out_at IS NULL AND r.ends_at < @dayStart)
                  )
                ORDER BY r.starts_at, r.renter_name NULLS LAST, r.id";

            var raw = new ShopDayRaw();
            raw.Rentals = (await _db.Query<ShopDayRentalRow>(rentalSql,
                new { tenantId, dayStart = dayStartUtc, dayEnd = dayEndUtc, includeCancelled })).ToList();
            if (raw.Rentals.Count == 0) return raw;

            var rentalIds = raw.Rentals.Select(r => r.Id).ToArray();

            // Lines, with the assigned unit and the product's category. The catalog joins are LEFT
            // so a line never drops off the sheet because its variant was deactivated or its
            // product re-categorised since booking; the name snapshot on the line is what prints.
            const string lineSql = @"
                SELECT l.rental_id AS RentalId, l.name_snapshot AS NameSnapshot,
                       l.variant_label AS VariantLabel, l.quantity AS Quantity, l.item_id AS ItemId,
                       i.label AS UnitLabel, i.serial AS UnitSerial,
                       c.id AS ProductTypeId, c.name AS ProductTypeName
                FROM shop_rental_line l
                JOIN shop_rental r ON r.id = l.rental_id AND r.tenant_id = @tenantId
                LEFT JOIN shop_item i ON i.id = l.item_id AND i.tenant_id = @tenantId
                LEFT JOIN shop_variant v ON v.id = l.variant_id AND v.tenant_id = @tenantId
                LEFT JOIN shop_product p ON p.id = v.product_id AND p.tenant_id = @tenantId
                LEFT JOIN shop_category c ON c.id = p.category_id AND c.tenant_id = @tenantId
                WHERE l.rental_id = ANY(@rentalIds)
                ORDER BY l.rental_id, l.created_at, l.id";
            raw.Lines = (await _db.Query<ShopDayLineRow>(lineSql, new { tenantId, rentalIds })).ToList();

            // Package purchases that composed one of these rentals (Find Your Ride and the like).
            const string packageSql = @"
                SELECT pp.shop_rental_id AS RentalId, pp.package_id AS PackageId, pk.name AS PackageName,
                       pp.session_start_at AS SessionStartAt, pp.buyer_name AS BuyerName
                FROM package_purchase pp
                JOIN package_product pk ON pk.id = pp.package_id AND pk.tenant_id = @tenantId
                WHERE pp.tenant_id = @tenantId
                  AND pp.shop_rental_id = ANY(@rentalIds)
                ORDER BY pp.created_at, pp.id";
            raw.PackageLinks = (await _db.Query<ShopDayPackageLinkRow>(packageSql, new { tenantId, rentalIds })).ToList();

            // The events behind event-tied rentals, with their type (the group) in one read.
            var eventIds = raw.Rentals.Where(r => r.EventId is not null).Select(r => r.EventId!.Value).Distinct().ToArray();
            if (eventIds.Length > 0)
            {
                const string eventSql = @"
                    SELECT e.id AS EventId, e.title AS Title, e.starts_at AS StartsAt,
                           et.id AS EventTypeId, et.name AS EventTypeName, et.code AS EventTypeCode,
                           et.color AS Color, et.sort_order AS SortOrder
                    FROM event e
                    JOIN tenant_event_type et ON et.id = e.event_type_id AND et.tenant_id = @tenantId
                    WHERE e.tenant_id = @tenantId
                      AND e.id = ANY(@eventIds)";
                raw.Events = (await _db.Query<ShopDayEventRow>(eventSql, new { tenantId, eventIds })).ToList();
            }

            return raw;
        }
    }
}
