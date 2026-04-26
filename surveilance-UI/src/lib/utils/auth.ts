/**
 * Decodes the JWT payload without verifying the signature (client-side convenience only).
 * Actual signature verification is always done on the server.
 */
function decodeJwtPayload(token: string): { exp?: number; role?: string; email?: string; sub?: string } | null {
    if (!token || token === 'undefined' || token === 'null') return null;
    try {
        const parts = token.split('.');
        if (parts.length !== 3) return null;

        const base64Url = parts[1];
        const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
        const json = decodeURIComponent(
            atob(base64)
                .split('')
                .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
                .join('')
        );
        const payload = JSON.parse(json);
        console.log('[Auth] Decoded token payload:', payload);
        return payload;
    } catch (e) {
        console.error('[Auth] Failed to decode token:', e);
        return null;
    }
}

/**
 * Returns true if the stored JWT is missing or has expired.
 */
export function isTokenExpired(token: string | null): boolean {
    if (!token || token === 'undefined' || token === 'null') return true;
    
    const payload = decodeJwtPayload(token);
    if (!payload) {
        console.warn('[Auth] Token present but could not be decoded. Treating as expired to be safe.');
        return true;
    }

    // Evict tokens issued under the old URI-style claim shape (e.g. ClaimTypes.Role =
    // "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"). The server now
    // validates against short names ("role", "sub"), so these legacy tokens will always
    // fail authorization. Treat them as expired so the user is forced to log in again.
    const hasLegacyClaimShape = Object.keys(payload).some((k) => k.includes('schemas.microsoft.com') || k.includes('schemas.xmlsoap.org'));
    if (hasLegacyClaimShape) {
        console.warn('[Auth] Token uses legacy URI-style claim names. Treating as expired to force re-login.');
        return true;
    }

    if (!payload.exp) {
        console.warn('[Auth] Token missing exp claim.');
        return false; // Treat as not expired if no exp claim present
    }
    
    const currentTime = Math.floor(Date.now() / 1000);
    const expired = currentTime >= payload.exp - 10;
    
    if (expired) {
        console.warn(`[Auth] Token expired. Current: ${currentTime}, Exp: ${payload.exp}`);
    }
    
    return expired;
}

export function getAuthHeaders(): HeadersInit {
    const token = localStorage.getItem('auth_token');
    return {
        'Content-Type': 'application/json',
        'Authorization': token ? `Bearer ${token}` : '',
    };
}

/**
 * Central fetch wrapper that:
 * 1. Checks token expiry BEFORE sending the request — synthetic 401 + logout if expired.
 * 2. On a real 401 from the server, only triggers logout if the token is ALSO locally expired.
 *    A server 401 due to wrong roles/missing claims should NOT log out a user with a valid token.
 */
export async function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
    const token = localStorage.getItem('auth_token');

    if (isTokenExpired(token)) {
        console.error('[apiFetch] SYNTHETIC 401: Local token expiry check failed before reaching server.');
        window.dispatchEvent(new Event('auth:expired'));
        // Return a synthetic 401 so callers see a normal error path
        return new Response(JSON.stringify({ error: 'Session expired' }), { status: 401 });
    }

    // Try to get tenantId from localStorage to propagate it automatically
    let tenantId: string | null = null;
    const storedTenant = localStorage.getItem('auth_tenant');
    if (storedTenant) {
        try {
            const tenantObj = JSON.parse(storedTenant);
            tenantId = tenantObj.id || null;
        } catch {
            // Ignore parse errors
        }
    }

    const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
        ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        ...(init?.headers as Record<string, string> ?? {}),
    };

    const response = await fetch(input, { ...init, headers });

    if (response.status === 401) {
        console.warn(`[apiFetch] SERVER 401: Server rejected the token for ${input}.`);
        if (isTokenExpired(localStorage.getItem('auth_token'))) {
            console.error('[apiFetch] SERVER 401 + EXPIRED: Triggering logout.');
            window.dispatchEvent(new Event('auth:expired'));
        } else {
            console.warn('[apiFetch] SERVER 401 BUT NOT EXPIRED: This suggests a role or signature mismatch.');
        }
    }

    return response;
}
