import { getAuthHeaders } from '@/lib/utils/auth';
import type { SopResponse } from '@/types/admin';
import type { TenantViolationRequestResponse } from '@/lib/api/requests';

const API_URL = '/api/tenant/sops';

export async function getTenantAvailableSops(): Promise<SopResponse[]> {
    const response = await fetch(API_URL, {
        headers: {
            ...getAuthHeaders(),
        },
    });
    if (!response.ok) throw new Error('Failed to fetch available SOPs');
    return response.json();
}

export async function getMySopRequests(): Promise<TenantViolationRequestResponse[]> {
    const response = await fetch(`${API_URL}/my-requests`, {
        headers: {
            ...getAuthHeaders(),
        },
    });
    if (!response.ok) throw new Error('Failed to fetch your SOP requests');
    return response.json();
}

export async function requestSopViolation(sopViolationTypeId: string): Promise<TenantViolationRequestResponse> {
    const response = await fetch(`${API_URL}/request`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            ...getAuthHeaders(),
        },
        body: JSON.stringify({ sopViolationTypeId }),
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || 'Failed to submit request');
    return data;
}
