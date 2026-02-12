// Authentication Request Types
export interface SuperAdminLoginRequest {
    email: string;
    password: string;
}

export interface TenantAdminLoginRequest {
    email: string;
    password: string;
    tenantSlug: string;
}

// Authentication Response Types
export interface AuthResponse {
    token: string;
    user: UserInfo;
    role: string;
    tenant?: TenantInfo;
}

export interface UserInfo {
    id: string;
    fullName: string;
    email: string;
    phoneNumber?: string;
    designation?: string;
}

export interface TenantInfo {
    id: string;
    tenantName: string;
    slug: string;
    logoUrl?: string;
}

// Auth Context Types
export interface AuthContextType {
    user: UserInfo | null;
    role: string | null;
    tenant: TenantInfo | null;
    token: string | null;
    isAuthenticated: boolean;
    isLoading: boolean;
    loginSuperAdmin: (email: string, password: string) => Promise<void>;
    loginTenantAdmin: (email: string, password: string, tenantSlug: string) => Promise<void>;
    logout: () => void;
}
