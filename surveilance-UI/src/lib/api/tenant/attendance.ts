import { apiFetch } from '@/lib/utils/auth';

export interface AttendanceRecordResponse {
    id: string;
    tenantId: string;
    employeeExternalId: string;
    employeeName?: string;
    shiftDate: string;
    shiftType: 'DayShift' | 'NightShift';
    firstInTime?: string;
    firstInCameraId?: string;
    firstInCameraName?: string;
    lastOutTime?: string;
    lastOutCameraId?: string;
    lastOutCameraName?: string;
    totalWorkMinutes: number;
    status: 'Incomplete' | 'InShift' | 'Completed';
}

export interface AttendanceSummaryResponse {
    shiftDate: string;
    totalPresent: number;
    dayShiftCount: number;
    nightShiftCount: number;
    completedShiftsCount: number;
    inProgressShiftsCount: number;
    averageWorkMinutes: number;
}

const API_BASE = '/api/tenant/attendance';

export async function getAttendanceRecords(params?: {
    shiftDate?: string;
    employeeExternalId?: string;
    status?: string;
    page?: number;
    pageSize?: number;
}): Promise<AttendanceRecordResponse[]> {
    try {
        const query = new URLSearchParams();
        if (params?.shiftDate) query.append('shiftDate', params.shiftDate);
        if (params?.employeeExternalId) query.append('employeeExternalId', params.employeeExternalId);
        if (params?.status) query.append('status', params.status);
        if (params?.page) query.append('page', params.page.toString());
        if (params?.pageSize) query.append('pageSize', params.pageSize.toString());

        const url = query.toString() ? `${API_BASE}?${query}` : API_BASE;
        const response = await apiFetch(url);

        if (!response.ok) {
            console.error('Failed to fetch attendance records:', response.statusText);
            return [];
        }

        const data = await response.json();
        return Array.isArray(data) ? data : [];
    } catch (err) {
        console.error('Error fetching attendance records:', err);
        return [];
    }
}

export async function getAttendanceSummary(params?: { shiftDate?: string }): Promise<AttendanceSummaryResponse> {
    const defaultSummary: AttendanceSummaryResponse = {
        shiftDate: params?.shiftDate || '',
        totalPresent: 0,
        dayShiftCount: 0,
        nightShiftCount: 0,
        completedShiftsCount: 0,
        inProgressShiftsCount: 0,
        averageWorkMinutes: 0
    };

    try {
        const query = new URLSearchParams();
        if (params?.shiftDate) query.append('shiftDate', params.shiftDate);

        const url = query.toString() ? `${API_BASE}/summary?${query}` : `${API_BASE}/summary`;
        const response = await apiFetch(url);

        if (!response.ok) {
            console.error('Failed to fetch attendance summary:', response.statusText);
            return defaultSummary;
        }

        const data = await response.json();
        return data || defaultSummary;
    } catch (err) {
        console.error('Error fetching attendance summary:', err);
        return defaultSummary;
    }
}
