'use client';

import { useState } from 'react';
import { X, Folder } from 'lucide-react';

interface NewFolderModalProps {
    parentName?: string;
    onClose: () => void;
    onCreate: (name: string) => Promise<void>;
}

export function NewFolderModal({ parentName, onClose, onCreate }: NewFolderModalProps) {
    const [name, setName] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!name.trim()) return;
        setLoading(true);
        setError('');
        try {
            await onCreate(name.trim());
            onClose();
        } catch (err) {
            setError((err as Error).message);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-sm">
                <div className="flex items-center justify-between p-5 border-b border-gray-100">
                    <h3 className="text-base font-bold text-gray-900 flex items-center gap-2">
                        <Folder className="w-5 h-5 text-yellow-500" /> New Folder
                    </h3>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
                </div>
                <form onSubmit={handleSubmit} className="p-5 space-y-4">
                    {parentName && <p className="text-xs text-gray-500">Inside: <strong>{parentName}</strong></p>}
                    <input
                        autoFocus
                        value={name}
                        onChange={e => setName(e.target.value)}
                        placeholder="Folder name"
                        className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl text-black outline-none focus:ring-2 focus:ring-blue-400 text-sm font-medium"
                    />
                    {error && <p className="text-xs text-red-600">{error}</p>}
                    <div className="flex justify-end gap-2">
                        <button type="button" onClick={onClose} className="px-4 py-2 text-sm font-semibold text-gray-600 border border-gray-200 rounded-xl hover:bg-gray-50">Cancel</button>
                        <button type="submit" disabled={!name.trim() || loading}
                            className="px-4 py-2 text-sm font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 disabled:opacity-50">
                            {loading ? 'Creating...' : 'Create'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}

interface RenameModalProps {
    currentName: string;
    type: 'folder' | 'file';
    onClose: () => void;
    onRename: (name: string) => Promise<void>;
}

export function RenameModal({ currentName, type, onClose, onRename }: RenameModalProps) {
    const [name, setName] = useState(currentName);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!name.trim() || name.trim() === currentName) { onClose(); return; }
        setLoading(true);
        setError('');
        try {
            await onRename(name.trim());
            onClose();
        } catch (err) {
            setError((err as Error).message);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-sm">
                <div className="flex items-center justify-between p-5 border-b border-gray-100">
                    <h3 className="text-base font-bold text-gray-900">Rename {type === 'folder' ? 'Folder' : 'File'}</h3>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
                </div>
                <form onSubmit={handleSubmit} className="p-5 space-y-4">
                    <input
                        autoFocus
                        value={name}
                        onChange={e => setName(e.target.value)}
                        className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl text-black outline-none focus:ring-2 focus:ring-blue-400 text-sm font-medium"
                    />
                    {error && <p className="text-xs text-red-600">{error}</p>}
                    <div className="flex justify-end gap-2">
                        <button type="button" onClick={onClose} className="px-4 py-2 text-sm font-semibold text-gray-600 border border-gray-200 rounded-xl hover:bg-gray-50">Cancel</button>
                        <button type="submit" disabled={!name.trim() || loading}
                            className="px-4 py-2 text-sm font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 disabled:opacity-50">
                            {loading ? 'Saving...' : 'Rename'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
