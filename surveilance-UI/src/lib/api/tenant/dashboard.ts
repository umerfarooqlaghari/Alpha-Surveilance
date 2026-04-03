import { getAuthHeaders } from '@/lib/utils/auth';

export interface DashboardStats {
    totalCameras: number;
    activeViolations: number;
    resolvedToday: number;
}

const API_BASE = '/api/tenant/dashboard';

export async function getStats(): Promise<DashboardStats> {
    const response = await fetch(`${API_BASE}/stats`, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch dashboard stats');
    }

    return response.json();
}

export async function getRecentViolations(): Promise<any[]> {
    const response = await fetch(`${API_BASE}/violations/recent`, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch recent violations');
    }

    return response.json();
}
