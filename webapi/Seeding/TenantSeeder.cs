using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Storage;

namespace webapi.Seeding
{
    // Populates a tenant with realistic demo data for STAGE + LOCAL. Everything is tenant-scoped and
    // uses the app's real tables. Not meant to be pretty — it's demo data — but the FKs are consistent
    // and every past racer gets a linked waiver signature (a hard requirement for the gate/report demo).
    public class TenantSeeder : ITenantSeeder
    {
        private readonly IDbHelper _db;
        private readonly IImageStorage _images;
        private readonly IPasswordHasher<User> _hasher;
        private readonly IConcessionRepository _concessions;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TenantSeeder> _logger;

        // Deterministic-ish randomness; a fixed seed keeps repeated stage seeds comparable.
        private readonly Random _rng = new(20260707);

        // The whole seed runs inside ONE transaction (opened in PopulateAsync) so a failure
        // partway through rolls the entire thing back instead of leaving orphan rows that
        // break the next retry. These hold the active connection/transaction; every seed
        // statement goes through Exec/Q below so it enlists in that transaction. The seeder
        // is registered Scoped, so these instance fields are per-request (no cross-run clash).
        private NpgsqlConnection? _conn;
        private NpgsqlTransaction? _tx;

        private Task<int> Exec(string sql, object? param = null) =>
            _conn!.ExecuteAsync(sql, param, _tx);

        private async Task<IEnumerable<T>> Q<T>(string sql, object? param = null) =>
            await _conn!.QueryAsync<T>(sql, param, _tx);

        public TenantSeeder(
            IDbHelper db,
            IImageStorage images,
            IPasswordHasher<User> hasher,
            IConcessionRepository concessions,
            IWebHostEnvironment env,
            ILogger<TenantSeeder> logger)
        {
            _db = db;
            _images = images;
            _hasher = hasher;
            _concessions = concessions;
            _env = env;
            _logger = logger;
        }

        private static readonly string[] FirstNames =
        {
            "Cody", "Hayden", "Brooke", "Mason", "Riley", "Jordan", "Casey", "Tanner", "Skylar", "Dakota",
            "Chase", "Peyton", "Morgan", "Bailey", "Logan", "Avery", "Parker", "Reese", "Quinn", "Harley",
            "Emerson", "Rowan", "Sawyer", "Kendall", "Blake"
        };
        private static readonly string[] LastNames =
        {
            "Reed", "Vance", "Holt", "Marsh", "Boone", "Cross", "Nash", "Pratt", "Shaw", "Wells",
            "Frost", "Lane", "Payne", "Rhodes", "Sloan", "Tate", "Webb", "York", "Beck", "Dunn"
        };
        private const string SeedPassword = "test"; // every seeded user logs in with "test"

        public async Task<TenantSeedSummary> PopulateAsync(Guid tenantId, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_db.ConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            _conn = conn;
            _tx = tx;
            try
            {
                var s = await SeedAllAsync(tenantId, ct);
                await tx.CommitAsync(ct);
                return s;
            }
            catch
            {
                // Any failure rolls back the whole seed (tx disposal rolls back if not committed),
                // so a partial run never leaves orphan rows to collide with the next attempt.
                _logger.LogError("Seeding failed for tenant {TenantId}; rolling back all demo data.", tenantId);
                throw;
            }
            finally
            {
                _conn = null;
                _tx = null;
            }
        }

        private async Task<TenantSeedSummary> SeedAllAsync(Guid tenantId, CancellationToken ct)
        {
            var s = new TenantSeedSummary();
            var passwordHash = _hasher.HashPassword(new User(), SeedPassword);

            _logger.LogInformation("Seeding demo data for tenant {TenantId}", tenantId);

            // Self-heal: clear any seed users left by an earlier (pre-transaction) partial run for
            // this tenant so their unique emails don't collide. Tenant-scoped staff and this tenant's
            // slug-tagged global riders only; nothing else's data is touched.
            await Exec("DELETE FROM users WHERE tenant_id = @tenantId AND email LIKE '%@seed.ridepass.io'",
                new { tenantId });
            await Exec("DELETE FROM users WHERE tenant_id IS NULL AND email LIKE @pattern",
                new { pattern = $"%.{tenantId.ToString("N").Substring(0, 8)}@seed.ridepass.io" });

            var waiverId = (await Q<Guid>(
                "SELECT id FROM tenant_waiver WHERE tenant_id = @tenantId AND is_active = true ORDER BY created_at DESC LIMIT 1",
                new { tenantId })).FirstOrDefault();

            var imgs = await StoreImagesAsync(tenantId, ct);
            await SeedBrandingAsync(tenantId, imgs);

            var staff = await SeedStaffAsync(tenantId, passwordHash);
            s.Staff = staff.Count;
            var riders = await SeedRidersAsync(tenantId, passwordHash);
            s.Riders = riders.Count;

            // All of the tenant's event types, so seeded events span a realistic mix (races,
            // open rides, practice, lessons) instead of every event being a race.
            var eventTypes = (await Q<EventTypeRow>(
                "SELECT id AS Id, code AS Code, name AS Name FROM tenant_event_type WHERE tenant_id = @tenantId",
                new { tenantId })).ToList();
            var fallbackType = eventTypes.FirstOrDefault(t => t.Code == "race") ?? eventTypes.First();
            EventTypeRow TypeFor(string code) => eventTypes.FirstOrDefault(t => t.Code == code) ?? fallbackType;

            // One event per time offset (past -> future), each assigned a type. Race-dominant so the
            // race-class demo stays rich, with open rides / practice / a lesson mixed in. Codes that a
            // tenant doesn't have fall back to race (or its first type).
            var plan = new (int Offset, string Code)[]
            {
                (-60, "race"), (-45, "open_ride"), (-30, "practice"), (-18, "race"), (-7, "open_ride"),
                (0, "race"), (3, "practice"), (14, "lesson"), (30, "open_ride"), (60, "race"),
            };
            var eventInfos = new List<SeedEvent>();
            for (int i = 0; i < plan.Length; i++)
            {
                var ev = await SeedEventAsync(tenantId, TypeFor(plan[i].Code), waiverId, plan[i].Offset, i + 1,
                    imgs.Tracks[i % imgs.Tracks.Count]);
                eventInfos.Add(ev);
                s.Events++;
            }

            // Purchases + registrations + waiver signatures per event.
            foreach (var ev in eventInfos)
            {
                var isPast = ev.StartsAt < DateTime.UtcNow;
                // ~10 riders per event; past events fully checked-in.
                var attendees = riders.OrderBy(_ => _rng.Next()).Take(10).ToList();
                foreach (var rider in attendees)
                {
                    var (tickets, sig) = await SeedRegistrationAsync(tenantId, ev, rider, waiverId, isPast, staff);
                    s.Tickets += tickets;
                    if (sig) s.WaiverSignatures++;
                }
            }

            s.SeasonPasses = await SeedSeasonPassesAsync(tenantId, riders, eventInfos, waiverId);
            s.Memberships = await SeedMembershipsAsync(tenantId, riders);
            s.GiftCards = await SeedGiftCardsAsync(tenantId, riders);
            s.Coupons = await SeedCouponsAsync(tenantId);
            await SeedRewardsAsync(tenantId, riders);
            s.Rentals = await SeedRentalsAsync(tenantId, riders, waiverId);
            s.Disputes = await SeedDisputesAsync(tenantId);
            (s.NewsletterSubscribers, s.Campaigns) = await SeedNewsletterAsync(tenantId, riders, staff);
            s.Blackouts = await SeedBlackoutsAsync(tenantId);
            s.ConcessionOrders = await SeedConcessionsAsync(tenantId, staff, riders);

            await Exec("UPDATE tenant SET seed_data_populated_at = now() WHERE id = @tenantId", new { tenantId });
            _logger.LogInformation("Seed complete for tenant {TenantId}: {Summary}", tenantId,
                System.Text.Json.JsonSerializer.Serialize(s));
            return s;
        }

        // ── Images ─────────────────────────────────────────────────────────────
        private record SeedImages(string Hero1, string Hero2, List<string> Tracks);

        private async Task<SeedImages> StoreImagesAsync(Guid tenantId, CancellationToken ct)
        {
            var dir = Path.Combine(_env.ContentRootPath, "SeedData", "images");
            async Task<string> Store(string file, string kind)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) { _logger.LogWarning("Seed image missing: {Path}", path); return ""; }
                await using var fs = File.OpenRead(path);
                var ext = Path.GetExtension(file);
                return await _images.SaveAsync(fs, tenantId, kind, ext, ct);
            }

            var hero1 = await Store("hero-01.jpg", "hero");
            var hero2 = await Store("hero-02.jpg", "hero");
            var tracks = new List<string>();
            for (int i = 1; i <= 9; i++)
            {
                var url = await Store($"track-{i:00}.webp", "event");
                if (!string.IsNullOrEmpty(url)) tracks.Add(url);
            }
            if (tracks.Count == 0) tracks.Add(hero1);
            return new SeedImages(hero1, hero2, tracks);
        }

        private async Task SeedBrandingAsync(Guid tenantId, SeedImages imgs)
        {
            // The tenant_branding row is created by a trigger on tenant insert, so update it in place.
            await Exec(@"
                UPDATE tenant_branding
                SET primary_color = '#D32F2F', secondary_color = '#1A1A1A', accent_color = '#F57C00',
                    tagline = 'Ride hard. Race harder.', theme_mode = 'dark',
                    hero_image_url = @hero1, secondary_hero_url = @hero2, home_benefits_image_url = @bench,
                    updated_at = now()
                WHERE tenant_id = @tenantId",
                new { tenantId, hero1 = imgs.Hero1, hero2 = imgs.Hero2, bench = imgs.Tracks[0] });

            await Exec(@"
                UPDATE tenant
                SET about_html = @about, refund_policy_html = @refund
                WHERE id = @tenantId",
                new
                {
                    tenantId,
                    about = "<p>A premier motocross facility with pro-level tracks, weekly race series, rentals, and a full concession stand. Family-owned since 1998.</p>",
                    refund = "<p>Refunds available up to 48 hours before an event. Gate fees are non-refundable once redeemed. Weather cancellations are credited to a future event.</p>",
                });
        }

        // ── Users ──────────────────────────────────────────────────────────────
        private record SeedUser(Guid Id, string FirstName, string LastName, string Email, DateTime? Birthdate);

        private async Task<List<SeedUser>> SeedStaffAsync(Guid tenantId, string passwordHash)
        {
            var pinHash = _hasher.HashPassword(new User(), "1234");
            var defs = new (string First, string Last, string Role, string[] Roles, bool Pin)[]
            {
                ("Dana", "Keller", "tenant_manager", new[] { "tenant_manager" }, true),
                ("Marco", "Ellis", "tenant_cashier", new[] { "tenant_cashier" }, false),
                ("Sam", "Boyd", "tenant_scanner", new[] { "tenant_scanner" }, false),
            };
            var list = new List<SeedUser>();
            foreach (var d in defs)
            {
                var id = Guid.NewGuid();
                var email = $"{d.First.ToLower()}.{d.Last.ToLower()}@seed.ridepass.io";
                await Exec(@"
                    INSERT INTO users (id, tenant_id, email, password_hash, first_name, last_name, role, roles, status, email_verified, pos_pin_hash)
                    VALUES (@id, @tenantId, @email, @pw, @first, @last, @role, @roles, 'active', true, @pin)",
                    new { id, tenantId, email, pw = passwordHash, first = d.First, last = d.Last, role = d.Role, roles = d.Roles, pin = d.Pin ? pinHash : null });
                list.Add(new SeedUser(id, d.First, d.Last, email, null));
            }
            return list;
        }

        private async Task<List<SeedUser>> SeedRidersAsync(Guid tenantId, string passwordHash)
        {
            var list = new List<SeedUser>();
            // Riders are GLOBAL accounts (tenant_id IS NULL, per chk_user_tenant_scope); the tenant
            // link comes from their purchases, not the users row. Their emails therefore fall under
            // the global-unique index idx_users_email_super_admin, so include a per-tenant token to
            // keep seeded riders distinct when more than one tenant is seeded.
            var tenantSlug = tenantId.ToString("N").Substring(0, 8);
            for (int i = 0; i < 22; i++)
            {
                var first = FirstNames[i % FirstNames.Length];
                var last = LastNames[(i * 3) % LastNames.Length];
                var id = Guid.NewGuid();
                var email = $"{first.ToLower()}.{last.ToLower()}{i}.{tenantSlug}@seed.ridepass.io";
                // Mostly adults; a few minors (index 4,9,15) so the guardian-signature path shows in demos.
                DateTime birth = (i is 4 or 9 or 15)
                    ? new DateTime(2011, 1, 1).AddDays(_rng.Next(0, 900))
                    : new DateTime(1985, 1, 1).AddDays(_rng.Next(0, 5000));
                await Exec(@"
                    INSERT INTO users (id, tenant_id, email, password_hash, first_name, last_name, role, roles, status, email_verified,
                                       phone, birthdate, emergency_contact_name, emergency_contact_phone)
                    VALUES (@id, NULL, @email, @pw, @first, @last, 'rider', ARRAY['rider'], 'active', true,
                            @phone, @birth, @ecn, @ecp)",
                    new
                    {
                        id, email, pw = passwordHash, first, last,
                        phone = $"555{_rng.Next(1000000, 9999999)}",
                        birth = birth.Date,
                        ecn = $"{LastNames[(i + 5) % LastNames.Length]} Family",
                        ecp = $"555{_rng.Next(1000000, 9999999)}",
                    });
                list.Add(new SeedUser(id, first, last, email, birth.Date));
            }
            return list;
        }

        // ── Events + tiers ───────────────────────────────────────────────────────
        private record SeedTier(Guid Id, string Kind, string Audience, string Name, int PriceCents, int ServiceChargeBps);
        private record SeedEvent(Guid Id, DateTime StartsAt, DateTime EndsAt, List<SeedTier> Tiers);
        private record EventTypeRow(Guid Id, string Code, string Name);

        private async Task<SeedEvent> SeedEventAsync(Guid tenantId, EventTypeRow type, Guid waiverId, int dayOffset, int seq, string imageUrl)
        {
            var start = DateTime.UtcNow.Date.AddDays(dayOffset).AddHours(9);
            var end = start.AddHours(8);
            var id = Guid.NewGuid();
            var isRace = type.Code == "race";
            // Title + copy follow the event type's display name (adapts to MX/MTB naming).
            var title = dayOffset < 0 ? $"{type.Name} #{seq}"
                : dayOffset == 0 ? $"{type.Name} (Today)"
                : $"Upcoming {type.Name} +{dayOffset}d";
            var desc = isRace
                ? "Gates open at 8am. Practice, then motos by class."
                : $"Gates open at 8am. {type.Name} all day.";
            await Exec(@"
                INSERT INTO event (id, tenant_id, event_type_id, title, description, starts_at, ends_at, all_day, capacity,
                                   location_label, status, allows_riders, allows_spectators,
                                   requires_rider_waiver, requires_spectator_waiver, racer_waiver_id, image_url)
                VALUES (@id, @tenantId, @typeId, @title, @desc, @start, @end, false, @cap,
                        'Main MX Track', 'scheduled', true, true, true, false, @waiverId, @img)",
                new { id, tenantId, typeId = type.Id, title, desc,
                      start, end, cap = 150, waiverId = waiverId == Guid.Empty ? (Guid?)null : waiverId, img = imageUrl });

            var tiers = new List<SeedTier>();
            async Task AddTier(string kind, string audience, string name, int price, string? ladder)
            {
                var tid = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO event_ticket_tier (id, tenant_id, event_id, kind, audience, required, name, price_cents,
                                                   inventory, sort_order, is_active, ladder_group, rider_paid_service_charge_bps)
                    VALUES (@tid, @tenantId, @eventId, @kind, @audience, false, @name, @price,
                            NULL, @sort, true, @ladder, 10000)",
                    new { tid, tenantId, eventId = id, kind, audience, name, price, sort = tiers.Count * 10, ladder });
                tiers.Add(new SeedTier(tid, kind, audience, name, price, 10000));
            }

            // Every event has a rider entry (gate fee / day pass) + a spectator gate. Only races add
            // race-entry classes; open rides / practice / lessons are just an entry, so registrations
            // for those events buy the gate alone (SeedRegistrationAsync handles the no-class case).
            await AddTier("gate_fee", "rider", isRace ? "Rider Gate Fee" : "Day Pass", 4000, null);
            await AddTier("gate_fee", "spectator", "Spectator Gate", 1500, null);
            if (isRace)
            {
                await AddTier("race_entry", "rider", "250 A", 3500, null);
                await AddTier("race_entry", "rider", "450 A", 3500, null);
                await AddTier("race_entry", "rider", "Open Amateur", 3000, null);
            }
            return new SeedEvent(id, start, end, tiers);
        }

        // ── Registration (tickets + waiver signature + ledger) ─────────────────────
        private async Task<(int tickets, bool signed)> SeedRegistrationAsync(
            Guid tenantId, SeedEvent ev, SeedUser rider, Guid waiverId, bool isPast, List<SeedUser> staff)
        {
            var status = isPast ? "redeemed" : "paid";
            var isMinor = rider.Birthdate is { } bd && (DateTime.UtcNow.Date.Year - bd.Year - (bd.Date > DateTime.UtcNow.Date.AddYears(-(DateTime.UtcNow.Year - bd.Year)) ? 1 : 0)) < 18;

            // One waiver signature per rider per event, linked from all their tickets. Every past racer
            // must have one (demo requirement); future racers get one too for consistency.
            Guid? sigId = null;
            if (waiverId != Guid.Empty)
            {
                sigId = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO rider_waiver_signature (id, tenant_id, user_id, waiver_id, signature_data_url,
                                                        signed_by_parent, parent_name, parent_phone,
                                                        signer_email, signer_name, spectator_first_name, spectator_last_name, spectator_birthdate, signed_at)
                    VALUES (@id, @tenantId, NULL, @waiverId, @dataUrl, @minor, @pName, @pPhone,
                            @signerEmail, @signerName, @first, @last, @birth, @signedAt)",
                    new
                    {
                        id = sigId, tenantId, waiverId,
                        dataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
                        minor = isMinor,
                        pName = isMinor ? $"Parent {rider.LastName}" : (string?)null,
                        pPhone = isMinor ? $"555{_rng.Next(1000000, 9999999)}" : (string?)null,
                        signerEmail = rider.Email,
                        signerName = isMinor ? $"Parent {rider.LastName}" : $"{rider.FirstName} {rider.LastName}",
                        first = rider.FirstName, last = rider.LastName, birth = rider.Birthdate,
                        signedAt = ev.StartsAt.AddHours(-1),
                    });
            }

            var registrantId = Guid.NewGuid();
            var soldBy = staff.Count > 0 ? staff[_rng.Next(staff.Count)].Id : (Guid?)null;
            var checkedInBy = isPast && staff.Count > 0 ? staff[_rng.Next(staff.Count)].Id : (Guid?)null;
            int count = 0;

            // Rider gate fee + 1-2 race classes.
            var riderTiers = ev.Tiers.Where(t => t.Audience == "rider").ToList();
            var gate = riderTiers.First(t => t.Kind == "gate_fee");
            var classes = riderTiers.Where(t => t.Kind == "race_entry").OrderBy(_ => _rng.Next()).Take(_rng.Next(1, 3)).ToList();

            foreach (var t in new[] { gate }.Concat(classes))
            {
                var raceNum = t.Kind == "race_entry" ? _rng.Next(1, 999).ToString() : null;
                await InsertTicketAsync(tenantId, ev, rider, t, status, registrantId, sigId, waiverId, isMinor, raceNum, soldBy, checkedInBy);
                count++;
            }
            return (count, sigId != null);
        }

        private async Task InsertTicketAsync(Guid tenantId, SeedEvent ev, SeedUser rider, SeedTier tier,
            string status, Guid registrantId, Guid? sigId, Guid waiverId, bool isMinor, string? raceNumber,
            Guid? soldBy, Guid? checkedInBy)
        {
            var id = Guid.NewGuid();
            var serviceCharge = (int)((long)tier.PriceCents * tier.ServiceChargeBps / 100_000L);
            var amount = tier.PriceCents + (int)((long)serviceCharge * 10000 / 10000L);
            await Exec(@"
                INSERT INTO event_ticket_purchase (id, tenant_id, tier_id, purchaser_user_id, amount_cents, service_charge_cents,
                    payment_method, status, purchaser_email, purchaser_name, sold_by_user_id, registration_complete,
                    waiver_signature_id, waiver_signed_at, waiver_id, rider_first_name, rider_last_name, rider_birthdate,
                    parent_guardian_name, emergency_contact_name, emergency_contact_phone, race_number, registrant_id,
                    redeemed_at_utc, redeemed_by_user_id, created_at)
                VALUES (@id, @tenantId, @tierId, @uid, @amount, @sc,
                    'stripe', @status, @email, @name, @soldBy, true,
                    @sigId, @signedAt, @waiverId, @first, @last, @birth,
                    @pName, @ecn, @ecp, @raceNum, @registrantId,
                    @redeemedAt, @redeemedBy, @createdAt)",
                new
                {
                    id, tenantId, tierId = tier.Id, uid = rider.Id, amount, sc = serviceCharge, status,
                    email = rider.Email, name = $"{rider.FirstName} {rider.LastName}", soldBy,
                    sigId, signedAt = sigId != null ? ev.StartsAt.AddHours(-1) : (DateTime?)null,
                    waiverId = waiverId == Guid.Empty ? (Guid?)null : waiverId,
                    first = rider.FirstName, last = rider.LastName, birth = rider.Birthdate,
                    pName = isMinor ? $"Parent {rider.LastName}" : (string?)null,
                    ecn = $"{rider.LastName} Family", ecp = $"555{_rng.Next(1000000, 9999999)}",
                    raceNum = raceNumber, registrantId,
                    redeemedAt = status == "redeemed" ? ev.StartsAt.AddHours(1) : (DateTime?)null,
                    redeemedBy = status == "redeemed" ? checkedInBy : null,
                    createdAt = ev.StartsAt.AddDays(-3),
                });

            await InsertSaleLedgerAsync(tenantId, "event_ticket", id, amount, serviceCharge, ev.StartsAt.AddDays(-3), "stripe", soldBy);
        }

        // ── Ledger ─────────────────────────────────────────────────────────────
        private async Task InsertSaleLedgerAsync(Guid tenantId, string sourceKind, Guid sourceId, int gross,
            int serviceCharge, DateTime occurredAt, string paymentMethod, Guid? soldBy)
        {
            var fee = paymentMethod == "cash" ? 0 : (int)(gross * 0.029 + 30);
            var cut = serviceCharge;
            var net = gross - fee - cut;
            try
            {
                await Exec(@"
                    INSERT INTO tenant_ledger_entry (tenant_id, entry_kind, source_kind, source_id, occurred_at_utc,
                        gross_cents, stripe_fee_cents, ridepass_cut_cents, net_to_tenant_cents, payment_method, sold_by_user_id)
                    VALUES (@tenantId, 'sale', @sourceKind, @sourceId, @occurredAt,
                        @gross, @fee, @cut, @net, @pm, @soldBy)",
                    new { tenantId, sourceKind, sourceId, occurredAt, gross, fee, cut, net, pm = paymentMethod, soldBy });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* one sale per source */ }
        }

        // ── Season passes ────────────────────────────────────────────────────────
        private async Task<int> SeedSeasonPassesAsync(Guid tenantId, List<SeedUser> riders, List<SeedEvent> events, Guid waiverId)
        {
            var productId = Guid.NewGuid();
            var from = DateTime.UtcNow.Date.AddMonths(-2);
            var to = DateTime.UtcNow.Date.AddMonths(10);
            await Exec(@"
                INSERT INTO season_pass_product (id, tenant_id, name, description, price_cents, valid_from_date, valid_to_date,
                    kind, requires_waiver, rider_paid_service_charge_bps, is_active, sort_order)
                VALUES (@id, @tenantId, 'Season Pass', 'Unlimited gate access for the full season.', 60000, @from, @to,
                    'unlimited', true, 10000, true, 10)",
                new { id = productId, tenantId, from, to });

            var holders = riders.Take(6).ToList();
            foreach (var r in holders)
            {
                var pid = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO season_pass_purchase (id, tenant_id, purchaser_user_id, product_id, amount_cents, service_charge_cents,
                        payment_method, status, purchaser_email, purchaser_name, valid_from_date, valid_to_date)
                    VALUES (@id, @tenantId, @uid, @productId, 66000, 6000, 'stripe', 'paid', @email, @name, @from, @to)",
                    new { id = pid, tenantId, uid = r.Id, productId, email = r.Email, name = $"{r.FirstName} {r.LastName}", from, to });
                await InsertSaleLedgerAsync(tenantId, "season_pass", pid, 66000, 6000, from, "stripe", null);

                // Reserve a couple of upcoming events.
                foreach (var ev in events.Where(e => e.StartsAt >= DateTime.UtcNow).Take(2))
                {
                    await Exec(@"
                        INSERT INTO season_pass_reservation (season_pass_purchase_id, event_id, status)
                        VALUES (@pid, @eventId, 'reserved') ON CONFLICT DO NOTHING",
                        new { pid, eventId = ev.Id });
                }
            }
            return holders.Count;
        }

        // ── Memberships ────────────────────────────────────────────────────────
        private async Task<int> SeedMembershipsAsync(Guid tenantId, List<SeedUser> riders)
        {
            var members = riders.Skip(6).Take(8).ToList();
            foreach (var r in members)
            {
                var id = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO membership_purchase (id, tenant_id, user_id, name_at_purchase, price_cents, duration_kind,
                        valid_from_utc, valid_to_utc, amount_cents, service_charge_cents, payment_method, status)
                    VALUES (@id, @tenantId, @uid, 'Track Membership', 10000, 'yearly',
                        @from, @to, 11000, 1000, 'stripe', 'paid')",
                    new { id, tenantId, uid = r.Id, from = DateTime.UtcNow.AddMonths(-3), to = DateTime.UtcNow.AddMonths(9) });
                await InsertSaleLedgerAsync(tenantId, "membership", id, 11000, 1000, DateTime.UtcNow.AddMonths(-3), "stripe", null);
            }
            return members.Count;
        }

        // ── Gift cards ───────────────────────────────────────────────────────────
        private async Task<int> SeedGiftCardsAsync(Guid tenantId, List<SeedUser> riders)
        {
            int n = 0;
            foreach (var r in riders.Take(4))
            {
                var amt = new[] { 2500, 5000, 10000 }[_rng.Next(3)];
                await Exec(@"
                    INSERT INTO gift_card (tenant_id, code, initial_amount_cents, balance_cents, buyer_user_id, buyer_name, buyer_email,
                        recipient_name, recipient_email, delivery_status, status)
                    VALUES (@tenantId, @code, @amt, @amt, @uid, @buyer, @email, @rname, @remail, 'delivered', 'active')",
                    new { tenantId, code = $"GIFT-{RandomCode(8)}", amt, uid = r.Id, buyer = $"{r.FirstName} {r.LastName}",
                          email = r.Email, rname = "A Friend", remail = $"friend{n}@seed.ridepass.io" });
                n++;
            }
            return n;
        }

        // ── Coupons ─────────────────────────────────────────────────────────────
        private async Task<int> SeedCouponsAsync(Guid tenantId)
        {
            await Exec(@"
                INSERT INTO coupon (tenant_id, code, description, discount_kind, discount_value, applicable_scope, is_active, valid_to_utc)
                VALUES (@tenantId, 'WELCOME10', '10% off your first entry', 'percent', 1000, 'all', true, @exp),
                       (@tenantId, 'GATE5', '$5 off a gate fee', 'amount', 500, 'event_ticket', true, @exp)",
                new { tenantId, exp = DateTime.UtcNow.AddMonths(6) });
            return 2;
        }

        // ── Rewards ─────────────────────────────────────────────────────────────
        private async Task SeedRewardsAsync(Guid tenantId, List<SeedUser> riders)
        {
            var programId = Guid.NewGuid();
            await Exec(@"
                INSERT INTO reward_program (id, tenant_id, name, description, enrollment_mode, requirement_kind, requirement_count, reward_percent_off, is_active)
                VALUES (@id, @tenantId, 'Loyalty Rewards', 'Buy 5 entries, get 50% off the next.', 'auto', 'event_ticket', 5, 50, true)",
                new { id = programId, tenantId });
            foreach (var r in riders.Take(10))
            {
                await Exec("INSERT INTO reward_enrollment (program_id, user_id) VALUES (@programId, @uid) ON CONFLICT DO NOTHING",
                    new { programId, uid = r.Id });
            }
            foreach (var r in riders.Take(3))
            {
                await Exec("INSERT INTO reward_redemption (program_id, user_id) VALUES (@programId, @uid)",
                    new { programId, uid = r.Id });
            }
        }

        // ── Rentals ─────────────────────────────────────────────────────────────
        private async Task<int> SeedRentalsAsync(Guid tenantId, List<SeedUser> riders, Guid waiverId)
        {
            var productId = Guid.NewGuid();
            await Exec(@"
                INSERT INTO rental_product (id, tenant_id, name, description, daily_rate_cents, deposit_cents, tracking_kind,
                    inventory_pool, requires_waiver, rider_paid_service_charge_bps, is_active, sort_order)
                VALUES (@id, @tenantId, 'Practice Bike (250F)', 'Well-maintained 250F for practice days.', 8000, 20000, 'pool', 6, true, 10000, true, 10)",
                new { id = productId, tenantId });

            int n = 0;
            foreach (var r in riders.Skip(2).Take(4))
            {
                var start = DateTime.UtcNow.Date.AddDays(-_rng.Next(1, 30));
                var days = _rng.Next(1, 3);
                var amount = 8000 * days;
                await Exec(@"
                    INSERT INTO rental_purchase (tenant_id, product_id, purchaser_user_id, purchaser_email, purchaser_name,
                        start_date, end_date, quantity, daily_rate_cents_frozen, days_count, amount_cents, service_charge_cents,
                        deposit_cents, status, payment_method)
                    VALUES (@tenantId, @productId, @uid, @email, @name, @start, @end, 1, 8000, @days, @amount, 800, 20000, 'returned', 'stripe')",
                    new { tenantId, productId, uid = r.Id, email = r.Email, name = $"{r.FirstName} {r.LastName}",
                          start, end = start.AddDays(days), days, amount });
                n++;
            }
            return n;
        }

        // ── Disputes ─────────────────────────────────────────────────────────────
        private async Task<int> SeedDisputesAsync(Guid tenantId)
        {
            var ticket = (await Q<Guid>(
                "SELECT id FROM event_ticket_purchase WHERE tenant_id = @tenantId AND status = 'redeemed' ORDER BY created_at LIMIT 1", new { tenantId })).FirstOrDefault();
            var rows = new[]
            {
                ("needs_response", 4000, DateTime.UtcNow.AddDays(6)),
                ("under_review", 3500, DateTime.UtcNow.AddDays(10)),
            };
            int n = 0;
            foreach (var (statusV, amt, due) in rows)
            {
                await Exec(@"
                    INSERT INTO dispute (tenant_id, event_ticket_purchase_id, stripe_dispute_id, stripe_payment_intent_id,
                        amount_cents, currency, reason, status, evidence_due_by, stripe_created_at)
                    VALUES (@tenantId, @ticket, @dp, @pi, @amt, 'usd', 'fraudulent', @statusV, @due, now())",
                    new { tenantId, ticket = ticket == Guid.Empty ? (Guid?)null : ticket,
                          dp = $"dp_seed_{RandomCode(14)}", pi = $"pi_seed_{RandomCode(14)}", amt, statusV, due });
                n++;
            }
            return n;
        }

        // ── Newsletter + campaign ──────────────────────────────────────────────────
        private async Task<(int subs, int campaigns)> SeedNewsletterAsync(Guid tenantId, List<SeedUser> riders, List<SeedUser> staff)
        {
            foreach (var r in riders)
            {
                await Exec(@"
                    INSERT INTO newsletter_subscriber (tenant_id, email, name, source)
                    VALUES (@tenantId, @email, @name, 'account') ON CONFLICT DO NOTHING",
                    new { tenantId, email = r.Email, name = $"{r.FirstName} {r.LastName}" });
            }

            var campId = Guid.NewGuid();
            await Exec(@"
                INSERT INTO email_campaign (id, tenant_id, subject, body_html, body_text, status, recipient_count, created_by_user_id)
                VALUES (@id, @tenantId, 'This weekend: Race Day!', '<p>Gates open at 8am. See you there!</p>', 'Gates open at 8am. See you there!',
                    'sent', @count, @by)",
                new { id = campId, tenantId, count = riders.Count, by = staff.FirstOrDefault()?.Id });
            foreach (var r in riders)
            {
                await Exec(@"
                    INSERT INTO email_campaign_send (campaign_id, email, name, status)
                    VALUES (@campId, @email, @name, 'sent') ON CONFLICT DO NOTHING",
                    new { campId, email = r.Email, name = $"{r.FirstName} {r.LastName}" });
            }
            return (riders.Count, 1);
        }

        // ── Blackouts ────────────────────────────────────────────────────────────
        private async Task<int> SeedBlackoutsAsync(Guid tenantId)
        {
            var d1 = DateTime.UtcNow.Date.AddDays(21);
            var d2 = DateTime.UtcNow.Date.AddDays(45);
            await Exec(@"
                INSERT INTO blackout (tenant_id, starts_at, ends_at, all_day, reason)
                VALUES (@tenantId, @s1, @e1, true, 'Track maintenance'),
                       (@tenantId, @s2, @e2, true, 'Private event')",
                new { tenantId, s1 = d1, e1 = d1.AddDays(1), s2 = d2, e2 = d2.AddDays(2) });
            return 2;
        }

        // ── Concessions (starter catalog + orders) ─────────────────────────────────
        private class SeedProduct { public Guid Id { get; set; } public string Name { get; set; } = ""; public int PriceCents { get; set; } }

        private async Task<int> SeedConcessionsAsync(Guid tenantId, List<SeedUser> staff, List<SeedUser> riders)
        {
            // The "normal" F&B seed the app already ships.
            await _concessions.SeedStarterCatalog(tenantId, onlyIfEmpty: true);
            await _concessions.MarkStarterSeeded(tenantId);

            var products = (await Q<SeedProduct>(
                "SELECT id AS Id, name AS Name, price_cents AS PriceCents FROM concession_product WHERE tenant_id = @tenantId AND is_active = true",
                new { tenantId })).ToList();
            if (products.Count == 0) return 0;

            int orders = 0;
            for (int i = 0; i < 40; i++)
            {
                var saleId = Guid.NewGuid();
                var when = DateTime.UtcNow.AddDays(-_rng.Next(0, 30)).AddHours(-_rng.Next(0, 6));
                var lineCount = _rng.Next(1, 4);
                var picks = products.OrderBy(_ => _rng.Next()).Take(lineCount).ToList();
                var subtotal = 0;
                var lines = new List<(SeedProduct p, int qty, int lineTotal)>();
                foreach (var p in picks)
                {
                    var qty = _rng.Next(1, 3);
                    var lt = p.PriceCents * qty;
                    subtotal += lt;
                    lines.Add((p, qty, lt));
                }
                var isComp = i % 12 == 0;
                var payMethod = _rng.Next(2) == 0 ? "cash" : "stripe";
                var discount = isComp ? subtotal : 0;
                var total = subtotal - discount;
                var soldBy = staff.Count > 0 ? staff[_rng.Next(staff.Count)].Id : (Guid?)null;

                await Exec(@"
                    INSERT INTO concession_sale (id, tenant_id, status, fulfillment_status, order_number, subtotal_cents, tax_cents,
                        prices_include_tax, discount_cents, discount_label, total_cents, payment_method, sold_by_user_id, paid_at, order_channel)
                    VALUES (@id, @tenantId, 'paid', 'completed', @orderNum, @subtotal, 0, false, @discount, @discountLabel,
                        @total, @pm, @soldBy, @paidAt, 'counter')",
                    new { id = saleId, tenantId, orderNum = 1000 + i, subtotal, discount,
                          discountLabel = isComp ? "Comp: Staff meal" : (string?)null, total, pm = payMethod, soldBy, paidAt = when });

                foreach (var (p, qty, lt) in lines)
                {
                    await Exec(@"
                        INSERT INTO concession_sale_line (sale_id, product_id, name_snapshot, unit_price_cents, quantity, line_total_cents)
                        VALUES (@saleId, @productId, @name, @unit, @qty, @lt)",
                        new { saleId, productId = p.Id, name = p.Name, unit = p.PriceCents, qty, lt });
                }

                await InsertSaleLedgerAsync(tenantId, "concession", saleId, total, 0, when, payMethod, soldBy);
                orders++;
            }
            return orders;
        }

        private string RandomCode(int len)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = alphabet[_rng.Next(alphabet.Length)];
            return new string(chars);
        }
    }
}
