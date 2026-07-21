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
    // Idempotent: each section skips itself when its data already exists, so re-running on a seeded
    // tenant only fills in sections added since the last run (it never duplicates users/events/sales).
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

        // Section guard: true when the given SELECT finds at least one row.
        private async Task<bool> Any(string selectSql, object? param = null) =>
            (await Q<bool>($"SELECT EXISTS ({selectSql})", param)).First();

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

            // Users: reuse the seed cast from a prior run if it exists (later sections reference them
            // by id), else self-heal any pre-transaction partial leftovers and create it. Staff are
            // tenant-scoped; riders are global accounts tagged with this tenant's slug in the email.
            const string userCols = "id AS Id, first_name AS FirstName, last_name AS LastName, email AS Email, birthdate AS Birthdate";
            var riderPattern = $"%.{tenantId.ToString("N").Substring(0, 8)}@seed.ridepass.io";
            var staff = (await Q<SeedUser>(
                $"SELECT {userCols} FROM users WHERE tenant_id = @tenantId AND email LIKE '%@seed.ridepass.io' ORDER BY email",
                new { tenantId })).ToList();
            var riders = (await Q<SeedUser>(
                $"SELECT {userCols} FROM users WHERE tenant_id IS NULL AND email LIKE @pattern ORDER BY email",
                new { pattern = riderPattern })).ToList();
            if (staff.Count == 0 || riders.Count == 0)
            {
                // Clear any seed users left by an earlier (pre-transaction) partial run for this
                // tenant so their unique emails don't collide. Tenant-scoped staff and this tenant's
                // slug-tagged global riders only; nothing else's data is touched.
                await Exec("DELETE FROM users WHERE tenant_id = @tenantId AND email LIKE '%@seed.ridepass.io'",
                    new { tenantId });
                await Exec("DELETE FROM users WHERE tenant_id IS NULL AND email LIKE @pattern",
                    new { pattern = riderPattern });
                staff = await SeedStaffAsync(tenantId, passwordHash);
                s.Staff = staff.Count;
                riders = await SeedRidersAsync(tenantId, passwordHash);
                s.Riders = riders.Count;
            }

            var waiverId = (await Q<Guid>(
                "SELECT id FROM tenant_waiver WHERE tenant_id = @tenantId AND is_active = true ORDER BY created_at DESC LIMIT 1",
                new { tenantId })).FirstOrDefault();

            // Reload any existing events (prior seed or real data); the top-up logic below and later
            // sections (season pass reservations pick upcoming events by StartsAt) both need them.
            // Tiers stay empty for reloaded events: only newly-created events get registrations.
            var eventInfos = (await Q<(Guid Id, DateTime StartsAt, DateTime EndsAt)>(
                "SELECT id, starts_at, ends_at FROM event WHERE tenant_id = @tenantId ORDER BY starts_at",
                new { tenantId }))
                .Select(e => new SeedEvent(e.Id, e.StartsAt, e.EndsAt, new List<SeedTier>())).ToList();

            // One event per time offset (past -> future), each assigned a type. Race-dominant so the
            // race-class demo stays rich, with open rides / practice / a lesson mixed in. Codes that a
            // tenant doesn't have fall back to race (or its first type).
            var plan = new (int Offset, string Code)[]
            {
                (-60, "race"), (-45, "open_ride"), (-30, "practice"), (-18, "race"), (-7, "open_ride"),
                (0, "race"), (3, "practice"), (14, "lesson"), (30, "open_ride"), (60, "race"),
            };

            // Top-up rather than all-or-nothing: a previously-seeded tenant's demo events drift into
            // the past, so first restore the upcoming slate (today + future plan entries) whenever the
            // tenant has no upcoming events, then fill from the rest of the plan (nearest to today
            // first) until it has at least 10 events. A fresh tenant gets the full 10-event plan.
            var toSeed = new List<(int Offset, string Code)>();
            if (!eventInfos.Any(e => e.StartsAt >= DateTime.UtcNow))
                toSeed.AddRange(plan.Where(p => p.Offset >= 0));
            foreach (var p in plan.Where(p => !toSeed.Contains(p))
                         .OrderBy(p => p.Offset >= 0 ? 1 : 0)  // prefer past entries for top-up
                         .ThenBy(p => Math.Abs(p.Offset)))
            {
                if (eventInfos.Count + toSeed.Count >= 10) break;
                toSeed.Add(p);
            }

            // Images upload files outside the transaction, so only store them when a section below
            // actually needs them (fresh branding and/or new events).
            var brandingDone = await Any(
                "SELECT 1 FROM tenant_branding WHERE tenant_id = @tenantId AND COALESCE(hero_image_url, '') <> ''",
                new { tenantId });
            SeedImages? imgs = null;
            if (!brandingDone || toSeed.Count > 0) imgs = await StoreImagesAsync(tenantId, ct);
            if (!brandingDone) await SeedBrandingAsync(tenantId, imgs!);

            if (toSeed.Count > 0)
            {
                // All of the tenant's event types, so seeded events span a realistic mix (races,
                // open rides, practice, lessons) instead of every event being a race.
                var eventTypes = (await Q<EventTypeRow>(
                    "SELECT id AS Id, code AS Code, name AS Name FROM tenant_event_type WHERE tenant_id = @tenantId",
                    new { tenantId })).ToList();
                var fallbackType = eventTypes.FirstOrDefault(t => t.Code == "race") ?? eventTypes.First();
                EventTypeRow TypeFor(string code) => eventTypes.FirstOrDefault(t => t.Code == code) ?? fallbackType;

                var created = new List<SeedEvent>();
                var seq = eventInfos.Count;
                for (int i = 0; i < toSeed.Count; i++)
                {
                    var ev = await SeedEventAsync(tenantId, TypeFor(toSeed[i].Code), waiverId, toSeed[i].Offset, ++seq,
                        imgs!.Tracks[i % imgs.Tracks.Count]);
                    created.Add(ev);
                    s.Events++;
                }

                // Purchases + registrations + waiver signatures per newly-created event (existing
                // events keep whatever they already have).
                foreach (var ev in created)
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
                eventInfos.AddRange(created);
            }

            s.SeasonPasses = await SeedSeasonPassesAsync(tenantId, riders, eventInfos, waiverId);
            s.Memberships = await SeedMembershipsAsync(tenantId, riders);
            s.GiftCards = await SeedGiftCardsAsync(tenantId, riders);
            s.Coupons = await SeedCouponsAsync(tenantId);
            await SeedRewardsAsync(tenantId, riders);
            s.Disputes = await SeedDisputesAsync(tenantId);
            (s.NewsletterSubscribers, s.Campaigns) = await SeedNewsletterAsync(tenantId, riders, staff);
            s.Blackouts = await SeedBlackoutsAsync(tenantId);
            s.ConcessionOrders = await SeedConcessionsAsync(tenantId, staff, riders);
            (s.ShopProducts, s.ShopSales, s.ShopWorkOrders) = await SeedBikeShopAsync(tenantId, riders, staff);

            // Turn rentals on and give them a tax rate so the Rentals page and its Settings tab demo
            // fully (an unset rate is a valid state but shows the "not set" warning). COALESCE so a
            // re-seed never clobbers a rate the tenant deliberately chose.
            await Exec(@"UPDATE tenant SET rentals_enabled = true,
                             rental_tax_bps = COALESCE(rental_tax_bps, 825)
                         WHERE id = @tenantId", new { tenantId });

            // Each of these guards on its own table, so they backfill on a re-run of a tenant that
            // was seeded before these sections existed (the bike shop section above short-circuits
            // once shop_product exists, but these still fill in).
            s.Instructors = await SeedInstructorsAsync(tenantId, eventInfos);
            (s.CustomerBikes, s.Inspections) = await SeedCustomerBikesAndInspectionsAsync(tenantId, riders, staff);
            s.ShopSales += await SeedExtraShopSalesAsync(tenantId, riders, staff);

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
                ("Jesse", "Fox", "tenant_shop_cashier", new[] { "tenant_shop_cashier" }, false),
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
            // Cash never passes through the platform: the tenant already has the money in the till and
            // owes US our cut out of the next payout, so net is negative. This mirrors what the real
            // cash paths write (CounterController's counter sale and ConcessionController.WriteCashLedger,
            // both "Cash sale, tenant owes service charge").
            //
            // gross - fee - cut would claim the platform owes them the whole sale for cash they are
            // already holding — MonthlyPayoutDrafter sums net_to_tenant, so a seeded tenant would get
            // drafted a payout for it, and the QuickBooks sync would refuse the day as unbalanced.
            var net = paymentMethod == "cash" ? -cut : gross - fee - cut;
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
            if (await Any("SELECT 1 FROM season_pass_product WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            if (await Any("SELECT 1 FROM membership_purchase WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            if (await Any("SELECT 1 FROM gift_card WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            if (await Any("SELECT 1 FROM coupon WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            if (await Any("SELECT 1 FROM reward_program WHERE tenant_id = @tenantId", new { tenantId })) return;
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


        // ── Disputes ─────────────────────────────────────────────────────────────
        private async Task<int> SeedDisputesAsync(Guid tenantId)
        {
            if (await Any("SELECT 1 FROM dispute WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            if (await Any("SELECT 1 FROM email_campaign WHERE tenant_id = @tenantId", new { tenantId })) return (0, 0);
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
            if (await Any("SELECT 1 FROM blackout WHERE tenant_id = @tenantId", new { tenantId })) return 0;
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
            // The "normal" F&B seed the app already ships (already guarded by onlyIfEmpty).
            await _concessions.SeedStarterCatalog(tenantId, onlyIfEmpty: true);
            await _concessions.MarkStarterSeeded(tenantId);

            // Demo orders only once; the catalog seed above stays safe to repeat.
            if (await Any("SELECT 1 FROM concession_sale WHERE tenant_id = @tenantId", new { tenantId })) return 0;

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

        // ── Bike shop (catalog + stock + sales + rental + work orders) ─────────────
        // Stock math is kept honest: every on-hand number below reconciles against the movement
        // rows seeded with it (initial adjustment, receive, sale, repair_consume, sale_return),
        // so the movements drill-down demos correctly instead of showing a count the ledger
        // can't explain. Turns the feature flag on so the demo is visible immediately.
        private async Task<(int Products, int Sales, int WorkOrders)> SeedBikeShopAsync(
            Guid tenantId, List<SeedUser> riders, List<SeedUser> staff)
        {
            // Guard first so a re-run doesn't re-enable the flag on a tenant that turned it off.
            if (await Any("SELECT 1 FROM shop_product WHERE tenant_id = @tenantId", new { tenantId })) return (0, 0, 0);
            await Exec("UPDATE tenant SET bike_shop_enabled = true WHERE id = @tenantId", new { tenantId });
            var soldBy = staff.FirstOrDefault()?.Id;

            // Tax + categories + suppliers.
            var taxId = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_tax_category (id, tenant_id, name, rate_bps, is_default)
                VALUES (@taxId, @tenantId, 'Standard', 825, true)", new { taxId, tenantId });
            var catBikes = Guid.NewGuid();
            var catParts = Guid.NewGuid();
            var catApparel = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_category (id, tenant_id, name, sort_order) VALUES
                (@catBikes, @tenantId, 'Bikes', 10), (@catParts, @tenantId, 'Parts', 20),
                (@catApparel, @tenantId, 'Apparel', 30)", new { catBikes, catParts, catApparel, tenantId });
            var supplierId = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_supplier (id, tenant_id, name, contact_name, email)
                VALUES (@supplierId, @tenantId, 'MX Distribution Co', 'Dana Ferris', 'orders@mxdist.example.com')",
                new { supplierId, tenantId });

            // Products + variants. Local helper keeps the inserts readable.
            async Task<Guid> Variant(Guid productId, string? sku, string? size, int? sale, int? daily,
                int deposit, int? cost, string kind, int stock, int? threshold)
            {
                var id = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_variant (id, tenant_id, product_id, sku, size, sale_price_cents,
                        daily_rate_cents, deposit_cents, cost_cents, tracking_kind, stock_on_hand, low_stock_threshold)
                    VALUES (@id, @tenantId, @productId, @sku, @size, @sale, @daily, @deposit, @cost, @kind, @stock, @threshold)",
                    new { id, tenantId, productId, sku, size, sale, daily, deposit, cost, kind, stock, threshold });
                return id;
            }
            async Task<Guid> Product(Guid? cat, string name, string desc, bool sellable, bool rentable, int sort)
            {
                var id = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_product (id, tenant_id, category_id, supplier_id, name, description,
                        is_sellable, is_rentable, sort_order, tax_category_id)
                    VALUES (@id, @tenantId, @cat, @supplierId, @name, @desc, @sellable, @rentable, @sort, @taxId)",
                    new { id, tenantId, cat, supplierId, name, desc, sellable, rentable, sort, taxId });
                return id;
            }
            Task Movement(Guid variantId, Guid? itemId, int delta, string reason, string? refKind, Guid? refId, string? note, DateTime at) =>
                Exec(@"
                    INSERT INTO shop_stock_movement (tenant_id, variant_id, item_id, delta, reason,
                        reference_kind, reference_id, note, created_by_user_id, created_at)
                    VALUES (@tenantId, @variantId, @itemId, @delta, @reason, @refKind, @refId, @note, @soldBy, @at)",
                    new { tenantId, variantId, itemId, delta, reason, refKind, refId, note, soldBy, at });

            var t0 = DateTime.UtcNow.AddDays(-21);
            var products = 0;

            // Serialized bikes: sellable + rentable, three units (one in maintenance).
            var bikeProduct = await Product(catBikes, "Trail Bike 250F", "Race-ready 250F, serviced between every rental.", true, true, 10);
            var bikeVariant = await Variant(bikeProduct, "BIKE-250F", null, 549900, 8000, 30000, 420000, "serialized", 0, null);
            products++;
            var bikeItems = new List<Guid>();
            foreach (var (label, serial, status) in new[] { ("250F #1", "MX250F-0101", "available"), ("250F #2", "MX250F-0102", "available"), ("250F #3", "MX250F-0103", "maintenance") })
            {
                var itemId = Guid.NewGuid();
                bikeItems.Add(itemId);
                await Exec(@"
                    INSERT INTO shop_item (id, tenant_id, variant_id, label, serial, status, acquired_cost_cents)
                    VALUES (@itemId, @tenantId, @bikeVariant, @label, @serial, @status, 420000)",
                    new { itemId, tenantId, bikeVariant, label, serial, status });
                await Movement(bikeVariant, itemId, 1, "receive", null, null, "Fleet intake", t0);
            }

            // Pool parts + apparel. Final stock = initial + receive - sold - repair + returned (below).
            var padsProduct = await Product(catParts, "Brake Pads (Sintered)", "Sintered pads, most 250/450 models.", true, false, 20);
            var padsVariant = await Variant(padsProduct, "BP-SIN", null, 2499, null, 0, 1100, "pool", 17, 5);
            var tubeProduct = await Product(catParts, "Heavy Duty Tube 21\"", "Front tube, heavy duty.", true, false, 21);
            var tubeVariant = await Variant(tubeProduct, "TUBE-21", null, 1299, null, 0, 520, "pool", 39, 10);
            var lubeProduct = await Product(catParts, "Chain Lube", "Off-road chain lube, 400ml.", true, false, 22);
            var lubeVariant = await Variant(lubeProduct, "LUBE-400", null, 999, null, 0, 380, "pool", 15, 4);
            var jerseyProduct = await Product(catApparel, "Team Jersey", "Track team jersey.", true, false, 30);
            var jerseyM = await Variant(jerseyProduct, "JRS-M", "M", 3999, null, 0, 1600, "pool", 7, 2);
            var jerseyL = await Variant(jerseyProduct, "JRS-L", "L", 3999, null, 0, 1600, "pool", 8, 2);
            var helmetProduct = await Product(catBikes, "Rental Helmet", "DOT helmet, sanitized between rentals.", false, true, 40);
            var helmetVariant = await Variant(helmetProduct, "HELM-RNT", null, null, 1500, 5000, 4500, "pool", 8, 2);
            products += 5;

            // Opening counts (the ledger's starting truth).
            await Movement(padsVariant, null, 20, "adjustment", null, null, "Initial count", t0);
            await Movement(tubeVariant, null, 20, "adjustment", null, null, "Initial count", t0);
            await Movement(lubeVariant, null, 15, "adjustment", null, null, "Initial count", t0);
            await Movement(jerseyM, null, 8, "adjustment", null, null, "Initial count", t0);
            await Movement(jerseyL, null, 8, "adjustment", null, null, "Initial count", t0);
            await Movement(helmetVariant, null, 8, "adjustment", null, null, "Initial count", t0);

            // A received purchase order (tubes +20) and an open one awaiting jerseys.
            var poReceived = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_purchase_order (id, tenant_id, supplier_id, reference, status, ordered_at, received_at)
                VALUES (@poReceived, @tenantId, @supplierId, 'PO-1042', 'received', @ordered, @received)",
                new { poReceived, tenantId, supplierId, ordered = t0.AddDays(2), received = t0.AddDays(6) });
            await Exec(@"
                INSERT INTO shop_po_line (po_id, variant_id, quantity_ordered, quantity_received, unit_cost_cents)
                VALUES (@poReceived, @tubeVariant, 20, 20, 520)", new { poReceived, tubeVariant });
            await Movement(tubeVariant, null, 20, "receive", "purchase_order", poReceived, null, t0.AddDays(6));
            var poOpen = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_purchase_order (id, tenant_id, supplier_id, reference, status, ordered_at, expected_at)
                VALUES (@poOpen, @tenantId, @supplierId, 'PO-1043', 'ordered', @ordered, @expected)",
                new { poOpen, tenantId, supplierId, ordered = DateTime.UtcNow.AddDays(-3), expected = DateTime.UtcNow.Date.AddDays(4) });
            await Exec(@"
                INSERT INTO shop_po_line (po_id, variant_id, quantity_ordered, quantity_received, unit_cost_cents)
                VALUES (@poOpen, @jerseyM, 10, 0, 1600)", new { poOpen, jerseyM });

            // Sales: two paid (cash + card) and one refunded-with-restock, with matching movements.
            var salesSeeded = 0;
            async Task<Guid> Sale(int orderNum, SeedUser? buyer, string method, string status, DateTime at,
                (Guid variant, string name, int unit, int qty)[] lines)
            {
                var saleId = Guid.NewGuid();
                var subtotal = lines.Sum(l => l.unit * l.qty);
                var tax = (int)Math.Round(subtotal * 825 / 10000.0, MidpointRounding.AwayFromZero);
                await Exec(@"
                    INSERT INTO shop_sale (id, tenant_id, buyer_user_id, buyer_email, buyer_name, status,
                        subtotal_cents, tax_cents, total_cents, payment_method, order_number, sold_by_user_id, created_at,
                        refunded_at, refund_note)
                    VALUES (@saleId, @tenantId, @uid, @email, @name, @status, @subtotal, @tax, @total, @method,
                        @orderNum, @soldBy, @at, @refundedAt, @refundNote)",
                    new
                    {
                        saleId, tenantId, uid = buyer?.Id, email = buyer?.Email,
                        name = buyer is null ? "Walk-in" : $"{buyer.FirstName} {buyer.LastName}",
                        status, subtotal, tax, total = subtotal + tax, method, orderNum, soldBy, at,
                        refundedAt = status == "refunded" ? at.AddHours(3) : (DateTime?)null,
                        refundNote = status == "refunded" ? "Wrong size" : null,
                    });
                foreach (var l in lines)
                {
                    var lineTax = (int)Math.Round(l.unit * l.qty * 825 / 10000.0, MidpointRounding.AwayFromZero);
                    await Exec(@"
                        INSERT INTO shop_sale_line (sale_id, variant_id, quantity, name_snapshot, unit_price_cents, tax_cents, tax_rate_bps)
                        VALUES (@saleId, @variant, @qty, @name, @unit, @lineTax, 825)",
                        new { saleId, l.variant, l.qty, l.name, l.unit, lineTax });
                    await Movement(l.variant, null, -l.qty, "sale", "shop_sale", saleId, null, at);
                }
                salesSeeded++;
                return saleId;
            }
            await Sale(1, riders.FirstOrDefault(), "stripe", "paid", DateTime.UtcNow.AddDays(-5),
                new[] { (padsVariant, "Brake Pads (Sintered)", 2499, 2), (tubeVariant, "Heavy Duty Tube 21\"", 1299, 1) });
            await Sale(2, null, "cash", "paid", DateTime.UtcNow.AddDays(-2),
                new[] { (jerseyM, "Team Jersey", 3999, 1) });
            var refunded = await Sale(1, riders.Skip(1).FirstOrDefault(), "cash", "refunded", DateTime.UtcNow.AddDays(-1),
                new[] { (lubeVariant, "Chain Lube", 999, 1) });
            await Movement(lubeVariant, null, 1, "sale_return", "shop_sale", refunded, null, DateTime.UtcNow.AddDays(-1).AddHours(3));

            // A returned rental last week (bike + helmet) and one paid for tomorrow.
            var renter = riders.Skip(2).FirstOrDefault();
            var rentalDone = Guid.NewGuid();
            var rStart = DateTime.UtcNow.Date.AddDays(-7).AddHours(9);
            // Fee + tax mirror the real rental math: service charge = amount * service_charge_bps,
            // the whole of it rider-paid at the seed default (10000 bps), taxed with the service
            // charge in the base (rental_tax_service_charge_taxable defaults true). Deposit is a
            // separate refundable hold and is never fee'd or taxed.
            const int rentalDoneAmount = 9500;
            var rentalDoneFee = rentalDoneAmount * 300 / 10000;               // 285
            var rentalDoneTax = (int)Math.Round((rentalDoneAmount + rentalDoneFee) * 825 / 10000.0, MidpointRounding.AwayFromZero);
            await Exec(@"
                INSERT INTO shop_rental (id, tenant_id, renter_user_id, renter_name, renter_email, starts_at, ends_at,
                    status, amount_cents, service_charge_cents, tax_cents, total_cents, deposit_cents, payment_method, order_number, sold_by_user_id,
                    checked_out_at, returned_at)
                VALUES (@rentalDone, @tenantId, @uid, @name, @email, @s, @e, 'returned', @amount, @fee, @tax, @total, 35000, 'stripe', 3,
                    @soldBy, @s, @e)",
                new { rentalDone, tenantId, uid = renter?.Id, name = renter is null ? "Walk-in" : $"{renter.FirstName} {renter.LastName}",
                      email = renter?.Email, s = rStart, e = rStart.AddHours(8), soldBy,
                      amount = rentalDoneAmount, fee = rentalDoneFee, tax = rentalDoneTax, total = rentalDoneAmount + rentalDoneFee + rentalDoneTax });
            await Exec(@"
                INSERT INTO shop_rental_line (rental_id, variant_id, item_id, quantity, name_snapshot, daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
                VALUES (@rentalDone, @bikeVariant, @item, 1, 'Trail Bike 250F', 8000, 30000, 8000),
                       (@rentalDone, @helmetVariant, NULL, 1, 'Rental Helmet', 1500, 5000, 1500)",
                new { rentalDone, bikeVariant, item = bikeItems[0], helmetVariant });
            await Movement(bikeVariant, bikeItems[0], -1, "rental_out", "shop_rental", rentalDone, null, rStart);
            await Movement(helmetVariant, null, -1, "rental_out", "shop_rental", rentalDone, null, rStart);
            await Movement(bikeVariant, bikeItems[0], 1, "rental_return", "shop_rental", rentalDone, null, rStart.AddHours(8));
            await Movement(helmetVariant, null, 1, "rental_return", "shop_rental", rentalDone, null, rStart.AddHours(8));
            var rentalUpcoming = Guid.NewGuid();
            var uStart = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
            const int rentalUpAmount = 8000;
            var rentalUpFee = rentalUpAmount * 300 / 10000;                   // 240
            var rentalUpTax = (int)Math.Round((rentalUpAmount + rentalUpFee) * 825 / 10000.0, MidpointRounding.AwayFromZero);
            await Exec(@"
                INSERT INTO shop_rental (id, tenant_id, renter_user_id, renter_name, renter_email, starts_at, ends_at,
                    status, amount_cents, service_charge_cents, tax_cents, total_cents, deposit_cents, payment_method, sold_by_user_id)
                VALUES (@rentalUpcoming, @tenantId, @uid, @name, @email, @s, @e, 'paid', @amount, @fee, @tax, @total, 30000, 'stripe', @soldBy)",
                new { rentalUpcoming, tenantId, uid = riders.Skip(3).FirstOrDefault()?.Id,
                      name = riders.Skip(3).FirstOrDefault() is { } r4 ? $"{r4.FirstName} {r4.LastName}" : "Walk-in",
                      email = riders.Skip(3).FirstOrDefault()?.Email, s = uStart, e = uStart.AddHours(8), soldBy,
                      amount = rentalUpAmount, fee = rentalUpFee, tax = rentalUpTax, total = rentalUpAmount + rentalUpFee + rentalUpTax });
            await Exec(@"
                INSERT INTO shop_rental_line (rental_id, variant_id, item_id, quantity, name_snapshot, daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
                VALUES (@rentalUpcoming, @bikeVariant, @item, 1, 'Trail Bike 250F', 8000, 30000, 8000)",
                new { rentalUpcoming, bikeVariant, item = bikeItems[1] });

            // Work orders: one on the bench (pads consumed), one open estimate.
            var woCustomer = riders.Skip(4).FirstOrDefault();
            var woActive = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_work_order (id, tenant_id, customer_user_id, customer_name, customer_phone,
                    customer_bike_desc, status, intake_notes, promised_at)
                VALUES (@woActive, @tenantId, @uid, @name, '555-0142', '2023 YZ250F, blue', 'in_progress',
                    'Front brake soft; replace pads and bleed.', @promised)",
                new { woActive, tenantId, uid = woCustomer?.Id,
                      name = woCustomer is null ? "Walk-in" : $"{woCustomer.FirstName} {woCustomer.LastName}",
                      promised = DateTime.UtcNow.Date.AddDays(2) });
            await Exec(@"
                INSERT INTO shop_work_order_line (work_order_id, line_kind, description, variant_id, quantity, unit_price_cents, consumed) VALUES
                (@woActive, 'labor', 'Brake bleed + pad install', NULL, 1, 6500, false),
                (@woActive, 'part', NULL, @padsVariant, 1, 2499, true)",
                new { woActive, padsVariant });
            await Movement(padsVariant, null, -1, "repair_consume", "shop_work_order", woActive, null, DateTime.UtcNow.AddDays(-1));
            var woEstimate = Guid.NewGuid();
            await Exec(@"
                INSERT INTO shop_work_order (id, tenant_id, customer_name, customer_phone, customer_bike_desc, status, intake_notes)
                VALUES (@woEstimate, @tenantId, 'Pat Malone', '555-0177', '2021 CRF450, red', 'estimate',
                    'Full suspension service quote.')", new { woEstimate, tenantId });
            await Exec(@"
                INSERT INTO shop_work_order_line (work_order_id, line_kind, description, variant_id, quantity, unit_price_cents, consumed)
                VALUES (@woEstimate, 'labor', 'Fork + shock full service', NULL, 1, 32500, false)",
                new { woEstimate });

            return (products, salesSeeded, 2);
        }

        // ── Instructors (assigned to the lesson event) ─────────────────────────────
        // Lessons carry real instructors that can't be double-booked; the demo needs at least one
        // on the seeded lesson event so the schedule shows a coach rather than an empty slot.
        private async Task<int> SeedInstructorsAsync(Guid tenantId, List<SeedEvent> events)
        {
            if (await Any("SELECT 1 FROM instructor WHERE tenant_id = @tenantId", new { tenantId })) return 0;

            var defs = new (string Name, string Email, string Bio, int Max)[]
            {
                ("Ricky Vance", "ricky.vance@seed.ridepass.io", "Former pro, 15 years coaching starts and cornering.", 6),
                ("Dani Cross", "dani.cross@seed.ridepass.io", "Womens and youth development coach.", 8),
            };
            var ids = new List<Guid>();
            var sort = 10;
            foreach (var d in defs)
            {
                var id = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO instructor (id, tenant_id, name, email, bio, is_active, sort_order, max_students_per_session)
                    VALUES (@id, @tenantId, @name, @email, @bio, true, @sort, @max)",
                    new { id, tenantId, d.Name, d.Email, d.Bio, sort, max = d.Max });
                ids.Add(id);
                sort += 10;
            }

            // Assign the first instructor to the tenant's lesson event(s), if any were seeded.
            var lessonEventIds = (await Q<Guid>(@"
                SELECT e.id FROM event e
                JOIN tenant_event_type t ON t.id = e.event_type_id
                WHERE e.tenant_id = @tenantId AND t.code = 'lesson'",
                new { tenantId })).ToList();
            foreach (var evId in lessonEventIds)
            {
                await Exec(@"
                    INSERT INTO event_instructor (event_id, instructor_id)
                    VALUES (@evId, @instructorId) ON CONFLICT DO NOTHING",
                    new { evId, instructorId = ids[0] });
            }
            return ids.Count;
        }

        // ── Customer bikes + multi-point inspections ───────────────────────────────
        // A customer's bike is a real record (serial-keyed) that inspections and service history
        // hang off. Seeds a few bikes, links one to the on-the-bench work order so the inspection
        // panel shows there, and records one completed inspection (with a couple of flagged items)
        // plus a draft so the mechanic and customer views both have something to render.
        private async Task<(int bikes, int inspections)> SeedCustomerBikesAndInspectionsAsync(
            Guid tenantId, List<SeedUser> riders, List<SeedUser> staff)
        {
            if (await Any("SELECT 1 FROM shop_customer_bike WHERE tenant_id = @tenantId", new { tenantId })) return (0, 0);
            // Nothing to hang bikes off if the bike shop was never seeded for this tenant.
            if (!await Any("SELECT 1 FROM shop_product WHERE tenant_id = @tenantId", new { tenantId })) return (0, 0);

            var mechanic = staff.FirstOrDefault()?.Id;

            // Ensure a default inspection template exists (matches what the app lazily creates, so a
            // later real call won't duplicate it). Motocross tenants get the MX checklist.
            var templateId = (await Q<Guid>(
                "SELECT id FROM shop_inspection_template WHERE tenant_id = @tenantId AND is_default = true ORDER BY created_at LIMIT 1",
                new { tenantId })).FirstOrDefault();
            var templateItems = new List<(Guid Id, string Group, string Label, int Sort)>();
            if (templateId == Guid.Empty)
            {
                templateId = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_inspection_template (id, tenant_id, name, is_default, is_active, sort_order)
                    VALUES (@templateId, @tenantId, 'Standard MX inspection', true, true, 10)",
                    new { templateId, tenantId });
                foreach (var (g, l, o) in DefaultMxChecklist)
                {
                    var itemId = Guid.NewGuid();
                    await Exec(@"
                        INSERT INTO shop_inspection_template_item (id, template_id, group_label, label, sort_order)
                        VALUES (@itemId, @templateId, @g, @l, @o)",
                        new { itemId, templateId, g, l, o });
                    templateItems.Add((itemId, g, l, o));
                }
            }
            else
            {
                templateItems = (await Q<(Guid Id, string Group, string Label, int Sort)>(@"
                    SELECT id AS Id, group_label AS Group, label AS Label, sort_order AS Sort
                    FROM shop_inspection_template_item
                    WHERE template_id = @templateId AND is_active = true ORDER BY sort_order",
                    new { templateId })).ToList();
            }

            // A few owned bikes, serial-keyed, tied to seeded riders.
            var bikeDefs = new (string Brand, string Model, int Year, string Color, string Size, string Serial)[]
            {
                ("Yamaha", "YZ250F", 2023, "Blue", null!, "JYACG44C1PA000123"),
                ("Honda", "CRF450R", 2021, "Red", null!, "JH2PE07A5MK000456"),
                ("KTM", "350 SX-F", 2024, "Orange", null!, "VBKMXG40XRM000789"),
            };
            var bikeIds = new List<Guid>();
            for (int i = 0; i < bikeDefs.Length; i++)
            {
                var d = bikeDefs[i];
                var owner = riders.Skip(i).FirstOrDefault();
                var id = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_customer_bike (id, tenant_id, customer_user_id, customer_name, customer_phone,
                        serial, brand, model, model_year, color, size)
                    VALUES (@id, @tenantId, @uid, @name, @phone, @serial, @brand, @model, @year, @color, @size)",
                    new { id, tenantId, uid = owner?.Id,
                          name = owner is null ? null : $"{owner.FirstName} {owner.LastName}",
                          phone = $"555{_rng.Next(1000000, 9999999)}",
                          d.Serial, d.Brand, d.Model, year = d.Year, d.Color, d.Size });
                bikeIds.Add(id);
            }

            // Link the first bike to the in-progress work order so its inspection panel is populated.
            var woId = (await Q<Guid>(@"
                SELECT id FROM shop_work_order
                WHERE tenant_id = @tenantId AND customer_bike_id IS NULL AND status <> 'picked_up'
                ORDER BY created_at LIMIT 1", new { tenantId })).FirstOrDefault();
            if (woId != Guid.Empty)
                await Exec("UPDATE shop_work_order SET customer_bike_id = @bikeId WHERE id = @woId",
                    new { bikeId = bikeIds[0], woId });

            if (templateItems.Count == 0) return (bikeIds.Count, 0);

            // One completed inspection on the first bike: mostly good, a couple flagged, so the
            // customer view's three tiles and the severity ordering both have something to show.
            var flagged = new Dictionary<string, (string Rating, string Note)>
            {
                ["Front and rear pads"] = ("attention", "Front pads at ~15%, replace before next ride."),
                ["Chain wear and tension"] = ("monitor", "Slight stretch, keep an eye on it."),
                ["Tire wear and pressure"] = ("monitor", "Rear knobs rounding, still serviceable."),
            };

            async Task<Guid> Inspection(Guid bikeId, string status, DateTime at, bool grade)
            {
                var inspId = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_inspection (id, tenant_id, customer_bike_id, work_order_id, template_id,
                        performed_by_user_id, status, performed_at, next_service_date, summary_notes)
                    VALUES (@inspId, @tenantId, @bikeId, @woId, @templateId, @mechanic, @status, @at, @next, @notes)",
                    new { inspId, tenantId, bikeId,
                          woId = status == "complete" && woId != Guid.Empty ? woId : (Guid?)null,
                          templateId, mechanic, status, at,
                          next = at.AddMonths(6).Date,
                          notes = grade ? "Race-ready aside from the flagged items." : (string?)null });
                foreach (var it in templateItems)
                {
                    var (rating, note) = grade && flagged.TryGetValue(it.Label, out var f)
                        ? (f.Rating, (string?)f.Note)
                        : (grade ? "good" : "na", (string?)null);
                    await Exec(@"
                        INSERT INTO shop_inspection_result (inspection_id, template_item_id, group_label, label, rating, notes, sort_order)
                        VALUES (@inspId, @itemId, @g, @l, @rating, @note, @sort)",
                        new { inspId, itemId = it.Id, g = it.Group, l = it.Label, rating, note, sort = it.Sort });
                }
                return inspId;
            }

            await Inspection(bikeIds[0], "complete", DateTime.UtcNow.AddDays(-3), grade: true);
            await Inspection(bikeIds[0], "draft", DateTime.UtcNow.AddHours(-2), grade: false);
            return (bikeIds.Count, 2);
        }

        // ── Extra shop sales (volume + variety for the sales filters) ───────────────
        // The base bike-shop seed writes only a handful of counter sales, which leaves the sales
        // filters (date range, channel, pickup queue, status/payment) with almost nothing to act
        // on. This adds a spread of sales across statuses, tenders, channels and dates. Lines are
        // custom (no variant), so this section never touches stock or the movement ledger and the
        // bike shop's carefully-reconciled counts stay intact.
        private async Task<int> SeedExtraShopSalesAsync(Guid tenantId, List<SeedUser> riders, List<SeedUser> staff)
        {
            if (!await Any("SELECT 1 FROM shop_product WHERE tenant_id = @tenantId", new { tenantId })) return 0;
            // Online sales only come from this section, so their presence is the idempotency guard.
            if (await Any("SELECT 1 FROM shop_sale WHERE tenant_id = @tenantId AND order_channel = 'online'", new { tenantId }))
                return 0;
            // Buyer selection indexes into riders with a modulo; never divide by zero.
            if (riders.Count == 0) return 0;

            var soldBy = staff.FirstOrDefault()?.Id;
            var itemPool = new (string Name, int Price)[]
            {
                ("Goggles", 4500), ("Grip set", 1800), ("Bar pad", 2200), ("Graphics kit", 8900),
                ("Nitrile gloves", 2900), ("Chain lube", 999), ("Air filter oil", 1600),
                ("Fork seal kit", 3400), ("Sprocket bolt set", 1500), ("Number plate decals", 2500),
            };
            var tenders = new[] { "cash", "stripe", "stripe_direct", "voucher" };

            int seeded = 0;
            for (int i = 0; i < 30; i++)
            {
                // Status mix: mostly paid, a scatter of refunds, a couple pending, one failed.
                var status = (i % 10 == 3 || i % 10 == 7) ? "refunded"
                    : i % 10 == 5 ? "pending"
                    : i % 15 == 14 ? "failed"
                    : "paid";
                var tender = tenders[i % tenders.Length];
                var online = i % 3 == 0;
                var buyer = (online || i % 4 != 0) ? riders[(i * 7) % riders.Count] : null;
                var when = DateTime.UtcNow.AddDays(-_rng.Next(0, 60)).AddHours(-_rng.Next(0, 10));

                // Online paid orders alternate between collected and still-waiting so the pickup
                // queue (and its header badge) has live entries.
                DateTime? pickedUpAt = null;
                if (online && status == "paid" && i % 2 == 0) pickedUpAt = when.AddHours(2);

                var lineCount = _rng.Next(1, 4);
                var picks = Enumerable.Range(0, lineCount).Select(k => itemPool[(i + k * 3) % itemPool.Length]).ToList();
                var subtotal = picks.Sum(p => p.Price);
                var tax = (int)Math.Round(subtotal * 825 / 10000.0, MidpointRounding.AwayFromZero);

                var saleId = Guid.NewGuid();
                await Exec(@"
                    INSERT INTO shop_sale (id, tenant_id, buyer_user_id, buyer_email, buyer_name, status,
                        subtotal_cents, tax_cents, total_cents, payment_method, order_number, sold_by_user_id,
                        order_channel, picked_up_at, created_at, refunded_at, refund_note)
                    VALUES (@saleId, @tenantId, @uid, @email, @name, @status, @subtotal, @tax, @total, @tender,
                        @orderNum, @soldBy, @channel, @pickedUpAt, @at, @refundedAt, @refundNote)",
                    new
                    {
                        saleId, tenantId, uid = buyer?.Id, email = buyer?.Email,
                        name = buyer is null ? "Walk-in" : $"{buyer.FirstName} {buyer.LastName}",
                        status, subtotal, tax, total = subtotal + tax, tender,
                        orderNum = 100 + i, soldBy,
                        channel = online ? "online" : "counter", pickedUpAt, at = when,
                        refundedAt = status == "refunded" ? when.AddHours(3) : (DateTime?)null,
                        refundNote = status == "refunded" ? "Customer returned it" : (string?)null,
                    });

                foreach (var p in picks)
                {
                    var lineTax = (int)Math.Round(p.Price * 825 / 10000.0, MidpointRounding.AwayFromZero);
                    await Exec(@"
                        INSERT INTO shop_sale_line (sale_id, variant_id, quantity, name_snapshot, unit_price_cents, tax_cents, tax_rate_bps)
                        VALUES (@saleId, NULL, 1, @name, @unit, @lineTax, 825)",
                        new { saleId, name = p.Name, unit = p.Price, lineTax });
                }
                seeded++;
            }
            return seeded;
        }

        // Mirrors BikeShopRepository.DefaultMxChecklist (that copy is private to the Services
        // assembly). Kept in sync by hand; only used when a fresh tenant has no template yet, and
        // the app's lazy EnsureDefaultInspectionTemplate guards on existence so it won't duplicate.
        private static readonly (string G, string L, int O)[] DefaultMxChecklist =
        {
            ("Engine","Engine oil level and condition",10),("Engine","Oil filter",20),
            ("Engine","Air filter",30),("Engine","Coolant level",40),("Engine","Radiators and hoses",50),
            ("Engine","Spark plug",60),("Engine","Valve clearance",70),("Engine","Top-end hours",80),
            ("Engine","Exhaust / silencer packing",90),
            ("Drivetrain","Chain wear and tension",110),("Drivetrain","Front and rear sprockets",120),
            ("Drivetrain","Chain slider and rollers",130),("Drivetrain","Clutch free play and plates",140),
            ("Suspension","Fork seals and oil",210),("Suspension","Fork action",220),
            ("Suspension","Shock seals and action",230),("Suspension","Linkage bearings",240),
            ("Suspension","Swingarm bearings",250),("Suspension","Race sag",260),
            ("Brakes","Front and rear pads",310),("Brakes","Rotors",320),("Brakes","Fluid and lines",330),
            ("Wheels & tires","Tire wear and pressure",410),("Wheels & tires","Spoke tension",420),
            ("Wheels & tires","Rim condition",430),("Wheels & tires","Wheel bearings",440),
            ("Controls","Throttle action and cable",510),("Controls","Clutch lever and cable",520),
            ("Controls","Grips and bar mounts",530),
            ("Chassis","Frame and subframe",610),("Chassis","Steering head bearings",620),
            ("Chassis","Footpegs and shifter",630),("Chassis","Bolt torque",640),
        };

        private string RandomCode(int len)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = alphabet[_rng.Next(alphabet.Length)];
            return new string(chars);
        }
    }
}
