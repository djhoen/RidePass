# Bike shop catalog: benchmark vs leading systems

Comparison of the RidePass bike-shop product catalog against the systems that dominate
independent bike shops. Researched 2026-07-19 from vendor documentation (sources inline).
Our side was verified against the schema and screens, not assumed.

Systems compared: **Lightspeed Retail R-Series** (LS-R) and **X-Series** (LS-X), **Ascend RMS**
(Trek's POS), **Rain POS**, **Shopmonkey** (SM), **RepairDesk** (RD), **Square for Retail**, **Shopify POS**.

## Verdict

The catalog **data model** is mid-tier and respectable. The **products screen** is the weak link.
The thing that actually keeps shops on Lightspeed and Ascend is neither: it is **distributor
catalog integration**.

## Where we already meet table stakes

- SKU + barcode, uniqueness enforced per tenant (case-insensitive SKU)
- Category tree (one level of nesting), suppliers, brand
- Cost and sale price; rental daily rate + deposit on the same variant
- Size / color / gender variants, per-variant SKU and stock, plus a matrix builder
- Purchase orders with receiving
- **Serial capture at receiving** (not just at sale). This is explicitly Ascend's documented best
  practice and the thing generic retail POS gets wrong for bikes. We already do it.
- Work orders can attach to one of our own serialized units (fleet service)
- Stock takes / counts, append-only stock movement ledger, CSV import

## Gaps vs leaders

| Gap | What leaders do |
|---|---|
| No product-list search | LS-R: one box accepting name, UPC, EAN, custom SKU, or manufacturer SKU |
| No barcode label printing | LS-R prints individually, from a PO, or via a batched label queue with templates |
| No MSRP field | LS-R separates Default Cost, Vendor Cost, MSRP, Price as four fields |
| No MPN / vendor part number | Ascend uses VPN as a primary catalog search key; LS-R has Manufacturer SKU + Vendor ID |
| No reorder point / level | LS-R stores reorder point *and* level per store. Ours is only an alert threshold |
| No ecommerce publish toggle | Ascend has a per-product eCommerce checkbox. We publish everything active + sellable + priced |
| No bin / shelf location | Ascend's `Space` field |
| Single-location stock | LS-R tracks quantity per store. Lower priority if a track is one location |

List-screen problems compound: no search, no filters, no sorting, no pagination (the endpoint
returns the whole catalog), no bulk edit, and the `low_stock_threshold` we already store is never
surfaced.

## The real moat: distributor integration

- **LS-R has 12 bicycle vendor integrations**, tiered: nightly catalog sync for all; real-time stock
  check for Giant, Hawley, QBP; full PO upload + electronic invoice download for Specialized, J&B,
  QBP, Hawley. Plus built-in NuORDER spanning 200+ vendors.
- **Ascend** has Trek B2B and QBP (its QBP integration is weaker: pushes to your QBP web cart,
  final submit is manual, cart sync lags up to 30 minutes).

If we ever build this, copy **Ascend's on-demand model, not full-catalog import**: search a supplier
by description / VPN / UPC and pull in up to 100 records, with description and MSRP overridable.
Far cheaper to build and a better experience than ingesting millions of junk SKUs.

## Other bike-specific gaps

- **eBike component serials**: Ascend captures motor, battery, computer, key separately. eBikes are
  a growing share of shop revenue.
- **Warranty / theft registry push**: Ascend registers Trek bikes at point of sale and pushes to
  Bike Index and Project 529. Both are free public registries. Cheap, high goodwill.
- **Bike build queue** as a first-class state between sold and delivered (Ascend: "Not Assembled
  Sold", technician assignment, assembly logged against the serial).

## Where we are ahead

- **Rentals are a first-class inventory type on the same variant as retail** (sale price, daily rate
  and deposit together), backed by the time-window reservation engine. Ascend has a rental flag and
  Rain markets rentals; Lightspeed, Square and Shopify effectively do not do rentals. For a business
  that rents and sells the same bikes this is real differentiation.
- **Breadth**: retail + rentals + service work orders + agreements + condition photos + service
  reminders + events/lessons in one system. Nothing in the competitive set spans events.
- Append-only stock movement ledger.

## Ideas worth stealing

1. **Unified scan-or-type search** (LS-R). Highest value, lowest effort.
2. **List header aggregates** (RepairDesk): stock retail value, cost value, low-stock count, units on PO.
3. **Cost-banded pricing matrix** (Shopmonkey): auto-compute retail from cost by margin band.
   Rare in retail POS, very well suited to parts.
4. **On-demand vendor catalog** (Ascend), described above.
5. **Stock forecasting that auto-drafts POs** (Square Plus): projects 14 days from trailing 30-day average.

## Priority

**Tier 1 (days, fixes the actual complaint). DONE 2026-07-19.** Search (name/brand/SKU/barcode in one
box), category/supplier/active/low-stock filters and server-side paging on the product list; low
stock surfaced from the threshold we already stored; cost and margin columns on the variant rows;
RepairDesk-style header aggregates (stock at retail, stock at cost, margin if sold, low-stock count,
units on order) computed over the whole filtered set. Rentable products moved to their own tab on
the Rentals page with per-window availability, a booking schedule, and start-rental.
*Still open from tier 1: product image thumbnail and barcode column on the list.*

**Tier 2 (weeks).** MSRP and MPN/VPN fields; reorder point and level; barcode label printing;
ecommerce publish toggle.

**Tier 3 (strategic, only with a business case).** Distributor catalog integration; eBike component
serials; warranty/theft registry push; bike build queue.

## Not gaps (corrected)

**Model year, frame size, and wheel size as structured fields exist on no system in this set**,
including Ascend and Lightspeed. The industry encodes them in the description or in matrix
attributes. An earlier draft wrongly listed these as gaps.

## Caveats on sourcing

- LS-R and Ascend claims come from vendor pages fetched in full.
- **LS X-Series specifics came from the search index**, not fetched pages (those URLs 403 automated fetch).
- **Unverified, treat as unknown rather than absent**: customer-group / tier pricing on any system;
  ASN / EDI / drop-ship anywhere in this set; matrix/variant support in Ascend and Rain.
- Rain is the least publicly documented of the six.
- The "Trek B2B ordering" depth claim came from a third-party review site, not Trek/Ascend primary docs.

### Key sources

- LS-R item import + SKU/barcode types: <https://retail-support.lightspeedhq.com/hc/en-us/articles/115004963387-Importing-and-updating-items-using-a-spreadsheet>, <https://retail-support.lightspeedhq.com/hc/en-us/articles/30950332908699-Adding-and-editing-scannable-barcodes>
- LS-R bicycle vendor integrations: <https://retail-support.lightspeedhq.com/hc/en-us/articles/228840347-About-bicycle-integrations>
- LS-R label printing: <https://retail-support.lightspeedhq.com/hc/en-us/articles/228842627-Printing-labels>
- Ascend serialization (incl. eBike fields): <https://help.ascendrms.com/serialization>
- Ascend cloud product catalog: <https://help.ascendrms.com/products/using-the-cloud-product-catalog>
- Ascend ecommerce toggle: <https://help.ascendrms.com/en_US/331645-ecommerce/get-started-with-ecommerce-integration>
- Ascend bike builds: <https://help.ascendrms.com/en_US/service-center/assemble-a-product-for-a-customer-bike-builds>
- Ascend Bike Index / Project 529: <https://help.ascendrms.com/send-purchase-and-registration-information-to-bike-index>
- Shopmonkey pricing matrix: <https://support.shopmonkey.io/hc/en-us/articles/38743262414100-Add-a-Pricing-Matrix>
- Square GTINs / inventory: <https://squareup.com/help/us/en/article/7176-using-gtins-with-square-for-retail>, <https://squareup.com/us/en/point-of-sale/features/inventory-management>
- RepairDesk variants: <https://help.repairdesk.co/portal/en/kb/articles/product-attributes-and-variants>
