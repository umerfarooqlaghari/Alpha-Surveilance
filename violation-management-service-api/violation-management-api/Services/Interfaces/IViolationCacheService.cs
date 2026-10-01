using System;
using System.Threading.Tasks;

namespace violation_management_api.Services.Interfaces
{
    /// <summary>
    /// Enterprise multi-tiered caching service for violation management.
    /// Handles outbound alert debouncing, active violation state tracking,
    /// and distributed Redis synchronization with resilient in-memory fallback.
    /// </summary>
    public interface IViolationCacheService
    {
        /// <summary>
        /// Checks whether an outbound alert for a specific camera and SOP violation type
        /// is currently suppressed by a debouncing cooldown window.
        /// If not debounced, records the alert and arms the cooldown.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="cameraId">The camera identifier or database ID.</param>
        /// <param name="sopViolationTypeId">The SOP violation type identifier.</param>
        /// <param name="cooldown">Optional cooldown duration (defaults to 10 seconds).</param>
        /// <returns>True if debounced (suppressed); False if alert should be emitted.</returns>
        Task<bool> IsAlertDebouncedAsync(Guid tenantId, string cameraId, Guid? sopViolationTypeId, TimeSpan? cooldown = null);

        /// <summary>
        /// Retrieves the active violation UUID for a specific camera and track ID if present.
        /// </summary>
        Task<string?> GetActiveViolationIdAsync(string cameraDbId, long trackId);

        /// <summary>
        /// Records an ongoing active violation UUID against a camera and track ID.
        /// </summary>
        Task SetActiveViolationIdAsync(string cameraDbId, long trackId, string violationId, TimeSpan? ttl = null);

        /// <summary>
        /// Evicts active violation tracking for a track when resolved or exited.
        /// </summary>
        Task RemoveActiveViolationIdAsync(string cameraDbId, long trackId);

        /// <summary>
        /// Generic strongly-typed cache retrieval.
        /// </summary>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// Generic strongly-typed cache write.
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan? ttl = null);

        /// <summary>
        /// Generic cache key removal.
        /// </summary>
        Task RemoveAsync(string key);
    }
}
