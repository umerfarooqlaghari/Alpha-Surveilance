import { getAuthHeaders } from '@/lib/utils/auth';
import { Employee, EmployeeRequest, BulkImportResponse } from '@/types/employee';

const API_BASE = '/api/employees';

export const getEmployees = async (params?: {
    page?: number;
    pageSize?: number;
    search?: string;
    department?: string;
    designation?: string;
}) => {
    const query = new URLSearchParams();
    if (params?.page) query.append('page', params.page.toString());
    if (params?.pageSize) query.append('pageSize', params.pageSize.toString());
    if (params?.search) query.append('search', params.search);
    if (params?.department) query.append('department', params.department);
    if (params?.designation) query.append('designation', params.designation);

    const response = await fetch(`${API_BASE}?${query.toString()}`, {
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        throw new Error('Failed to fetch employees');
    }

    return response.json() as Promise<Employee[]>;
};

export const getEmployee = async (id: string) => {
    const response = await fetch(`${API_BASE}/${id}`, {
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        throw new Error('Failed to fetch employee');
    }

    return response.json() as Promise<Employee>;
};

export const createEmployee = async (data: EmployeeRequest) => {
    const response = await fetch(API_BASE, {
        method: 'POST',
        headers: {
            ...getAuthHeaders() as Record<string, string>,
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.message || 'Failed to create employee');
    }

    return response.json() as Promise<Employee>;
};

export const updateEmployee = async (id: string, data: EmployeeRequest) => {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'PUT',
        headers: {
            ...getAuthHeaders() as Record<string, string>,
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(data),
    });

    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.message || 'Failed to update employee');
    }
};

export const deleteEmployee = async (id: string) => {
    const response = await fetch(`${API_BASE}/${id}`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
    });

    if (!response.ok) {
        throw new Error('Failed to delete employee');
    }
};

export const bulkImportEmployees = async (file: File) => {
    const formData = new FormData();
    formData.append('file', file);

    const headers = getAuthHeaders() as Record<string, string>;
    // Remove Content-Type to let browser set it with boundary for FormData
    delete headers['Content-Type'];

    const response = await fetch(`${API_BASE}/bulk-import`, {
        method: 'POST',
        headers: {
            ...headers,
        },
        body: formData,
    });

    if (!response.ok) {
        const error = await response.json().catch(() => ({ message: 'Bulk import failed' }));
        throw new Error(error.message || 'Bulk import failed');
    }

    return response.json() as Promise<BulkImportResponse>;
};

export const downloadTemplate = async () => {
    try {
        const response = await fetch(`${API_BASE}/template`, {
            method: 'GET',
            headers: getAuthHeaders(),
        });

        if (!response.ok) throw new Error('Failed to download template');

        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'employees_template.csv';
        document.body.appendChild(a);
        a.click();
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);
    } catch (error) {
        console.error('Download failed:', error);
        alert('Failed to download template');
    }
};
