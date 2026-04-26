'use client';

import { useState, useEffect, useCallback } from 'react';
import { Plus, Edit2, Trash2, ChevronDown, ChevronRight, CheckCircle2, ShieldAlert } from 'lucide-react';
import { getSops, deleteSop, deleteViolationType } from '@/lib/api/sops';
import type { SopResponse, SopViolationTypeResponse } from '@/types/admin';
import SopFormModal from './components/SopFormModal';
import ViolationFormModal from './components/ViolationFormModal';
import DeleteWarningModal from './components/DeleteWarningModal';

export default function SopsPage() {
    const [sops, setSops] = useState<SopResponse[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Accordion state
    const [expandedSops, setExpandedSops] = useState<Set<string>>(new Set());

    // Modal states
    const [isSopModalOpen, setIsSopModalOpen] = useState(false);
    const [editingSop, setEditingSop] = useState<SopResponse | undefined>();

    const [isViolationModalOpen, setIsViolationModalOpen] = useState(false);
    const [editingViolation, setEditingViolation] = useState<SopViolationTypeResponse | undefined>();
    const [activeSopIdForViolation, setActiveSopIdForViolation] = useState<string | null>(null);

    // Delete Warning Modal State
    const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
    const [deleteModalConfig, setDeleteModalConfig] = useState<{
        id: string;
        name: string;
        type: 'SOP' | 'Violation';
    } | null>(null);

    const loadSops = useCallback(async () => {
        try {
            setIsLoading(true);
            const data = await getSops();
            setSops(data);
            setError(null);
        } catch (err) {
            setError((err as Error).message || 'Failed to load SOPs');
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => {
        loadSops();
    }, [loadSops]);

    const toggleAccordion = (sopId: string) => {
        const newExpanded = new Set(expandedSops);
        if (newExpanded.has(sopId)) {
            newExpanded.delete(sopId);
        } else {
            newExpanded.add(sopId);
        }
        setExpandedSops(newExpanded);
    };

    const handleDeleteSop = (id: string, name: string) => {
        setDeleteModalConfig({ id, name, type: 'SOP' });
        setIsDeleteModalOpen(true);
    };

    const handleDeleteViolation = (id: string, name: string) => {
        setDeleteModalConfig({ id, name, type: 'Violation' });
        setIsDeleteModalOpen(true);
    };

    const confirmDelete = async () => {
        if (!deleteModalConfig) return;

        const { id, type } = deleteModalConfig;

        try {
            if (type === 'SOP') {
                await deleteSop(id);
                setSops(sops.filter(s => s.id !== id));
            } else {
                await deleteViolationType(id);
                loadSops();
            }
            setIsDeleteModalOpen(false);
            setDeleteModalConfig(null);
        } catch (err) {
            alert((err as Error).message || `Failed to delete ${type}`);
        }
    };

    return (
        <div className="space-y-6 text-black">
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">Standard Operating Procedures</h1>
                    <p className="text-gray-500 mt-1">Manage the global catalog of safety and security requirements.</p>
                </div>
                <button
                    onClick={() => {
                        setEditingSop(undefined);
                        setIsSopModalOpen(true);
                    }}
                    className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
                >
                    <Plus className="w-4 h-4" />
                    Create New SOP
                </button>
            </div>

            {error && (
                <div className="p-4 bg-red-50 text-red-700 rounded-lg border border-red-100">
                    {error}
                </div>
            )}

            {isLoading ? (
                <div className="text-center py-12 text-gray-500">Loading SOPs...</div>
            ) : sops.length === 0 ? (
                <div className="text-center py-12 bg-white rounded-xl border border-gray-200">
                    <ShieldAlert className="w-12 h-12 text-gray-400 mx-auto mb-3" />
                    <h3 className="text-lg font-medium text-gray-900">No SOPs found</h3>
                    <p className="text-gray-500 mt-1">Get started by creating your first Standard Operating Procedure.</p>
                </div>
            ) : (
                <div className="bg-white border text-black border-gray-200 rounded-xl overflow-hidden shadow-sm">
                    {sops.map((sop) => {
                        const isExpanded = expandedSops.has(sop.id);
                        const violations = sop.violationTypes || [];

                        return (
                            <div key={sop.id} className="border-b border-gray-100 last:border-0">
                                {/* SOP Header Row */}
                                <div className="flex items-center justify-between p-4 hover:bg-gray-50 transition-colors">
                                    <div
                                        className="flex items-center gap-3 cursor-pointer flex-1"
                                        onClick={() => toggleAccordion(sop.id)}
                                    >
                                        <button className="text-gray-400 hover:text-gray-600">
                                            {isExpanded ? <ChevronDown className="w-5 h-5" /> : <ChevronRight className="w-5 h-5" />}
                                        </button>
                                        <div>
                                            <h3 className="text-lg font-medium text-gray-900">{sop.name}</h3>
                                            <p className="text-sm text-gray-500">{sop.description}</p>
                                        </div>
                                    </div>

                                    <div className="flex flex-col items-end gap-2">
                                        <div className="flex items-center gap-2">
                                            <button
                                                onClick={() => {
                                                    setEditingSop(sop);
                                                    setIsSopModalOpen(true);
                                                }}
                                                className="p-1.5 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"
                                                title="Edit SOP"
                                            >
                                                <Edit2 className="w-4 h-4" />
                                            </button>
                                            <button
                                                onClick={() => handleDeleteSop(sop.id, sop.name)}
                                                className="p-1.5 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                                title="Delete SOP"
                                            >
                                                <Trash2 className="w-4 h-4" />
                                            </button>
                                        </div>
                                    </div>
                                </div>

                                {/* Violations Body */}
                                {isExpanded && (
                                    <div className="bg-gray-50/50 p-4 border-t border-gray-100 pl-14">
                                        <div className="flex justify-between items-center mb-4">
                                            <h4 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">
                                                Detectible Violations ({violations.length})
                                            </h4>
                                            <button
                                                onClick={() => {
                                                    setActiveSopIdForViolation(sop.id);
                                                    setEditingViolation(undefined);
                                                    setIsViolationModalOpen(true);
                                                }}
                                                className="flex items-center justify-center gap-1 px-3 py-1.5 text-sm bg-white border border-gray-200 text-gray-700 rounded-lg hover:bg-gray-50 hover:text-blue-600 transition-colors"
                                            >
                                                <Plus className="w-3.5 h-3.5" />
                                                Add Violation Type
                                            </button>
                                        </div>

                                        {violations.length === 0 ? (
                                            <div className="text-sm text-gray-500 italic py-2">
                                                No violation types defined for this SOP yet.
                                            </div>
                                        ) : (
                                            <div className="grid gap-3">
                                                {violations.map((v) => (
                                                    <div key={v.id} className="bg-white p-3 rounded-lg border border-gray-200 shadow-sm flex justify-between items-start group">
                                                        <div className="flex items-start gap-3">
                                                            <CheckCircle2 className="w-4 h-4 text-emerald-500 mt-1 flex-shrink-0" />
                                                            <div>
                                                                <div className="flex items-center gap-2">
                                                                    <span className="font-medium text-gray-900">{v.name}</span>
                                                                    <span className="text-xs px-2 py-0.5 bg-gray-100 text-gray-600 rounded font-mono border border-gray-200">
                                                                        {v.modelIdentifier}
                                                                    </span>
                                                                </div>
                                                                <p className="text-sm text-gray-500 mt-1">{v.description}</p>
                                                                {v.triggerLabels && (
                                                                    <div className="flex flex-wrap gap-1 mt-2">
                                                                        {v.triggerLabels.split(',').map(l => l.trim()).filter(Boolean).map(label => (
                                                                            <span key={label} className="px-1.5 py-0.5 bg-blue-50 text-blue-700 text-[10px] font-mono font-bold border border-blue-100 rounded">
                                                                                {label}
                                                                            </span>
                                                                        ))}
                                                                    </div>
                                                                )}
                                                            </div>
                                                        </div>
                                                        <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                                            <button
                                                                onClick={() => {
                                                                    setActiveSopIdForViolation(sop.id);
                                                                    setEditingViolation(v);
                                                                    setIsViolationModalOpen(true);
                                                                }}
                                                                className="p-1.5 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"
                                                            >
                                                                <Edit2 className="w-4 h-4" />
                                                            </button>
                                                            <button
                                                                onClick={() => handleDeleteViolation(v.id, v.name)}
                                                                className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                                            >
                                                                <Trash2 className="w-4 h-4" />
                                                            </button>
                                                        </div>
                                                    </div>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            )}

            {/* Modals */}
            <SopFormModal
                isOpen={isSopModalOpen}
                onClose={() => setIsSopModalOpen(false)}
                onSuccess={() => {
                    setIsSopModalOpen(false);
                    loadSops();
                }}
                initialData={editingSop}
            />

            {activeSopIdForViolation && (
                <ViolationFormModal
                    isOpen={isViolationModalOpen}
                    onClose={() => setIsViolationModalOpen(false)}
                    onSuccess={() => {
                        setIsViolationModalOpen(false);
                        loadSops();
                    }}
                    sopId={activeSopIdForViolation}
                    initialData={editingViolation}
                />
            )}

            <DeleteWarningModal
                isOpen={isDeleteModalOpen}
                onClose={() => setIsDeleteModalOpen(false)}
                onConfirm={confirmDelete}
                title={`Delete ${deleteModalConfig?.type}`}
                itemName={deleteModalConfig?.name || ''}
                type={deleteModalConfig?.type || 'SOP'}
            />
        </div>
    );
}
