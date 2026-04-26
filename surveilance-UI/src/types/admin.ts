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
    whipUrl?: string;
    whepUrl?: string;
    isStreaming?: boolean;
    targetFps?: number;
    activeViolations: CameraViolationAssignment[];
}

export interface UpdateCameraRequest {
    name?: string;
    location?: string;
    rtspUrl?: string;
    whipUrl?: string;
    whepUrl?: string;
    isStreaming?: boolean;
    targetFps?: number;
    activeViolations?: CameraViolationAssignment[];
}

export interface CameraResponse {
    id: string;
    tenantId: string;
    tenantName?: string;
    cameraId: string;
    name: string;
    location: string;
    status: string;
    whipUrl: string;
    whepUrl: string;
    isStreaming: boolean;
    targetFps?: number;
    activeViolations: CameraViolationResponse[];
    createdAt: string;
}

// Camera violation assignment (for creating/updating cameras)
export interface CameraViolationAssignment {
    sopViolationTypeId: string;
    triggerLabels?: string;
}

// Camera violation response (for reading camera data)
export interface CameraViolationResponse {
    sopViolationTypeId: string;
    triggerLabels?: string;
}

// SOP Management API Types
export interface SopViolationTypeResponse {
    id: string;
    sopId: string;
    name: string;
    modelIdentifier: string;
    description: string;
    triggerLabels?: string;
}

export interface SopResponse {
    id: string;
    name: string;
    description: string;
    createdAt: string;
    violationTypes: SopViolationTypeResponse[];
}

export interface CreateSopRequest {
    name: string;
    description: string;
}

export interface UpdateSopRequest {
    name?: string;
    description?: string;
}

export interface CreateSopViolationTypeRequest {
    name: string;
    modelIdentifier: string;
    description: string;
    triggerLabels?: string;
}

export interface UpdateSopViolationTypeRequest {
    name?: string;
    modelIdentifier?: string;
    description?: string;
    triggerLabels?: string;
}

