'use client';

import { Folder, MoreVertical, Eye, Pencil, Trash2, Link, FolderOpen } from 'lucide-react';
import type { FolderDto, FileDto } from '@/lib/api/fileManager';
import { getFileIcon, formatFileSize } from './FilePreviewModal';
import { useState, useRef, useEffect } from 'react';

// ─── File Card ───────────────────────────────────────────────────────────────

interface FileCardProps {
    file: FileDto;
    onPreview: (f: FileDto) => void;
    onRename: (f: FileDto) => void;
    onDelete: (f: FileDto) => void;
    onCopyLink: (url: string) => void;
}

export function FileCard({ file, onPreview, onRename, onDelete, onCopyLink }: FileCardProps) {
    const [menuOpen, setMenuOpen] = useState(false);
    const menuRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const handler = (e: MouseEvent) => { if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false); };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, []);

    const isImage = file.contentType.startsWith('image/');

    return (
        <div
            className="group relative bg-white border border-gray-200 rounded-2xl overflow-hidden hover:border-blue-300 hover:shadow-lg transition-all cursor-pointer"
            onDoubleClick={() => onPreview(file)}
        >
            {/* Preview area */}
            <div className="h-32 bg-gradient-to-br from-gray-50 to-gray-100 flex items-center justify-center overflow-hidden">
                {isImage ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={file.url} alt={file.name} className="w-full h-full object-cover" />
                ) : (
                    <span className="text-4xl">{getFileIcon(file.contentType) as string}</span>
                )}
            </div>

            {/* Info */}
            <div className="p-3">
                <p className="text-xs font-bold text-gray-800 truncate leading-tight">{file.name}</p>
                <p className="text-[10px] text-gray-400 mt-0.5">{formatFileSize(file.sizeBytes)}</p>
            </div>

            {/* Context menu button */}
            <div className="absolute top-2 right-2" ref={menuRef}>
                <button
                    onClick={(e) => { e.stopPropagation(); setMenuOpen(v => !v); }}
                    className="opacity-0 group-hover:opacity-100 transition-opacity bg-white/90 backdrop-blur-sm p-1 rounded-lg shadow-sm hover:bg-white"
                >
                    <MoreVertical className="w-4 h-4 text-gray-600" />
                </button>
                {menuOpen && (
                    <div className="absolute right-0 top-8 bg-white border border-gray-200 rounded-xl shadow-xl z-20 py-1 min-w-[140px]">
                        <MenuItem icon={<Eye className="w-4 h-4" />} label="Preview" onClick={() => { setMenuOpen(false); onPreview(file); }} />
                        <MenuItem icon={<Link className="w-4 h-4" />} label="Copy Link" onClick={() => { setMenuOpen(false); onCopyLink(file.url); }} />
                        <MenuItem icon={<Pencil className="w-4 h-4" />} label="Rename" onClick={() => { setMenuOpen(false); onRename(file); }} />
                        <div className="border-t border-gray-100 my-1" />
                        <MenuItem icon={<Trash2 className="w-4 h-4 text-red-500" />} label="Delete" labelClass="text-red-600" onClick={() => { setMenuOpen(false); onDelete(file); }} />
                    </div>
                )}
            </div>
        </div>
    );
}

// ─── Folder Card ─────────────────────────────────────────────────────────────

interface FolderCardProps {
    folder: FolderDto;
    onOpen: (f: FolderDto) => void;
    onRename: (f: FolderDto) => void;
    onDelete: (f: FolderDto) => void;
}

export function FolderCard({ folder, onOpen, onRename, onDelete }: FolderCardProps) {
    const [menuOpen, setMenuOpen] = useState(false);
    const menuRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const handler = (e: MouseEvent) => { if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false); };
        document.addEventListener('mousedown', handler);
        return () => document.removeEventListener('mousedown', handler);
    }, []);

    return (
        <div
            className="group relative bg-yellow-50 border border-yellow-200 rounded-2xl p-4 hover:border-yellow-400 hover:shadow-lg transition-all cursor-pointer flex items-start gap-3"
            onDoubleClick={() => onOpen(folder)}
        >
            <FolderOpen className="w-10 h-10 text-yellow-500 flex-shrink-0" />
            <div className="flex-1 min-w-0">
                <p className="text-sm font-bold text-gray-800 truncate">{folder.name}</p>
                <p className="text-[10px] text-gray-500 mt-0.5">
                    {folder.childFolderCount > 0 && `${folder.childFolderCount} folder${folder.childFolderCount !== 1 ? 's' : ''}`}
                    {folder.childFolderCount > 0 && folder.fileCount > 0 && ' · '}
                    {folder.fileCount > 0 && `${folder.fileCount} file${folder.fileCount !== 1 ? 's' : ''}`}
                    {folder.childFolderCount === 0 && folder.fileCount === 0 && 'Empty'}
                </p>
            </div>

            <div className="absolute top-2 right-2" ref={menuRef}>
                <button
                    onClick={(e) => { e.stopPropagation(); setMenuOpen(v => !v); }}
                    className="opacity-0 group-hover:opacity-100 transition-opacity p-1 hover:bg-yellow-100 rounded-lg"
                >
                    <MoreVertical className="w-4 h-4 text-gray-600" />
                </button>
                {menuOpen && (
                    <div className="absolute right-0 top-8 bg-white border border-gray-200 rounded-xl shadow-xl z-20 py-1 min-w-[140px]">
                        <MenuItem icon={<FolderOpen className="w-4 h-4" />} label="Open" onClick={() => { setMenuOpen(false); onOpen(folder); }} />
                        <MenuItem icon={<Pencil className="w-4 h-4" />} label="Rename" onClick={() => { setMenuOpen(false); onRename(folder); }} />
                        <div className="border-t border-gray-100 my-1" />
                        <MenuItem icon={<Trash2 className="w-4 h-4 text-red-500" />} label="Delete" labelClass="text-red-600" onClick={() => { setMenuOpen(false); onDelete(folder); }} />
                    </div>
                )}
            </div>
        </div>
    );
}

// ─── Shared ──────────────────────────────────────────────────────────────────

function MenuItem({ icon, label, onClick, labelClass }: { icon: React.ReactNode; label: string; onClick: () => void; labelClass?: string }) {
    return (
        <button onClick={onClick} className="w-full flex items-center gap-2 px-3 py-1.5 text-sm hover:bg-gray-50 transition-colors text-left">
            <span className="text-gray-500">{icon}</span>
            <span className={labelClass || 'text-gray-700'}>{label}</span>
        </button>
    );
}
