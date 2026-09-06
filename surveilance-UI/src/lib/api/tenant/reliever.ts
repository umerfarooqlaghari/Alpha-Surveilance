import { apiFetch } from '@/lib/utils/auth';

export type WorkstationStatus = 'Staffed' | 'PendingHandover' | 'UnderRelief' | 'UnattendedViolation' | 'Inactive';
export type ReliefSessionStatus = 'PendingHandover' | 'ActiveRelief' | 'Completed' | 'UnattendedViolation' | 'OverdueAlert';

export interface WorkstationTimingWindow {
    id: string;
    label: string;
    startTime: string; // "HH:mm"
    endTime: string;   // "HH:mm"
    daysOfWeek: number[]; // 0=Sun, 1=Mon, ..., 6=Sat
    isActive: boolean;
    primaryEmployeeIds?: string[];
    relieverEmployeeIds?: string[];
}

export interface WorkstationResponse {
    id: string;
    tenantId: string;
    locationId?: string;
    locationName?: string;
    cameraId: string;
    cameraName: string;
    cameraExternalId: string;
    name: string;
    code: string;
    polygonJson: string;
    requiredPrimaryWorkers: number;
    maxRelievers: number;
    handoverThresholdSeconds: number;
    maxReliefDurationSeconds: number;
    currentStatus: WorkstationStatus;
    isActive: boolean;
    primaryWorkersCount: number;
    relieversCount: number;
    operatingScheduleJson?: string;
    createdAt: string;
    updatedAt: string;
}

export interface WorkstationDetailResponse extends WorkstationResponse {
    assignments: WorkstationAssignmentResponse[];
    activeSession?: ReliefSessionResponse;
}

export interface WorkstationAssignmentResponse {
    id: string;
    employeeId: string;
    employeeName: string;
    employeeExternalId: string;
    role: 'Primary' | 'Reliever';
    shiftStartTime?: string;
    shiftEndTime?: string;
    isActive: boolean;
}

export interface ReliefSessionResponse {
    id: string;
    workstationId: string;
    workstationName: string;
    workstationCode: string;
    cameraId: string;
    cameraName: string;
    primaryEmployeeId?: string;
    primaryEmployeeName?: string;
    primaryEmployeeExternalId?: string;
    relieverEmployeeId?: string;
    relieverEmployeeName?: string;
    relieverEmployeeExternalId?: string;
    primaryLeftAt: string;
    relieverArrivedAt?: string;
    handoverLatencySeconds?: number;
    reliefEndedAt?: string;
    reliefDurationSeconds?: number;
    status: ReliefSessionStatus;
    violationId?: string;
    notes?: string;
    createdAt: string;
}

export interface WorkstationLiveBoardItem {
    workstationId: string;
    name: string;
    code: string;
    cameraId: string;
    cameraName: string;
    locationName?: string;
    status: WorkstationStatus;
    requiredPrimaryWorkers: number;
    handoverThresholdSeconds: number;
    maxReliefDurationSeconds: number;
    activePrimaryWorker?: string;
    activeReliever?: string;
    lastEventTimestamp?: string;
    remainingGraceSeconds?: number;
    activeReliefDurationSeconds?: number;
    activeReliefsTodayCount: number;
    todayUptimePercentage: number;
}

export interface ReliefAnalyticsSummary {
    totalWorkstations: number;
    activeWorkstations: number;
    overallStaffingComplianceRate: number;
    totalReliefEventsCount: number;
    averageHandoverLatencySeconds: number;
    averageReliefDurationMinutes: number;
    totalUnattendedViolationsCount: number;
    totalDowntimeMinutes: number;
    topRelievers: RelieverWorkloadMetric[];
    workstationMetrics: WorkstationComplianceMetric[];
    hourlyDistribution: HourlyReliefDistribution[];
}

export interface RelieverWorkloadMetric {
    employeeId: string;
    employeeName: string;
    employeeExternalId: string;
    reliefCount: number;
    totalReliefMinutes: number;
    averageResponseLatencySeconds: number;
}

export interface WorkstationComplianceMetric {
    workstationId: string;
    workstationName: string;
    workstationCode: string;
    staffingCompliancePercentage: number;
    totalReliefsReceived: number;
    totalViolations: number;
    totalUnstaffedMinutes: number;
}

export interface HourlyReliefDistribution {
    hour: number;
    reliefEventsCount: number;
    unattendedIncidentsCount: number;
}

export interface CreateWorkstationPayload {
    name: string;
    code: string;
    locationId?: string;
    cameraId: string;
    polygonJson: string;
    requiredPrimaryWorkers: number;
    maxRelievers: number;
    handoverThresholdSeconds: number;
    maxReliefDurationSeconds: number;
    operatingScheduleJson?: string;
    primaryEmployeeIds?: string[];
    relieverEmployeeIds?: string[];
}

export interface UpdateWorkstationPayload {
    name?: string;
    code?: string;
    locationId?: string;
    cameraId?: string;
    polygonJson?: string;
    requiredPrimaryWorkers?: number;
    maxRelievers?: number;
    handoverThresholdSeconds?: number;
    maxReliefDurationSeconds?: number;
    operatingScheduleJson?: string;
    isActive?: boolean;
    primaryEmployeeIds?: string[];
    relieverEmployeeIds?: string[];
}

const API_BASE = '/api/tenant/reliever';

export async function getWorkstations(params?: { locationId?: string; cameraId?: string }): Promise<WorkstationResponse[]> {
    try {
        const query = new URLSearchParams();
        if (params?.locationId) query.append('locationId', params.locationId);
        if (params?.cameraId) query.append('cameraId', params.cameraId);

        const url = query.toString() ? `${API_BASE}/workstations?${query}` : `${API_BASE}/workstations`;
        const res = await apiFetch(url);
        if (!res.ok) return [];
        const data = await res.json();
        return Array.isArray(data) ? data : [];
    } catch (err) {
        console.error('Failed to fetch workstations:', err);
        return [];
    }
}

export async function getWorkstationById(id: string): Promise<WorkstationDetailResponse | null> {
    try {
        const res = await apiFetch(`${API_BASE}/workstations/${id}`);
        if (!res.ok) return null;
        return await res.json();
    } catch (err) {
        console.error(`Failed to fetch workstation ${id}:`, err);
        return null;
    }
}

export async function createWorkstation(payload: CreateWorkstationPayload): Promise<WorkstationResponse> {
    const res = await apiFetch(`${API_BASE}/workstations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    });
    if (!res.ok) {
        const errData = await res.json().catch(() => ({}));
        throw new Error(errData.error || errData.message || 'Failed to create workstation');
    }
    return await res.json();
}

export async function updateWorkstation(id: string, payload: UpdateWorkstationPayload): Promise<WorkstationResponse> {
    const res = await apiFetch(`${API_BASE}/workstations/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    });
    if (!res.ok) {
        const errData = await res.json().catch(() => ({}));
        throw new Error(errData.error || errData.message || 'Failed to update workstation');
    }
    return await res.json();
}

export async function deleteWorkstation(id: string): Promise<boolean> {
    const res = await apiFetch(`${API_BASE}/workstations/${id}`, { method: 'DELETE' });
    return res.ok;
}

export async function getLiveBoard(params?: { locationId?: string }): Promise<WorkstationLiveBoardItem[]> {
    try {
        const query = new URLSearchParams();
        if (params?.locationId) query.append('locationId', params.locationId);

        const url = query.toString() ? `${API_BASE}/live-board?${query}` : `${API_BASE}/live-board`;
        const res = await apiFetch(url);
        if (!res.ok) return [];
        const data = await res.json();
        return Array.isArray(data) ? data : [];
    } catch (err) {
        console.error('Failed to fetch live board:', err);
        return [];
    }
}

export async function getReliefSessions(params?: {
    startDate?: string;
    endDate?: string;
    workstationId?: string;
    status?: string;
    employeeExternalId?: string;
}): Promise<ReliefSessionResponse[]> {
    try {
        const query = new URLSearchParams();
        if (params?.startDate) query.append('startDate', params.startDate);
        if (params?.endDate) query.append('endDate', params.endDate);
        if (params?.workstationId) query.append('workstationId', params.workstationId);
        if (params?.status) query.append('status', params.status);
        if (params?.employeeExternalId) query.append('employeeExternalId', params.employeeExternalId);

        const url = query.toString() ? `${API_BASE}/sessions?${query}` : `${API_BASE}/sessions`;
        const res = await apiFetch(url);
        if (!res.ok) return [];
        const data = await res.json();
        return Array.isArray(data) ? data : [];
    } catch (err) {
        console.error('Failed to fetch relief sessions:', err);
        return [];
    }
}

export async function getReliefAnalytics(params?: {
    startDate?: string;
    endDate?: string;
    locationId?: string;
}): Promise<ReliefAnalyticsSummary | null> {
    try {
        const query = new URLSearchParams();
        if (params?.startDate) query.append('startDate', params.startDate);
        if (params?.endDate) query.append('endDate', params.endDate);
        if (params?.locationId) query.append('locationId', params.locationId);

        const url = query.toString() ? `${API_BASE}/analytics/summary?${query}` : `${API_BASE}/analytics/summary`;
        const res = await apiFetch(url);
        if (!res.ok) return null;
        return await res.json();
    } catch (err) {
        console.error('Failed to fetch relief analytics:', err);
        return null;
    }
}
