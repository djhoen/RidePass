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
            // The child under the parent's account, and the parent's own (credential naming nobody).
            Assert.That(RiderPhotoRules.RiderKey("Ana", "Rojas", Parent), Is.EqualTo("ana rojas"));
            Assert.That(RiderPhotoRules.RiderKey(null, null, Parent), Is.EqualTo("pat rojas"));
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
