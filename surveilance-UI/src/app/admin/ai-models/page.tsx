'use client';

import { useState, useEffect, useCallback } from 'react';
import { Plus, Edit2, Trash2, Power, PowerOff, Brain, AlertCircle, CheckCircle2, Clock, Download, XCircle } from 'lucide-react';
import { getAiModels, enableAiModel, disableAiModel, deleteAiModel } from '@/lib/api/aiModels';
import type { AiModelResponse, AiModelStatus } from '@/types/admin';
import AiModelFormModal from './components/AiModelFormModal';

// ── Status badge ──────────────────────────────────────────────────────────────

function StatusBadge({ status, errorMessage }: { status: AiModelStatus; errorMessage?: string }) {
    const cfg: Record<AiModelStatus, { label: string; className: string; icon: React.ReactNode }> = {
        Available:   { label: 'Available',   className: 'bg-green-100 text-green-700',  icon: <CheckCircle2 className="w-3 h-3" /> },
        Registered:  { label: 'Registered',  className: 'bg-gray-100 text-gray-500',   icon: <Clock className="w-3 h-3" /> },
        Downloading: { label: 'Downloading', className: 'bg-blue-100 text-blue-600',   icon: <Download className="w-3 h-3 animate-bounce" /> },
        Disabled:    { label: 'Disabled',    className: 'bg-amber-100 text-amber-600', icon: <PowerOff className="w-3 h-3" /> },
        Error:       { label: 'Error',       className: 'bg-red-100 text-red-600',     icon: <XCircle className="w-3 h-3" /> },
    };
    const { label, className, icon } = cfg[status] ?? cfg.Registered;
    return (
        <span
            title={status === 'Error' && errorMessage ? errorMessage : undefined}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ${className}`}
        >
            {icon}{label}
        </span>
    );
}

function ModelTypeBadge({ type }: { type: string }) {
    const map: Record<string, string> = {
        YoloLocal:     'bg-indigo-100 text-indigo-700',
        YoloFineTuned: 'bg-violet-100 text-violet-700',
        RoboflowCloud: 'bg-sky-100 text-sky-700',
    };
    const labels: Record<string, string> = {
        YoloLocal:     'YOLO Local',
        YoloFineTuned: 'YOLO Fine-Tuned',
        RoboflowCloud: 'Roboflow Cloud',
    };
    return (
        <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${map[type] ?? 'bg-gray-100 text-gray-500'}`}>
            {labels[type] ?? type}
        </span>
    );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function AiModelsPage() {
    const [models, setModels] = useState<AiModelResponse[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [actionError, setActionError] = useState<string | null>(null);

    const [isFormOpen, setIsFormOpen] = useState(false);
    const [editing, setEditing] = useState<AiModelResponse | undefined>();

    const load = useCallback(async () => {
        try {
            setIsLoading(true);
            const data = await getAiModels();
            setModels(data);
            setError(null);
        } catch (err) {
            setError((err as Error).message || 'Failed to load AI models');
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => { load(); }, [load]);

    const handleToggle = async (model: AiModelResponse) => {
        setActionError(null);
        try {
            if (model.status === 'Disabled') {
                await enableAiModel(model.id);
            } else {
                await disableAiModel(model.id);
            }
            await load();
        } catch (err) {
            setActionError((err as Error).message);
        }
    };

    const handleDelete = async (model: AiModelResponse) => {
        if (!confirm(`Delete model "${model.displayName}"? This cannot be undone.`)) return;
        setActionError(null);
        try {
            await deleteAiModel(model.id);
            setModels(prev => prev.filter(m => m.id !== model.id));
        } catch (err) {
            setActionError((err as Error).message);
        }
    };

    const openCreate = () => { setEditing(undefined); setIsFormOpen(true); };
    const openEdit   = (m: AiModelResponse) => { setEditing(m); setIsFormOpen(true); };

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-2">
                        <Brain className="w-6 h-6 text-blue-500" />
                        AI Model Registry
                    </h1>
                    <p className="text-sm text-gray-500 mt-1">
                        Manage YOLO and Roboflow models used by the Vision Inference Service.
                    </p>
                </div>
                <button
                    onClick={openCreate}
                    className="flex items-center gap-2 bg-blue-500 hover:bg-blue-600 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow transition-colors"
                >
                    <Plus className="w-4 h-4" /> Register Model
                </button>
            </div>

            {/* Errors */}
            {error && (
                <div className="flex items-center gap-2 bg-red-50 border border-red-200 text-red-700 rounded-xl px-4 py-3 text-sm">
                    <AlertCircle className="w-4 h-4 flex-shrink-0" />{error}
                </div>
            )}
            {actionError && (
                <div className="flex items-center gap-2 bg-amber-50 border border-amber-200 text-amber-700 rounded-xl px-4 py-3 text-sm">
                    <AlertCircle className="w-4 h-4 flex-shrink-0" />{actionError}
                </div>
            )}

            {/* Table */}
            <div className="bg-white rounded-2xl shadow-sm border border-gray-100 overflow-hidden">
                {isLoading ? (
                    <div className="p-12 text-center text-gray-400 text-sm">Loading models…</div>
                ) : models.length === 0 ? (
                    <div className="p-12 text-center text-gray-400 text-sm">
                        No models registered yet.{' '}
                        <button onClick={openCreate} className="text-blue-500 hover:underline">Register one</button>.
                    </div>
                ) : (
                    <table className="w-full text-sm">
                        <thead className="bg-gray-50 border-b border-gray-100">
                            <tr>
                                <th className="text-left px-6 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">Model</th>
                                <th className="text-left px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">Type</th>
                                <th className="text-left px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">Status</th>
                                <th className="text-left px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">Version</th>
                                <th className="text-right px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">SOP Rules</th>
                                <th className="text-right px-6 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wide">Actions</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-50">
                            {models.map(m => (
                                <tr key={m.id} className="hover:bg-gray-50/60 transition-colors">
                                    <td className="px-6 py-4">
                                        <p className="font-semibold text-gray-900">{m.displayName}</p>
                                        <p className="text-xs text-gray-400 font-mono mt-0.5">{m.modelKey}</p>
                                        {m.description && (
                                            <p className="text-xs text-gray-400 mt-0.5 max-w-xs truncate">{m.description}</p>
                                        )}
                                    </td>
                                    <td className="px-4 py-4">
                                        <ModelTypeBadge type={m.modelType} />
                                    </td>
                                    <td className="px-4 py-4">
                                        <StatusBadge status={m.status} errorMessage={m.errorMessage} />
                                    </td>
                                    <td className="px-4 py-4 text-gray-500 text-xs">{m.version ?? '—'}</td>
                                    <td className="px-4 py-4 text-right">
                                        <span className="text-gray-700 font-medium">{m.sopRuleCount}</span>
                                    </td>
                                    <td className="px-6 py-4">
                                        <div className="flex items-center justify-end gap-2">
                                            {/* Enable / Disable toggle */}
                                            <button
                                                onClick={() => handleToggle(m)}
                                                title={m.status === 'Disabled' ? 'Enable model' : 'Disable model'}
                                                className={`p-1.5 rounded-lg transition-colors ${
                                                    m.status === 'Disabled'
                                                        ? 'text-green-500 hover:bg-green-50'
                                                        : 'text-amber-500 hover:bg-amber-50'
                                                }`}
                                            >
                                                {m.status === 'Disabled'
                                                    ? <Power className="w-4 h-4" />
                                                    : <PowerOff className="w-4 h-4" />
                                                }
                                            </button>
                                            {/* Edit */}
                                            <button
                                                onClick={() => openEdit(m)}
                                                className="p-1.5 rounded-lg text-blue-500 hover:bg-blue-50 transition-colors"
                                                title="Edit model"
                                            >
                                                <Edit2 className="w-4 h-4" />
                                            </button>
                                            {/* Delete */}
                                            <button
                                                onClick={() => handleDelete(m)}
                                                disabled={m.sopRuleCount > 0}
                                                title={m.sopRuleCount > 0 ? `Cannot delete: ${m.sopRuleCount} SOP rule(s) still reference this model` : 'Delete model'}
                                                className="p-1.5 rounded-lg text-red-400 hover:bg-red-50 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
                                            >
                                                <Trash2 className="w-4 h-4" />
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            {/* Form Modal */}
            {isFormOpen && (
                <AiModelFormModal
                    model={editing}
                    onClose={() => setIsFormOpen(false)}
                    onSaved={() => { setIsFormOpen(false); load(); }}
                />
            )}
        </div>
    );
}
