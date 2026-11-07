using Services.Repositories.Data.WristbandData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Daily rider wristbands (Script0312). Every query is tenant-scoped.</summary>
    public interface IRiderDayBandRepository
    {
        /// <summary>The rider's active band on that business date, if any.</summary>
        Task<RiderDayBand?> GetActiveForRider(Guid tenantId, DateOnly date, string ownerKey, string riderKey);

        /// <summary>The active band with that code on that business date, if any.</summary>
        Task<RiderDayBand?> GetActiveByCode(Guid tenantId, DateOnly date, string code);

        /// <summary>Active bands with that code whose business date is within a day of <paramref name="date"/>
        /// (a band issued the evening before its day is on a wrist both days), nearest first.</summary>
        Task<List<RiderDayBand>> ListActiveByCodeNear(Guid tenantId, DateOnly date, string code);

        /// <summary>The active bands of these riders (owner key + rider key) on these business dates, in one read.</summary>
        Task<List<RiderDayBand>> ListActiveForRiders(Guid tenantId, IReadOnlyCollection<DateOnly> dates,
            IReadOnlyCollection<(string OwnerKey, string RiderKey)> riders);

        /// <summary>Inserts a band and links the admission, in one transaction. Null when the rider or the
        /// code already has an active band that day (a race the caller's checks lost).</summary>
        Task<RiderDayBand?> Issue(RiderDayBand band, Guid? reservationId, Guid? ticketId);

        /// <summary>Voids <paramref name="oldBandId"/> with a reason, inserts its replacement and moves the
        /// old band's admissions to it, in one transaction. Null on a unique conflict (the new code is
        /// active on someone else that day) or when the old band is no longer active.</summary>
        Task<RiderDayBand?> Replace(Guid oldBandId, RiderDayBand band, string reason, Guid? staffUserId);

        /// <summary>Ties another admission to a band (idempotent).</summary>
        Task LinkAdmission(Guid tenantId, Guid bandId, Guid? reservationId, Guid? ticketId, Guid? staffUserId);

        /// <summary>Takes one admission off whatever band covers it (the band stays worn; the row is kept, unlinked, so a later band on it is a correction). Returns rows unlinked.</summary>
        Task<int> UnlinkReservation(Guid tenantId, Guid reservationId);

        /// <summary>Takes one ticket off whatever bands cover it (kept, unlinked, as above). Returns rows unlinked.</summary>
        Task<int> UnlinkTicket(Guid tenantId, Guid ticketId);

        /// <summary>The band covers, or covered until it was unlinked, that admission (a reservation or a ticket).</summary>
        Task<bool> Covers(Guid tenantId, Guid bandId, Guid? reservationId, Guid? ticketId);

        /// <summary>The admissions a band covers.</summary>
        Task<List<RiderDayBandAdmissionRow>> ListAdmissions(Guid tenantId, Guid bandId);

        /// <summary>Active band codes covering these reservations, keyed by reservation id.</summary>
        Task<Dictionary<Guid, string>> GetActiveCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId);

        /// <summary>Active band codes covering these tickets (a multi-day ticket: its latest day's band).</summary>
        Task<Dictionary<Guid, string>> GetActiveCodesForTickets(IEnumerable<Guid> ticketIds, Guid tenantId);
    }
}
