export interface Employee {
    id: string;
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
    createdAt: string;
    updatedAt: string;
}

export interface EmployeeRequest {
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
