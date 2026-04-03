import type { SuperAdminLoginRequest, TenantAdminLoginRequest, AuthResponse } from '@/types/auth';

const API_BASE = '/api/auth';

export async function loginSuperAdmin(email: string, password: string): Promise<AuthResponse> {
    const response = await fetch(`${API_BASE}/superadmin/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password } as SuperAdminLoginRequest),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Login failed');
    }

    return response.json();
}

export async function loginTenantAdmin(
    email: string,
    password: string,
    tenantSlug: string
): Promise<AuthResponse> {
    const response = await fetch(`${API_BASE}/tenant/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password, tenantSlug } as TenantAdminLoginRequest),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Login failed');
    }

    return response.json();
}

export async function validateToken(token: string): Promise<boolean> {
    try {
        const response = await fetch(`${API_BASE}/validate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token }),
        });

        if (!response.ok) return false;

        const data = await response.json();
        return data.valid === true;
    } catch {
        return false;
    }
}
