import { getAuthHeaders } from '@/lib/utils/auth';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface FolderDto {
    id: string;
    name: string;
    parentFolderId: string | null;
    createdAt: string;
    childFolderCount: number;
    fileCount: number;
}

export interface FileDto {
    id: string;
    name: string;
    originalFileName: string;
    url: string;
    contentType: string;
    sizeBytes: number;
    folderId: string | null;
    createdAt: string;
}

export interface BreadcrumbItem {
    id: string;
    name: string;
}

export interface FolderContentsResponse {
    id: string;
    name: string;
    parentFolderId: string | null;
    breadcrumb: BreadcrumbItem[];
    subFolders: FolderDto[];
    files: FileDto[];
}

export interface SearchResult {
    folders: FolderDto[];
    files: FileDto[];
}

export interface RootContentsResponse {
    folders: FolderDto[];
    files: FileDto[];
}


const BASE = '/api/filemanager';

async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
    const res = await fetch(url, { ...init, headers: { ...getAuthHeaders(), ...(init?.headers || {}) } });
    if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `Request failed: ${res.status}`);
    }
    if (res.status === 204) return undefined as T;
    return res.json();
}

export const fileManagerApi = {
    getRootFolders: () => apiFetch<RootContentsResponse>(`${BASE}/folders`),

    getFolderContents: (id: string) => apiFetch<FolderContentsResponse>(`${BASE}/folders/${id}`),

    createFolder: (name: string, parentFolderId?: string | null) =>
        apiFetch<FolderDto>(`${BASE}/folders`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, parentFolderId: parentFolderId ?? null }),
        }),

    renameFolder: (id: string, name: string) =>
        apiFetch<{ id: string; name: string }>(`${BASE}/folders/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name }),
        }),

    deleteFolder: (id: string) =>
        apiFetch<void>(`${BASE}/folders/${id}`, { method: 'DELETE' }),

    uploadFiles: (files: File[], parentFolderId?: string | null): Promise<FileDto[]> => {
        const form = new FormData();
        files.forEach(f => form.append('files', f));
        if (parentFolderId) form.append('parentFolderId', parentFolderId);

        // DO NOT use apiFetch here — getAuthHeaders() sets Content-Type: application/json
        // which overwrites the browser's automatic multipart/form-data; boundary=... header.
        // For FormData, we must let the browser set Content-Type by itself.
        const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
        return fetch(`${BASE}/files/upload`, {
            method: 'POST',
            headers: token ? { 'Authorization': `Bearer ${token}` } : {},
            body: form,
        }).then(async res => {
            if (!res.ok) {
                const body = await res.json().catch(() => ({}));
                throw new Error((body as any).error || `Upload failed: ${res.status}`);
            }
            return res.json();
        });
    },

    renameFile: (id: string, name: string) =>
        apiFetch<{ id: string; name: string }>(`${BASE}/files/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name }),
        }),

    deleteFile: (id: string) =>
        apiFetch<void>(`${BASE}/files/${id}`, { method: 'DELETE' }),

    search: (q: string) =>
        apiFetch<SearchResult>(`${BASE}/search?q=${encodeURIComponent(q)}`),
};
