import { getAuthHeaders } from '@/lib/utils/auth';

const BASE = '/api/tenant/violationaudits';

export type AuditRecordStatus = 0 | 1 | 2; // Draft | Submitted | Reviewed

export interface ViolationAuditRequest {
    violationId: string;
    status: AuditRecordStatus;
    executiveSummary?: string;
    rootCauseAnalysis?: string;
    contributingFactors?: string;
    stakeholdersAffected?: string;
    estimatedImpact?: string;
    measuresTaken?: string;
    resolvedBy?: string;
    resolvedAt?: string;
    preventionMeasures?: string;
    followUpActions?: string;
    reviewedBy?: string;
    reviewedAt?: string;
    internalNotes?: string;
}

export interface ViolationAuditResponse extends ViolationAuditRequest {
    id: string;
    tenantId: string;
    statusLabel: string;
    createdAt: string;
    updatedAt: string;
    createdByUserId?: string;
}

export async function getAudits(): Promise<ViolationAuditResponse[]> {
    const res = await fetch(BASE, { headers: getAuthHeaders() });
    if (!res.ok) throw new Error('Failed to fetch audits');
    return res.json();
}

export async function getAuditByViolation(violationId: string): Promise<ViolationAuditResponse | null> {
    const res = await fetch(`${BASE}/violation/${violationId}`, { headers: getAuthHeaders() });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error('Failed to fetch audit');
    return res.json();
}

export async function createAudit(data: ViolationAuditRequest): Promise<ViolationAuditResponse> {
    const res = await fetch(BASE, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: JSON.stringify(data),
    });
    if (!res.ok) throw new Error('Failed to create audit');
    return res.json();
}

export async function updateAudit(id: string, data: ViolationAuditRequest): Promise<ViolationAuditResponse> {
    const res = await fetch(`${BASE}/${id}`, {
        method: 'PUT',
        headers: getAuthHeaders(),
        body: JSON.stringify(data),
    });
    if (!res.ok) throw new Error('Failed to update audit');
    return res.json();
}
