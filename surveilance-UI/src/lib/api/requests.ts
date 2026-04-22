import { apiFetch } from '@/lib/utils/auth';

export interface TenantViolationRequestResponse {
    id: string;
    tenantId: string;
    tenantName: string;
    sopViolationTypeId: string;
    violationTypeName: string;
    sopId: string;
    sopName: string;
    sopTriggerLabels?: string; // Comma-separated labels defined on the SOP violation type
    status: number; // 0=Pending, 1=Approved, 2=Rejected
    requestedAt: string;
    resolvedAt?: string;
}

const API_URL = '/api/admin/tenantviolationrequests';

export async function getAllRequests(): Promise<TenantViolationRequestResponse[]> {
    const response = await apiFetch(`${API_URL}/all`);
    if (!response.ok) throw new Error('Failed to fetch all requests');
    return response.json();
}

export async function unassignRequest(id: string): Promise<void> {
    const response = await apiFetch(`${API_URL}/${id}`, { method: 'DELETE' });
    if (!response.ok) throw new Error('Failed to unassign request');
}

export async function getPendingRequests(): Promise<TenantViolationRequestResponse[]> {
    const response = await apiFetch(`${API_URL}/pending`);
    if (!response.ok) throw new Error('Failed to fetch pending requests');
    return response.json();
}

export async function getApprovedRequests(tenantId: string): Promise<TenantViolationRequestResponse[]> {
    const response = await apiFetch(`${API_URL}/approved/${tenantId}`);
    if (!response.ok) throw new Error('Failed to fetch approved requests');
    return response.json();
}

export async function resolveRequest(id: string, status: number): Promise<TenantViolationRequestResponse> {
    const response = await apiFetch(`${API_URL}/${id}/resolve`, {
        method: 'PATCH',
        body: JSON.stringify({ status }),
    });
    if (!response.ok) throw new Error('Failed to resolve request');
    return response.json();
}

export async function assignProactiveRequest(tenantId: string, sopViolationTypeId: string): Promise<TenantViolationRequestResponse> {
    const response = await apiFetch(`${API_URL}/assign-proactive`, {
        method: 'POST',
        body: JSON.stringify({ tenantId, sopViolationTypeId }),
    });
    const errorData = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(errorData.error || 'Failed to dynamically assign the request');
    return errorData;
}
