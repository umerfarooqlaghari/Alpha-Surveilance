'use client';

import { useState, useEffect } from 'react';
import { Loader2, X, Search, AlertTriangle } from 'lucide-react';
import { getViolations, Violation } from '@/lib/api/tenant/violations';

interface SelectViolationsModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSelect: (selectedIds: string[]) => void;
    initialSelectedIds?: string[];
}

export default function SelectViolationsModal({ isOpen, onClose, onSelect, initialSelectedIds = [] }: SelectViolationsModalProps) {
    const [violations, setViolations] = useState<Violation[]>([]);
    const [loading, setLoading] = useState(false);
    const [search, setSearch] = useState('');
    const [selectedIds, setSelectedIds] = useState<string[]>(initialSelectedIds);

    useEffect(() => {
        if (isOpen) {
            fetchViolations();
            setSelectedIds(initialSelectedIds);
        }
    }, [isOpen]);

    const fetchViolations = async () => {
        setLoading(true);
        try {
            const data = await getViolations();
            setViolations(data);
        } catch (error) {
            console.error('Failed to fetch violations', error);
        } finally {
            setLoading(false);
        }
    };

    const filteredViolations = violations.filter(v =>
        v.type.toLowerCase().includes(search.toLowerCase()) ||
        v.id.toLowerCase().includes(search.toLowerCase())
    );

    const toggleSelection = (id: string) => {
        setSelectedIds(prev =>
            prev.includes(id)
                ? prev.filter(x => x !== id)
                : [...prev, id]
        );
    };

    const handleConfirm = () => {
        onSelect(selectedIds);
        onClose();
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
            <div className="bg-white rounded-2xl shadow-xl w-full max-w-2xl flex flex-col max-h-[90vh]">
                <div className="p-6 border-b border-gray-100 flex justify-between items-center">
                    <h2 className="text-xl font-bold text-gray-900">Select Violations</h2>
                    <button onClick={onClose} className="p-2 text-gray-400 hover:text-gray-600 rounded-full hover:bg-gray-100">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <div className="p-6 border-b border-gray-100">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 w-5 h-5" />
                        <input
                            type="text"
                            placeholder="Search by type or ID..."
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            className="w-full pl-10 pr-4 py-2 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none"
                        />
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto p-2">
                    {loading ? (
                        <div className="flex justify-center p-8"><Loader2 className="w-6 h-6 animate-spin text-blue-600" /></div>
                    ) : filteredViolations.length === 0 ? (
                        <div className="text-center p-8 text-gray-500">No violations found.</div>
                    ) : (
                        <div className="space-y-1">
                            {filteredViolations.map(v => (
                                <div
                                    key={v.id}
                                    onClick={() => toggleSelection(v.id)}
                                    className={`flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-colors ${selectedIds.includes(v.id) ? 'bg-blue-50 border border-blue-200' : 'hover:bg-gray-50 border border-transparent'}`}
                                >
                                    <input
                                        type="checkbox"
                                        checked={selectedIds.includes(v.id)}
                                        readOnly
                                        className="w-4 h-4 text-blue-600 rounded border-gray-300 focus:ring-blue-500"
                                    />
                                    <div className={`w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold ${v.severity === 'Critical' || v.severity === '3' ? 'bg-red-100 text-red-700' :
                                            v.severity === 'High' || v.severity === '2' ? 'bg-orange-100 text-orange-700' :
                                                'bg-blue-100 text-blue-700'
                                        }`}>
                                        <AlertTriangle className="w-4 h-4" />
                                    </div>
                                    <div className="flex-1">
                                        <div className="flex justify-between">
                                            <p className="text-sm font-medium text-gray-900">{v.type}</p>
                                            <span className="text-xs text-gray-400">{new Date(v.timestamp).toLocaleDateString()}</span>
                                        </div>
                                        <p className="text-xs text-gray-500">ID: {v.id.substring(0, 8)}... • Camera: {v.cameraId || 'N/A'}</p>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                <div className="p-6 border-t border-gray-100 flex justify-between items-center bg-gray-50/50 rounded-b-2xl">
                    <span className="text-sm text-gray-500">{selectedIds.length} selected</span>
                    <div className="flex gap-3">
                        <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-200/50 rounded-lg">Cancel</button>
                        <button
                            onClick={handleConfirm}
                            className="px-4 py-2 text-sm font-medium bg-blue-600 text-white rounded-lg hover:bg-blue-700 shadow-lg shadow-blue-500/30"
                        >
                            Confirm Selection
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}
