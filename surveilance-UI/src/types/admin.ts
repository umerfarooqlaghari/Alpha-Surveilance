// Tenant Management API Types
export interface CreateTenantRequest {
    tenantName: string;
    slug: string;
    employeeCount: number;
    address: string;
    city: string;
    country: string;
    industry: string;
}

export interface UpdateTenantRequest {
    tenantName?: string;
    employeeCount?: number;
    address?: string;
    city?: string;
    country?: string;
    industry?: string;
}

export interface TenantResponse {
    id: string;
    tenantName: string;
    slug: string;
    employeeCount: number;
    address: string;
    city: string;
    country: string;
    industry: string;
    status: string;
    logoUrl?: string;
    userCount: number;
    cameraCount: number;
    createdAt: string;
}

export interface TenantListResponse {
    tenants: TenantResponse[];
    totalCount: number;
    pageNumber: number;
    pageSize: number;
}

// User Management API Types
export interface CreateUserRequest {
    tenantId?: string;
    fullName: string;
    email: string;
    phoneNumber: string;
    employeeCode?: string;
    designation?: string;
    password: string;
    roleIds: string[];
}

export interface UpdateUserRequest {
    fullName?: string;
    phoneNumber?: string;
    employeeCode?: string;
    designation?: string;
}

export interface UserResponse {
    id: string;
    tenantId?: string;
    tenantName?: string;
    fullName: string;
    email: string;
    phoneNumber: string;
    employeeCode?: string;
    designation?: string;
    isActive: boolean;
    roles: string[];
    createdAt: string;
    lastLoginAt?: string;
}

// Camera Management API Types
export interface CreateCameraRequest {
    tenantId: string;
    cameraId: string;
    name: string;
    location: string;
    rtspUrl: string;
    enableSafetyViolations: boolean;
    enableSecurityViolations: boolean;
    enableOperationalViolations: boolean;
    enableComplianceViolations: boolean;
}

export interface UpdateCameraRequest {
    name?: string;
    location?: string;
    rtspUrl?: string;
    enableSafetyViolations?: boolean;
    enableSecurityViolations?: boolean;
    enableOperationalViolations?: boolean;
    enableComplianceViolations?: boolean;
}

export interface CameraResponse {
    id: string;
    tenantId: string;
    tenantName?: string;
    cameraId: string;
    name: string;
    location: string;
    status: string;
    enableSafetyViolations: boolean;
    enableSecurityViolations: boolean;
    enableOperationalViolations: boolean;
    enableComplianceViolations: boolean;
    createdAt: string;
}
