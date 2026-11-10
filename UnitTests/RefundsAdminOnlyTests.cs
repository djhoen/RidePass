using NUnit.Framework;
using webapi.AuthPolicies;

namespace UnitTests
{
    // A venue can keep refunds for its Admins (Script0314): Managers lose only the refund permissions.
    [TestFixture]
    public class RefundsAdminOnlyTests
    {
        [Test]
        public void Off_changes_nothing() =>
            Assert.That(TenantPermissions.ForRolesAt(new[] { "tenant_manager" }, false),
                Does.Contain(TenantPermissions.SalesRefund).And.Contain(TenantPermissions.SalesRefundOverride));

        [Test]
        public void On_a_manager_keeps_everything_but_refunds()
        {
            var held = TenantPermissions.ForRolesAt(new[] { "tenant_manager" }, true);
            Assert.That(held, Does.Not.Contain(TenantPermissions.SalesRefund));
            Assert.That(held, Does.Not.Contain(TenantPermissions.SalesRefundOverride));
            Assert.That(held, Does.Contain(TenantPermissions.SalesCancel));
            Assert.That(held, Does.Contain(TenantPermissions.SalesCounter));
        }

        [Test]
        public void On_an_admin_keeps_refunds() =>
            Assert.That(TenantPermissions.ForRolesAt(new[] { "tenant_admin" }, true),
                Does.Contain(TenantPermissions.SalesRefund).And.Contain(TenantPermissions.SalesRefundOverride));

        [Test]
        public void On_a_manager_who_is_also_an_admin_keeps_refunds() =>
            Assert.That(TenantPermissions.ForRolesAt(new[] { "tenant_manager", "tenant_admin" }, true),
                Does.Contain(TenantPermissions.SalesRefund));
    }
}
