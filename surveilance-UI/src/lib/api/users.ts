import { apiFetch } from '@/lib/utils/auth';
import type {
    CreateUserRequest,
    UpdateUserRequest,
    UserResponse
} from '@/types/admin';


const API_BASE = '/api/admin/users';

export async function createUser(data: CreateUserRequest): Promise<UserResponse> {
    const response = await apiFetch(API_BASE, {
        method: 'POST',
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to create user');
    }

    return response.json();
}

export async function getUsers(tenantId?: string): Promise<UserResponse[]> {
    const url = tenantId ? `${API_BASE}?tenantId=${tenantId}` : API_BASE;
    const response = await apiFetch(url);

    if (!response.ok) {
        throw new Error('Failed to fetch users');
    }

    return response.json();
}

export async function getUser(id: string): Promise<UserResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`);

    if (!response.ok) {
        throw new Error('Failed to fetch user');
    }

    return response.json();
}

export async function updateUser(id: string, data: UpdateUserRequest): Promise<UserResponse> {
    const response = await apiFetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to update user');
    }

    return response.json();
}

export async function resetPassword(id: string, newPassword: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}/reset-password`, {
        method: 'POST',
        body: JSON.stringify({ newPassword }),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to reset password');
    }
}

export async function toggleUserStatus(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}/toggle-status`, { method: 'PATCH' });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to toggle user status');
    }
}

export async function deleteUser(id: string): Promise<void> {
    const response = await apiFetch(`${API_BASE}/${id}`, { method: 'DELETE' });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Failed to delete user');
    }
}
