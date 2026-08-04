using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    public interface IBikeShopRepository : ICatalogImporter
    {
        // ── Categories ────────────────────────────────────────────────────────────
        Task<List<ShopCategory>> ListCategories(Guid tenantId, bool activeOnly);
        Task<Guid> CreateCategory(ShopCategory c);
        Task<int> UpdateCategory(ShopCategory c);
        Task<int> DeleteCategory(Guid id, Guid tenantId);

        // ── Suppliers ─────────────────────────────────────────────────────────────
        Task<List<ShopSupplier>> ListSuppliers(Guid tenantId, bool activeOnly);
        Task<Guid> CreateSupplier(ShopSupplier s);
        Task<int> UpdateSupplier(ShopSupplier s);

        // ── Products + variants ───────────────────────────────────────────────────
        Task<List<ShopProductWithVariants>> ListProducts(Guid tenantId, bool activeOnly);

        /// <summary>
        /// One page of the catalog, filtered and searched, plus the total matching count and the
        /// header aggregates for the whole filtered set. Variants are hydrated for the page only.
        /// </summary>
        Task<ShopCatalogPage> SearchProducts(Guid tenantId, ShopProductQuery query);
        /// <summary>Resolve a scanned/typed code to one sellable variant: normalised barcode
        /// first, then SKU, then MPN. Null when nothing matches; never guesses.</summary>
        Task<ShopScanMatch?> ResolveVariantByCode(Guid tenantId, string code, string? gtin14);

        /// <summary>Links one of this tenant's variants to its entry in the shared parts library.
        /// The link is identity only; price, cost and stock stay entirely on the variant.</summary>
        Task<int> LinkVariantToPlatformPart(Guid variantId, Guid tenantId, Guid platformPartId);

        Task<ShopProductWithVariants?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(ShopProduct p);
        Task<int> UpdateProduct(ShopProduct p);

        // ── Product gallery (Script0230) ──────────────────────────────────────────
        // shop_product.image_url stays the cover; these are the ADDITIONAL photos.
        Task<List<ShopProductImage>> ListProductImages(Guid productId, Guid tenantId);

        /// <summary>Galleries for many products at once, keyed by product id, so the
        /// storefront catalog stays a fixed number of queries.</summary>
        Task<Dictionary<Guid, List<ShopProductImage>>> ListImagesForProducts(IEnumerable<Guid> productIds, Guid tenantId);
        Task<ShopProductImage?> GetProductImage(Guid imageId, Guid tenantId);
        Task<int> CountProductImages(Guid productId, Guid tenantId);

        /// <summary>Appends at max(sort_order)+10 when SortOrder is 0, so two admins
        /// uploading at once cannot collide on a client-guessed position.</summary>
        Task<ShopProductImage> AddProductImage(ShopProductImage image);
        Task<int> UpdateProductImageCaption(Guid imageId, Guid tenantId, string? caption);
        Task<int> DeleteProductImage(Guid imageId, Guid tenantId);
        Task ReorderProductImages(Guid productId, Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order);

        /// <summary>True when this blob is still referenced by a product cover or another
        /// gallery row in this tenant. Guards blob deletion, because "Make cover" copies
        /// a url rather than moving it.</summary>
        Task<bool> IsImageUrlReferenced(Guid tenantId, string imageUrl, Guid exceptImageId);

        Task<ShopVariant?> GetVariant(Guid id, Guid tenantId);
        Task<Guid> CreateVariant(ShopVariant v);
        Task<int> UpdateVariant(ShopVariant v);

        // ── Serialized items ──────────────────────────────────────────────────────
        Task<List<ShopItem>> ListItems(Guid variantId, Guid tenantId);
        Task<ShopItem?> GetItem(Guid id, Guid tenantId);
        Task<Guid> CreateItem(ShopItem i);
        Task<int> UpdateItem(ShopItem i);

        // ── Stock ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Adjusts a POOL variant's on-hand by <paramref name="delta"/> in a single atomic
        /// statement that writes the movement AND updates the cached count together. Returns the
        /// new on-hand, or null if the variant isn't a pool variant in this tenant. Rejects a
        /// negative result so stock can't be driven below zero.
        /// </summary>
        Task<int?> AdjustPoolStock(Guid variantId, Guid tenantId, int delta, string reason,
            string? note, Guid? byUserId, string? referenceKind = null, Guid? referenceId = null);

        Task<List<ShopStockMovement>> ListMovements(Guid variantId, Guid tenantId, int limit);

        /// <summary>Atomically claims variants newly at/below their low-stock threshold (one alert
        /// per low episode) and returns them with display names for the notification.</summary>
        Task<List<(string ProductName, string? VariantLabel, int Available)>> MarkAndGetNewlyLowShopStock(Guid tenantId);

        // ── Purchase orders ───────────────────────────────────────────────────────
        Task<List<ShopPurchaseOrder>> ListPurchaseOrders(Guid tenantId);
        Task<ShopPurchaseOrderWithLines?> GetPurchaseOrder(Guid id, Guid tenantId);
        Task<Guid> CreatePurchaseOrder(ShopPurchaseOrder po);
        /// <summary>Pool variants at or below their reorder point, with suggested top-up quantities.</summary>
        Task<List<ShopReorderRow>> GetReorderWorklist(Guid tenantId);
        /// <summary>Create a PO and its lines in one transaction (from the reorder worklist).</summary>
        Task<Guid?> CreatePurchaseOrderWithLines(Guid tenantId, Guid? supplierId, string? reference, DateTime? expectedAt, Guid? createdByUserId, IReadOnlyList<(Guid VariantId, int Qty, int? UnitCostCents)> lines);
        Task<int> UpdatePurchaseOrder(ShopPurchaseOrder po);
        Task<Guid> AddPurchaseOrderLine(ShopPoLine line, Guid tenantId);
        Task<ShopPoLine?> GetPurchaseOrderLine(Guid lineId, Guid tenantId);

        /// <summary>
        /// Receives <paramref name="quantity"/> units against a PO line, atomically: bumps the
        /// line's received count, writes the variant's cost, and for a POOL variant increments
        /// stock + writes a 'receive' movement, or for a SERIALIZED variant mints
        /// <paramref name="quantity"/> ShopItems (one movement each). Rolls the PO status forward
        /// to 'partial' / 'received'. For serialized lines, <paramref name="serialLabels"/> supplies
        /// each unit's label (and optional serial). Returns false if the line isn't in this tenant
        /// or the receipt would exceed what's ordered.
        /// </summary>
        Task<bool> ReceivePurchaseOrderLine(Guid lineId, Guid tenantId, int quantity,
            IReadOnlyList<(string Label, string? Serial)>? serialUnits, Guid? byUserId);

        // ── Tax categories ────────────────────────────────────────────────────────
        Task<List<ShopTaxCategory>> ListTaxCategories(Guid tenantId, bool activeOnly);
        Task<Guid> CreateTaxCategory(ShopTaxCategory c);
        Task<int> UpdateTaxCategory(ShopTaxCategory c);

        // ── Sales ─────────────────────────────────────────────────────────────────
        Task<(Guid Id, Guid ReceiptToken)> CreateSale(ShopSale sale, IEnumerable<ShopSaleLine> lines);
        Task<ShopSaleWithLines?> GetSale(Guid id, Guid tenantId);
        Task<ShopSale?> GetSaleByPaymentIntentId(string paymentIntentId);
        Task SetSalePaymentIntent(Guid id, string paymentIntentId);
        Task MarkSaleDirectCharge(Guid id, Guid tenantId, string connectedAccountId);

        /// <summary>Flips pending -> paid exactly once. Returns true only for the call that actually
        /// flipped it, so depletion + ledger run a single time under duplicate webhook/reconciler fires.</summary>
        Task<bool> TryMarkSalePaid(Guid id, Guid tenantId);
        /// <summary>Dead-payment flip from 'pending'. status is 'failed' (Stripe reported a
        /// declined attempt) or 'abandoned' (no attempt ever completed; reconciler only).</summary>
        Task MarkSaleFailed(Guid id, string status = "failed");
        Task<int> MarkSaleRefunded(Guid id, Guid tenantId, string? note);

        /// <summary>Reverses a sale's depletion when the goods came back: pool lines add stock,
        /// serialized lines return their unit to 'available'; each writes a 'sale_return' movement.
        /// Call only after <see cref="MarkSaleRefunded"/> flipped the sale (so it runs once).</summary>
        Task RestockForSale(Guid saleId, Guid tenantId, Guid? byUserId);

        /// <summary>Applies stock movements for a paid sale: pool lines decrement the cached count,
        /// serialized lines flip their unit to 'sold'; each writes a 'sale' movement referencing the
        /// sale. Call only after <see cref="TryMarkSalePaid"/> returned true.</summary>
        Task DepleteForSale(Guid saleId, Guid tenantId, Guid? byUserId);

        /// <summary>Ring-up pricing/availability for a set of variants, in one query.</summary>
        Task<List<ShopVariantSaleInfo>> GetVariantsForSale(IEnumerable<Guid> variantIds, Guid tenantId);

        /// <summary>Next per-tenant, per-local-day sale number (atomic upsert). Assigned on paid.</summary>
        Task<int> NextOrderNumber(Guid tenantId);
        Task SetSaleOrderNumber(Guid id, int orderNumber);

        // ── Inspections ───────────────────────────────────────────────────────────
        /// <summary>The tenant's default checklist, created on first use.</summary>
        Task<ShopInspectionTemplate> EnsureDefaultInspectionTemplate(Guid tenantId);
        Task<List<ShopInspectionTemplateItem>> ListTemplateItems(Guid templateId);
        // Template editing: the checklist differs by discipline (MX vs MTB) and by shop, so it is
        // data, not code.
        Task<List<ShopInspectionTemplate>> ListInspectionTemplates(Guid tenantId);
        Task<ShopInspectionTemplate?> GetInspectionTemplate(Guid id, Guid tenantId);
        Task<Guid> CreateInspectionTemplate(Guid tenantId, string name);
        Task<int> UpdateInspectionTemplate(Guid id, Guid tenantId, string name, bool isActive);
        Task SetDefaultInspectionTemplate(Guid id, Guid tenantId);
        Task<Guid> UpsertTemplateItem(ShopInspectionTemplateItem item, Guid tenantId);
        Task<int> DeleteTemplateItem(Guid itemId, Guid tenantId);
        Task<Guid> CreateInspection(ShopInspection insp, IEnumerable<ShopInspectionResult> results);
        Task<ShopInspectionWithResults?> GetInspection(Guid id, Guid tenantId);
        /// <summary>Every inspection on a bike, newest first.</summary>
        Task<List<ShopInspection>> ListInspectionsForBike(Guid bikeId, Guid tenantId);
        Task<int> UpdateInspectionHeader(Guid id, Guid tenantId, string status, DateTime? nextServiceDate, string? summaryNotes);
        Task SaveInspectionResults(Guid inspectionId, Guid tenantId, IEnumerable<(Guid Id, string Rating, string? Notes)> rows);

        // ── Customer bikes ────────────────────────────────────────────────────────
        Task<ShopCustomerBike?> GetCustomerBike(Guid id, Guid tenantId);
        /// <summary>The bike this serial belongs to, matched case-insensitively. Null when new.</summary>
        Task<ShopCustomerBike?> FindCustomerBikeBySerial(string serial, Guid tenantId);
        Task<List<ShopCustomerBike>> ListCustomerBikes(Guid tenantId, Guid? customerUserId, string? phone);
        Task<Guid> CreateCustomerBike(ShopCustomerBike b);
        Task<int> UpdateCustomerBike(ShopCustomerBike b);
        /// <summary>Every job on this bike, newest first.</summary>
        Task<List<ShopBikeHistoryRow>> ListBikeHistory(Guid bikeId, Guid tenantId, int limit = 50);
        /// <summary>A unit WE sold with this serial, for intake auto-fill. Null when we didn't.</summary>
        Task<ShopSoldUnitMatch?> FindSoldUnitBySerial(string serial, Guid tenantId);

        // ── Rentals ───────────────────────────────────────────────────────────────
        /// <summary>Units of a POOL variant free for the half-open window [startsAt, endsAt):
        /// fleet (on-hand + currently out) minus overlapping active reservations.</summary>
        Task<int> GetPoolAvailability(Guid variantId, Guid tenantId, DateTime startsAt, DateTime endsAt);

        /// <summary>Serialized units free for the window: rentable-fleet items (available or
        /// currently rented_out) with no overlapping active reservation.</summary>
        Task<List<ShopItem>> GetFreeSerializedUnits(Guid variantId, Guid tenantId, DateTime startsAt, DateTime endsAt);

        /// <summary>The whole rental fleet plus every reservation overlapping the window, for the
        /// Rental Board timeline. One call rather than a probe per variant.</summary>
        Task<ShopRentalBoard> GetRentalBoard(Guid tenantId, DateTime startsAt, DateTime endsAt, Guid? categoryId);

        /// <summary>Public signing page lookup by token (also tenant-scoped).</summary>
        Task<ShopRentalWithLines?> GetRentalBySignatureToken(Guid token, Guid tenantId);
        Task MarkRentalSignatureRequestSent(Guid rentalId, Guid tenantId);

        /// <summary>Links a captured waiver signature to a rental for the checkout gate.</summary>
        /// <summary>Records another signed rider against the rental (many per rental).</summary>
        Task<bool> AddRentalWaiverSignature(Guid rentalId, Guid tenantId, Guid signatureId);
        Task<int> CountRentalWaiverSignatures(Guid rentalId, Guid tenantId);
        Task<List<RentalSignerInfo>> ListRentalWaiverSigners(Guid rentalId, Guid tenantId);
        Task<bool> SetRentalRidersRequired(Guid rentalId, Guid tenantId, int ridersRequired);

        // ── Service notifications ────────────────────────────────────────────────
        /// <summary>Claims the ready-for-pickup notice exactly once.</summary>
        Task<bool> TryClaimReadyNotice(Guid workOrderId, Guid tenantId);
        /// <summary>Schedules the follow-up reminder at pickup; days = 0 clears it.</summary>
        Task ScheduleServiceReminder(Guid workOrderId, Guid tenantId, int days);
        Task<List<ShopWorkOrder>> ListDueServiceReminders(int take);
        /// <summary>Claims one due reminder for sending, exactly once.</summary>
        Task<bool> TryClaimServiceReminder(Guid workOrderId);

        // ── Job templates (saved standard repair jobs) ───────────────────────────
        Task<List<ShopJobTemplateWithLines>> ListJobTemplates(Guid tenantId, bool activeOnly);
        /// <summary>Creates or updates a template, replacing its lines wholesale.</summary>
        Task<Guid> SaveJobTemplate(ShopJobTemplate t, IEnumerable<ShopJobTemplateLine> lines);
        Task<int> DeleteJobTemplate(Guid id, Guid tenantId);
        /// <summary>Copies a template onto a work order, resolving CURRENT part prices and
        /// skipping inactive variants. Returns how many lines landed and what was skipped.</summary>
        Task<(int Added, List<string> Skipped)> ApplyJobTemplate(Guid templateId, Guid workOrderId, Guid tenantId);

        // ── Agreements (rental agreement / repair authorization) ─────────────────
        Task<ShopAgreement?> GetActiveAgreement(Guid tenantId, string kind);
        /// <summary>Publishes a new version and retires the previous one atomically.</summary>
        Task<Guid> PublishAgreement(Guid tenantId, string kind, string title, string body);
        /// <summary>Records a signature; null when the owner isn't this tenant's.</summary>
        Task<Guid?> AddAgreementSignature(ShopAgreementSignature sig);
        Task<List<ShopAgreementSignature>> ListAgreementSignatures(Guid? workOrderId, Guid? rentalId, Guid tenantId);
        /// <summary>Whether this rental is signed against the CURRENTLY active agreement.</summary>
        Task<bool> HasCurrentAgreementSignature(Guid rentalId, Guid tenantId, string kind);

        // ── Customer-facing counter display sessions ─────────────────────────────
        Task<Guid> CreateDisplay(Guid tenantId, string pairCode);
        Task<ShopDisplay?> GetDisplay(Guid id, Guid tenantId);
        Task<ShopDisplay?> GetDisplayByCode(string pairCode, Guid tenantId);
        /// <summary>Pushes a new snapshot; always clears any pending customer response.</summary>
        Task UpdateDisplayState(Guid id, Guid tenantId, string? stateJson);
        /// <summary>The customer's answer (signature + details), read back by the staff device.</summary>
        Task SetDisplayResponse(Guid id, Guid tenantId, string responseJson);

        // ── Condition photos (work orders + rentals) ─────────────────────────────
        /// <summary>Adds a photo; returns null when the owner isn't this tenant's.</summary>
        Task<Guid?> AddConditionPhoto(ShopConditionPhoto photo);
        Task<List<ShopConditionPhoto>> ListConditionPhotosForWorkOrder(Guid workOrderId, Guid tenantId);
        Task<List<ShopConditionPhoto>> ListConditionPhotosForRental(Guid rentalId, Guid tenantId);
        Task<int> CountConditionPhotos(Guid? workOrderId, Guid? rentalId, string stage, Guid tenantId);
        /// <summary>Deletes and returns the stored URL so the file can be removed too.</summary>
        Task<string?> DeleteConditionPhoto(Guid id, Guid tenantId);

        // Bikes offered with a lesson (shop_lesson_rentable), with per-lesson price overrides.
        Task<List<LessonRentableInfo>> ListLessonRentables(Guid eventId, Guid tenantId);
        Task<LessonRentableInfo?> GetLessonRentable(Guid eventId, Guid variantId, Guid tenantId);
        Task ReplaceLessonRentables(Guid eventId, Guid tenantId,
            IEnumerable<(Guid VariantId, int? PriceCentsOverride)> rows);

        Task<(Guid Id, Guid ReceiptToken)> CreateRental(ShopRental rental, IEnumerable<ShopRentalLine> lines);
        Task<ShopRentalWithLines?> GetRental(Guid id, Guid tenantId);
        Task<List<ShopRentalWithLines>> ListRentals(Guid tenantId, bool activeOnly, int limit);
        /// <summary>One filtered page of bookings (past and future) for the All Bookings screen.</summary>
        Task<ShopRentalPage> SearchRentals(Guid tenantId, ShopRentalQuery query);
        Task<List<ShopRentalWithLines>> ListRentalsForUser(Guid userId, Guid tenantId, int limit);
        Task<ShopRental?> GetRentalByFeePaymentIntentId(string paymentIntentId);
        Task<ShopRental?> GetRentalByDepositPaymentIntentId(string paymentIntentId);
        Task SetRentalPaymentIntent(Guid id, string paymentIntentId);
        Task SetRentalDepositIntent(Guid id, string paymentIntentId);
        Task MarkRentalDirectCharge(Guid id, Guid tenantId, string connectedAccountId);

        /// <summary>Flips pending -> paid exactly once (idempotent gate for the finalizer).</summary>
        Task<bool> TryMarkRentalPaid(Guid id, Guid tenantId);
        Task SetRentalOrderNumber(Guid id, int orderNumber);
        /// <summary>Dead-payment flip from 'pending'; status as on <see cref="MarkSaleFailed"/>.</summary>
        Task MarkRentalFailed(Guid id, string status = "failed");
        Task<int> CancelRental(Guid id, Guid tenantId);

        /// <summary>paid -> out: hands the gear over. Pool lines decrement stock, serialized units
        /// flip to rented_out; each writes a 'rental_out' movement. Returns false unless paid.</summary>
        Task<bool> CheckOutRental(Guid id, Guid tenantId, Guid? byUserId);

        /// <summary>out -> returned/damaged: gear back. Reverses the checkout stock moves with
        /// 'rental_return' movements and records condition + captured damage.</summary>
        Task<bool> ReturnRental(Guid id, Guid tenantId, Guid? byUserId, bool damaged,
            int depositCapturedCents, string? conditionNotes);

        // ── Work orders ───────────────────────────────────────────────────────────
        Task<Guid> CreateWorkOrder(ShopWorkOrder wo);
        Task<int> UpdateWorkOrder(ShopWorkOrder wo);
        Task<ShopWorkOrderWithLines?> GetWorkOrder(Guid id, Guid tenantId);
        Task<List<ShopWorkOrderWithLines>> ListWorkOrders(Guid tenantId, bool includeClosed, int limit);

        /// <summary>Adds a line. A part line on a committed (non-estimate) order consumes stock
        /// atomically (decrement + 'repair_consume' movement, consumed=true). Returns the line id,
        /// or null when the order/variant isn't in this tenant.</summary>
        Task<Guid?> AddWorkOrderLine(ShopWorkOrderLine line, Guid tenantId, Guid? byUserId);

        /// <summary>Removes a line, reversing its consumption (positive 'repair_consume' movement)
        /// when it had consumed stock.</summary>
        Task<int> RemoveWorkOrderLine(Guid lineId, Guid tenantId, Guid? byUserId);

        /// <summary>Record (non-null checker, stamps checked_at) or clear (null) the QC sign-off.</summary>
        Task<int> SetWorkOrderQcCheck(Guid workOrderId, Guid tenantId, Guid? checkedByUserId);

        // ── Labor time tracking ─────────────────────────────────────────────────────
        Task<int> StartWorkOrderTimer(Guid workOrderId, Guid tenantId);
        Task<int> StopWorkOrderTimer(Guid workOrderId, Guid tenantId);
        Task<int> SetWorkOrderActualMinutes(Guid workOrderId, Guid tenantId, int minutes);

        /// <summary>Set one work-order line's approval (pending | approved | declined).</summary>
        Task<int> SetLineApproval(Guid lineId, Guid tenantId, string status, Guid? byUserId);
        /// <summary>Approve every still-pending line on a work order.</summary>
        Task<int> ApproveAllPendingLines(Guid workOrderId, Guid tenantId, Guid? byUserId);

        // ── Work order statuses (tenant-customizable) ───────────────────────────────
        Task EnsureDefaultWorkOrderStatuses(Guid tenantId);
        Task<List<ShopWorkOrderStatus>> ListWorkOrderStatuses(Guid tenantId, bool activeOnly = false);
        Task<ShopWorkOrderStatus?> GetWorkOrderStatus(Guid id, Guid tenantId);
        Task UpdateWorkOrderStatusSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
        Task<ShopWorkOrderStatus?> CreateWorkOrderStatus(Guid tenantId, string code, string name, string color, bool notifyCustomer, int sortOrder);
        Task<int> UpdateWorkOrderStatusPresentation(Guid id, Guid tenantId, string name, string color, bool notifyCustomer, int sortOrder, bool isActive);
        Task<int> SetDefaultWorkOrderStatus(Guid id, Guid tenantId);
        Task<int> CountWorkOrdersInStatus(Guid tenantId, string code);
        Task<int> DeleteWorkOrderStatus(Guid id, Guid tenantId);

        // ── Customer visit grouping ─────────────────────────────────────────────────
        Task<List<ShopWorkOrderGroupMember>> ListGroupMembers(Guid groupId, Guid tenantId, Guid? excludeId = null);
        /// <summary>Return the order's visit group, creating one if it has none. Null if not this tenant's.</summary>
        Task<Guid?> EnsureWorkOrderGroup(Guid workOrderId, Guid tenantId);
        Task<bool> GroupExistsForTenant(Guid groupId, Guid tenantId);

        /// <summary>Internal notes thread for a work order, newest first, with author names.</summary>
        Task<List<ShopWorkOrderNote>> ListWorkOrderNotes(Guid workOrderId, Guid tenantId);
        /// <summary>Appends an internal note; null if the work order isn't this tenant's.</summary>
        Task<ShopWorkOrderNote?> AddWorkOrderNote(Guid workOrderId, Guid tenantId, string body, Guid? byUserId);

        /// <summary>A rental's internal note thread, newest first.</summary>
        Task<List<ShopRentalNote>> ListRentalNotes(Guid rentalId, Guid tenantId);

        /// <summary>Append a rental note. Null when the rental isn't this tenant's.</summary>
        Task<ShopRentalNote?> AddRentalNote(Guid rentalId, Guid tenantId, string body, Guid? byUserId);

        /// <summary>Note counts per rental, for badging a list without an N+1.</summary>
        Task<Dictionary<Guid, int>> CountRentalNotes(IEnumerable<Guid> rentalIds, Guid tenantId);

        /// <summary>Consumes every unconsumed part line (the estimate -> committed transition).</summary>
        Task ConsumePartsForWorkOrder(Guid workOrderId, Guid tenantId, Guid? byUserId);

        /// <summary>Reverses every consumed part line (cancellation).</summary>
        Task ReverseConsumedParts(Guid workOrderId, Guid tenantId, Guid? byUserId);

        Task SetWorkOrderSale(Guid workOrderId, Guid tenantId, Guid saleId);

        /// <summary>Marks the work order behind a bill-out sale picked up (called when that sale
        /// settles, from either payment path).</summary>
        Task MarkWorkOrderPickedUpBySale(Guid saleId);

        // ── Work order deposits ───────────────────────────────────────────────────
        /// <summary>Sets the deposit amount; refuses (0 rows) once paid or refunded.</summary>
        Task<int> SetWorkOrderDeposit(Guid workOrderId, Guid tenantId, int depositCents);
        Task MarkWorkOrderDepositRequestSent(Guid workOrderId, Guid tenantId);
        /// <summary>Public payment-link lookup: token + the resolved tenant.</summary>
        Task<ShopWorkOrderWithLines?> GetWorkOrderByDepositToken(Guid token, Guid tenantId);
        Task SetWorkOrderDepositIntent(Guid workOrderId, Guid tenantId, string piId, string? stripeAccountId);
        /// <summary>Drops a failed PI so the customer can retry; no-op once paid.</summary>
        Task ClearWorkOrderDepositIntent(Guid workOrderId, Guid tenantId);
        /// <summary>Idempotent paid flip; the winner books the ledger entry (mirrors TryMarkSalePaid).</summary>
        Task<bool> TryMarkWorkOrderDepositPaid(Guid workOrderId, Guid tenantId, string paymentMethod);
        Task<ShopWorkOrder?> GetWorkOrderByDepositPaymentIntentId(string paymentIntentId);
        /// <summary>Consumes part of the deposit (partial refund or conversion to store credit);
        /// compare-and-swap on the running refunded count; stamps deposit_refunded_at when the
        /// whole deposit has been returned. False when the CAS or cap guard rejects.</summary>
        Task<bool> TryAddWorkOrderDepositRefund(Guid workOrderId, Guid tenantId, int cents, int expectedRefundedBefore);

        // ── Special orders (work-order lines riding on supplier POs) ──────────────
        /// <summary>Points an un-arrived part line at a PO line, un-consuming it if it had
        /// already taken stock (the real consumption happens when the receipt lands).</summary>
        Task<bool> LinkWorkOrderLineToPoLine(Guid lineId, Guid tenantId, Guid poLineId);
        /// <summary>After a PO line receipt: stamps arrivals, consumes parts for committed jobs,
        /// advances awaiting_parts orders, and returns per-work-order notify info.</summary>
        Task<List<ShopWoArrival>> ProcessArrivalsForPoLine(Guid poLineId, Guid tenantId, Guid? byUserId);

        /// <summary>Stamps an online order collected; false unless it's a paid, un-collected
        /// online sale (idempotent gate).</summary>
        Task<bool> MarkSalePickedUp(Guid saleId, Guid tenantId);

        // ── Sales history ─────────────────────────────────────────────────────────
        /// <summary>
        /// Filtered, sorted, paged sales with money totals over the whole filtered set. Paged
        /// because sales only accumulate: a "most recent N" slice makes a lookup for an older
        /// sale look like the sale does not exist.
        /// </summary>
        Task<ShopSalesPage> SearchSales(Guid tenantId, ShopSaleQuery query);

        /// <summary>The signed-in rider's own shop purchases, for My Orders. Excludes repair
        /// bill-outs (parts and labor on a work order are not an "order") and pending rows
        /// (an abandoned checkout must never read as a purchase).</summary>
        Task<List<ShopSaleWithLines>> ListSalesForBuyer(Guid tenantId, Guid userId, int limit);

        // ── CSV import + variant matrix ───────────────────────────────────────────
        /// <summary>One-transaction validated catalog import; creates categories/suppliers by
        /// name and writes opening-stock adjustment movements.</summary>
        /// <summary>
        /// Commit a parsed CSV. Creates by default; with <see cref="ShopImportOptions.UpdateExisting"/>
        /// it matches rows to existing variants (barcode, then MPN, then SKU) and updates them in
        /// place, writing only the columns the file carried.
        /// </summary>
        // Declared on ICatalogImporter, which this inherits, so the distributor sync can depend
        // on just that one operation and be testable without faking ~180 members.

        /// <summary>Inserts each missing size/color combination for a product, skipping combos
        /// (or generated SKUs) that already exist. Returns (created, skipped).</summary>
        Task<(int Created, int Skipped)> GenerateVariants(Guid tenantId, Guid productId,
            IReadOnlyList<(string? Size, string? Color)> combos, string? skuPrefix,
            int? salePriceCents, int? costCents, int depositCents, int? lowStockThreshold);

        // ── Inventory reports ─────────────────────────────────────────────────────
        Task<List<ShopValuationRow>> GetValuationReport(Guid tenantId);
        Task<List<ShopSalesReportRow>> GetSalesReport(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        /// <summary>Per-job estimated-vs-actual labor time for jobs with any time recorded in a range.</summary>
        Task<List<ShopLaborTimeRow>> GetLaborTimeReport(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<ShopDeadStockRow>> GetDeadStockReport(Guid tenantId, DateTime cutoffUtc);

        /// <summary>A customer's whole shop footprint (sales, rentals, work orders) matched by
        /// account id, email, or phone, so walk-ins are findable by whatever they left.</summary>
        Task<(List<ShopSale> Sales, List<ShopRental> Rentals, List<ShopWorkOrder> WorkOrders)>
            GetCustomerHistory(Guid tenantId, Guid? userId, string? email, string? phone, int limit);

        // ── Stock takes (pool variants only) ──────────────────────────────────────
        /// <summary>Opens a count, snapshotting every active pool variant's on-hand as expected.</summary>
        Task<Guid> CreateStockCount(Guid tenantId, Guid? byUserId, string? notes);
        Task<List<ShopStockCount>> ListStockCounts(Guid tenantId, int limit);
        Task<ShopStockCountWithLines?> GetStockCount(Guid id, Guid tenantId);
        Task<int> SetStockCountLine(Guid lineId, Guid tenantId, int? countedQty);

        /// <summary>
        /// Applies a count: every counted line trues stock to the counted quantity via a
        /// 'stocktake' movement for the difference against the CURRENT on-hand (stock moves while
        /// you count), then closes the count. Uncounted lines are skipped. Returns false unless
        /// the count was open.
        /// </summary>
        Task<bool> CompleteStockCount(Guid id, Guid tenantId, Guid? byUserId);
        Task<int> CancelStockCount(Guid id, Guid tenantId);
    }
}
