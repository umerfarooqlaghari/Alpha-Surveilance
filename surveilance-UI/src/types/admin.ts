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

/** Recurring UTC sleep window — detection is skipped while inside this range. */
export interface DetectionSchedule {
    id?: string;
    /** Bitmask: Sun=1, Mon=2, Tue=4, Wed=8, Thu=16, Fri=32, Sat=64. 0 or 127 = every day. */
    daysOfWeek: number;
    /** "HH:mm" UTC */
    startTime: string;
    /** "HH:mm" UTC — may be less than startTime for overnight windows */
    endTime: string;
    label?: string;
    isActive: boolean;
}

export interface CreateCameraRequest {
    tenantId: string;
    locationId?: string | null;
    cameraId: string;
    name: string;
    location: string;
    rtspUrl: string;
    whipUrl?: string;
    whepUrl?: string;
    isStreaming?: boolean;
    targetFps?: number;
    isDetectionEnabled?: boolean;
    activeViolations: CameraViolationAssignment[];
    detectionSchedules?: DetectionSchedule[];
}

export interface UpdateCameraRequest {
    name?: string;
    location?: string;
    /**
     * Set to a string GUID to assign / change the Location.
     * Set to '00000000-0000-0000-0000-000000000000' to detach.
     * Omit / null to leave unchanged.
     */
    locationId?: string | null;
    rtspUrl?: string;
    whipUrl?: string;
    whepUrl?: string;
    isStreaming?: boolean;
    targetFps?: number;
    isDetectionEnabled?: boolean;
    activeViolations?: CameraViolationAssignment[];
    detectionSchedules?: DetectionSchedule[];
}

export interface CameraResponse {
    id: string;
    tenantId: string;
    tenantName?: string;
    locationId?: string | null;
    locationName?: string | null;
    locationCode?: string | null;
    cameraId: string;
    name: string;
    location: string;
    status: string;
    whipUrl: string;
    whepUrl: string;
    isStreaming: boolean;
    targetFps?: number;
    isDetectionEnabled: boolean;
    activeViolations: CameraViolationResponse[];
    detectionSchedules: DetectionSchedule[];
    createdAt: string;
}

// Camera violation assignment (for creating/updating cameras)
export interface CameraViolationAssignment {
    sopViolationTypeId: string;
    triggerLabels?: string;
    ruleConfigurationJson?: string;
}

// Camera violation response (for reading camera data)
export interface CameraViolationResponse {
    sopViolationTypeId: string;
    triggerLabels?: string;
    ruleConfigurationJson?: string;
}

// SOP Management API Types
export interface SopViolationTypeResponse {
    id: string;
    sopId: string;
    name: string;
    modelIdentifier: string;
    description: string;
    triggerLabels?: string;
    /** D-9: server-driven flag indicating this SOP type supports the
     *  non-spatial "Anomaly" rule type (typically PPE/missing-equipment
     *  detections).  Replaces a fragile client-side label-prefix regex. */
    supportsAnomalyRule?: boolean;
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

