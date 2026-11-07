using Services.Repositories.Interfaces;
using Services.Storage;

namespace webapi.Workers
{
    /// <summary>
    /// Deletes private images (rider gate photos) whose rider_photo row is gone or was pointed at a new
    /// image. The Script0307 trigger queues the old key in private_image_trash on every such delete or
    /// update, including the ON DELETE CASCADE when an account or a tenant is deleted, so no deletion
    /// path (there is no account-deletion endpoint today; tenants are deleted by TenantSync) can leave a
    /// rider's photo behind. An image a live row points at again is never deleted.
    /// </summary>
    public class PrivateImageTrashWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<PrivateImageTrashWorker> _logger;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

        public PrivateImageTrashWorker(IServiceProvider services, ILogger<PrivateImageTrashWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Private image trash sweep failed"); }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        public async Task<int> RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var photos = scope.ServiceProvider.GetRequiredService<IRiderPhotoRepository>();
            var storage = scope.ServiceProvider.GetRequiredService<IImageStorage>();
            var done = 0;
            foreach (var item in await photos.ListTrash(200))
            {
                if (ct.IsCancellationRequested) break;
                if (!await photos.IsReferenced(item.TenantId, item.StorageKey))
                    await storage.DeletePrivateAsync(item.StorageKey, ct);
                await photos.RemoveFromTrash(item.Id, item.TenantId);
                done++;
            }
            if (done > 0) _logger.LogInformation("Deleted {Count} dropped private images", done);
            return done;
        }
    }
}
