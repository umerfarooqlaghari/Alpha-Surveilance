'use client';

import { X, Download, ExternalLink } from 'lucide-react';
import type { FileDto } from '@/lib/api/fileManager';

interface FilePreviewModalProps {
    file: FileDto;
    onClose: () => void;
}

function getFileCategory(contentType: string) {
    if (contentType.startsWith('image/')) return 'image';
    if (contentType === 'application/pdf') return 'pdf';
    if (contentType.startsWith('video/')) return 'video';
    if (contentType.startsWith('audio/')) return 'audio';
    if (contentType.startsWith('text/')) return 'text';
    return 'other';
}

export function getFileIcon(contentType: string, size: 'sm' | 'lg' = 'sm') {
    const s = size === 'lg' ? 'text-5xl' : 'text-2xl';
    const cat = getFileCategory(contentType);
    const icons: Record<string, string> = {
        image: '🖼️', pdf: '📄', video: '🎬', audio: '🎵', text: '📝', other: '📎'
    };
    if (contentType.includes('word') || contentType.includes('document')) return size === 'lg' ? <span className={s}>📝</span> : '📝';
    if (contentType.includes('sheet') || contentType.includes('excel')) return size === 'lg' ? <span className={s}>📊</span> : '📊';
    if (contentType.includes('zip') || contentType.includes('compressed')) return size === 'lg' ? <span className={s}>🗜️</span> : '🗜️';
    return size === 'lg' ? <span className={s}>{icons[cat]}</span> : icons[cat];
}

export function formatFileSize(bytes: number) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1048576).toFixed(1)} MB`;
}

export default function FilePreviewModal({ file, onClose }: FilePreviewModalProps) {
    const category = getFileCategory(file.contentType);

    return (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-md flex items-center justify-center z-50 p-4" onClick={onClose}>
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-4xl max-h-[90vh] flex flex-col" onClick={e => e.stopPropagation()}>
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b border-gray-100 flex-shrink-0">
                    <div className="flex items-center gap-3 min-w-0">
                        <span className="text-2xl flex-shrink-0">{typeof getFileIcon(file.contentType) === 'string' ? getFileIcon(file.contentType) : '📎'}</span>
                        <div className="min-w-0">
                            <p className="font-bold text-gray-900 truncate text-sm">{file.name}</p>
                            <p className="text-xs text-gray-400">{formatFileSize(file.sizeBytes)} · {file.contentType}</p>
                        </div>
                    </div>
                    <div className="flex items-center gap-2 flex-shrink-0">
                        <a href={file.url} target="_blank" rel="noopener noreferrer"
                            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold text-blue-600 bg-blue-50 rounded-lg hover:bg-blue-100 transition-colors">
                            <ExternalLink className="w-3.5 h-3.5" /> Open
                        </a>
                        <a href={file.url} download={file.originalFileName}
                            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors">
                            <Download className="w-3.5 h-3.5" /> Download
                        </a>
                        <button onClick={onClose} className="p-1.5 text-gray-400 hover:text-gray-700 hover:bg-gray-100 rounded-lg">
                            <X className="w-5 h-5" />
                        </button>
                    </div>
                </div>

                {/* Preview body */}
                <div className="flex-1 overflow-auto bg-gray-50 rounded-b-2xl flex items-center justify-center p-6 min-h-[300px]">
                    {category === 'image' && (
                        // eslint-disable-next-line @next/next/no-img-element
                        <img src={file.url} alt={file.name} className="max-w-full max-h-[70vh] object-contain rounded-xl shadow-md" />
                    )}
                    {category === 'pdf' && (
                        <iframe src={file.url} className="w-full h-[70vh] rounded-xl border border-gray-200" title={file.name} />
                    )}
                    {category === 'video' && (
                        <video controls className="max-w-full max-h-[70vh] rounded-xl shadow-md">
                            <source src={file.url} type={file.contentType} />
                        </video>
                    )}
                    {category === 'audio' && (
                        <audio controls className="w-full max-w-md">
                            <source src={file.url} type={file.contentType} />
                        </audio>
                    )}
                    {(category === 'other' || category === 'text') && (
                        <div className="text-center">
                            <span className="text-6xl block mb-4">📎</span>
                            <p className="text-gray-600 font-semibold">{file.name}</p>
                            <p className="text-gray-400 text-sm mt-1 mb-5">{formatFileSize(file.sizeBytes)}</p>
                            <a href={file.url} download={file.originalFileName}
                                className="inline-flex items-center gap-2 px-5 py-2.5 bg-blue-600 text-white rounded-xl font-bold hover:bg-blue-700 transition-colors text-sm">
                                <Download className="w-4 h-4" /> Download File
                            </a>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
