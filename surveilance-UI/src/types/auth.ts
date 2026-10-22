// Authentication Request Types
export interface SuperAdminLoginRequest {
    email: string;
    password: string;
    turnstileToken?: string;
}

export interface TenantAdminLoginRequest {
    email: string;
    password: string;
    tenantSlug: string;
    turnstileToken?: string;
}

export interface TenantSignupRequest {
    tenantName: string;
    slug: string;
    adminFullName: string;
    adminEmail: string;
    adminPassword: string;
    phoneNumber?: string;
    industry?: string;
    employeeCount?: number;
    address?: string;
    city?: string;
    country?: string;
    turnstileToken?: string;
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
    loginSuperAdmin: (email: string, password: string, turnstileToken?: string) => Promise<void>;
    loginTenantAdmin: (email: string, password: string, tenantSlug: string, turnstileToken?: string) => Promise<void>;
    signupTenant: (data: TenantSignupRequest) => Promise<void>;
    logout: (targetTab?: 'tenant' | 'superadmin') => void;
}

