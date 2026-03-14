'use client';

import { useState, useEffect, useCallback } from 'react';
import {
    FolderOpen, Upload, FolderPlus, Search, ChevronRight, Home, Loader2,
    Grid3X3, List, FolderOpen as FolderIcon
} from 'lucide-react';
import {
    fileManagerApi, type FolderDto, type FileDto, type BreadcrumbItem,
    type FolderContentsResponse, type RootContentsResponse
} from '@/lib/api/fileManager';
import FolderTree from './components/FolderTree';
import UploadModal from './components/UploadModal';
import { NewFolderModal, RenameModal } from './components/Modals';
import FilePreviewModal from './components/FilePreviewModal';
import { FileCard, FolderCard } from './components/FileGrid';

type ViewMode = 'grid' | 'list';
type ModalState =
    | null
    | { type: 'upload' }
    | { type: 'newFolder'; parentId: string | null }
    | { type: 'renameFile'; file: FileDto }
    | { type: 'renameFolder'; folder: FolderDto }
    | { type: 'preview'; file: FileDto };

export default function FileManagerPage() {
    const [viewMode, setViewMode] = useState<ViewMode>('grid');
    const [rootFolders, setRootFolders] = useState<FolderDto[]>([]);
    const [rootFiles, setRootFiles] = useState<FileDto[]>([]);
    const [currentContents, setCurrentContents] = useState<FolderContentsResponse | null>(null);
    const [currentFolderId, setCurrentFolderId] = useState<string | null>(null); // null = root
    const [files, setFiles] = useState<FileDto[]>([]); // root files
    const [loading, setLoading] = useState(true);
    const [modal, setModal] = useState<ModalState>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<{ folders: FolderDto[]; files: FileDto[] } | null>(null);
    const [isSearching, setIsSearching] = useState(false);
    const [toast, setToast] = useState<string | null>(null);

    const showToast = (msg: string) => { setToast(msg); setTimeout(() => setToast(null), 2500); };

    // ── Load root or folder contents ─────────────────────────────────────────
    const loadRoot = useCallback(async () => {
        setLoading(true);
        try {
            const result = await fileManagerApi.getRootFolders();
            // Guard against old API returning a plain array (before backend restart)
            if (Array.isArray(result)) {
                setRootFolders(result as unknown as FolderDto[]);
                setRootFiles([]);
            } else {
                setRootFolders(result.folders ?? []);
                setRootFiles(result.files ?? []);
            }
            setCurrentContents(null);
            setCurrentFolderId(null);
        } finally {
            setLoading(false);
        }
    }, []);

    const loadFolder = useCallback(async (id: string) => {
        setLoading(true);
        try {
            const contents = await fileManagerApi.getFolderContents(id);
            setCurrentContents(contents);
            setCurrentFolderId(id);
            setSearchQuery('');
            setSearchResults(null);
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { loadRoot(); }, [loadRoot]);

    // ── Search ────────────────────────────────────────────────────────────────
    useEffect(() => {
        if (!searchQuery.trim()) { setSearchResults(null); return; }
        const timer = setTimeout(async () => {
            setIsSearching(true);
            try {
                const results = await fileManagerApi.search(searchQuery);
                setSearchResults(results);
            } finally {
                setIsSearching(false);
            }
        }, 400);
        return () => clearTimeout(timer);
    }, [searchQuery]);

    // ── Folder actions ────────────────────────────────────────────────────────
    const handleCreateFolder = async (name: string) => {
        const parentId = modal?.type === 'newFolder' ? (modal as any).parentId : null;
        const folder = await fileManagerApi.createFolder(name, parentId);
        if (!parentId) {
            setRootFolders(prev => [...prev, folder].sort((a, b) => a.name.localeCompare(b.name)));
        } else if (currentFolderId === parentId) {
            setCurrentContents(prev => prev ? { ...prev, subFolders: [...prev.subFolders, folder].sort((a, b) => a.name.localeCompare(b.name)) } : prev);
        }
        showToast(`Folder "${name}" created`);
    };

    const handleRenameFolder = async (folder: FolderDto, name: string) => {
        await fileManagerApi.renameFolder(folder.id, name);
        if (!folder.parentFolderId) {
            setRootFolders(prev => prev.map(f => f.id === folder.id ? { ...f, name } : f));
        }
        setCurrentContents(prev => prev ? { ...prev, subFolders: prev.subFolders.map(f => f.id === folder.id ? { ...f, name } : f) } : prev);
        showToast('Folder renamed');
    };

    const handleDeleteFolder = async (folder: FolderDto) => {
        if (!confirm(`Delete "${folder.name}" and all its contents? This cannot be undone.`)) return;
        await fileManagerApi.deleteFolder(folder.id);
        setRootFolders(prev => prev.filter(f => f.id !== folder.id));
        setCurrentContents(prev => prev ? { ...prev, subFolders: prev.subFolders.filter(f => f.id !== folder.id) } : prev);
        showToast(`Folder deleted`);
    };

    // ── File actions ──────────────────────────────────────────────────────────
    const handleUpload = async (uploadFiles: File[], folderId: string | null) => {
        await fileManagerApi.uploadFiles(uploadFiles, folderId);
        // Reload the current view to show new files
        if (folderId) {
            await loadFolder(folderId);
        } else {
            await loadRoot();
        }
        showToast(`${uploadFiles.length} file${uploadFiles.length !== 1 ? 's' : ''} uploaded`);
    };

    const handleRenameFile = async (file: FileDto, name: string) => {
        await fileManagerApi.renameFile(file.id, name);
        setCurrentContents(prev => prev ? { ...prev, files: prev.files.map(f => f.id === file.id ? { ...f, name } : f) } : prev);
        setRootFiles(prev => prev.map(f => f.id === file.id ? { ...f, name } : f));
        showToast('File renamed');
    };

    const handleDeleteFile = async (file: FileDto) => {
        if (!confirm(`Delete "${file.name}"? This cannot be undone.`)) return;
        await fileManagerApi.deleteFile(file.id);
        setCurrentContents(prev => prev ? { ...prev, files: prev.files.filter(f => f.id !== file.id) } : prev);
        setRootFiles(prev => prev.filter(f => f.id !== file.id));
        showToast('File deleted');
    };

    const handleCopyLink = (url: string) => {
        navigator.clipboard.writeText(url).then(() => showToast('Link copied to clipboard'));
    };

    // ── Derived display data ──────────────────────────────────────────────────
    const displayFolders = searchResults ? searchResults.folders
        : currentContents ? currentContents.subFolders
            : rootFolders;

    const displayFiles = searchResults ? searchResults.files
        : currentContents ? currentContents.files
            : rootFiles;  // root-level files (FolderId = null)

    const breadcrumb: BreadcrumbItem[] = currentContents?.breadcrumb || [];
    const currentFolderName = currentContents?.name;

    return (
        <div className="flex h-[calc(100vh-73px)] bg-gray-50 -m-8">
            {/* ── Sidebar ── */}
            <aside className="w-56 bg-white border-r border-gray-200 flex flex-col flex-shrink-0 overflow-y-auto">
                {/* My Drive root link */}
                <button
                    onClick={loadRoot}
                    className={`flex items-center gap-2.5 px-4 py-3 text-sm font-bold transition-colors border-b border-gray-100 ${!currentFolderId && !searchQuery ? 'text-blue-700 bg-blue-50' : 'text-gray-700 hover:bg-gray-50'}`}
                >
                    <FolderIcon className="w-5 h-5 text-yellow-500" /> My Drive
                </button>

                {/* Folder tree */}
                <div className="flex-1 overflow-y-auto">
                    <FolderTree
                        folders={rootFolders}
                        activeFolderId={currentFolderId}
                        onFolderClick={loadFolder}
                        onCreateFolder={(parentId) => setModal({ type: 'newFolder', parentId })}
                        loadChildren={async (id) => {
                            const c = await fileManagerApi.getFolderContents(id);
                            return c.subFolders;
                        }}
                    />
                </div>
            </aside>

            {/* ── Main Content ── */}
            <div className="flex-1 flex flex-col overflow-hidden">
                {/* Top toolbar */}
                <div className="bg-white border-b border-gray-200 px-6 py-3 flex items-center gap-4 flex-shrink-0">
                    {/* Search */}
                    <div className="relative flex-1 max-w-md">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                        <input
                            value={searchQuery}
                            onChange={e => setSearchQuery(e.target.value)}
                            placeholder="Search files and folders..."
                            className="w-full pl-9 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-xl text-sm text-black outline-none focus:ring-2 focus:ring-blue-400"
                        />
                        {isSearching && <Loader2 className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 animate-spin" />}
                    </div>

                    <div className="flex items-center gap-2 ml-auto">
                        {/* View mode toggle */}
                        <button onClick={() => setViewMode('grid')} className={`p-2 rounded-lg transition-colors ${viewMode === 'grid' ? 'bg-blue-50 text-blue-600' : 'text-gray-400 hover:text-gray-600 hover:bg-gray-50'}`}>
                            <Grid3X3 className="w-4 h-4" />
                        </button>
                        <button onClick={() => setViewMode('list')} className={`p-2 rounded-lg transition-colors ${viewMode === 'list' ? 'bg-blue-50 text-blue-600' : 'text-gray-400 hover:text-gray-600 hover:bg-gray-50'}`}>
                            <List className="w-4 h-4" />
                        </button>

                        <div className="w-px h-5 bg-gray-200 mx-1" />

                        {/* New Folder */}
                        <button
                            onClick={() => setModal({ type: 'newFolder', parentId: currentFolderId })}
                            className="flex items-center gap-1.5 px-3 py-2 text-sm font-semibold text-gray-600 bg-white border border-gray-200 rounded-xl hover:bg-gray-50 transition-colors"
                        >
                            <FolderPlus className="w-4 h-4" /> New Folder
                        </button>

                        {/* Upload */}
                        <button
                            onClick={() => setModal({ type: 'upload' })}
                            className="flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 transition-colors shadow-sm shadow-blue-500/20"
                        >
                            <Upload className="w-4 h-4" /> Upload
                        </button>
                    </div>
                </div>

                {/* Breadcrumb */}
                {!searchQuery && (
                    <div className="flex items-center gap-1.5 px-6 py-2.5 text-sm text-gray-500 bg-white border-b border-gray-100 flex-shrink-0">
                        <button onClick={loadRoot} className="flex items-center gap-1 hover:text-blue-600 transition-colors font-medium">
                            <Home className="w-3.5 h-3.5" /> My Drive
                        </button>
                        {breadcrumb.map((crumb, i) => (
                            <span key={crumb.id} className="flex items-center gap-1.5">
                                <ChevronRight className="w-3.5 h-3.5 text-gray-300" />
                                <button
                                    onClick={() => i < breadcrumb.length - 1 && loadFolder(crumb.id)}
                                    className={`font-medium transition-colors ${i === breadcrumb.length - 1 ? 'text-gray-900 cursor-default' : 'hover:text-blue-600'}`}
                                >
                                    {crumb.name}
                                </button>
                            </span>
                        ))}
                    </div>
                )}
                {searchQuery && (
                    <div className="px-6 py-2 text-xs text-gray-500 bg-white border-b border-gray-100 flex-shrink-0">
                        Search results for <strong>"{searchQuery}"</strong> — {displayFolders.length + displayFiles.length} items found
                    </div>
                )}

                {/* Content area */}
                <div className="flex-1 overflow-y-auto p-6">
                    {loading ? (
                        <div className="flex items-center justify-center h-48">
                            <Loader2 className="w-6 h-6 text-blue-500 animate-spin" />
                        </div>
                    ) : displayFolders.length === 0 && displayFiles.length === 0 ? (
                        <div className="flex flex-col items-center justify-center h-64 text-center">
                            <FolderOpen className="w-16 h-16 text-gray-200 mb-4" />
                            <p className="text-gray-500 font-semibold">
                                {searchQuery ? 'No results found' : 'This folder is empty'}
                            </p>
                            {!searchQuery && (
                                <p className="text-gray-400 text-sm mt-1">
                                    Upload files or create a folder to get started
                                </p>
                            )}
                        </div>
                    ) : (
                        <div className="space-y-6">
                            {/* Folders section */}
                            {displayFolders.length > 0 && (
                                <div>
                                    <p className="text-xs font-bold text-gray-400 uppercase tracking-widest mb-3">Folders</p>
                                    <div className={viewMode === 'grid'
                                        ? 'grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3'
                                        : 'space-y-1'}>
                                        {displayFolders.map(folder => (
                                            <FolderCard
                                                key={folder.id}
                                                folder={folder}
                                                onOpen={(f) => loadFolder(f.id)}
                                                onRename={(f) => setModal({ type: 'renameFolder', folder: f })}
                                                onDelete={handleDeleteFolder}
                                            />
                                        ))}
                                    </div>
                                </div>
                            )}

                            {/* Files section */}
                            {displayFiles.length > 0 && (
                                <div>
                                    <p className="text-xs font-bold text-gray-400 uppercase tracking-widest mb-3">Files</p>
                                    <div className={viewMode === 'grid'
                                        ? 'grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-3'
                                        : 'space-y-1'}>
                                        {displayFiles.map(file => (
                                            <FileCard
                                                key={file.id}
                                                file={file}
                                                onPreview={(f) => setModal({ type: 'preview', file: f })}
                                                onRename={(f) => setModal({ type: 'renameFile', file: f })}
                                                onDelete={handleDeleteFile}
                                                onCopyLink={handleCopyLink}
                                            />
                                        ))}
                                    </div>
                                </div>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {/* ── Modals ── */}
            {modal?.type === 'upload' && (
                <UploadModal
                    folderId={currentFolderId}
                    folderName={currentFolderName}
                    onClose={() => setModal(null)}
                    onUpload={handleUpload}
                />
            )}
            {modal?.type === 'newFolder' && (
                <NewFolderModal
                    parentName={modal.parentId ? currentFolderName : undefined}
                    onClose={() => setModal(null)}
                    onCreate={handleCreateFolder}
                />
            )}
            {modal?.type === 'renameFolder' && (
                <RenameModal
                    currentName={modal.folder.name}
                    type="folder"
                    onClose={() => setModal(null)}
                    onRename={(name) => handleRenameFolder(modal.folder, name)}
                />
            )}
            {modal?.type === 'renameFile' && (
                <RenameModal
                    currentName={modal.file.name}
                    type="file"
                    onClose={() => setModal(null)}
                    onRename={(name) => handleRenameFile(modal.file, name)}
                />
            )}
            {modal?.type === 'preview' && (
                <FilePreviewModal
                    file={modal.file}
                    onClose={() => setModal(null)}
                />
            )}

            {/* Toast */}
            {toast && (
                <div className="fixed bottom-6 left-1/2 -translate-x-1/2 bg-gray-900 text-white text-sm font-semibold px-5 py-2.5 rounded-full shadow-xl z-50 animate-fade-in">
                    {toast}
                </div>
            )}
        </div>
    );
}
