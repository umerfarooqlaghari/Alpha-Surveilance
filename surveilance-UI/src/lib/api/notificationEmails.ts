import { apiFetch } from '@/lib/utils/auth';

export interface NotificationEmailEntry {
    id: string;
    email: string;
    label?: string;
    isActive: boolean;
    createdAt: string;
}

const BASE = '/api/notification-emails';

export async function getNotificationEmails(): Promise<NotificationEmailEntry[]> {
    const res = await apiFetch(BASE);
    if (!res.ok) throw new Error('Failed to fetch notification emails');
    return res.json();
}

export async function addNotificationEmail(email: string, label?: string): Promise<NotificationEmailEntry> {
    const res = await apiFetch(BASE, {
        method: 'POST',
        body: JSON.stringify({ email, label }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Failed to add email');
    return data;
}

export async function toggleNotificationEmail(id: string): Promise<NotificationEmailEntry> {
    const res = await apiFetch(`${BASE}/${id}/toggle`, { method: 'PATCH' });
    if (!res.ok) throw new Error('Failed to toggle email');
    return res.json();
}

export async function deleteNotificationEmail(id: string): Promise<void> {
    const res = await apiFetch(`${BASE}/${id}`, { method: 'DELETE' });
    if (!res.ok) throw new Error('Failed to delete email');
}
