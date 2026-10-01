using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services
{
    /// <summary>
    /// Enterprise multi-tiered caching implementation supporting L1 in-memory
    /// and L2 distributed cache (Redis) with automatic circuit breaker / fallback.
    /// </summary>
    public class ViolationCacheService : IViolationCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache? _distributedCache;
        private readonly ILogger<ViolationCacheService> _logger;
        private static readonly TimeSpan DefaultDebounceCooldown = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultActiveViolationTtl = TimeSpan.FromHours(1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ViolationCacheService(
            IMemoryCache memoryCache,
            ILogger<ViolationCacheService> logger,
            IDistributedCache? distributedCache = null)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _distributedCache = distributedCache;
        }

        public async Task<bool> IsAlertDebouncedAsync(
            Guid tenantId,
            string cameraId,
            Guid? sopViolationTypeId,
            TimeSpan? cooldown = null)
        {
            if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(cameraId))
            {
                return false;
            }

            // A zero or negative cooldown disables debouncing
            if (cooldown.HasValue && cooldown.Value <= TimeSpan.Zero)
            {
                return false;
            }

            var cleanCamId = cameraId.Trim().ToLowerInvariant();
            var duration = cooldown ?? DefaultDebounceCooldown;
            var key = $"Alert_{tenantId}_{cleanCamId}_{sopViolationTypeId?.ToString() ?? "All"}";

            // 1. Check L1 Memory Cache (Fastest, 0 latency)
            if (_memoryCache.TryGetValue(key, out _))
            {
                return true;
            }

            // 2. Check L2 Distributed Cache if configured
            if (_distributedCache != null)
            {
                try
                {
                    var distributedVal = await _distributedCache.GetStringAsync(key);
                    if (!string.IsNullOrEmpty(distributedVal))
                    {
                        // Sync to L1
                        _memoryCache.Set(key, true, duration);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache check failed for key {Key}. Falling back to in-memory cache.", key);
                }
            }

            // Not debounced -> Arm the cooldown in both L1 and L2
            _memoryCache.Set(key, true, duration);

            if (_distributedCache != null)
            {
                try
                {
                    await _distributedCache.SetStringAsync(
                        key,
                        "1",
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache set failed for key {Key}.", key);
                }
            }

            return false;
        }

        public async Task<string?> GetActiveViolationIdAsync(string cameraDbId, long trackId)
        {
            if (string.IsNullOrWhiteSpace(cameraDbId) || trackId < 0)
            {
                return null;
            }

            var cleanCamId = cameraDbId.Trim().ToLowerInvariant();
            var key = $"ActiveViol_{cleanCamId}_{trackId}";

            if (_memoryCache.TryGetValue(key, out string? memoryVal) && !string.IsNullOrEmpty(memoryVal))
            {
                return memoryVal;
            }

            if (_distributedCache != null)
            {
                try
                {
                    var distributedVal = await _distributedCache.GetStringAsync(key);
                    if (!string.IsNullOrEmpty(distributedVal))
                    {
                        _memoryCache.Set(key, distributedVal, DefaultActiveViolationTtl);
                        return distributedVal;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache read failed for key {Key}.", key);
                }
            }

            return null;
        }

        public async Task SetActiveViolationIdAsync(string cameraDbId, long trackId, string violationId, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(cameraDbId) || trackId < 0 || string.IsNullOrWhiteSpace(violationId))
            {
                return;
            }

            var cleanCamId = cameraDbId.Trim().ToLowerInvariant();
            var duration = (ttl.HasValue && ttl.Value > TimeSpan.Zero) ? ttl.Value : DefaultActiveViolationTtl;
            var key = $"ActiveViol_{cleanCamId}_{trackId}";

            _memoryCache.Set(key, violationId, duration);

            if (_distributedCache != null)
            {
                try
                {
                    await _distributedCache.SetStringAsync(
                        key,
                        violationId,
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache write failed for key {Key}.", key);
                }
            }
        }

        public async Task RemoveActiveViolationIdAsync(string cameraDbId, long trackId)
        {
            if (string.IsNullOrWhiteSpace(cameraDbId) || trackId < 0)
            {
                return;
            }

            var cleanCamId = cameraDbId.Trim().ToLowerInvariant();
            var key = $"ActiveViol_{cleanCamId}_{trackId}";
            _memoryCache.Remove(key);

            if (_distributedCache != null)
            {
                try
                {
                    await _distributedCache.RemoveAsync(key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache remove failed for key {Key}.", key);
                }
            }
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return default;
            }

            if (_memoryCache.TryGetValue(key, out T? memoryVal))
            {
                return memoryVal;
            }

            if (_distributedCache != null)
            {
                try
                {
                    var raw = await _distributedCache.GetStringAsync(key);
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var deserialized = JsonSerializer.Deserialize<T>(raw, JsonOptions);
                        if (deserialized != null)
                        {
                            _memoryCache.Set(key, deserialized, TimeSpan.FromMinutes(5));
                            return deserialized;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache get failed for key {Key}.", key);
                }
            }

            return default;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
            {
                return;
            }

            var duration = (ttl.HasValue && ttl.Value > TimeSpan.Zero) ? ttl.Value : TimeSpan.FromMinutes(10);
            _memoryCache.Set(key, value, duration);

            if (_distributedCache != null)
            {
                try
                {
                    var raw = JsonSerializer.Serialize(value, JsonOptions);
                    await _distributedCache.SetStringAsync(
                        key,
                        raw,
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache set failed for key {Key}.", key);
                }
            }
        }

        public async Task RemoveAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _memoryCache.Remove(key);

            if (_distributedCache != null)
            {
                try
                {
                    await _distributedCache.RemoveAsync(key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed cache remove failed for key {Key}.", key);
                }
            }
        }
    }
}
