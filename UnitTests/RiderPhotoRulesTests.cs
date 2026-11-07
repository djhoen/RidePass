using NUnit.Framework;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Riders;
using Services.Storage;

namespace UnitTests
{
    // Whose photo is on file, where it comes from, and what upload is accepted. A wrong answer either
    // shows staff the wrong face (a parent's profile photo for their child) or stores something that
    // isn't an image.
    [TestFixture]
    public class RiderPhotoRulesTests
    {
        private static readonly User Parent = new() { Id = Guid.NewGuid(), FirstName = "Pat", LastName = "Rojas", ImageUrl = "/uploads/platform/avatar-pat.jpg" };
        private static readonly RiderPhoto Taken = new() { Id = Guid.NewGuid(), TakenAt = new DateTime(2026, 11, 1, 15, 0, 0) };

        [Test]
        public void The_venue_photo_wins_then_the_pass_photo_then_the_account_photo()
        {
            Assert.That(RiderPhotoRules.Resolve(Taken, "data:image/jpeg;base64,x", Parent, true)!.Source, Is.EqualTo(RiderPhotoSource.Rider));
            Assert.That(RiderPhotoRules.Resolve(null, "data:image/jpeg;base64,x", Parent, true)!.Source, Is.EqualTo(RiderPhotoSource.Pass));
            var account = RiderPhotoRules.Resolve(null, null, Parent, true);
            Assert.That(account!.Source, Is.EqualTo(RiderPhotoSource.Account));
            Assert.That(account.Url, Is.EqualTo(Parent.ImageUrl));
        }

        [Test]
        public void A_parent_profile_photo_never_stands_in_for_their_child()
        {
            Assert.That(RiderPhotoRules.Resolve(null, null, Parent, accountIsRider: false), Is.Null);
        }

        [Test]
        public void Photos_are_filed_under_the_account_else_the_buyer_email_and_the_rider_name()
        {
            var id = Guid.NewGuid();
            Assert.That(RiderPhotoRules.OwnerKey(id, "x@y.test"), Is.EqualTo($"user:{id:D}"));
            Assert.That(RiderPhotoRules.OwnerKey(null, "  Walk.Up@Y.test "), Is.EqualTo("email:walk.up@y.test"));
            Assert.That(RiderPhotoRules.OwnerKey(null, " "), Is.Null);
            // The credential's OWN rider name, never the buyer's: naming nobody files it under the credential.
            var kid = RiderPhotoRules.Key(Parent, "p@y.test", "Ana", "Rojas", "pass", Guid.NewGuid());
            Assert.That((kid.OwnerKey, kid.RiderKey), Is.EqualTo(($"user:{Parent.Id:D}", "ana rojas")));
            var passId = Guid.NewGuid();
            var unregistered = RiderPhotoRules.Key(Parent, "p@y.test", null, null, "pass", passId);
            Assert.That((unregistered.OwnerKey, unregistered.RiderKey), Is.EqualTo(($"pass:{passId:D}", "credential")));
            // A parent's profile photo: only for a credential that names them.
            Assert.That(RiderPhotoRules.AccountIsNamedRider(Parent, null, null), Is.False);
            Assert.That(RiderPhotoRules.AccountIsNamedRider(Parent, "Ana", "Rojas"), Is.False);
            Assert.That(RiderPhotoRules.AccountIsNamedRider(Parent, "pat", "ROJAS"), Is.True);
        }

        [Test]
        public void With_nothing_to_key_by_the_photo_is_filed_under_the_credential_itself()
        {
            var ticketId = Guid.NewGuid();
            var k = RiderPhotoRules.Key(null, "walk.up@y.test", null, null, "ticket", ticketId);
            Assert.That(k.OwnerKey, Is.EqualTo($"ticket:{ticketId:D}"));
            Assert.That(k.RiderKey, Is.EqualTo(RiderPhotoRules.CredentialRiderKey));
            Assert.That(k.UserId, Is.Null);
            var named = RiderPhotoRules.Key(null, " Walk.Up@Y.test", "Ana", "Rojas", "ticket", ticketId);
            Assert.That((named.OwnerKey, named.RiderKey, named.OwnerEmail), Is.EqualTo(("email:walk.up@y.test", "ana rojas", "walk.up@y.test")));
            var account = RiderPhotoRules.Key(Parent, "x@y.test", "Ana", "Rojas", "pass", Guid.NewGuid());
            Assert.That((account.OwnerKey, account.UserId), Is.EqualTo(($"user:{Parent.Id:D}", (Guid?)Parent.Id)));
        }

        [Test]
        public void A_ticket_naming_nobody_is_the_buyers_own_photo()
        {
            var ticketId = Guid.NewGuid();
            // The buyer's account: keyed by the account's own name (as the ID check keys it), so a photo on
            // file for them (a kiosk photo, an earlier named ticket) is found.
            var mine = RiderPhotoRules.Key(Parent, "p@y.test", null, null, "ticket", ticketId, "Pat Rojas");
            Assert.That((mine.OwnerKey, mine.RiderKey), Is.EqualTo(($"user:{Parent.Id:D}", "pat rojas")));
            // A walk-up with only an email: their purchaser name.
            var walkUp = RiderPhotoRules.Key(null, "walk.up@y.test", null, null, "ticket", ticketId, " Zed  Walker ");
            Assert.That((walkUp.OwnerKey, walkUp.RiderKey), Is.EqualTo(("email:walk.up@y.test", "zed walker")));
            // Nobody to key by: the ticket itself.
            var none = RiderPhotoRules.Key(null, null, null, null, "ticket", ticketId, "Zed");
            Assert.That(none.OwnerKey, Is.EqualTo($"ticket:{ticketId:D}"));
            // An unregistered pass stays under the pass (its holder may not be the buyer).
            var passId = Guid.NewGuid();
            Assert.That(RiderPhotoRules.Key(Parent, "p@y.test", null, null, "pass", passId, "Pat Rojas").OwnerKey,
                Is.EqualTo($"pass:{passId:D}"));
        }

        [Test]
        public void Only_a_platform_or_this_tenants_account_counts_as_the_buyer()
        {
            var tenant = Guid.NewGuid();
            Assert.That(BuyerAccounts.ForTenant(Parent, tenant), Is.SameAs(Parent));   // platform account
            var own = new User { Id = Guid.NewGuid(), TenantId = tenant };
            Assert.That(BuyerAccounts.ForTenant(own, tenant), Is.SameAs(own));
            var other = new User { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ImageUrl = "/x.jpg" };
            Assert.That(BuyerAccounts.ForTenant(other, tenant), Is.Null);
            Assert.That(BuyerAccounts.ForTenant(null, tenant), Is.Null);
        }

        [Test]
        public void Only_real_jpeg_or_png_data_within_bounds_is_accepted()
        {
            var jpeg = new byte[2000]; jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF;
            var png = new byte[2000]; png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/jpeg;base64," + Convert.ToBase64String(jpeg))!.Value.Extension, Is.EqualTo(".jpg"));
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/png;base64," + Convert.ToBase64String(png))!.Value.Extension, Is.EqualTo(".png"));
            // Labelled JPEG, but the bytes are a PNG (or anything else): refused.
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/jpeg;base64," + Convert.ToBase64String(png)), Is.Null);
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/svg+xml;base64," + Convert.ToBase64String(jpeg)), Is.Null);
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/jpeg;base64,AAAA"), Is.Null);   // too small
            Assert.That(RiderPhotoRules.DecodePhoto("data:image/jpeg;base64," + new string('A', 2_000_000)), Is.Null);   // too big
        }

        [Test]
        public void Private_image_keys_name_their_tenant_and_cannot_escape_the_private_area()
        {
            var tenant = Guid.NewGuid();
            var key = PrivateImageKey.New(tenant, "rider-photo", ".jpg");
            Assert.That(PrivateImageKey.IsValid(key), Is.True);
            Assert.That(PrivateImageKey.TenantOf(key), Is.EqualTo(tenant));
            Assert.That(PrivateImageKey.IsValid($"private/{tenant:D}/../../appsettings.json"), Is.False);
            Assert.That(PrivateImageKey.IsValid($"uploads/{tenant:D}/rider-photo-{Guid.NewGuid():N}.jpg"), Is.False);
            Assert.That(PrivateImageKey.IsValid(null), Is.False);
        }
    }
}
