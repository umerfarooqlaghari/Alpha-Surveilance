import { apiFetch } from '@/lib/utils/auth';

import { Employee } from '@/types/employee';

export interface Violation {
    id: string;
    type: string;
    severity?: string | number;
    timestamp: string;
    framePath?: string;
    frameUrl?: string;       // pre-signed S3 URL (24 h), populated server-side
    cameraId?: string;
    cameraName?: string;
    sopName?: string;
    violationTypeName?: string;
    modelIdentifier?: string;
    status: string;
    employeeId?: string;
    employee?: Employee;
    metadataJson?: string;
    // False-positive metadata. `isFalsePositive=true` rows are returned only
    // from getFalsePositiveViolations(); the standard getViolations() hides them.
    isFalsePositive?: boolean;
    falsePositiveMarkedAt?: string;
    falsePositiveMarkedBy?: string;
    falsePositiveReason?: string;
}

const API_BASE = '/api/tenant/violations';

export async function getViolations(): Promise<Violation[]> {
    const response = await apiFetch(API_BASE);

    if (!response.ok) {
        throw new Error('Failed to fetch violations');
    }

    return response.json();
}

export async function getFalsePositiveViolations(): Promise<Violation[]> {
    const response = await apiFetch(`${API_BASE}/false-positives`);
    if (!response.ok) throw new Error('Failed to fetch false-positive violations');
    return response.json();
}

export async function markViolationsFalsePositive(ids: string[], reason?: string): Promise<{ marked: number }> {
    const response = await apiFetch(`${API_BASE}/false-positives/mark`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ violationIds: ids, reason }),
    });
    if (!response.ok) throw new Error('Failed to mark violations as false-positive');
    return response.json();
}

export async function unmarkViolationsFalsePositive(ids: string[]): Promise<{ unmarked: number }> {
    const response = await apiFetch(`${API_BASE}/false-positives/unmark`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ violationIds: ids }),
    });
    if (!response.ok) throw new Error('Failed to restore violations');
    return response.json();
}

export async function getViolation(id: string): Promise<Violation> {
    const response = await apiFetch(`${API_BASE}/${id}`);

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
    hourlyHeatmap: HeatmapData[];
    byCamera: { cameraName: string; count: number }[];
    byStatus: { status: string; count: number }[];
}

export async function getAnalytics(filters?: { startDate?: string; endDate?: string; cameraId?: string; locationId?: string }): Promise<AnalyticsResponse> {
    const params = new URLSearchParams();
    if (filters?.startDate) params.append('startDate', filters.startDate);
    if (filters?.endDate) params.append('endDate', filters.endDate);
    if (filters?.cameraId) params.append('cameraId', filters.cameraId);
    if (filters?.locationId) params.append('locationId', filters.locationId);

    const response = await apiFetch(`${API_BASE}/analytics?${params.toString()}`);

    if (!response.ok) {
        throw new Error('Failed to fetch analytics');
    }

    return response.json();
}

export interface HeatmapData {
    cameraName?: string;
    hour: number;
    count: number;
}
