'use client';

import { useState, useEffect, KeyboardEvent } from 'react';
import { X, Check, Loader2, Tag, ChevronDown, ChevronUp } from 'lucide-react';
import type { CameraResponse, CreateCameraRequest, UpdateCameraRequest, CameraViolationAssignment } from '@/types/admin';
import { getApprovedRequests } from '@/lib/api/requests';
import type { TenantViolationRequestResponse } from '@/lib/api/requests';
import { useAuth } from '@/contexts/AuthContext';

interface CameraFormModalProps {
    camera?: CameraResponse | null;
    tenantId: string;
    onClose: () => void;
    onCreate: (data: CreateCameraRequest) => Promise<CameraResponse>;
    onUpdate: (id: string, data: UpdateCameraRequest) => Promise<CameraResponse>;
}

export default function CameraFormModal({
    camera,
    tenantId,
    onClose,
    onCreate,
    onUpdate
}: CameraFormModalProps) {
    const { role } = useAuth();
    const isSuperAdmin = role === 'SuperAdmin';

    const [formData, setFormData] = useState({
        cameraId: '',
        name: '',
        location: '',
        rtspUrl: '',
        whipUrl: '',
        whepUrl: '',
        isStreaming: false,
        targetFps: '' as string,
        activeViolations: [] as CameraViolationAssignment[],
    });

    const [approvedViolations, setApprovedViolations] = useState<TenantViolationRequestResponse[]>([]);
    const [loading, setLoading] = useState(false);
    const [isLoadingApproved, setIsLoadingApproved] = useState(true);
    const [expandedViolation, setExpandedViolation] = useState<string | null>(null);

    // SuperAdmin free-text label inputs (per violation)
    const [labelInputs, setLabelInputs] = useState<Record<string, string>>({});

    useEffect(() => {
        async function loadApproved() {
            if (!tenantId) return;
            try {
                setIsLoadingApproved(true);
                const data = await getApprovedRequests(tenantId);
                setApprovedViolations(data);
            } catch (err) {
                console.error('Failed to load approved violations:', err);
            } finally {
                setIsLoadingApproved(false);
            }
        }
        loadApproved();
    }, [tenantId]);

    useEffect(() => {
        if (camera) {
            setFormData({
                cameraId: camera.cameraId,
                name: camera.name,
                location: camera.location,
                rtspUrl: '',
                whipUrl: camera.whipUrl || '',
                whepUrl: camera.whepUrl || '',
                isStreaming: camera.isStreaming || false,
                targetFps: camera.targetFps != null ? String(camera.targetFps) : '',
                activeViolations: camera.activeViolations?.map(v => ({
                    sopViolationTypeId: v.sopViolationTypeId,
                    triggerLabels: v.triggerLabels || '',
                })) || [],
            });
        }
    }, [camera]);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value, type, checked } = e.target;
        setFormData(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }));
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        try {
            const parsedFps = formData.targetFps.trim() === '' ? undefined : Number(formData.targetFps);
            const targetFps = parsedFps !== undefined && !Number.isNaN(parsedFps) && parsedFps > 0 ? parsedFps : undefined;
            if (camera) {
                await onUpdate(camera.id, {
                    name: formData.name,
                    location: formData.location,
                    rtspUrl: formData.rtspUrl || undefined,
                    whipUrl: formData.whipUrl || undefined,
                    whepUrl: formData.whepUrl || undefined,
                    isStreaming: formData.isStreaming,
                    targetFps,
                    activeViolations: formData.activeViolations,
                });
                alert('Camera updated successfully');
            } else {
                const { targetFps: _omit, ...rest } = formData;
                await onCreate({ ...rest, tenantId, targetFps: targetFps ?? 1.0 });
                alert('Camera created successfully');
            }
            onClose();
        } catch (error) {
            alert((error as Error).message || 'Failed to save camera');
        } finally {
            setLoading(false);
        }
    };

    // ── Violation selection helpers ──────────────────────────────────────────
    const isViolationActive = (id: string) =>
        formData.activeViolations.some(v => v.sopViolationTypeId === id);

    const getAssignment = (id: string) =>
        formData.activeViolations.find(v => v.sopViolationTypeId === id);

    const toggleViolation = (id: string) => {
        setFormData(prev => {
            const active = prev.activeViolations.some(v => v.sopViolationTypeId === id);
            if (active) {
                return { ...prev, activeViolations: prev.activeViolations.filter(v => v.sopViolationTypeId !== id) };
            } else {
                return { ...prev, activeViolations: [...prev.activeViolations, { sopViolationTypeId: id, triggerLabels: '' }] };
            }
        });
    };

    const getActiveTags = (id: string): string[] => {
        const labels = getAssignment(id)?.triggerLabels || '';
        return labels.split(',').map(l => l.trim()).filter(Boolean);
    };

    const setTagsForViolation = (id: string, tags: string[]) => {
        setFormData(prev => ({
            ...prev,
            activeViolations: prev.activeViolations.map(v =>
                v.sopViolationTypeId === id ? { ...v, triggerLabels: tags.join(', ') } : v
            )
        }));
    };

    // ── SuperAdmin: free-text tag input ────────────────────────────────────
    const handleSuperAdminLabelKeyDown = (e: KeyboardEvent<HTMLInputElement>, id: string) => {
        if (e.key === 'Enter' || e.key === ',') {
            e.preventDefault();
            const trimmed = (labelInputs[id] || '').trim().toLowerCase();
            if (!trimmed) return;
            const tags = getActiveTags(id);
            if (!tags.includes(trimmed)) setTagsForViolation(id, [...tags, trimmed]);
            setLabelInputs(prev => ({ ...prev, [id]: '' }));
        }
    };

    const flushSuperAdminInput = (id: string) => {
        const trimmed = (labelInputs[id] || '').trim().toLowerCase();
        if (!trimmed) return;
        const tags = getActiveTags(id);
        if (!tags.includes(trimmed)) setTagsForViolation(id, [...tags, trimmed]);
        setLabelInputs(prev => ({ ...prev, [id]: '' }));
    };

    const removeSuperAdminTag = (id: string, tag: string) => {
        setTagsForViolation(id, getActiveTags(id).filter(t => t !== tag));
    };

    // ── TenantAdmin: toggle from SOP pool ──────────────────────────────────
    const toggleTenantLabel = (violationId: string, label: string) => {
        const activeTags = getActiveTags(violationId);
        if (activeTags.includes(label)) {
            setTagsForViolation(violationId, activeTags.filter(t => t !== label));
        } else {
            setTagsForViolation(violationId, [...activeTags, label]);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto border border-white/20">
                {/* Header */}
                <div className="flex justify-between items-center p-6 border-b border-gray-200/50">
                    <h3 className="text-2xl font-bold text-gray-900">
                        {camera ? 'Edit Camera' : 'Add New Camera'}
                    </h3>
                    <button onClick={onClose} className="text-gray-500 hover:text-gray-700 transition-colors bg-gray-100/50 hover:bg-gray-100 p-2 rounded-full">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Camera ID *</label>
                            <input type="text" name="cameraId" value={formData.cameraId} onChange={handleChange} required disabled={!!camera}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 font-medium transition-all hover:border-gray-300 disabled:bg-gray-50 disabled:text-gray-500"
                                placeholder="CAM-001" />
                        </div>
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Name *</label>
                            <input type="text" name="name" value={formData.name} onChange={handleChange} required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="Front Entrance" />
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">Location *</label>
                        <input type="text" name="location" value={formData.location} onChange={handleChange} required
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                            placeholder="Building A, Floor 1" />
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            RTSP URL {!camera && '*'}
                        </label>
                        <input type="text" name="rtspUrl" value={formData.rtspUrl} onChange={handleChange} required={!camera}
                            placeholder={camera ? 'Leave empty to keep existing URL' : 'rtsp://username:password@host:port/path'}
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 font-medium transition-all hover:border-gray-300" />
                        <p className="text-xs text-gray-500 mt-2 ml-1 font-medium">RTSP URL will be encrypted before storage</p>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Cloudflare WHIP URL (Publish)</label>
                            <input type="url" name="whipUrl" value={formData.whipUrl} readOnly disabled
                                className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl text-gray-500 font-medium cursor-not-allowed"
                                placeholder={camera ? 'Generating...' : 'Auto-generated on save...'} />
                            <p className="text-xs text-gray-400 mt-2 ml-1">Managed automatically by the Cloudflare Bridge</p>
                        </div>
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Cloudflare WHEP URL (Play)</label>
                            <input type="url" name="whepUrl" value={formData.whepUrl} readOnly disabled
                                className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl text-gray-500 font-medium cursor-not-allowed"
                                placeholder={camera ? 'Generating...' : 'Auto-generated on save...'} />
                            <p className="text-xs text-gray-400 mt-2 ml-1">Managed automatically by the Cloudflare Bridge</p>
                        </div>
                    </div>

                    <div className="flex items-center gap-3 bg-gray-50 p-4 rounded-xl border border-gray-100">
                        <input
                            type="checkbox"
                            id="isStreaming"
                            name="isStreaming"
                            checked={formData.isStreaming}
                            onChange={handleChange}
                            className="w-5 h-5 text-blue-600 rounded bg-white border-gray-300 focus:ring-blue-500 focus:ring-offset-0 disabled:opacity-50"
                        />
                        <label htmlFor="isStreaming" className="text-sm font-semibold text-gray-800 cursor-pointer">
                            Live Stream Active
                        </label>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Target Frames Per Second (FPS)
                        </label>
                        <input
                            type="number"
                            name="targetFps"
                            value={formData.targetFps}
                            onChange={handleChange}
                            min="0.1"
                            max="30"
                            step="0.1"
                            placeholder="1"
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                        />
                        <p className="text-xs text-gray-500 mt-2 ml-1 font-medium">
                            Frames per second the AI engine will analyze for this camera. Leave blank to use the system default (1 FPS). Higher values increase inference cost.
                        </p>
                    </div>

                    {/* Violation Models Selection */}
                    <div className="border-t border-gray-200/50 pt-6">
                        <label className="block text-sm font-semibold text-gray-700 mb-4">
                            Active SOP Violation Models
                        </label>
                        {isLoadingApproved ? (
                            <div className="flex items-center gap-2 text-sm text-gray-500">
                                <Loader2 className="w-4 h-4 animate-spin" />
                                Checking approved models...
                            </div>
                        ) : approvedViolations.length === 0 ? (
                            <div className="p-4 bg-amber-50 rounded-xl border border-amber-100">
                                <p className="text-sm text-amber-900 font-bold">No approved models found.</p>
                                <p className="text-xs text-amber-700 mt-1">
                                    {isSuperAdmin ? 'Associate an SOP for this tenant first.' : 'Request an SOP first.'}
                                </p>
                            </div>
                        ) : (
                            <div className="grid gap-3">
                                {approvedViolations.map((req) => {
                                    const active = isViolationActive(req.sopViolationTypeId);
                                    const isExpanded = expandedViolation === req.sopViolationTypeId;

                                    // Parse the SOP's allowed label pool
                                    const sopLabels = req.sopTriggerLabels
                                        ? req.sopTriggerLabels.split(',').map(l => l.trim()).filter(Boolean)
                                        : [];

                                    const activeTags = getActiveTags(req.sopViolationTypeId);

                                    return (
                                        <div key={req.sopViolationTypeId}
                                            className={`border rounded-xl transition-all ${active ? 'border-blue-500 bg-blue-50/30' : 'border-gray-200 bg-white shadow-sm'}`}>
                                            {/* Violation header — toggle active */}
                                            <button type="button" onClick={() => toggleViolation(req.sopViolationTypeId)}
                                                className="w-full flex items-center justify-between p-4">
                                                <div className="text-left">
                                                    <p className="text-[10px] font-bold text-blue-700 uppercase tracking-widest leading-none">
                                                        {req.sopName || 'Standard'}
                                                    </p>
                                                    <p className="text-sm font-bold text-gray-900 mt-1.5 leading-tight">
                                                        {req.violationTypeName || 'Violation Model'}
                                                    </p>
                                                </div>
                                                {active && (
                                                    <div className="bg-blue-600 text-white p-1 rounded-full shadow-sm">
                                                        <Check className="w-3.5 h-3.5" />
                                                    </div>
                                                )}
                                            </button>

                                            {/* Label editor — only shown when violation is active */}
                                            {active && sopLabels.length > 0 && (
                                                <div className="px-4 pb-4 border-t border-blue-100 pt-3">
                                                    <button type="button"
                                                        onClick={() => setExpandedViolation(isExpanded ? null : req.sopViolationTypeId)}
                                                        className="flex items-center gap-1.5 text-xs font-semibold text-blue-700 hover:text-blue-900 mb-3">
                                                        <Tag className="w-3 h-3" />
                                                        {isSuperAdmin ? 'Override Trigger Labels' : 'Customize Trigger Labels'}
                                                        {isExpanded ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
                                                    </button>

                                                    {isExpanded && (
                                                        <>
                                                            {isSuperAdmin ? (
                                                                /* SuperAdmin: free-text tag chip input */
                                                                <div>
                                                                    <div className="min-h-[36px] px-3 py-2 bg-white border border-blue-200 rounded-lg focus-within:ring-2 focus-within:ring-blue-400 flex flex-wrap gap-1.5 items-center text-black">
                                                                        {activeTags.map(tag => (
                                                                            <span key={tag} className="flex items-center gap-1 px-2 py-0.5 bg-blue-100 text-blue-800 text-xs font-mono font-bold rounded border border-blue-200">
                                                                                {tag}
                                                                                <button type="button" onClick={() => removeSuperAdminTag(req.sopViolationTypeId, tag)} className="text-blue-400 hover:text-blue-700">
                                                                                    <X className="w-2.5 h-2.5" />
                                                                                </button>
                                                                            </span>
                                                                        ))}
                                                                        <input
                                                                            type="text"
                                                                            value={labelInputs[req.sopViolationTypeId] || ''}
                                                                            onChange={(e) => setLabelInputs(prev => ({ ...prev, [req.sopViolationTypeId]: e.target.value }))}
                                                                            onKeyDown={(e) => handleSuperAdminLabelKeyDown(e, req.sopViolationTypeId)}
                                                                            onBlur={() => flushSuperAdminInput(req.sopViolationTypeId)}
                                                                            className="flex-1 min-w-[100px] outline-none text-xs bg-transparent"
                                                                            placeholder={activeTags.length === 0 ? "Type label, press Enter..." : "Add more..."}
                                                                        />
                                                                    </div>
                                                                    <p className="text-[10px] text-gray-400 mt-1">Leave empty to use the SOP default labels.</p>
                                                                </div>
                                                            ) : (
                                                                /* TenantAdmin: toggle-only from SOP label pool */
                                                                <div>
                                                                    <p className="text-[10px] text-gray-500 mb-2">
                                                                        Select which labels you want active. You can remove or re-add from the SOP's predefined set.
                                                                    </p>
                                                                    <div className="flex flex-wrap gap-2">
                                                                        {sopLabels.map(label => {
                                                                            const isOn = activeTags.includes(label);
                                                                            return (
                                                                                <button
                                                                                    key={label}
                                                                                    type="button"
                                                                                    onClick={() => toggleTenantLabel(req.sopViolationTypeId, label)}
                                                                                    className={`flex items-center gap-1.5 px-2.5 py-1 text-xs font-mono font-bold rounded-lg border transition-all ${isOn
                                                                                        ? 'bg-blue-600 text-white border-blue-700 shadow-sm'
                                                                                        : 'bg-white text-gray-400 border-gray-300 line-through opacity-60 hover:opacity-80'
                                                                                        }`}
                                                                                >
                                                                                    {isOn && <Check className="w-3 h-3" />}
                                                                                    {label}
                                                                                </button>
                                                                            );
                                                                        })}
                                                                    </div>
                                                                    <p className="text-[10px] text-gray-400 mt-2">
                                                                        All labels selected = use all SOP defaults. Contact your administrator to change the label pool.
                                                                    </p>
                                                                </div>
                                                            )}
                                                        </>
                                                    )}
                                                </div>
                                            )}

                                            {/* If active but SOP has no labels defined yet */}
                                            {active && sopLabels.length === 0 && isSuperAdmin && (
                                                <div className="px-4 pb-4 border-t border-blue-100 pt-3">
                                                    <button type="button"
                                                        onClick={() => setExpandedViolation(isExpanded ? null : req.sopViolationTypeId)}
                                                        className="flex items-center gap-1.5 text-xs font-semibold text-blue-700 hover:text-blue-900 mb-3">
                                                        <Tag className="w-3 h-3" />
                                                        Override Trigger Labels
                                                        {isExpanded ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
                                                    </button>
                                                    {isExpanded && (
                                                        <div>
                                                            <div className="min-h-[36px] px-3 py-2 bg-white border border-blue-200 rounded-lg focus-within:ring-2 focus-within:ring-blue-400 flex flex-wrap gap-1.5 items-center text-black">
                                                                {activeTags.map(tag => (
                                                                    <span key={tag} className="flex items-center gap-1 px-2 py-0.5 bg-blue-100 text-blue-800 text-xs font-mono font-bold rounded border border-blue-200">
                                                                        {tag}
                                                                        <button type="button" onClick={() => removeSuperAdminTag(req.sopViolationTypeId, tag)} className="text-blue-400 hover:text-blue-700">
                                                                            <X className="w-2.5 h-2.5" />
                                                                        </button>
                                                                    </span>
                                                                ))}
                                                                <input
                                                                    type="text"
                                                                    value={labelInputs[req.sopViolationTypeId] || ''}
                                                                    onChange={(e) => setLabelInputs(prev => ({ ...prev, [req.sopViolationTypeId]: e.target.value }))}
                                                                    onKeyDown={(e) => handleSuperAdminLabelKeyDown(e, req.sopViolationTypeId)}
                                                                    onBlur={() => flushSuperAdminInput(req.sopViolationTypeId)}
                                                                    className="flex-1 min-w-[100px] outline-none text-xs bg-transparent"
                                                                    placeholder={activeTags.length === 0 ? "Type label, press Enter..." : "Add more..."}
                                                                />
                                                            </div>
                                                            <p className="text-[10px] text-gray-400 mt-1">Leave empty to use the SOP default labels.</p>
                                                        </div>
                                                    )}
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        )}
                    </div>

                    {/* Actions */}
                    <div className="flex justify-end gap-3 pt-6 border-t border-gray-200/50">
                        <button type="button" onClick={onClose}
                            className="px-6 py-2.5 text-gray-700 bg-white border border-gray-300 rounded-xl hover:bg-gray-50 hover:text-gray-900 transition-all font-bold shadow-sm">
                            Cancel
                        </button>
                        <button type="submit" disabled={loading || isLoadingApproved}
                            className="px-6 py-2.5 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all font-bold shadow-md shadow-blue-500/20 disabled:opacity-50 disabled:shadow-none">
                            {loading ? 'Saving...' : camera ? 'Update Camera' : 'Create Camera'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
