export interface Employee {
    id: string;
    locationId?: string | null;
    firstName: string;
    lastName: string;
    email: string;
    employeeId: string;
    number?: string;
    companyName?: string;
    designation?: string;
    department?: string;
    tenure?: string;
    grade?: string;
    gender?: string;
    managerId?: string;
    metadata?: Record<string, any>;
    faceScanStatus: 'NotAssigned' | 'Pending' | 'Completed';
    faceScanCompletedAt?: string;
    faceScanInviteSentAt?: string;
    createdAt: string;
    updatedAt: string;
}

export interface EmployeeRequest {
    firstName: string;
    lastName: string;
    email: string;
    employeeId: string;
    /**
     * Pass a GUID to assign / change Location.
     * Pass '00000000-0000-0000-0000-000000000000' to detach.
     * Omit / null leaves it unchanged on update.
     */
    locationId?: string | null;
    number?: string;
    companyName?: string;
    designation?: string;
    department?: string;
    tenure?: string;
    grade?: string;
    gender?: string;
    managerId?: string;
    metadata?: Record<string, any>;
}

export interface BulkImportResponse {
    totalProcessed: number;
    successCount: number;
    failureCount: number;
    failures: BulkImportFailure[];
}

export interface BulkImportFailure {
    rowIndex: number;
    email: string;
    reason: string;
}
