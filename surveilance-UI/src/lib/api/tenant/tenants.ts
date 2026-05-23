import { apiFetch } from '@/lib/utils/auth';

const API_BASE = '/api/tenant/tenants';

export async function getMyModules(): Promise<string[]> {
    const response = await apiFetch(`${API_BASE}/my-modules`, { cache: 'no-store' });

    if (!response.ok) {
        throw new Error('Failed to fetch modules');
    }

    return response.json();
}
