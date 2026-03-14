export interface EmailTemplate {
    id: string;
    name: string;
    subject: string;
    body: string;
    tenantId: string;
    createdAt: string;
    updatedAt?: string;
}

export interface CreateEmailTemplateRequest {
    name: string;
    subject: string;
    body: string;
}

export interface UpdateEmailTemplateRequest {
    id: string;
    name: string;
    subject: string;
    body: string;
}

export interface SendEmailRequest {
    employeeIds: string[];
    violationIds: string[];
    subject: string;
    body: string;
    attachments?: File[];
}
