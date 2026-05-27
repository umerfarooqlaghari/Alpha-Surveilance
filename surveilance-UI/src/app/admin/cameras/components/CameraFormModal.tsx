'use client';

import { useState, useEffect, KeyboardEvent } from 'react';
import { X, Check, Loader2, Tag, ChevronDown, ChevronUp, Settings2 } from 'lucide-react';
import type { CameraResponse, CreateCameraRequest, UpdateCameraRequest, CameraViolationAssignment, DetectionSchedule } from '@/types/admin';
import { getApprovedRequests } from '@/lib/api/requests';
import { getCameraRtspUrl } from '@/lib/api/cameras';
import type { TenantViolationRequestResponse } from '@/lib/api/requests';
import { useAuth } from '@/contexts/AuthContext';
import LocationSelect from '@/components/locations/LocationSelect';
import PolygonEditor from './PolygonEditor';
import AnomalyEditor from './AnomalyEditor';

/**
 * Pick the editor that matches an already-saved rule_config. When the config
 * is empty the intent is "whole frame" (no zone restriction).
 */
type RuleEditorType = 'none' | 'geofence' | 'dwell' | 'anomaly';

function detectRuleType(json: string | undefined | null): RuleEditorType {
    if (!json) return 'none';
    try {
        const obj = JSON.parse(json);
        const t = String(obj?.type || '').toLowerCase();
        if (t === 'anomaly' || t === 'dwell') return t;
    } catch {
        /* fall through */
    }
    return 'geofence';
}

/**
 * Parse a label string that may be JSON-array format (["no-glove","no-mask"])
 * or plain comma-separated ("no-glove, no-mask"). Always returns a clean string[].
 */
function parseLabels(raw: string | null | undefined): string[] {
    if (!raw) return [];
    const s = raw.trim();
    if (s.startsWith('[')) {
        try {
            const parsed = JSON.parse(s);
            if (Array.isArray(parsed)) return parsed.map(l => String(l).trim()).filter(Boolean);
        } catch { /* fall through to comma-split */ }
    }
    return s.split(',').map(l => l.trim()).filter(Boolean);
}

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

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
        locationId: null as string | null,
        rtspUrl: '',
        whipUrl: '',
        whepUrl: '',
        isStreaming: false,
        isDetectionEnabled: true,
        targetFps: '' as string,
        activeViolations: [] as CameraViolationAssignment[],
        detectionSchedules: [] as DetectionSchedule[],
    });

    const [approvedViolations, setApprovedViolations] = useState<TenantViolationRequestResponse[]>([]);
    const [loading, setLoading] = useState(false);
    const [isLoadingApproved, setIsLoadingApproved] = useState(true);
    const [expandedViolation, setExpandedViolation] = useState<string | null>(null);

    // SuperAdmin free-text label inputs (per violation)
    const [labelInputs, setLabelInputs] = useState<Record<string, string>>({});

    // Per-violation editor selection: none | geofence | anomaly | dwell. Initialized
    // from the saved rule_config so an existing anomaly rule opens in the
    // anomaly editor (not blanked into the geofence editor and overwritten).
    const [ruleTypeChoice, setRuleTypeChoice] = useState<Record<string, RuleEditorType>>({});
    // D-6 fix: in-modal banner replaces blocking ``alert()`` calls. ``alert``
    // freezes the entire renderer and — because Playwright/Cypress can't
    // dismiss native dialogs without explicit handlers — made happy-path
    // tests flaky.  ``feedback`` is auto-cleared after a short timer so users
    // don't have to click anything to dismiss success messages.
    const [feedback, setFeedback] = useState<{ kind: 'success' | 'error'; message: string } | null>(null);
    useEffect(() => {
        if (!feedback || feedback.kind === 'error') return;
        const t = setTimeout(() => setFeedback(null), 1800);
        return () => clearTimeout(t);
    }, [feedback]);

    // D-6 phase 2: promise-based confirmation replaces ``window.confirm``,
    // which (a) blocks the JS event loop, (b) cannot be styled to match the
    // app, and (c) cannot be dismissed by Playwright/Cypress without an
    // explicit ``page.on('dialog', ...)`` handler.  ``requestConfirm``
    // returns a Promise<boolean> so callsites can `await` it like the native
    // API.
    const [confirmState, setConfirmState] = useState<
        | { message: string; confirmLabel: string; cancelLabel: string; resolve: (ok: boolean) => void }
        | null
    >(null);
    const requestConfirm = (
        message: string,
        opts?: { confirmLabel?: string; cancelLabel?: string }
    ): Promise<boolean> =>
        new Promise<boolean>((resolve) => {
            setConfirmState({
                message,
                confirmLabel: opts?.confirmLabel ?? 'Continue',
                cancelLabel: opts?.cancelLabel ?? 'Cancel',
                resolve,
            });
        });

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
                locationId: camera.locationId ?? null,
                rtspUrl: '',
                whipUrl: camera.whipUrl || '',
                whepUrl: camera.whepUrl || '',
                isStreaming: camera.isStreaming || false,
                isDetectionEnabled: camera.isDetectionEnabled ?? true,
                targetFps: camera.targetFps != null ? String(camera.targetFps) : '',
                activeViolations: camera.activeViolations?.map(v => ({
                    sopViolationTypeId: v.sopViolationTypeId,
                    triggerLabels: v.triggerLabels || '',
                    ruleConfigurationJson: v.ruleConfigurationJson || '',
                })) || [],
                detectionSchedules: camera.detectionSchedules?.map(s => ({
                    id: s.id,
                    daysOfWeek: s.daysOfWeek,
                    startTime: s.startTime,
                    endTime: s.endTime,
                    label: s.label || '',
                    isActive: s.isActive,
                })) || [],
            });
            // Seed the editor selection from each saved rule_config.
            const seeded: Record<string, RuleEditorType> = {};
            for (const v of camera.activeViolations || []) {
                seeded[v.sopViolationTypeId] = detectRuleType(v.ruleConfigurationJson);
            }
            setRuleTypeChoice(seeded);
            // Auto-expand the first active violation so settings are visible by default for SuperAdmin.
            if (isSuperAdmin && camera.activeViolations?.length) {
                setExpandedViolation(camera.activeViolations[0].sopViolationTypeId);
            }

            // SuperAdmin gets to see the current RTSP URL pre-populated when
            // editing so they can verify/tweak existing config. Tenants keep
            // the empty-field behaviour (per security policy: never expose
            // decrypted credentials below the SA tier).
            if (isSuperAdmin && camera.id) {
                let cancelled = false;
                (async () => {
                    try {
                        const url = await getCameraRtspUrl(camera.id);
                        if (!cancelled && url) {
                            setFormData(prev => ({ ...prev, rtspUrl: url }));
                        }
                    } catch {
                        /* non-fatal: leave field blank */
                    }
                })();
                return () => { cancelled = true; };
            }
        }
    }, [camera, isSuperAdmin]);

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
                // For updates: null in state means "detach" only if user actively cleared the original.
                // null + originally null → leave unchanged (don't send the field).
                const wasAssigned = camera.locationId != null;
                const isAssigned = formData.locationId != null;
                let locationIdForUpdate: string | null | undefined;
                if (isAssigned) locationIdForUpdate = formData.locationId; // assign / change
                else if (wasAssigned) locationIdForUpdate = EMPTY_GUID;     // detach
                else locationIdForUpdate = undefined;                       // unchanged

                await onUpdate(camera.id, {
                    name: formData.name,
                    location: formData.location,
                    locationId: locationIdForUpdate,
                    rtspUrl: formData.rtspUrl || undefined,
                    whipUrl: formData.whipUrl || undefined,
                    whepUrl: formData.whepUrl || undefined,
                    isStreaming: formData.isStreaming,
                    isDetectionEnabled: formData.isDetectionEnabled,
                    targetFps,
                    activeViolations: formData.activeViolations,
                    detectionSchedules: formData.detectionSchedules,
                });
                setFeedback({ kind: 'success', message: 'Camera updated successfully' });
            } else {
                const { targetFps: _omit, ...rest } = formData;
                await onCreate({
                    ...rest,
                    tenantId,
                    locationId: formData.locationId ?? null,
                    isDetectionEnabled: formData.isDetectionEnabled,
                    targetFps: targetFps ?? 1.0,
                    detectionSchedules: formData.detectionSchedules,
                });
                setFeedback({ kind: 'success', message: 'Camera created successfully' });
            }
            onClose();
        } catch (error) {
            setFeedback({ kind: 'error', message: (error as Error).message || 'Failed to save camera' });
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
        const active = formData.activeViolations.some(v => v.sopViolationTypeId === id);
        if (active) {
            if (expandedViolation === id) setExpandedViolation(null);
            setFormData(prev => ({
                ...prev,
                activeViolations: prev.activeViolations.filter(v => v.sopViolationTypeId !== id),
            }));
        } else {
            // When adding a violation, pre-populate with the SOP's default trigger labels
            const violationObj = approvedViolations.find(v => v.sopViolationTypeId === id);
            const defaultLabels = violationObj?.sopTriggerLabels || '';
            if (isSuperAdmin) setExpandedViolation(id); // auto-open settings panel for SuperAdmin
            setFormData(prev => ({
                ...prev,
                activeViolations: [...prev.activeViolations, { sopViolationTypeId: id, triggerLabels: defaultLabels, ruleConfigurationJson: '' }],
            }));
        }
    };

    const getActiveTags = (id: string): string[] => {
        return parseLabels(getAssignment(id)?.triggerLabels);
    };

    const setTagsForViolation = (id: string, tags: string[]) => {
        setFormData(prev => ({
            ...prev,
            activeViolations: prev.activeViolations.map(v =>
                v.sopViolationTypeId === id ? { ...v, triggerLabels: tags.join(', ') } : v
            )
        }));
    };

    const setConfigForViolation = (id: string, config: string) => {
        setFormData(prev => ({
            ...prev,
            activeViolations: prev.activeViolations.map(v =>
                v.sopViolationTypeId === id ? { ...v, ruleConfigurationJson: config } : v
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

                {/* D-6: in-modal feedback banner (replaces blocking alert()) */}
                {feedback && (
                    <div
                        role={feedback.kind === 'error' ? 'alert' : 'status'}
                        className={`mx-6 mt-4 px-4 py-3 rounded-xl text-sm font-medium flex items-start justify-between gap-3 ${
                            feedback.kind === 'error'
                                ? 'bg-red-50 border border-red-200 text-red-800'
                                : 'bg-green-50 border border-green-200 text-green-800'
                        }`}
                    >
                        <span>{feedback.message}</span>
                        <button
                            type="button"
                            onClick={() => setFeedback(null)}
                            className="text-current/60 hover:text-current shrink-0"
                            aria-label="Dismiss"
                        >
                            <X className="w-4 h-4" />
                        </button>
                    </div>
                )}

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
                        <LocationSelect
                            label="Assigned Location"
                            value={formData.locationId}
                            onChange={(id) => setFormData(prev => ({ ...prev, locationId: id }))}
                            unassignedLabel="— Unassigned —"
                            tenantId={isSuperAdmin ? tenantId : undefined}
                        />
                        <p className="text-xs text-gray-500 mt-2 ml-1 font-medium">Optional. Group cameras under a structured location for filtering and analytics.</p>
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

                    {/* Detection enable / disable — putting the camera "to sleep" */}
                    <div className={`flex items-start gap-4 p-4 rounded-xl border ${formData.isDetectionEnabled ? 'bg-green-50 border-green-200' : 'bg-amber-50 border-amber-200'} transition-colors`}>
                        <button
                            type="button"
                            role="switch"
                            aria-checked={formData.isDetectionEnabled}
                            onClick={() => setFormData(prev => ({ ...prev, isDetectionEnabled: !prev.isDetectionEnabled }))}
                            className={`relative mt-0.5 flex-shrink-0 inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-offset-2 ${formData.isDetectionEnabled ? 'bg-green-500 focus:ring-green-500' : 'bg-gray-300 focus:ring-gray-400'}`}
                        >
                            <span
                                className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${formData.isDetectionEnabled ? 'translate-x-6' : 'translate-x-1'}`}
                            />
                        </button>
                        <div>
                            <p className="text-sm font-semibold text-gray-800">
                                {formData.isDetectionEnabled ? 'Detection Active' : 'Detection Paused (camera sleeping)'}
                            </p>
                            <p className="text-xs text-gray-500 mt-0.5">
                                {formData.isDetectionEnabled
                                    ? 'The AI engine is processing this camera\'s stream and raising violations.'
                                    : 'The Vision Service will not open an RTSP connection to this camera. No decoding, no inference, no violations until re-enabled.'}
                            </p>
                        </div>
                    </div>

                    {/* ── Detection Sleep Windows (schedules) ──────────────────── */}
                    <div className="border border-gray-200 rounded-xl overflow-hidden">
                        <div className="flex items-center justify-between px-4 py-3 bg-gray-50 border-b border-gray-200">
                            <div>
                                <p className="text-sm font-semibold text-gray-800">Detection Sleep Windows</p>
                                <p className="text-xs text-gray-500 mt-0.5">Camera skips AI inference during these recurring UTC time windows.</p>
                            </div>
                            <button
                                type="button"
                                onClick={() => setFormData(prev => ({
                                    ...prev,
                                    detectionSchedules: [
                                        ...prev.detectionSchedules,
                                        { daysOfWeek: 127, startTime: '22:00', endTime: '06:00', label: '', isActive: true },
                                    ],
                                }))}
                                className="text-xs font-semibold text-blue-600 hover:text-blue-700 bg-blue-50 hover:bg-blue-100 px-3 py-1.5 rounded-lg transition-colors"
                            >
                                + Add Window
                            </button>
                        </div>

                        {formData.detectionSchedules.length === 0 ? (
                            <p className="text-xs text-gray-400 text-center py-4">No sleep windows configured — detection runs 24/7.</p>
                        ) : (
                            <div className="divide-y divide-gray-100">
                                {formData.detectionSchedules.map((sched, idx) => {
                                    const DAY_BITS = [
                                        { label: 'Su', bit: 1 },
                                        { label: 'Mo', bit: 2 },
                                        { label: 'Tu', bit: 4 },
                                        { label: 'We', bit: 8 },
                                        { label: 'Th', bit: 16 },
                                        { label: 'Fr', bit: 32 },
                                        { label: 'Sa', bit: 64 },
                                    ];
                                    const updateSched = (patch: Partial<DetectionSchedule>) =>
                                        setFormData(prev => ({
                                            ...prev,
                                            detectionSchedules: prev.detectionSchedules.map((s, i) =>
                                                i === idx ? { ...s, ...patch } : s
                                            ),
                                        }));
                                    const toggleDay = (bit: number) =>
                                        updateSched({ daysOfWeek: (sched.daysOfWeek ^ bit) & 127 || 127 });

                                    return (
                                        <div key={idx} className={`p-4 space-y-3 ${!sched.isActive ? 'opacity-50' : ''}`}>
                                            {/* Row 1: enable toggle + time inputs + delete */}
                                            <div className="flex items-center gap-3 flex-wrap">
                                                <button
                                                    type="button"
                                                    role="switch"
                                                    aria-checked={sched.isActive}
                                                    onClick={() => updateSched({ isActive: !sched.isActive })}
                                                    className={`relative flex-shrink-0 inline-flex h-5 w-9 items-center rounded-full transition-colors ${sched.isActive ? 'bg-blue-500' : 'bg-gray-300'}`}
                                                    title={sched.isActive ? 'Disable this window' : 'Enable this window'}
                                                >
                                                    <span className={`inline-block h-3 w-3 transform rounded-full bg-white shadow transition-transform ${sched.isActive ? 'translate-x-5' : 'translate-x-1'}`} />
                                                </button>
                                                <span className="text-xs text-gray-500 font-medium">From</span>
                                                <input
                                                    type="time"
                                                    value={sched.startTime}
                                                    onChange={e => updateSched({ startTime: e.target.value })}
                                                    className="px-2 py-1.5 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black bg-white"
                                                />
                                                <span className="text-xs text-gray-500 font-medium">to</span>
                                                <input
                                                    type="time"
                                                    value={sched.endTime}
                                                    onChange={e => updateSched({ endTime: e.target.value })}
                                                    className="px-2 py-1.5 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black bg-white"
                                                />
                                                <span className="text-xs text-gray-400 ml-auto italic">
                                                    {sched.startTime > sched.endTime ? 'overnight' : ''}
                                                </span>
                                                <button
                                                    type="button"
                                                    onClick={() => setFormData(prev => ({
                                                        ...prev,
                                                        detectionSchedules: prev.detectionSchedules.filter((_, i) => i !== idx),
                                                    }))}
                                                    className="text-red-400 hover:text-red-600 p-1 rounded transition-colors ml-1"
                                                    title="Remove this window"
                                                >
                                                    <X className="w-4 h-4" />
                                                </button>
                                            </div>

                                            {/* Row 2: day-of-week pills */}
                                            <div className="flex items-center gap-1.5 flex-wrap">
                                                <span className="text-xs text-gray-500 font-medium mr-1">Days:</span>
                                                {DAY_BITS.map(({ label, bit }) => {
                                                    const active = (sched.daysOfWeek & bit) !== 0;
                                                    return (
                                                        <button
                                                            key={bit}
                                                            type="button"
                                                            onClick={() => toggleDay(bit)}
                                                            className={`w-8 h-8 text-xs font-semibold rounded-full transition-colors ${active ? 'bg-blue-500 text-white' : 'bg-gray-100 text-gray-500 hover:bg-gray-200'}`}
                                                        >
                                                            {label}
                                                        </button>
                                                    );
                                                })}
                                                <button
                                                    type="button"
                                                    onClick={() => updateSched({ daysOfWeek: 127 })}
                                                    className="text-xs text-blue-500 hover:text-blue-700 ml-1 underline"
                                                >All</button>
                                                <button
                                                    type="button"
                                                    onClick={() => updateSched({ daysOfWeek: 2 + 4 + 8 + 16 + 32 })}
                                                    className="text-xs text-blue-500 hover:text-blue-700 underline"
                                                >Weekdays</button>
                                                <button
                                                    type="button"
                                                    onClick={() => updateSched({ daysOfWeek: 1 + 64 })}
                                                    className="text-xs text-blue-500 hover:text-blue-700 underline"
                                                >Weekends</button>
                                            </div>

                                            {/* Row 3: optional label */}
                                            <input
                                                type="text"
                                                value={sched.label}
                                                onChange={e => updateSched({ label: e.target.value })}
                                                placeholder="Label (e.g. Night quiet hours)"
                                                maxLength={200}
                                                className="w-full px-3 py-1.5 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-black placeholder-gray-400 bg-white"
                                            />
                                        </div>
                                    );
                                })}
                            </div>
                        )}
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

                                    // Parse the SOP's allowed label pool (may be JSON array or comma-separated)
                                    const sopLabels = parseLabels(req.sopTriggerLabels);

                                    const activeTags = getActiveTags(req.sopViolationTypeId);
                                    const savedJson = getAssignment(req.sopViolationTypeId)?.ruleConfigurationJson || '';
                                    // chosenType defaults to 'none' (whole frame) when not explicitly set —
                                    // this matches the backend behaviour where empty ruleConfigurationJson
                                    // passes all detections through with no zone restriction.
                                    const chosenType: RuleEditorType = ruleTypeChoice[req.sopViolationTypeId] ?? 'none';
                                    const isConfigured = isSuperAdmin && (!!savedJson || chosenType === 'none');

                                    // Anomaly rules only make sense for quality/attribute violations (PPE/defect).
                                    // Any label starting with "no-", "incorrect-", or "missing-" signals an
                                    // attribute-based violation — this covers current and future PPE types for
                                    // any industry (no-hardhat, no-vest, incorrect-glove, missing-goggles …).
                                    // We check both the SOP's default labels AND any per-camera override labels
                                    // so the detection works even when the DB row has no sopTriggerLabels set.
                                    // D-9 fix: ``isPPEViolation`` (conceptually "supports anomaly rule")
                                    // now comes from the SOP type itself via ``req.supportsAnomalyRule``.
                                    // The server is the source of truth: it knows whether a SOP type's
                                    // detections are inherently anomalous (PPE, missing-equipment, etc.)
                                    // versus spatially-defined (unauthorized-person, vehicle-in-zone).
                                    //
                                    // Fallback: while clusters are mid-rollout and the migration may not
                                    // have run yet, fall back to the legacy label-prefix regex so older
                                    // PPE rules don't silently lose their Anomaly tab.  Once every
                                    // environment is on the new schema this fallback can be deleted.
                                    const PPE_LABEL_RE = /^(no[-_]|incorrect[-_]|missing[-_])/i;
                                    const allLabelsForPPECheck = [...sopLabels, ...activeTags];
                                    const isPPEViolation = req.supportsAnomalyRule
                                        ?? allLabelsForPPECheck.some(l => PPE_LABEL_RE.test(l.trim()));
                                    const availableRuleTypes: readonly RuleEditorType[] = isPPEViolation
                                        ? (['none', 'geofence', 'dwell', 'anomaly'] as const)
                                        : (['none', 'geofence', 'dwell'] as const);
                                    // If a stale 'anomaly' choice exists for a spatial-only violation, reset it.
                                    const effectiveChosenType: RuleEditorType = (!isPPEViolation && chosenType === 'anomaly') ? 'geofence' : chosenType;

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
                                                <div className="flex items-center gap-2">
                                                    {active && isConfigured && (
                                                        <span className="flex items-center gap-1 px-2 py-0.5 bg-emerald-100 text-emerald-700 text-[10px] font-bold rounded-full border border-emerald-200 whitespace-nowrap">
                                                            <Settings2 className="w-2.5 h-2.5" /> {chosenType === 'none' ? 'Whole Frame' : 'Configured'}
                                                        </span>
                                                    )}
                                                    {active && (
                                                        <div className="bg-blue-600 text-white p-1 rounded-full shadow-sm">
                                                            <Check className="w-3.5 h-3.5" />
                                                        </div>
                                                    )}
                                                </div>
                                            </button>

                                            {/* Label editor — only shown when violation is active */}
                                            {active && (sopLabels.length > 0 || isSuperAdmin) && (
                                                <div className="px-4 pb-4 border-t border-blue-100 pt-3">
                                                    <button type="button"
                                                        onClick={() => setExpandedViolation(isExpanded ? null : req.sopViolationTypeId)}
                                                        className="flex items-center gap-1.5 text-xs font-semibold text-blue-700 hover:text-blue-900 mb-3">
                                                        <Tag className="w-3 h-3" />
                                                        {isSuperAdmin ? 'Camera Settings' : 'Override Trigger Labels'}
                                                        {isExpanded ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
                                                    </button>

                                                    {isExpanded && (
                                                        <>
                                                            {isSuperAdmin ? (
                                                                /* SuperAdmin: show SOP defaults + free-text add option */
                                                                <div>
                                                                    <p className="text-[10px] text-gray-500 mb-2">SOP Default Labels (deselect to disable, or add custom)</p>
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
                                                                            placeholder="Add custom label..."
                                                                        />
                                                                    </div>
                                                                    <p className="text-[10px] text-gray-400 mt-1">Type a new label and press Enter to add custom triggers beyond the SOP defaults.</p>
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

                                                            {/* Rule Configuration Editor (SuperAdmin only) */}
                                                            {isSuperAdmin && (
                                                                    <div className="mt-4">
                                                                        <label className="block text-[10px] font-bold text-gray-700 uppercase mb-2">
                                                                            Policy Configuration
                                                                        </label>
                                                                        <div className={`inline-flex rounded-lg border p-0.5 mb-3 ${isConfigured ? 'border-emerald-300 bg-emerald-50/40' : 'border-gray-300 bg-gray-50'}`} role="tablist">
                                                                            {availableRuleTypes.map(t => (
                                                                                <button
                                                                                    key={t}
                                                                                    type="button"
                                                                                    role="tab"
                                                                                    aria-selected={effectiveChosenType === t}
                                                                                    onClick={async () => {
                                                                                        // Switching rule type wipes the saved JSON for this
                                                                                        // violation — the schemas are incompatible. Confirm
                                                                                        // before clobbering a non-empty policy.
                                                                                        if (savedJson && effectiveChosenType !== t) {
                                                                                            const label = t === 'none' ? 'Whole Frame (no zone)' : t;
                                                                                            const ok = await requestConfirm(
                                                                                                `Switching to ${label} will discard the existing ${effectiveChosenType} configuration for this violation. Continue?`,
                                                                                                { confirmLabel: 'Discard & switch', cancelLabel: 'Keep existing' }
                                                                                            );
                                                                                            if (!ok) return;
                                                                                            setConfigForViolation(req.sopViolationTypeId, '');
                                                                                        }
                                                                                        setRuleTypeChoice(prev => ({ ...prev, [req.sopViolationTypeId]: t }));
                                                                                    }}
                                                                                    className={`px-3 py-1 text-[11px] font-bold rounded-md transition-all ${effectiveChosenType === t
                                                                                        ? 'bg-white text-emerald-700 shadow-sm'
                                                                                        : 'text-gray-500 hover:text-gray-800'}`}
                                                                                >
                                                                                    {t === 'none' ? 'Whole Frame' : t === 'geofence' ? 'Geofence' : t === 'dwell' ? 'Dwell' : 'Anomaly'}
                                                                                </button>
                                                                            ))}
                                                                        </div>
                                                                        {!isPPEViolation && effectiveChosenType !== 'none' && (
                                                                            <p className="text-[9px] text-amber-600 font-semibold mb-2">
                                                                                Anomaly is not available for spatial violations — use Geofence to trigger on entry/exit or Dwell to trigger after a person lingers in the zone.
                                                                            </p>
                                                                        )}
                                                                        {effectiveChosenType === 'none' ? (
                                                                            <div className="rounded-lg border border-green-200 bg-green-50/60 p-3">
                                                                                <p className="text-xs font-semibold text-green-800">Whole Frame — no zone restriction</p>
                                                                                <p className="text-[10px] text-green-700 mt-1">
                                                                                    The AI model fires on any detection anywhere in the camera frame.
                                                                                    No polygon needs to be drawn. Ideal for test cameras, entrance monitors,
                                                                                    or any scene where you want to catch violations across the full view.
                                                                                </p>
                                                                            </div>
                                                                        ) : effectiveChosenType === 'anomaly' ? (
                                                                            <AnomalyEditor
                                                                                value={savedJson}
                                                                                onChange={(json: string) => setConfigForViolation(req.sopViolationTypeId, json)}
                                                                                suggestedLabels={sopLabels}
                                                                            />
                                                                        ) : (
                                                                            <PolygonEditor
                                                                                value={savedJson}
                                                                                onChange={(json: string) => setConfigForViolation(req.sopViolationTypeId, json)}
                                                                                whepUrl={camera?.whepUrl}
                                                                                ruleType={effectiveChosenType as 'geofence' | 'dwell'}
                                                                            />
                                                                        )}
                                                                        {savedJson && (
                                                                            <div className="mt-3 rounded-lg bg-gray-950 border border-gray-700 overflow-hidden">
                                                                                <div className="flex items-center justify-between px-3 py-1.5 bg-gray-800/80 border-b border-gray-700">
                                                                                    <span className="text-[9px] font-bold text-gray-400 uppercase tracking-wider">Rule Config JSON</span>
                                                                                    <span className="text-[9px] text-emerald-400 font-mono font-bold">&#x2713; saved</span>
                                                                                </div>
                                                                                <pre className="text-[10px] text-emerald-300 font-mono overflow-auto max-h-40 p-3 whitespace-pre-wrap break-all leading-relaxed">
                                                                                    {(() => { try { return JSON.stringify(JSON.parse(savedJson), null, 2); } catch { return savedJson; } })()}
                                                                                </pre>
                                                                            </div>
                                                                        )}
                                                                        <p className="text-[9px] text-gray-400 mt-2 italic">
                                                                            {effectiveChosenType === 'none' && 'Detects anywhere in the frame with no spatial restriction. Best for test cameras or full-scene monitoring.'}
                                                                            {effectiveChosenType === 'geofence' && 'Draw a zone — alerts fire only for detections inside (entry) or outside (exit) the polygon.'}
                                                                            {effectiveChosenType === 'dwell' && 'Draw a zone — alerts fire only when a subject stays continuously in the zone for the configured duration.'}
                                                                            {effectiveChosenType === 'anomaly' && 'Filter detections by model confidence and an optional label whitelist. Non-spatial.'}
                                                                            {' '}Only SuperAdmins can author policies; tenant admins inherit the configuration.
                                                                        </p>
                                                                    </div>
                                                            )}
                                                        </>
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

            {/* D-6 phase 2: confirmation dialog (replaces ``window.confirm``).
                Rendered inside the modal root so it overlays the form.  z-[60]
                puts it above the modal backdrop (z-50). */}
            {confirmState && (
                <div
                    className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
                    role="dialog"
                    aria-modal="true"
                    aria-labelledby="confirm-dialog-title"
                >
                    <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md border border-white/20 overflow-hidden">
                        <div className="p-6">
                            <h4 id="confirm-dialog-title" className="text-base font-bold text-gray-900 mb-2">
                                Confirm change
                            </h4>
                            <p className="text-sm text-gray-700 leading-relaxed">{confirmState.message}</p>
                        </div>
                        <div className="flex justify-end gap-2 px-6 py-4 bg-gray-50 border-t border-gray-200">
                            <button
                                type="button"
                                autoFocus
                                onClick={() => {
                                    confirmState.resolve(false);
                                    setConfirmState(null);
                                }}
                                className="px-4 py-2 text-sm text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 font-semibold"
                            >
                                {confirmState.cancelLabel}
                            </button>
                            <button
                                type="button"
                                onClick={() => {
                                    confirmState.resolve(true);
                                    setConfirmState(null);
                                }}
                                className="px-4 py-2 text-sm text-white bg-red-600 rounded-lg hover:bg-red-700 font-semibold shadow-sm"
                            >
                                {confirmState.confirmLabel}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
