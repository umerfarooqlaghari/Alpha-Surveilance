import { EmailTemplate, CreateEmailTemplateRequest, UpdateEmailTemplateRequest, SendEmailRequest } from '@/types/emailing';
import { apiFetch } from '@/lib/utils/auth';

const API_URL = '/api/EmailTemplates'; // Proxy to BFF
const SEND_API_URL = '/api/Email/send'; // Proxy to BFF

export const emailingApi = {
    // Templates
    getTemplates: async (): Promise<EmailTemplate[]> => {
        const response = await apiFetch(API_URL);
        if (!response.ok) throw new Error('Failed to fetch templates');
        return response.json();
    },

    getTemplate: async (id: string): Promise<EmailTemplate> => {
        const response = await apiFetch(`${API_URL}/${id}`);
        if (!response.ok) throw new Error('Failed to fetch template');
        return response.json();
    },

    createTemplate: async (data: CreateEmailTemplateRequest): Promise<EmailTemplate> => {
        const response = await apiFetch(API_URL, {
            method: 'POST',
            body: JSON.stringify(data),
        });
        if (!response.ok) throw new Error('Failed to create template');
        return response.json();
    },

    updateTemplate: async (data: UpdateEmailTemplateRequest): Promise<void> => {
        const response = await apiFetch(`${API_URL}/${data.id}`, {
            method: 'PUT',
            body: JSON.stringify(data),
        });
        if (!response.ok) throw new Error('Failed to update template');
    },

    deleteTemplate: async (id: string): Promise<void> => {
        const response = await apiFetch(`${API_URL}/${id}`, { method: 'DELETE' });
        if (!response.ok) throw new Error('Failed to delete template');
    },

    // Sending
    sendEmail: async (data: SendEmailRequest): Promise<void> => {
        const formData = new FormData();

        data.employeeIds.forEach(id => formData.append('EmployeeIds', id));
        data.violationIds.forEach(id => formData.append('ViolationIds', id));
        formData.append('Subject', data.subject);
        formData.append('Body', data.body);

        if (data.attachments) {
            data.attachments.forEach(file => {
                formData.append('attachments', file);
            });
        }

        // FormData: let browser set Content-Type with multipart boundary
        const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
        const response = await fetch(SEND_API_URL, {
            method: 'POST',
            headers: token ? { 'Authorization': `Bearer ${token}` } : {},
            body: formData,
        });

        if (response.status === 401) window.dispatchEvent(new Event('auth:expired'));

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || 'Failed to send email');
        }
    }
};
