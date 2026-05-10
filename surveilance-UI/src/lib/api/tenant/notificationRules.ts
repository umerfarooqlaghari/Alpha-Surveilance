import { apiFetch } from '@/lib/utils/auth';

export interface TimeIntervalDto {
    start: string;
    end: string;
}

export interface NotificationRule {
    id: string;
    name: string;
    targetEmails: string[];
    filterLocationIds: string[];
    filterCameraIds: string[];
    filterViolationTypeIds: string[];
    filterSeverities: string[];
    filterDepartments: string[];
    timeIntervals: TimeIntervalDto[];
    isActive: boolean;
    createdAt: string;
}

export interface NotificationRuleRequest {
    name: string;
    targetEmails: string[];
    filterLocationIds: string[];
    filterCameraIds: string[];
    filterViolationTypeIds: string[];
    filterSeverities: string[];
    filterDepartments: string[];
    timeIntervals: TimeIntervalDto[];
    isActive: boolean;
}

const API_BASE = '/api/tenant/notificationrules';

export async function getNotificationRules(): Promise<NotificationRule[]> {
    const response = await apiFetch(API_BASE);
    if (!response.ok) {
        throw new Error('Failed to fetch notification rules');
    }
    return response.json();
}

export async function createNotificationRule(rule: NotificationRuleRequest): Promise<NotificationRule> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(rule),
    });
    if (!response.ok) {
        throw new Error('Failed to create notification rule');
    }
    return response.json();
}

export async function updateNotificationRule(id: string, rule: NotificationRuleRequest): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(rule),
    });
    if (!response.ok) {
        throw new Error('Failed to update notification rule');
    }
}

export async function deleteNotificationRule(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'DELETE',
    });
    if (!response.ok) {
        throw new Error('Failed to delete notification rule');
    }
}
