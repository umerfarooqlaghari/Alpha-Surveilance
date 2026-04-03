import { getAuthHeaders } from '@/lib/utils/auth';

export interface Violation {
    id: string;
    type: string;
    severity?: string | number;
    timestamp: string;
    framePath?: string;
    cameraId?: string;
    cameraName?: string;
    sopName?: string;
    violationTypeName?: string;
    status: string;
}

const API_BASE = '/api/tenant/violations';

export async function getViolations(): Promise<Violation[]> {
    const response = await fetch(API_BASE, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch violations');
    }

    return response.json();
}

export async function getViolation(id: string): Promise<Violation> {
    const response = await fetch(`${API_BASE}/${id}`, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch violation');
    }

    return response.json();
}

export interface AnalyticsResponse {
    summary: {
        totalViolations: number;
        activeViolations: number;
        resolvedViolations: number;
        criticalViolations: number;
    };
    dailyTrends: { date: string; count: number }[];
    byCategory: { type: string; count: number }[];
    bySeverity: { severity: string; count: number }[];
    hourlyHeatmap: { hour: number; count: number }[];
    byCamera: { cameraName: string; count: number }[];
}

export async function getAnalytics(filters?: { startDate?: string; endDate?: string; cameraId?: string }): Promise<AnalyticsResponse> {
    const params = new URLSearchParams();
    if (filters?.startDate) params.append('startDate', filters.startDate);
    if (filters?.endDate) params.append('endDate', filters.endDate);
    if (filters?.cameraId) params.append('cameraId', filters.cameraId);

    const response = await fetch(`${API_BASE}/analytics?${params.toString()}`, {
        headers: getAuthHeaders()
    });

    if (!response.ok) {
        throw new Error('Failed to fetch analytics');
    }

    return response.json();
}
