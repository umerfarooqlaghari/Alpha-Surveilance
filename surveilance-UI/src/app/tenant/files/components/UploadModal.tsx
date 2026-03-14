'use client';

import { useState, useRef } from 'react';
import { Upload, X, Loader2, CheckCircle, AlertCircle } from 'lucide-react';

interface UploadModalProps {
    folderId: string | null;
    folderName?: string;
    onClose: () => void;
    onUpload: (files: File[], folderId: string | null) => Promise<void>;
}

interface FileUploadStatus {
    file: File;
    status: 'pending' | 'uploading' | 'done' | 'error';
    error?: string;
}

export default function UploadModal({ folderId, folderName, onClose, onUpload }: UploadModalProps) {
    const [files, setFiles] = useState<FileUploadStatus[]>([]);
    const [isDragging, setIsDragging] = useState(false);
    const [isUploading, setIsUploading] = useState(false);
    const [done, setDone] = useState(false);
    const inputRef = useRef<HTMLInputElement>(null);

    const addFiles = (incoming: FileList | File[]) => {
        const arr = Array.from(incoming);
        setFiles(prev => [
            ...prev,
            ...arr.filter(f => !prev.some(p => p.file.name === f.name && p.file.size === f.size))
                .map(f => ({ file: f, status: 'pending' as const }))
        ]);
    };

    const removeFile = (i: number) => setFiles(prev => prev.filter((_, idx) => idx !== i));

    const handleDrop = (e: React.DragEvent) => {
        e.preventDefault();
        setIsDragging(false);
        addFiles(e.dataTransfer.files);
    };

    const formatSize = (bytes: number) => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / 1048576).toFixed(1)} MB`;
    };

    const getFileIcon = (type: string) => {
        if (type.startsWith('image/')) return '🖼️';
        if (type === 'application/pdf') return '📄';
        if (type.includes('video')) return '🎬';
        if (type.includes('audio')) return '🎵';
        if (type.includes('word') || type.includes('document')) return '📝';
        if (type.includes('sheet') || type.includes('excel')) return '📊';
        if (type.includes('zip') || type.includes('compressed')) return '🗜️';
        return '📎';
    };

    const handleUpload = async () => {
        if (!files.length) return;
        setIsUploading(true);
        try {
            await onUpload(files.map(f => f.file), folderId);
            setFiles(prev => prev.map(f => ({ ...f, status: 'done' as const })));
            setDone(true);
            setTimeout(onClose, 1200);
        } catch (err) {
            setFiles(prev => prev.map(f => ({ ...f, status: 'error' as const, error: (err as Error).message })));
        } finally {
            setIsUploading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg">
                <div className="flex items-center justify-between p-5 border-b border-gray-100">
                    <div>
                        <h3 className="text-lg font-bold text-gray-900">Upload Files</h3>
                        {folderName && <p className="text-xs text-gray-500 mt-0.5">Into: <strong>{folderName}</strong></p>}
                    </div>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600 p-1 rounded-full hover:bg-gray-100">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <div className="p-5 space-y-4">
                    {/* Drop zone */}
                    <div
                        onDragOver={e => { e.preventDefault(); setIsDragging(true); }}
                        onDragLeave={() => setIsDragging(false)}
                        onDrop={handleDrop}
                        onClick={() => inputRef.current?.click()}
                        className={`border-2 border-dashed rounded-xl p-8 text-center cursor-pointer transition-all ${isDragging ? 'border-blue-400 bg-blue-50' : 'border-gray-200 hover:border-blue-300 hover:bg-gray-50'}`}
                    >
                        <Upload className={`w-8 h-8 mx-auto mb-2 ${isDragging ? 'text-blue-500' : 'text-gray-400'}`} />
                        <p className="text-sm font-semibold text-gray-700">Drag &amp; drop files here</p>
                        <p className="text-xs text-gray-400 mt-1">or click to browse — up to 50 MB per file</p>
                        <input ref={inputRef} type="file" multiple className="hidden" onChange={e => e.target.files && addFiles(e.target.files)} />
                    </div>

                    {/* File list */}
                    {files.length > 0 && (
                        <div className="space-y-2 max-h-52 overflow-y-auto">
                            {files.map((f, i) => (
                                <div key={i} className="flex items-center gap-3 p-2.5 bg-gray-50 rounded-lg">
                                    <span className="text-lg flex-shrink-0">{getFileIcon(f.file.type)}</span>
                                    <div className="flex-1 min-w-0">
                                        <p className="text-xs font-semibold text-gray-800 truncate">{f.file.name}</p>
                                        <p className="text-[10px] text-gray-400">{formatSize(f.file.size)}</p>
                                    </div>
                                    {f.status === 'done' && <CheckCircle className="w-4 h-4 text-green-500 flex-shrink-0" />}
                                    {f.status === 'error' && <AlertCircle className="w-4 h-4 text-red-500 flex-shrink-0" />}
                                    {f.status === 'uploading' && <Loader2 className="w-4 h-4 text-blue-500 animate-spin flex-shrink-0" />}
                                    {f.status === 'pending' && (
                                        <button onClick={() => removeFile(i)} className="text-gray-300 hover:text-red-500 flex-shrink-0">
                                            <X className="w-4 h-4" />
                                        </button>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                <div className="flex justify-end gap-3 px-5 pb-5">
                    <button onClick={onClose} className="px-4 py-2 text-sm font-semibold text-gray-600 bg-white border border-gray-200 rounded-xl hover:bg-gray-50">
                        Cancel
                    </button>
                    <button onClick={handleUpload} disabled={!files.length || isUploading || done}
                        className="px-5 py-2 text-sm font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2 shadow-sm">
                        {isUploading && <Loader2 className="w-4 h-4 animate-spin" />}
                        {done ? '✓ Done!' : isUploading ? 'Uploading...' : `Upload ${files.length} file${files.length !== 1 ? 's' : ''}`}
                    </button>
                </div>
            </div>
        </div>
    );
}
