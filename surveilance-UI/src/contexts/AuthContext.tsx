'use client';

import { createContext, useContext, useState, useEffect, useCallback, ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import type { AuthContextType, UserInfo, TenantInfo } from '@/types/auth';
import * as authApi from '@/lib/api/auth';
import { isTokenExpired } from '@/lib/utils/auth';

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<UserInfo | null>(null);
    const [role, setRole] = useState<string | null>(null);
    const [tenant, setTenant] = useState<TenantInfo | null>(null);
    const [token, setToken] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const router = useRouter();

    // Stable logout reference used by event listeners and intervals below
    const clearSession = useCallback(() => {
        setToken(null);
        setUser(null);
        setRole(null);
        setTenant(null);
        localStorage.removeItem('auth_token');
        localStorage.removeItem('auth_user');
        localStorage.removeItem('auth_role');
        localStorage.removeItem('auth_tenant');
        router.push('/');
    }, [router]);

    useEffect(() => {
        // Load auth state from localStorage on mount
        const storedToken = localStorage.getItem('auth_token');
        const storedUser = localStorage.getItem('auth_user');
        const storedRole = localStorage.getItem('auth_role');
        const storedTenant = localStorage.getItem('auth_tenant');

        if (storedToken && storedUser && storedRole) {
            // Immediately log out if the stored token is already expired
            if (isTokenExpired(storedToken)) {
                // Inline clearing logic to avoid dependency on clearSession/router ref changes during mount
                localStorage.removeItem('auth_token');
                localStorage.removeItem('auth_user');
                localStorage.removeItem('auth_role');
                localStorage.removeItem('auth_tenant');
                setToken(null);
                setUser(null);
                setRole(null);
                setTenant(null);
            } else {
                setToken(storedToken);
                setUser(JSON.parse(storedUser));
                setRole(storedRole);
                if (storedTenant) {
                    setTenant(JSON.parse(storedTenant));
                }
            }
        }

        setIsLoading(false);
    }, []); // Only run ONCE on mount

    // Listen for the `auth:expired` event dispatched by apiFetch on 401s
    useEffect(() => {
        window.addEventListener('auth:expired', clearSession);
        return () => window.removeEventListener('auth:expired', clearSession);
    }, [clearSession]);

    // Poll every 30 s to catch expiry while the user is idle (no API calls)
    useEffect(() => {
        const id = setInterval(() => {
            const t = localStorage.getItem('auth_token');
            if (t && isTokenExpired(t)) {
                clearSession();
            }
        }, 30_000);
        return () => clearInterval(id);
    }, [clearSession]);

    const loginSuperAdmin = async (email: string, password: string) => {
        try {
            const response = await authApi.loginSuperAdmin(email, password);

            setToken(response.token);
            setUser(response.user);
            setRole(response.role);
            setTenant(null);

            localStorage.setItem('auth_token', response.token);
            localStorage.setItem('auth_user', JSON.stringify(response.user));
            localStorage.setItem('auth_role', response.role);
            localStorage.removeItem('auth_tenant');

            router.push('/admin');
        } catch (error: any) {
            throw error;
        }
    };

    const loginTenantAdmin = async (email: string, password: string, tenantSlug: string) => {
        try {
            const response = await authApi.loginTenantAdmin(email, password, tenantSlug);

            setToken(response.token);
            setUser(response.user);
            setRole(response.role);
            setTenant(response.tenant || null);

            localStorage.setItem('auth_token', response.token);
            localStorage.setItem('auth_user', JSON.stringify(response.user));
            localStorage.setItem('auth_role', response.role);
            if (response.tenant) {
                localStorage.setItem('auth_tenant', JSON.stringify(response.tenant));
            }

            router.push('/tenant/analytics');
        } catch (error: any) {
            throw error;
        }
    };

    const logout = () => {
        clearSession();
    };

    const value: AuthContextType = {
        user,
        role,
        tenant,
        token,
        isAuthenticated: !!token && !!user,
        isLoading,
        loginSuperAdmin,
        loginTenantAdmin,
        logout,
    };

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
    const context = useContext(AuthContext);
    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
}
