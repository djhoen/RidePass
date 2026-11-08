using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Riders
{
    /// <summary>How a credential's rider was identified.</summary>
    public enum RiderKeyingKind
    {
        /// <summary>By name under the buying account (every credential before Script0309).</summary>
        Names,
        /// <summary>The ticket names the rider's own account (a Gate Sale friend, Script0309).</summary>
        RiderAccount,
        /// <summary>The ticket names a minor linked to their guardian (rider_dependent, Script0309).</summary>
        Dependent,
        /// <summary>
        /// One of several rider tickets the buyer bought for an event that name nobody (a parent buying three
        /// unnamed entries): each is its own rider, keyed by the ticket ("ticket:{id}"), never the buyer.
        /// </summary>
        PerTicket,
    }

    /// <summary>
    /// Whose per-rider records (ID check, rider photo, account signature, birthdate, email) a credential
    /// reads and writes. <see cref="Account"/> is the (tenant-filtered) account they are keyed under and
    /// <see cref="RiderKey"/> the rider under it; <see cref="AccountIsRider"/> says whether that account IS
    /// the rider (its birthdate, email, profile photo and signatures are the rider's own).
    /// </summary>
    public sealed record RiderKeying(RiderKeyingKind Kind, User? Account, string RiderKey, bool AccountIsRider)
    {
        public const string DependentKeyPrefix = "dependent:";
    }

    /// <summary>
    /// The ONE rule for "who is this ticket's rider", shared by the gate, the ID check recorder and the
    /// rider photo recorder. When a ticket carries a profile identity (Script0309) that identity decides,
    /// never a name match to the payer: a dependent is keyed under their guardian by the dependent row's id
    /// (stable, not a name, so a son named like his father is never the father), and a rider with their own
    /// account is keyed under that account (it IS the rider). Otherwise the name under the buying account,
    /// as before.
    /// </summary>
    public static class TicketRiderKeys
    {
        /// <param name="purchaser">The buying account, tenant-filtered (BuyerAccounts.ForTenant).</param>
        /// <param name="riderAccount">The ticket's rider_user_id account, tenant-filtered; null when absent.</param>
        /// <param name="guardian">The dependent's guardian account, tenant-filtered; null when absent.</param>
        public static RiderKeying Resolve(User? purchaser, string? riderFirst, string? riderLast,
            Guid? riderUserId, Guid? riderDependentId, User? riderAccount, RiderDependent? dependent, User? guardian)
        {
            if (riderDependentId is Guid depId)
                // A minor on someone else's (or their guardian's) order: never the buying account.
                return new RiderKeying(RiderKeyingKind.Dependent, dependent is not null ? guardian : null,
                    $"{RiderKeying.DependentKeyPrefix}{depId:D}", false);
            if (riderUserId is Guid ruid && ruid != (purchaser?.Id ?? Guid.Empty))
                return riderAccount is not null
                    ? new RiderKeying(RiderKeyingKind.RiderAccount, riderAccount,
                        IdCheckRules.RiderKey(null, null, riderAccount.FirstName, riderAccount.LastName), true)
                    // The rider's account isn't usable here: key by name only, under no account.
                    : new RiderKeying(RiderKeyingKind.RiderAccount, null, IdCheckRules.Normalize($"{riderFirst} {riderLast}"), false);
            return FromNames(purchaser, riderFirst, riderLast);
        }

        /// <summary>A rider who is their own account (an adult kiosk sign-up, a Gate Sale friend).</summary>
        public static RiderKeying ForRiderAccount(User account) =>
            new(RiderKeyingKind.RiderAccount, account, IdCheckRules.RiderKey(null, null, account.FirstName, account.LastName), true);

        /// <summary>A minor linked to their guardian (rider_dependent): keyed under the guardian by the row id.</summary>
        public static RiderKeying ForDependent(User guardian, Guid dependentId) =>
            new(RiderKeyingKind.Dependent, guardian, $"{RiderKeying.DependentKeyPrefix}{dependentId:D}", false);

        /// <summary>An unnamed rider ticket that is not the buyer's only one for its event: keyed by the ticket itself
        /// (the same "ticket:{id}" key the photo store used for a credential before rider keys existed).</summary>
        public static RiderKeying PerTicket(Guid ticketId) =>
            new(RiderKeyingKind.PerTicket, null, $"ticket:{ticketId:D}", false);

        /// <summary>A rider ticket that names nobody.</summary>
        public static bool IsUnnamed(EventTicketPurchase t) =>
            t.RiderUserId is null && t.RiderDependentId is null
            && IdCheckRules.Normalize($"{t.RiderFirstName} {t.RiderLastName}").Length == 0;

        /// <summary>The name-based keying (passes, and tickets without a profile identity).</summary>
        public static RiderKeying FromNames(User? account, string? riderFirst, string? riderLast) =>
            account is null
                ? new RiderKeying(RiderKeyingKind.Names, null, IdCheckRules.Normalize($"{riderFirst} {riderLast}"), false)
                : new RiderKeying(RiderKeyingKind.Names, account,
                    IdCheckRules.RiderKey(riderFirst, riderLast, account.FirstName, account.LastName),
                    IdCheckRules.AccountIsRider(riderFirst, riderLast, account.FirstName, account.LastName));
    }

    /// <summary>Resolves <see cref="RiderKeying"/> for a ticket (the lookups behind TicketRiderKeys).</summary>
    public interface ITicketRiderIdentity
    {
        Task<RiderKeying> ForTicket(EventTicketPurchase ticket, Guid tenantId);

        /// <summary>
        /// A ticket that names nobody is the buyer's own ONLY when it is the buyer's single unnamed rider ticket
        /// for that event; otherwise it is one of several riders the buyer brought, keyed per ticket.
        /// </summary>
        Task<bool> IsSharedUnnamed(EventTicketPurchase ticket, Guid tenantId);
    }

    /// <summary>Scoped; memoises the accounts and dependents it reads for the request.</summary>
    public class TicketRiderIdentity : ITicketRiderIdentity
    {
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly Dictionary<Guid, User?> _userCache = new();
        private readonly Dictionary<Guid, RiderDependent?> _depCache = new();
        private readonly Dictionary<Guid, bool> _sharedCache = new();
        private readonly IEventTicketPurchaseRepository _tickets;

        public TicketRiderIdentity(IUserRepository users, IRiderDependentRepository dependents, IEventTicketPurchaseRepository tickets)
        {
            _tickets = tickets;
            _users = users;
            _dependents = dependents;
        }

        public async Task<RiderKeying> ForTicket(EventTicketPurchase ticket, Guid tenantId)
        {
            var purchaser = ticket.PurchaserUserId is Guid pid ? await Account(pid, tenantId) : null;
            User? riderAccount = null, guardian = null;
            RiderDependent? dependent = null;
            // Several unnamed rider tickets from one buyer for one event: each its own rider.
            if (await IsSharedUnnamed(ticket, tenantId)) return TicketRiderKeys.PerTicket(ticket.Id);
            if (ticket.RiderDependentId is Guid did)
            {
                if (!_depCache.TryGetValue(did, out dependent))
                    _depCache[did] = dependent = await _dependents.GetById(did, tenantId);
                if (dependent is not null) guardian = await Account(dependent.GuardianUserId, tenantId);
            }
            else if (ticket.RiderUserId is Guid ruid)
            {
                riderAccount = await Account(ruid, tenantId);
            }
            return TicketRiderKeys.Resolve(purchaser, ticket.RiderFirstName, ticket.RiderLastName,
                ticket.RiderUserId, ticket.RiderDependentId, riderAccount, dependent, guardian);
        }

        public async Task<bool> IsSharedUnnamed(EventTicketPurchase ticket, Guid tenantId)
        {
            if (!TicketRiderKeys.IsUnnamed(ticket)) return false;
            if (_sharedCache.TryGetValue(ticket.Id, out var shared)) return shared;
            var (count, included) = await _tickets.CountUnnamedRiderTickets(ticket, tenantId);
            return _sharedCache[ticket.Id] = included && count > 1;
        }

        private async Task<User?> Account(Guid id, Guid tenantId)
        {
            if (id == Guid.Empty) return null;
            if (!_userCache.TryGetValue(id, out var u)) _userCache[id] = u = await _users.GetById(id);
            return BuyerAccounts.ForTenant(u, tenantId);
        }
    }
}
