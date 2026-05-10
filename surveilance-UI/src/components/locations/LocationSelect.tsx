'use client';

import { useEffect, useState } from 'react';
import type { Location } from '@/types/location';
import { getLocations as getTenantLocations } from '@/lib/api/tenant/locations';
import { getLocations as getAdminLocations } from '@/lib/api/locations';

interface LocationSelectProps {
    value: string | null | undefined;
    onChange: (locationId: string | null) => void;
    label?: string;
    /** Show an "All locations" / "Unassigned" entry as the first option. */
    includeUnassigned?: boolean;
    /** Text for the "All / unassigned" option. */
    unassignedLabel?: string;
    disabled?: boolean;
    className?: string;
    required?: boolean;
    /**
     * If provided, the component fetches locations via the admin endpoint
     * (`/api/admin/locations?tenantId=...`). Otherwise it uses the tenant
     * endpoint scoped to the JWT.
     */
    tenantId?: string;
}

/**
 * Tenant-scoped Location dropdown.
 * `value` is the location GUID (or null for "no location").
 */
export default function LocationSelect({
    value,
    onChange,
    label,
    includeUnassigned = true,
    unassignedLabel = 'No location',
    disabled = false,
    className = '',
    required = false,
    tenantId,
}: LocationSelectProps) {
    const [locations, setLocations] = useState<Location[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                setLoading(true);
                const data = tenantId
                    ? await getAdminLocations(tenantId)
                    : await getTenantLocations();
                if (!cancelled) setLocations(data);
            } catch (e: any) {
                if (!cancelled) setError(e?.message || 'Failed to load locations');
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [tenantId]);

    return (
        <div className={className}>
            {label && (
                <label className="block text-sm font-semibold text-gray-700 mb-2">
                    {label}
                    {required && <span className="text-red-500 ml-1">*</span>}
                </label>
            )}
            <select
                value={value ?? ''}
                onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
                disabled={disabled || loading}
                required={required}
                className="w-full px-4 py-2.5 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black disabled:bg-gray-50 disabled:text-gray-500"
            >
                {includeUnassigned && <option value="">{loading ? 'Loading...' : unassignedLabel}</option>}
                {locations.map((loc) => (
                    <option key={loc.id} value={loc.id}>
                        {loc.name} ({loc.code})
                    </option>
                ))}
            </select>
            {error && <p className="text-xs text-red-500 mt-1">{error}</p>}
            {!loading && !error && locations.length === 0 && (
                <p className="text-[10px] text-gray-400 mt-1">No locations defined yet.</p>
            )}
        </div>
    );
}
