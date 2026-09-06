'use client';

import React, { useEffect, useState, useRef, useMemo, useCallback } from 'react';
import { 
    X, 
    Save, 
    Loader2, 
    Users, 
    Clock, 
    Crosshair, 
    Layers, 
    Sliders, 
    Trash2, 
    Info, 
    Check, 
    Camera as CameraIcon,
    Upload,
    Undo2,
    RefreshCw,
    Play,
    Image as ImageIcon,
    AlertCircle,
    Maximize2,
    Calendar,
    Plus,
    Search
} from 'lucide-react';
import { getCameras } from '@/lib/api/tenant/cameras';
import { getEmployees } from '@/lib/api/tenant/employees';
import { getViolations, Violation } from '@/lib/api/tenant/violations';
import type { CameraResponse } from '@/types/admin';
import type { Employee } from '@/types/employee';
import { 
    WorkstationResponse, 
    WorkstationDetailResponse, 
    CreateWorkstationPayload, 
    UpdateWorkstationPayload,
    WorkstationTimingWindow
} from '@/lib/api/tenant/reliever';

interface WorkstationFormModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
    workstation?: WorkstationDetailResponse | WorkstationResponse | null;
}

export default function WorkstationFormModal({
    isOpen,
    onClose,
    onSuccess,
    workstation
}: WorkstationFormModalProps) {
    const isEdit = !!workstation;

    // Form Fields
    const [name, setName] = useState('');
    const [code, setCode] = useState('');
    const [cameraId, setCameraId] = useState('');
    
    // Numeric fields with string support to allow backspacing/erasing cleanly
    const [requiredPrimaryWorkers, setRequiredPrimaryWorkers] = useState<number | string>(1);
    const [maxRelievers, setMaxRelievers] = useState<number | string>(1);
    const [handoverThresholdSeconds, setHandoverThresholdSeconds] = useState<number | string>(60);
    const [maxReliefDurationSeconds, setMaxReliefDurationSeconds] = useState<number | string>(900);
    const [isActive, setIsActive] = useState(true);

    // Worker assignments
    const [selectedPrimaryIds, setSelectedPrimaryIds] = useState<string[]>([]);
    const [selectedRelieverIds, setSelectedRelieverIds] = useState<string[]>([]);
    const [workerSearchTerm, setWorkerSearchTerm] = useState('');
    const [activeShiftRosterTab, setActiveShiftRosterTab] = useState<string>('all');

    // Operating Timing Windows / Shifts
    const [timingWindows, setTimingWindows] = useState<WorkstationTimingWindow[]>([]);

    // Polygon coordinates state: array of [x, y] in normalized (0..1) coords
    const [polygonPoints, setPolygonPoints] = useState<[number, number][]>([]);

    // External resources
    const [cameras, setCameras] = useState<CameraResponse[]>([]);
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [violations, setViolations] = useState<Violation[]>([]);
    const [isLoadingResources, setIsLoadingResources] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    // Frame backdrop & live feed state
    const [historicalFrameUrl, setHistoricalFrameUrl] = useState<string | null>(null);
    const [customFrameUrl, setCustomFrameUrl] = useState<string | null>(null);
    const [showLiveFeed, setShowLiveFeed] = useState(false);
    const [captureError, setCaptureError] = useState<string | null>(null);
    const liveVideoRef = useRef<HTMLDivElement | null>(null);
    const fileInputRef = useRef<HTMLInputElement | null>(null);

    // Canvas drawing and drag ref (vibration-free pointer tracking)
    const svgRef = useRef<SVGSVGElement | null>(null);
    const [dragIdx, setDragIdx] = useState<number | null>(null);
    const dragIdxRef = useRef<number | null>(null);

    // Selected vertex for click-to-delete options
    const [selectedVertexIdx, setSelectedVertexIdx] = useState<number | null>(null);
    const pointerStartRef = useRef<{ x: number; y: number } | null>(null);
    const hasMovedRef = useRef<boolean>(false);

    // Load WHEP component on client
    useEffect(() => {
        if (typeof window !== 'undefined') {
            // @ts-ignore
            import('@eyevinn/whep-video-component').catch(console.error);
        }
    }, []);

    useEffect(() => {
        if (!isOpen) return;

        const loadResources = async () => {
            setIsLoadingResources(true);
            try {
                const [cams, emps, vios] = await Promise.all([
                    getCameras().catch(() => []),
                    getEmployees().catch(() => []),
                    getViolations().catch(() => [])
                ]);
                setCameras(cams);
                setEmployees(emps);
                setViolations(vios);
            } catch (err) {
                console.error('Failed to load cameras/employees for modal:', err);
            } finally {
                setIsLoadingResources(false);
            }
        };
        loadResources();

        if (workstation) {
            setName(workstation.name);
            setCode(workstation.code);
            setCameraId(workstation.cameraId);
            setRequiredPrimaryWorkers(workstation.requiredPrimaryWorkers);
            setMaxRelievers(workstation.maxRelievers);
            setHandoverThresholdSeconds(workstation.handoverThresholdSeconds);
            setMaxReliefDurationSeconds(workstation.maxReliefDurationSeconds);
            setIsActive(workstation.isActive);

            try {
                const parsedPoly = JSON.parse(workstation.polygonJson);
                if (Array.isArray(parsedPoly)) {
                    setPolygonPoints(parsedPoly);
                }
            } catch {
                setPolygonPoints([]);
            }

            if (workstation.operatingScheduleJson) {
                try {
                    const parsedSched = JSON.parse(workstation.operatingScheduleJson);
                    if (Array.isArray(parsedSched)) {
                        setTimingWindows(parsedSched);
                    } else {
                        setTimingWindows([]);
                    }
                } catch {
                    setTimingWindows([]);
                }
            } else {
                setTimingWindows([]);
            }

            // Populate assignments if available
            const detailWs = workstation as WorkstationDetailResponse;
            if (detailWs.assignments) {
                setSelectedPrimaryIds(
                    detailWs.assignments.filter(a => a.role === 'Primary' && a.isActive).map(a => a.employeeId)
                );
                setSelectedRelieverIds(
                    detailWs.assignments.filter(a => a.role === 'Reliever' && a.isActive).map(a => a.employeeId)
                );
                setActiveShiftRosterTab('all');
            }
        } else {
            // Reset to defaults for create
            setActiveShiftRosterTab('all');
            setName('');
            setCode('');
            setCameraId('');
            setRequiredPrimaryWorkers(1);
            setMaxRelievers(1);
            setHandoverThresholdSeconds(60);
            setMaxReliefDurationSeconds(900);
            setIsActive(true);
            setSelectedPrimaryIds([]);
            setSelectedRelieverIds([]);
            setTimingWindows([]);
            setPolygonPoints([
                [0.2, 0.2],
                [0.8, 0.2],
                [0.8, 0.8],
                [0.2, 0.8]
            ]);
            setCustomFrameUrl(null);
            setHistoricalFrameUrl(null);
        }
    }, [isOpen, workstation]);

    const selectedCamera = useMemo(() => {
        return cameras.find(c => c.id === cameraId);
    }, [cameras, cameraId]);

    // Lookup historical frame whenever camera is chosen
    useEffect(() => {
        if (!selectedCamera) {
            setHistoricalFrameUrl(null);
            return;
        }

        // Find the latest violation frame for this camera
        const camVio = violations.find(v => 
            (v.cameraId === selectedCamera.cameraId || v.cameraId === selectedCamera.id) &&
            (v.frameUrl || v.framePath)
        );

        if (camVio) {
            setHistoricalFrameUrl(camVio.frameUrl || camVio.framePath || null);
        } else {
            setHistoricalFrameUrl(null);
        }
    }, [selectedCamera, violations]);

    // Active backdrop image (custom uploaded/captured image takes priority over auto historical frame)
    const activeFrameImage = customFrameUrl || historicalFrameUrl;

    // Capacity bounds (minimum concurrent operators & maximum concurrent relievers on-duty)
    const primaryCapacity = Math.max(1, Number(requiredPrimaryWorkers) || 1);
    const relieverCapacity = Math.max(1, Number(maxRelievers) || 1);

    // Vibration-free pointer drag tracking via window event listeners
    useEffect(() => {
        if (dragIdx === null) return;

        const handlePointerMove = (e: PointerEvent) => {
            if (dragIdxRef.current === null || !svgRef.current) return;

            // Track movement to distinguish click from drag
            if (pointerStartRef.current) {
                const dx = e.clientX - pointerStartRef.current.x;
                const dy = e.clientY - pointerStartRef.current.y;
                if (Math.hypot(dx, dy) > 4) {
                    hasMovedRef.current = true;
                }
            }

            const rect = svgRef.current.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) return;

            const rawX = (e.clientX - rect.left) / rect.width;
            const rawY = (e.clientY - rect.top) / rect.height;
            const clampedX = Math.max(0, Math.min(1, Math.round(rawX * 1000) / 1000));
            const clampedY = Math.max(0, Math.min(1, Math.round(rawY * 1000) / 1000));

            setPolygonPoints(prev => {
                const targetIdx = dragIdxRef.current;
                if (targetIdx === null || targetIdx < 0 || targetIdx >= prev.length) return prev;
                const copy = [...prev];
                copy[targetIdx] = [clampedX, clampedY];
                return copy;
            });
        };

        const handlePointerUp = () => {
            // If pointer didn't move appreciably, user clicked the vertex
            if (!hasMovedRef.current && dragIdxRef.current !== null) {
                const clickedIdx = dragIdxRef.current;
                setSelectedVertexIdx(prev => (prev === clickedIdx ? null : clickedIdx));
            }
            dragIdxRef.current = null;
            setDragIdx(null);
            pointerStartRef.current = null;
        };

        window.addEventListener('pointermove', handlePointerMove);
        window.addEventListener('pointerup', handlePointerUp);
        window.addEventListener('pointercancel', handlePointerUp);

        return () => {
            window.removeEventListener('pointermove', handlePointerMove);
            window.removeEventListener('pointerup', handlePointerUp);
            window.removeEventListener('pointercancel', handlePointerUp);
        };
    }, [dragIdx]);

    // Keyboard shortcut: Delete or Backspace removes the selected vertex
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (selectedVertexIdx !== null && (e.key === 'Delete' || e.key === 'Backspace')) {
                const tag = (document.activeElement?.tagName || '').toLowerCase();
                if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
                e.preventDefault();
                handleRemovePoint(selectedVertexIdx);
            }
        };
        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [selectedVertexIdx]);

    const handleVertexPointerDown = (index: number, e: React.PointerEvent) => {
        e.preventDefault();
        e.stopPropagation();
        dragIdxRef.current = index;
        setDragIdx(index);
        pointerStartRef.current = { x: e.clientX, y: e.clientY };
        hasMovedRef.current = false;
    };

    const handleSvgClick = (e: React.MouseEvent<SVGSVGElement>) => {
        if (dragIdxRef.current !== null || dragIdx !== null) return;
        if (selectedVertexIdx !== null) {
            // Click outside vertices deselects
            setSelectedVertexIdx(null);
            return;
        }
        if (!svgRef.current) return;
        const rect = svgRef.current.getBoundingClientRect();
        const rawX = (e.clientX - rect.left) / rect.width;
        const rawY = (e.clientY - rect.top) / rect.height;
        const clampedX = Math.max(0, Math.min(1, Math.round(rawX * 1000) / 1000));
        const clampedY = Math.max(0, Math.min(1, Math.round(rawY * 1000) / 1000));
        setPolygonPoints(prev => [...prev, [clampedX, clampedY]]);
    };

    const handleRemovePoint = (index: number, e?: React.MouseEvent) => {
        if (e) e.stopPropagation();
        setPolygonPoints(prev => prev.filter((_, i) => i !== index));
        setSelectedVertexIdx(null);
    };

    const handleUndoPoint = () => {
        setPolygonPoints(prev => (prev.length > 0 ? prev.slice(0, -1) : prev));
        setSelectedVertexIdx(null);
    };

    const handleResetPolygon = () => {
        setPolygonPoints([
            [0.2, 0.2],
            [0.8, 0.2],
            [0.8, 0.8],
            [0.2, 0.8]
        ]);
        setSelectedVertexIdx(null);
    };

    const handleSetEntireFrame = () => {
        setPolygonPoints([
            [0.0, 0.0],
            [1.0, 0.0],
            [1.0, 1.0],
            [0.0, 1.0]
        ]);
        setSelectedVertexIdx(null);
    };

    const handleClearPolygon = () => {
        setPolygonPoints([]);
        setSelectedVertexIdx(null);
    };

    // Frame capture from live stream
    const handleCaptureLive = () => {
        const container = liveVideoRef.current;
        if (!container) {
            setCaptureError('Live stream element not mounted.');
            return;
        }
        const video = container.querySelector('video') as HTMLVideoElement | null;
        if (!video || !video.videoWidth || !video.videoHeight) {
            setCaptureError('Live stream not ready. Please wait a few seconds.');
            return;
        }
        try {
            const canvas = document.createElement('canvas');
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            const ctx = canvas.getContext('2d');
            if (!ctx) throw new Error('Canvas context not available');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            const dataUrl = canvas.toDataURL('image/jpeg', 0.85);
            setCustomFrameUrl(dataUrl);
            setShowLiveFeed(false);
            setCaptureError(null);
        } catch (err: any) {
            setCaptureError('Failed to capture frame: ' + (err.message || err));
        }
    };

    const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        if (!file.type.startsWith('image/')) {
            setCaptureError('Please choose a valid image file.');
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            setCustomFrameUrl(String(reader.result));
            setCaptureError(null);
        };
        reader.readAsDataURL(file);
        e.target.value = '';
    };

    // Worker Selection for Station-Level Pool & Shift-Specific Rostering
    const currentActiveShift = useMemo(() => {
        if (activeShiftRosterTab === 'all') return null;
        return timingWindows.find(w => w.id === activeShiftRosterTab) || null;
    }, [activeShiftRosterTab, timingWindows]);

    const activeSelectedPrimaryIds = useMemo(() => {
        if (currentActiveShift) {
            return currentActiveShift.primaryEmployeeIds || [];
        }
        return selectedPrimaryIds;
    }, [currentActiveShift, selectedPrimaryIds]);

    const activeSelectedRelieverIds = useMemo(() => {
        if (currentActiveShift) {
            return currentActiveShift.relieverEmployeeIds || [];
        }
        return selectedRelieverIds;
    }, [currentActiveShift, selectedRelieverIds]);

    const togglePrimaryEmployee = (empId: string) => {
        if (activeShiftRosterTab === 'all') {
            setSelectedPrimaryIds(prev => 
                prev.includes(empId) ? prev.filter(id => id !== empId) : [...prev, empId]
            );
            setSelectedRelieverIds(prev => prev.filter(id => id !== empId));
        } else {
            setTimingWindows(prev => prev.map(win => {
                if (win.id !== activeShiftRosterTab) return win;
                const currentPrimaries = win.primaryEmployeeIds || [];
                const currentRelievers = win.relieverEmployeeIds || [];
                const nextPrimaries = currentPrimaries.includes(empId)
                    ? currentPrimaries.filter(id => id !== empId)
                    : [...currentPrimaries, empId];
                const nextRelievers = currentRelievers.filter(id => id !== empId);
                return {
                    ...win,
                    primaryEmployeeIds: nextPrimaries,
                    relieverEmployeeIds: nextRelievers
                };
            }));
        }
    };

    const toggleRelieverEmployee = (empId: string) => {
        if (activeShiftRosterTab === 'all') {
            setSelectedRelieverIds(prev => 
                prev.includes(empId) ? prev.filter(id => id !== empId) : [...prev, empId]
            );
            setSelectedPrimaryIds(prev => prev.filter(id => id !== empId));
        } else {
            setTimingWindows(prev => prev.map(win => {
                if (win.id !== activeShiftRosterTab) return win;
                const currentPrimaries = win.primaryEmployeeIds || [];
                const currentRelievers = win.relieverEmployeeIds || [];
                const nextRelievers = currentRelievers.includes(empId)
                    ? currentRelievers.filter(id => id !== empId)
                    : [...currentRelievers, empId];
                const nextPrimaries = currentPrimaries.filter(id => id !== empId);
                return {
                    ...win,
                    primaryEmployeeIds: nextPrimaries,
                    relieverEmployeeIds: nextRelievers
                };
            }));
        }
    };

    const filteredEmployees = useMemo(() => {
        if (!workerSearchTerm.trim()) return employees;
        const term = workerSearchTerm.toLowerCase();
        return employees.filter(e =>
            `${e.firstName} ${e.lastName}`.toLowerCase().includes(term) ||
            (e.employeeId && e.employeeId.toLowerCase().includes(term))
        );
    }, [employees, workerSearchTerm]);

    // Real-time Window Overlap Guard
    const overlapError = useMemo(() => {
        const active = timingWindows.filter(w => w.isActive);
        for (let i = 0; i < active.length; i++) {
            const w1 = active[i];
            if (w1.startTime === w1.endTime) {
                return `Shift '${w1.label || `Shift ${i + 1}`}' cannot have identical start and end times (${w1.startTime}).`;
            }

            for (let j = i + 1; j < active.length; j++) {
                const w2 = active[j];
                if (w2.startTime === w2.endTime) {
                    return `Shift '${w2.label || `Shift ${j + 1}`}' cannot have identical start and end times (${w2.startTime}).`;
                }

                const days1 = w1.daysOfWeek && w1.daysOfWeek.length > 0 ? w1.daysOfWeek : [0, 1, 2, 3, 4, 5, 6];
                const days2 = w2.daysOfWeek && w2.daysOfWeek.length > 0 ? w2.daysOfWeek : [0, 1, 2, 3, 4, 5, 6];

                const sharedDays = days1.filter(d => days2.includes(d));
                if (sharedDays.length === 0) continue;

                const [sh1, sm1] = w1.startTime.split(':').map(Number);
                const [eh1, em1] = w1.endTime.split(':').map(Number);
                let s1 = sh1 * 60 + sm1;
                let e1 = eh1 * 60 + em1;
                if (e1 === 0 && s1 > 0) e1 = 1440;

                const [sh2, sm2] = w2.startTime.split(':').map(Number);
                const [eh2, em2] = w2.endTime.split(':').map(Number);
                let s2 = sh2 * 60 + sm2;
                let e2 = eh2 * 60 + em2;
                if (e2 === 0 && s2 > 0) e2 = 1440;

                const intervals1 = s1 < e1 ? [[s1, e1]] : [[s1, 1440], [0, e1]];
                const intervals2 = s2 < e2 ? [[s2, e2]] : [[s2, 1440], [0, e2]];

                let overlaps = false;
                for (const [start1, end1] of intervals1) {
                    for (const [start2, end2] of intervals2) {
                        if (Math.max(start1, start2) < Math.min(end1, end2)) {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) break;
                }

                if (overlaps) {
                    const dayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
                    const dayNames = sharedDays.map(d => dayLabels[d]).join(', ');
                    return `Shift conflict: '${w1.label || `Shift ${i + 1}`}' (${w1.startTime} - ${w1.endTime}) overlaps with '${w2.label || `Shift ${j + 1}`}' (${w2.startTime} - ${w2.endTime}) on ${dayNames}. Operating windows cannot overlap.`;
                }
            }
        }
        return null;
    }, [timingWindows]);

    const handleAddTimingWindow = () => {
        const newWindow: WorkstationTimingWindow = {
            id: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : `shift_${Date.now()}`,
            label: `Shift ${timingWindows.length + 1}`,
            startTime: '08:00',
            endTime: '16:00',
            daysOfWeek: [1, 2, 3, 4, 5],
            isActive: true,
        };
        setTimingWindows(prev => [...prev, newWindow]);
    };

    const handleUpdateTimingWindow = (id: string, updates: Partial<WorkstationTimingWindow>) => {
        setTimingWindows(prev => prev.map(w => (w.id === id ? { ...w, ...updates } : w)));
    };

    const handleDeleteTimingWindow = (id: string) => {
        setTimingWindows(prev => prev.filter(w => w.id !== id));
        if (activeShiftRosterTab === id) {
            setActiveShiftRosterTab('all');
        }
    };

    const handleToggleDay = (id: string, day: number) => {
        setTimingWindows(prev =>
            prev.map(w => {
                if (w.id !== id) return w;
                const current = w.daysOfWeek || [0, 1, 2, 3, 4, 5, 6];
                const updated = current.includes(day)
                    ? current.filter(d => d !== day)
                    : [...current, day].sort();
                return { ...w, daysOfWeek: updated };
            })
        );
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setErrorMessage(null);

        if (!name.trim()) {
            setErrorMessage('Workstation name is required.');
            return;
        }

        if (!code.trim()) {
            setErrorMessage('Workstation code is required.');
            return;
        }

        if (!cameraId) {
            setErrorMessage('A camera must be selected for this workstation.');
            return;
        }

        if (polygonPoints.length < 3) {
            setErrorMessage('A valid polygon must have at least 3 vertices.');
            return;
        }

        if (overlapError) {
            setErrorMessage(overlapError);
            return;
        }

        setIsSubmitting(true);
        try {
            const polygonJson = JSON.stringify(polygonPoints);
            const reqWorkers = Math.max(1, Number(requiredPrimaryWorkers) || 1);
            const maxRel = Math.max(1, Number(maxRelievers) || 1);
            const handoverSec = Math.max(5, Number(handoverThresholdSeconds) || 60);
            const maxRelSec = Math.max(30, Number(maxReliefDurationSeconds) || 900);
            const opSchedJson = timingWindows.length > 0 ? JSON.stringify(timingWindows) : undefined;

            // Union of all shift-specific assignments and global pool for database consistency
            const allPrimaryIds = Array.from(new Set([
                ...selectedPrimaryIds,
                ...timingWindows.flatMap(w => w.primaryEmployeeIds || [])
            ]));
            const allRelieverIds = Array.from(new Set([
                ...selectedRelieverIds,
                ...timingWindows.flatMap(w => w.relieverEmployeeIds || [])
            ])).filter(id => !allPrimaryIds.includes(id));

            if (isEdit && workstation) {
                const { updateWorkstation } = await import('@/lib/api/tenant/reliever');
                const payload: UpdateWorkstationPayload = {
                    name: name.trim(),
                    code: code.trim().toUpperCase(),
                    cameraId,
                    polygonJson,
                    requiredPrimaryWorkers: reqWorkers,
                    maxRelievers: maxRel,
                    handoverThresholdSeconds: handoverSec,
                    maxReliefDurationSeconds: maxRelSec,
                    operatingScheduleJson: opSchedJson,
                    isActive,
                    primaryEmployeeIds: allPrimaryIds,
                    relieverEmployeeIds: allRelieverIds
                };
                await updateWorkstation(workstation.id, payload);
            } else {
                const { createWorkstation } = await import('@/lib/api/tenant/reliever');
                const payload: CreateWorkstationPayload = {
                    name: name.trim(),
                    code: code.trim().toUpperCase(),
                    cameraId,
                    polygonJson,
                    requiredPrimaryWorkers: reqWorkers,
                    maxRelievers: maxRel,
                    handoverThresholdSeconds: handoverSec,
                    maxReliefDurationSeconds: maxRelSec,
                    operatingScheduleJson: opSchedJson,
                    primaryEmployeeIds: allPrimaryIds,
                    relieverEmployeeIds: allRelieverIds
                };
                await createWorkstation(payload);
            }

            onSuccess();
            onClose();
        } catch (err: any) {
            console.error('Failed to save workstation:', err);
            setErrorMessage(err.message || 'An error occurred while saving.');
        } finally {
            setIsSubmitting(false);
        }
    };

    if (!isOpen) return null;

    const polygonSvgPoints = polygonPoints.map(([x, y]) => `${x * 100},${y * 100}`).join(' ');

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4 overflow-y-auto">
            <div className="bg-white border border-gray-100 rounded-3xl w-full max-w-5xl text-gray-900 shadow-2xl overflow-hidden my-8 max-h-[92vh] flex flex-col animate-in zoom-in-95 duration-150">
                {/* Header */}
                <div className="px-8 py-5 border-b border-gray-100 flex items-center justify-between bg-white">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-xl bg-blue-50 border border-blue-100 flex items-center justify-center text-blue-600">
                            <Layers className="w-5 h-5" />
                        </div>
                        <div>
                            <h2 className="text-xl font-bold text-gray-900">
                                {isEdit ? `Edit Workstation (${workstation?.code})` : 'Configure New Workstation'}
                            </h2>
                            <p className="text-xs text-gray-500">
                                Define polygon ROI geofence, operator capacities, handover grace timers, and assigned staff.
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="text-gray-400 hover:text-gray-600 p-2 rounded-xl hover:bg-gray-100 transition"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form Body */}
                <form onSubmit={handleSubmit} className="p-8 overflow-y-auto space-y-6 flex-1">
                    {errorMessage && (
                        <div className="p-4 rounded-2xl bg-rose-50 border border-rose-200 text-rose-700 text-sm flex items-center gap-3">
                            <Info className="w-5 h-5 flex-shrink-0 text-rose-500" />
                            <span>{errorMessage}</span>
                        </div>
                    )}

                    <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
                        {/* Left Column: Basic Parameters & Capacities */}
                        <div className="lg:col-span-6 space-y-5">
                            <div className="grid grid-cols-2 gap-4">
                                <div className="space-y-1.5">
                                    <label className="text-xs font-semibold uppercase tracking-wider text-gray-700">
                                        Workstation Name *
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        placeholder="e.g. Line 1 - Packaging Unit"
                                        value={name}
                                        onChange={e => setName(e.target.value)}
                                        className="w-full px-4 py-2.5 rounded-xl bg-gray-50/80 border border-gray-200 text-gray-900 placeholder:text-gray-400 text-sm focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
                                    />
                                </div>
                                <div className="space-y-1.5">
                                    <label className="text-xs font-semibold uppercase tracking-wider text-gray-700">
                                        Workstation Code *
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        placeholder="e.g. WS-PKG-01"
                                        value={code}
                                        onChange={e => setCode(e.target.value)}
                                        className="w-full px-4 py-2.5 rounded-xl bg-gray-50/80 border border-gray-200 text-gray-900 placeholder:text-gray-400 text-sm focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 uppercase transition-all"
                                    />
                                </div>
                            </div>

                            <div className="space-y-1.5">
                                <label className="text-xs font-semibold uppercase tracking-wider text-gray-700 flex items-center justify-between">
                                    <span>Associated Camera *</span>
                                    <span className="text-[11px] text-gray-500">{cameras.length} available</span>
                                </label>
                                <select
                                    required
                                    value={cameraId}
                                    onChange={e => setCameraId(e.target.value)}
                                    className="w-full px-4 py-2.5 rounded-xl bg-gray-50/80 border border-gray-200 text-gray-900 text-sm focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
                                >
                                    <option value="">Select Camera Stream...</option>
                                    {cameras.map(c => (
                                        <option key={c.id} value={c.id}>
                                            {c.name} ({c.cameraId})
                                        </option>
                                    ))}
                                </select>
                            </div>

                            {/* Dynamic Capacities & Thresholds (Editable with Backspace/Erase Support) */}
                            <div className="p-5 rounded-2xl bg-gray-50/70 border border-gray-200 space-y-4">
                                <div className="flex items-center gap-2 text-blue-700 font-semibold text-xs uppercase tracking-wider">
                                    <Sliders className="w-4 h-4" /> Dynamic Capacities & Thresholds
                                </div>

                                <div className="grid grid-cols-2 gap-4">
                                    <div className="space-y-1">
                                        <label className="text-xs font-medium text-gray-700">
                                            Required Primary Workers
                                        </label>
                                        <input
                                            type="number"
                                            min={1}
                                            max={20}
                                            value={requiredPrimaryWorkers}
                                            onChange={e => {
                                                const v = e.target.value;
                                                if (v === '') {
                                                    setRequiredPrimaryWorkers('');
                                                } else {
                                                    const n = parseInt(v, 10);
                                                    if (!isNaN(n)) setRequiredPrimaryWorkers(n);
                                                }
                                            }}
                                            onBlur={() => {
                                                if (requiredPrimaryWorkers === '' || Number(requiredPrimaryWorkers) < 1) {
                                                    setRequiredPrimaryWorkers(1);
                                                }
                                            }}
                                            className="w-full px-3 py-2 rounded-xl bg-white border border-gray-200 text-gray-900 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                                        />
                                        <p className="text-[11px] text-gray-500">Min operators required ({primaryCapacity})</p>
                                    </div>
                                    <div className="space-y-1">
                                        <label className="text-xs font-medium text-gray-700">
                                            Max Relievers Allowed
                                        </label>
                                        <input
                                            type="number"
                                            min={1}
                                            max={10}
                                            value={maxRelievers}
                                            onChange={e => {
                                                const v = e.target.value;
                                                if (v === '') {
                                                    setMaxRelievers('');
                                                } else {
                                                    const n = parseInt(v, 10);
                                                    if (!isNaN(n)) setMaxRelievers(n);
                                                }
                                            }}
                                            onBlur={() => {
                                                if (maxRelievers === '' || Number(maxRelievers) < 1) {
                                                    setMaxRelievers(1);
                                                }
                                            }}
                                            className="w-full px-3 py-2 rounded-xl bg-white border border-gray-200 text-gray-900 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                                        />
                                        <p className="text-[11px] text-gray-500">Simultaneous relief limit ({relieverCapacity})</p>
                                    </div>
                                </div>

                                <div className="grid grid-cols-2 gap-4 pt-1">
                                    <div className="space-y-1">
                                        <label className="text-xs font-medium text-gray-700 flex items-center gap-1.5">
                                            <Clock className="w-3.5 h-3.5 text-amber-500" /> Handover Grace (s)
                                        </label>
                                        <input
                                            type="number"
                                            min={5}
                                            max={1800}
                                            value={handoverThresholdSeconds}
                                            onChange={e => {
                                                const v = e.target.value;
                                                if (v === '') {
                                                    setHandoverThresholdSeconds('');
                                                } else {
                                                    const n = parseInt(v, 10);
                                                    if (!isNaN(n)) setHandoverThresholdSeconds(n);
                                                }
                                            }}
                                            onBlur={() => {
                                                if (handoverThresholdSeconds === '' || Number(handoverThresholdSeconds) < 5) {
                                                    setHandoverThresholdSeconds(60);
                                                }
                                            }}
                                            className="w-full px-3 py-2 rounded-xl bg-white border border-gray-200 text-gray-900 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                                        />
                                        <p className="text-[11px] text-gray-500">Unstaffed alert threshold</p>
                                    </div>
                                    <div className="space-y-1">
                                        <label className="text-xs font-medium text-gray-700 flex items-center gap-1.5">
                                            <Clock className="w-3.5 h-3.5 text-blue-500" /> Max Relief Duration (s)
                                        </label>
                                        <input
                                            type="number"
                                            min={30}
                                            max={86400}
                                            value={maxReliefDurationSeconds}
                                            onChange={e => {
                                                const v = e.target.value;
                                                if (v === '') {
                                                    setMaxReliefDurationSeconds('');
                                                } else {
                                                    const n = parseInt(v, 10);
                                                    if (!isNaN(n)) setMaxReliefDurationSeconds(n);
                                                }
                                            }}
                                            onBlur={() => {
                                                if (maxReliefDurationSeconds === '' || Number(maxReliefDurationSeconds) < 30) {
                                                    setMaxReliefDurationSeconds(900);
                                                }
                                            }}
                                            className="w-full px-3 py-2 rounded-xl bg-white border border-gray-200 text-gray-900 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                                        />
                                        <p className="text-[11px] text-gray-500">{Math.round((Number(maxReliefDurationSeconds) || 900) / 60)} mins max relief</p>
                                    </div>
                                </div>
                            </div>

                            {/* Operating Shifts & Timing Windows (Multi-Shift & Overlap Guard) */}
                            <div className="p-5 rounded-2xl bg-gray-50/70 border border-gray-200 space-y-3">
                                <div className="flex items-center justify-between">
                                    <div className="flex items-center gap-2 text-blue-700 font-semibold text-xs uppercase tracking-wider">
                                        <Clock className="w-4 h-4" /> Operating Shifts & Timing Windows
                                    </div>
                                    <button
                                        type="button"
                                        onClick={handleAddTimingWindow}
                                        className="text-xs text-blue-700 hover:text-blue-900 font-semibold px-2.5 py-1 rounded-lg bg-blue-50 hover:bg-blue-100 border border-blue-200 transition flex items-center gap-1 shadow-2xs"
                                    >
                                        <Plus className="w-3.5 h-3.5" /> Add Window
                                    </button>
                                </div>

                                {overlapError && (
                                    <div className="p-3 rounded-xl bg-amber-50 border border-amber-200 text-amber-900 text-xs flex items-start gap-2 shadow-xs">
                                        <AlertCircle className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
                                        <div className="leading-relaxed">
                                            <strong className="font-bold">Window Overlap Conflict:</strong> {overlapError}
                                        </div>
                                    </div>
                                )}

                                {timingWindows.length === 0 ? (
                                    <div className="p-3.5 rounded-xl bg-white border border-gray-200/80 text-xs text-gray-500 space-y-1">
                                        <div className="font-semibold text-gray-700 flex items-center gap-1.5">
                                            <Calendar className="w-3.5 h-3.5 text-blue-500" /> Continuous 24/7 Operation
                                        </div>
                                        <p className="text-[11px] text-gray-400">
                                            No timing windows configured. Handover tracking and unattended alerts run 24/7. Click &quot;Add Window&quot; to limit monitoring to specific shifts and suppress off-duty alarms.
                                        </p>
                                    </div>
                                ) : (
                                    <div className="space-y-3 max-h-64 overflow-y-auto pr-1">
                                        {timingWindows.map((win, idx) => {
                                            const days = win.daysOfWeek || [0, 1, 2, 3, 4, 5, 6];
                                            const dayConfigs = [
                                                { d: 1, l: 'M' },
                                                { d: 2, l: 'T' },
                                                { d: 3, l: 'W' },
                                                { d: 4, l: 'T' },
                                                { d: 5, l: 'F' },
                                                { d: 6, l: 'S' },
                                                { d: 0, l: 'S' },
                                            ];

                                            return (
                                                <div
                                                    key={win.id || idx}
                                                    className="p-3 rounded-xl bg-white border border-gray-200 shadow-2xs space-y-2.5 transition hover:border-blue-300"
                                                >
                                                    <div className="flex items-center justify-between gap-2">
                                                        <input
                                                            type="text"
                                                            value={win.label}
                                                            placeholder={`Shift ${idx + 1}`}
                                                            onChange={e => handleUpdateTimingWindow(win.id, { label: e.target.value })}
                                                            className="text-xs font-bold text-gray-900 bg-transparent border-b border-gray-200 focus:border-blue-500 px-1 py-0.5 focus:outline-none w-36"
                                                        />
                                                        <div className="flex items-center gap-2">
                                                            <label className="text-[11px] font-medium text-gray-600 flex items-center gap-1.5 cursor-pointer">
                                                                <input
                                                                    type="checkbox"
                                                                    checked={win.isActive}
                                                                    onChange={e => handleUpdateTimingWindow(win.id, { isActive: e.target.checked })}
                                                                    className="rounded text-blue-600 focus:ring-blue-500"
                                                                />
                                                                Active
                                                            </label>
                                                            <button
                                                                type="button"
                                                                onClick={() => handleDeleteTimingWindow(win.id)}
                                                                title="Delete this timing window"
                                                                className="p-1 rounded-lg hover:bg-rose-50 text-gray-400 hover:text-rose-600 transition"
                                                            >
                                                                <Trash2 className="w-3.5 h-3.5" />
                                                            </button>
                                                        </div>
                                                    </div>

                                                    <div className="grid grid-cols-2 gap-2">
                                                        <div className="space-y-0.5">
                                                            <span className="text-[10px] font-semibold text-gray-500 uppercase">Start Time</span>
                                                            <input
                                                                type="time"
                                                                value={win.startTime}
                                                                onChange={e => handleUpdateTimingWindow(win.id, { startTime: e.target.value })}
                                                                className="w-full px-2 py-1 rounded-lg bg-gray-50 border border-gray-200 text-xs font-mono text-gray-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                            />
                                                        </div>
                                                        <div className="space-y-0.5">
                                                            <span className="text-[10px] font-semibold text-gray-500 uppercase">End Time</span>
                                                            <input
                                                                type="time"
                                                                value={win.endTime}
                                                                onChange={e => handleUpdateTimingWindow(win.id, { endTime: e.target.value })}
                                                                className="w-full px-2 py-1 rounded-lg bg-gray-50 border border-gray-200 text-xs font-mono text-gray-900 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                            />
                                                        </div>
                                                    </div>

                                                    <div className="space-y-1">
                                                        <span className="text-[10px] font-semibold text-gray-500 uppercase">Active Days</span>
                                                        <div className="flex items-center gap-1">
                                                            {dayConfigs.map(({ d, l }) => {
                                                                const isSelected = days.includes(d);
                                                                return (
                                                                    <button
                                                                        key={d}
                                                                        type="button"
                                                                        onClick={() => handleToggleDay(win.id, d)}
                                                                        className={`w-6 h-6 rounded-md text-[11px] font-bold transition flex items-center justify-center ${
                                                                            isSelected
                                                                                ? 'bg-blue-600 text-white shadow-2xs'
                                                                                : 'bg-gray-100 text-gray-400 hover:bg-gray-200 hover:text-gray-700'
                                                                        }`}
                                                                    >
                                                                        {l}
                                                                    </button>
                                                                );
                                                            })}
                                                        </div>
                                                    </div>

                                                    {/* Shift Crew Roster Quick Indicator */}
                                                    <div className="flex items-center justify-between pt-2 border-t border-gray-100 text-[11px]">
                                                        <div className="text-gray-500 flex items-center gap-1.5">
                                                            <Users className="w-3 h-3 text-blue-500" />
                                                            <span>
                                                                {((win.primaryEmployeeIds?.length || 0) + (win.relieverEmployeeIds?.length || 0)) === 0
                                                                    ? 'Inherits global pool'
                                                                    : `${win.primaryEmployeeIds?.length || 0} Primary, ${win.relieverEmployeeIds?.length || 0} Reliever`}
                                                            </span>
                                                        </div>
                                                        <button
                                                            type="button"
                                                            onClick={() => setActiveShiftRosterTab(win.id)}
                                                            className="text-blue-600 hover:text-blue-800 font-semibold hover:underline flex items-center gap-1"
                                                        >
                                                            {activeShiftRosterTab === win.id ? 'Configuring Below ↓' : 'Configure Shift Crew →'}
                                                        </button>
                                                    </div>
                                                </div>
                                            );
                                        })}
                                    </div>
                                )}
                            </div>

                            {/* Worker Assignments Pool (Supports rotating shifts across all timing windows) */}
                            <div className="p-5 rounded-2xl bg-gray-50/70 border border-gray-200 space-y-3">
                                <div className="flex items-center justify-between">
                                    <div className="flex items-center gap-2 text-blue-700 font-semibold text-xs uppercase tracking-wider">
                                        <Users className="w-4 h-4" /> Operator & Reliever Pool
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <span className={`px-2 py-0.5 rounded-lg text-[11px] font-semibold border ${
                                            activeSelectedPrimaryIds.length >= primaryCapacity
                                                ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
                                                : 'bg-amber-50 border-amber-200 text-amber-700'
                                        }`}>
                                            Primary Pool: {activeSelectedPrimaryIds.length} ({primaryCapacity} req on-duty)
                                        </span>
                                        <span className="px-2 py-0.5 rounded-lg text-[11px] font-semibold border bg-teal-50 border-teal-200 text-teal-700">
                                            Relievers: {activeSelectedRelieverIds.length}
                                        </span>
                                    </div>
                                </div>

                                {/* Shift Selector Tabs if shifts are configured */}
                                {timingWindows.length > 0 && (
                                    <div className="space-y-1.5 pb-2 border-b border-gray-200/70">
                                        <div className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider flex items-center justify-between">
                                            <span>Roster Scope</span>
                                            <span className="text-[10px] text-blue-600 font-medium">
                                                {currentActiveShift 
                                                    ? `Assigning for: ${currentActiveShift.label || 'Shift'} (${currentActiveShift.startTime} - ${currentActiveShift.endTime})` 
                                                    : 'Assigning for: All Shifts (Global Station Fallback Pool)'}
                                            </span>
                                        </div>
                                        <div className="flex items-center gap-1.5 p-1 bg-gray-200/60 rounded-xl overflow-x-auto text-xs">
                                            <button
                                                type="button"
                                                onClick={() => setActiveShiftRosterTab('all')}
                                                className={`px-3 py-1.5 rounded-lg font-semibold transition whitespace-nowrap ${
                                                    activeShiftRosterTab === 'all'
                                                        ? 'bg-white text-blue-700 shadow-xs'
                                                        : 'text-gray-600 hover:text-gray-900'
                                                }`}
                                            >
                                                All Shifts (Global Pool)
                                            </button>
                                            {timingWindows.map((win, idx) => {
                                                const isSelected = activeShiftRosterTab === win.id;
                                                const pCount = win.primaryEmployeeIds?.length || 0;
                                                const rCount = win.relieverEmployeeIds?.length || 0;
                                                const hasCustomCrew = pCount > 0 || rCount > 0;

                                                return (
                                                    <button
                                                        key={win.id || idx}
                                                        type="button"
                                                        onClick={() => setActiveShiftRosterTab(win.id)}
                                                        className={`px-3 py-1.5 rounded-lg font-semibold transition flex items-center gap-1.5 whitespace-nowrap ${
                                                            isSelected
                                                                ? 'bg-white text-blue-700 shadow-xs'
                                                                : 'text-gray-600 hover:text-gray-900'
                                                        }`}
                                                    >
                                                        <span>{win.label || `Shift ${idx + 1}`}</span>
                                                        <span className="text-[10px] text-gray-400 font-mono font-normal">
                                                            ({win.startTime || '00:00'} - {win.endTime || '00:00'})
                                                        </span>
                                                        {hasCustomCrew && (
                                                            <span className="px-1.5 py-0.2 rounded-full bg-blue-100 text-blue-700 text-[9px] font-bold">
                                                                {pCount}P/{rCount}R
                                                            </span>
                                                        )}
                                                    </button>
                                                );
                                            })}
                                        </div>
                                    </div>
                                )}

                                {activeSelectedPrimaryIds.length < primaryCapacity && (
                                    <div className="text-[11px] text-amber-700 bg-amber-50 px-3 py-1.5 rounded-xl border border-amber-200 flex items-center gap-1.5">
                                        <AlertCircle className="w-3.5 h-3.5 text-amber-600 shrink-0" />
                                        <span>Assign at least {primaryCapacity} primary operator(s) to staff the required shift capacity.</span>
                                    </div>
                                )}

                                <div className="relative">
                                    <Search className="w-3.5 h-3.5 text-gray-400 absolute left-3 top-2.5" />
                                    <input
                                        type="text"
                                        placeholder="Search employees by name or ID to assign to pool..."
                                        value={workerSearchTerm}
                                        onChange={e => setWorkerSearchTerm(e.target.value)}
                                        className="w-full pl-8 pr-3 py-1.5 rounded-xl bg-white border border-gray-200 text-xs text-gray-900 placeholder:text-gray-400 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                    />
                                </div>

                                <div className="max-h-52 overflow-y-auto space-y-2 pr-1">
                                    {filteredEmployees.length === 0 ? (
                                        <p className="text-xs text-gray-500 italic py-3 text-center">
                                            {employees.length === 0
                                                ? 'No employees registered yet. Go to Employee directory to register team members.'
                                                : 'No employees match the search filter.'}
                                        </p>
                                    ) : (
                                        filteredEmployees.map(emp => {
                                            const isPrimary = activeSelectedPrimaryIds.includes(emp.id);
                                            const isReliever = activeSelectedRelieverIds.includes(emp.id);

                                            return (
                                                <div
                                                    key={emp.id}
                                                    className={`flex items-center justify-between p-3 rounded-xl border text-xs transition ${
                                                        isPrimary 
                                                            ? 'bg-blue-50/50 border-blue-200' 
                                                            : isReliever 
                                                            ? 'bg-teal-50/50 border-teal-200' 
                                                            : 'bg-white border-gray-200 hover:border-gray-300'
                                                    }`}
                                                >
                                                    <div>
                                                        <span className="font-semibold text-gray-900">
                                                            {emp.firstName} {emp.lastName}
                                                        </span>
                                                        <span className="text-gray-500 text-[11px] ml-2 font-mono">
                                                            ({emp.employeeId || 'ID'})
                                                        </span>
                                                        {isPrimary && (
                                                            <span className="ml-2 text-[10px] font-semibold text-blue-600 bg-blue-100/60 px-1.5 py-0.5 rounded-md">
                                                                Assigned Primary
                                                            </span>
                                                        )}
                                                        {isReliever && (
                                                            <span className="ml-2 text-[10px] font-semibold text-teal-600 bg-teal-100/60 px-1.5 py-0.5 rounded-md">
                                                                Assigned Reliever
                                                            </span>
                                                        )}
                                                    </div>
                                                    <div className="flex items-center gap-1.5">
                                                        <button
                                                            type="button"
                                                            onClick={() => togglePrimaryEmployee(emp.id)}
                                                            className={`px-3 py-1.5 rounded-lg font-medium transition flex items-center gap-1 ${
                                                                isPrimary
                                                                    ? 'bg-blue-600 text-white font-semibold shadow-xs'
                                                                    : 'bg-gray-100 text-gray-600 hover:bg-blue-50 hover:text-blue-700'
                                                            }`}
                                                        >
                                                            {isPrimary && <Check className="w-3 h-3" />}
                                                            Primary
                                                        </button>
                                                        <button
                                                            type="button"
                                                            onClick={() => toggleRelieverEmployee(emp.id)}
                                                            className={`px-3 py-1.5 rounded-lg font-medium transition flex items-center gap-1 ${
                                                                isReliever
                                                                    ? 'bg-teal-600 text-white font-semibold shadow-xs'
                                                                    : 'bg-gray-100 text-gray-600 hover:bg-teal-50 hover:text-teal-700'
                                                            }`}
                                                        >
                                                            {isReliever && <Check className="w-3 h-3" />}
                                                            Reliever
                                                        </button>
                                                    </div>
                                                </div>
                                            );
                                        })
                                    )}
                                </div>
                            </div>
                        </div>

                        {/* Right Column: Interactive Polygon ROI Geofence & Camera Backdrop */}
                        <div className="lg:col-span-6 space-y-4">
                            <div className="flex items-center justify-between">
                                <label className="text-xs font-semibold uppercase tracking-wider text-gray-700 flex items-center gap-2">
                                    <Crosshair className="w-4 h-4 text-blue-600" /> Workstation Geofence Polygon
                                </label>
                                <div className="flex items-center gap-2">
                                    <span className="text-xs text-blue-700 font-mono font-semibold">
                                        {polygonPoints.length} vertices
                                    </span>
                                    <button
                                        type="button"
                                        onClick={handleSetEntireFrame}
                                        title="Cover 100% entire camera frame"
                                        className="text-xs text-blue-700 hover:text-blue-900 px-2.5 py-1 rounded-lg bg-blue-50 hover:bg-blue-100 border border-blue-200 font-semibold transition flex items-center gap-1"
                                    >
                                        <Maximize2 className="w-3 h-3 text-blue-600" /> Entire Frame
                                    </button>
                                    <button
                                        type="button"
                                        onClick={handleResetPolygon}
                                        className="text-xs text-gray-600 hover:text-gray-900 px-2.5 py-1 rounded-lg bg-gray-100 hover:bg-gray-200 border border-gray-200 font-medium transition flex items-center gap-1"
                                    >
                                        <RefreshCw className="w-3 h-3" /> Reset Box
                                    </button>
                                    <button
                                        type="button"
                                        onClick={handleUndoPoint}
                                        disabled={polygonPoints.length === 0}
                                        className="text-xs text-gray-600 hover:text-gray-900 px-2.5 py-1 rounded-lg bg-gray-100 hover:bg-gray-200 border border-gray-200 font-medium transition disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1"
                                    >
                                        <Undo2 className="w-3 h-3" /> Undo
                                    </button>
                                    <button
                                        type="button"
                                        onClick={handleClearPolygon}
                                        disabled={polygonPoints.length === 0}
                                        className="text-xs text-rose-600 hover:text-rose-700 px-2 py-1 rounded-lg bg-rose-50 hover:bg-rose-100 border border-rose-200 font-medium transition disabled:opacity-40"
                                    >
                                        <Trash2 className="w-3 h-3" />
                                    </button>
                                </div>
                            </div>

                            {/* Viewport Canvas with Snapshot/Live Backdrop */}
                            <div className="relative aspect-video rounded-2xl overflow-hidden bg-slate-950 border border-gray-200 shadow-inner group select-none">
                                {/* Historical or Custom Image Backdrop */}
                                {activeFrameImage && !showLiveFeed && (
                                    <img
                                        src={activeFrameImage}
                                        alt="Camera Viewport Frame"
                                        className="absolute inset-0 w-full h-full object-cover pointer-events-none select-none opacity-90"
                                        draggable={false}
                                    />
                                )}

                                {/* Fallback Background Grid when no image */}
                                {(!activeFrameImage || showLiveFeed) && (
                                    <div className="absolute inset-0 bg-[radial-gradient(#334155_1px,transparent_1px)] [background-size:16px_16px] opacity-40 pointer-events-none" />
                                )}

                                {/* Live WebRTC Feed Overlay (if toggled) */}
                                {showLiveFeed && selectedCamera?.whepUrl && (
                                    <div ref={liveVideoRef} className="absolute inset-0 w-full h-full bg-black z-0">
                                        {React.createElement('whep-video', {
                                            src: selectedCamera.whepUrl,
                                            autoplay: 'true',
                                            muted: 'true',
                                            playsinline: 'true',
                                            crossorigin: 'anonymous',
                                            style: { width: '100%', height: '100%', objectFit: 'cover' }
                                        })}
                                    </div>
                                )}

                                {/* Camera info badge & backdrop selector */}
                                <div className="absolute top-3 left-3 z-10 flex items-center gap-2">
                                    <div className="flex items-center gap-1.5 px-3 py-1 rounded-full bg-slate-900/80 backdrop-blur-md border border-slate-700/60 text-[11px] text-white">
                                        <CameraIcon className="w-3 h-3 text-blue-400" />
                                        <span>{selectedCamera ? selectedCamera.name : 'Select a camera'}</span>
                                    </div>
                                    {customFrameUrl && (
                                        <span className="px-2.5 py-0.5 rounded-full bg-blue-500/80 backdrop-blur-md text-[10px] font-medium text-white">
                                            Custom Frame
                                        </span>
                                    )}
                                    {!customFrameUrl && historicalFrameUrl && (
                                        <span className="px-2.5 py-0.5 rounded-full bg-emerald-500/80 backdrop-blur-md text-[10px] font-medium text-white">
                                            Historical Frame
                                        </span>
                                    )}
                                </div>

                                {/* Viewport Toolbar (Upload Snapshot / Live Capture / Reset) */}
                                <div className="absolute top-3 right-3 z-10 flex items-center gap-1.5">
                                    <input
                                        ref={fileInputRef}
                                        type="file"
                                        accept="image/*"
                                        className="hidden"
                                        onChange={handleFileUpload}
                                    />
                                    <button
                                        type="button"
                                        onClick={() => fileInputRef.current?.click()}
                                        title="Upload camera snapshot or floor plan from disk"
                                        className="px-2.5 py-1 rounded-lg bg-slate-900/80 hover:bg-slate-800 backdrop-blur-md border border-slate-700/60 text-[11px] text-slate-200 hover:text-white transition flex items-center gap-1"
                                    >
                                        <Upload className="w-3 h-3 text-blue-400" />
                                        <span>Upload Frame</span>
                                    </button>

                                    {selectedCamera?.whepUrl && (
                                        <button
                                            type="button"
                                            onClick={() => {
                                                if (showLiveFeed) {
                                                    handleCaptureLive();
                                                } else {
                                                    setShowLiveFeed(true);
                                                }
                                            }}
                                            className={`px-2.5 py-1 rounded-lg backdrop-blur-md border text-[11px] font-medium transition flex items-center gap-1 ${
                                                showLiveFeed
                                                    ? 'bg-emerald-600 hover:bg-emerald-700 border-emerald-500 text-white animate-pulse'
                                                    : 'bg-slate-900/80 hover:bg-slate-800 border-slate-700/60 text-slate-200'
                                            }`}
                                        >
                                            <CameraIcon className="w-3 h-3 text-emerald-400" />
                                            <span>{showLiveFeed ? 'Snap This Frame' : 'Live Stream'}</span>
                                        </button>
                                    )}

                                    {activeFrameImage && (
                                        <button
                                            type="button"
                                            onClick={() => {
                                                setCustomFrameUrl(null);
                                                setHistoricalFrameUrl(null);
                                            }}
                                            title="Clear frame backdrop"
                                            className="p-1 rounded-lg bg-slate-900/80 hover:bg-slate-800 backdrop-blur-md border border-slate-700/60 text-slate-400 hover:text-slate-200"
                                        >
                                            <X className="w-3.5 h-3.5" />
                                        </button>
                                    )}
                                </div>

                                {/* No Frame / Guide Notice when canvas is blank */}
                                {!activeFrameImage && !showLiveFeed && (
                                    <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none p-6 text-center">
                                        <div className="w-12 h-12 rounded-2xl bg-slate-800/80 border border-slate-700/60 flex items-center justify-center text-slate-400 mb-2">
                                            <ImageIcon className="w-6 h-6 text-blue-400" />
                                        </div>
                                        <p className="text-xs font-semibold text-slate-200">
                                            {selectedCamera ? `${selectedCamera.name} Canvas` : 'No Camera Selected'}
                                        </p>
                                        <p className="text-[11px] text-slate-400 max-w-xs mt-1">
                                            Click &quot;Upload Frame&quot; above to trace on a snapshot, or drag vertex points directly on this grid.
                                        </p>
                                    </div>
                                )}

                                {/* Helper overlay tooltip */}
                                <div className="absolute bottom-3 left-3 z-10 px-2.5 py-1 rounded-lg bg-slate-900/80 backdrop-blur-md border border-slate-700/60 text-[10px] text-slate-300 pointer-events-none">
                                    Click vertex dot for delete option · Drag to move · Click &quot;Entire Frame&quot; for 100% view
                                </div>

                                {/* Floating Delete Popover on Selected Vertex */}
                                {selectedVertexIdx !== null && polygonPoints[selectedVertexIdx] && (
                                    <div 
                                        className="absolute z-30 transform -translate-x-1/2 -translate-y-full mb-3 pointer-events-auto"
                                        style={{
                                            left: `${Math.max(12, Math.min(88, polygonPoints[selectedVertexIdx][0] * 100))}%`,
                                            top: `${Math.max(14, polygonPoints[selectedVertexIdx][1] * 100)}%`
                                        }}
                                    >
                                        <div className="bg-slate-900/95 backdrop-blur-md border border-slate-700 text-white rounded-xl shadow-2xl p-1.5 flex items-center gap-2 text-xs animate-in zoom-in-95 duration-100">
                                            <div className="flex items-center gap-1.5 pl-1.5 pr-2 border-r border-slate-700">
                                                <span className="w-2 h-2 rounded-full bg-rose-500 animate-pulse" />
                                                <span className="font-semibold text-slate-200">
                                                    Vertex P{selectedVertexIdx + 1}
                                                </span>
                                            </div>
                                            <button
                                                type="button"
                                                onClick={(e) => {
                                                    e.stopPropagation();
                                                    handleRemovePoint(selectedVertexIdx);
                                                }}
                                                className="px-2.5 py-1 rounded-lg bg-rose-600 hover:bg-rose-700 text-white font-semibold flex items-center gap-1.5 shadow-sm transition"
                                            >
                                                <Trash2 className="w-3.5 h-3.5" />
                                                <span>Delete</span>
                                            </button>
                                            <button
                                                type="button"
                                                onClick={(e) => {
                                                    e.stopPropagation();
                                                    setSelectedVertexIdx(null);
                                                }}
                                                className="p-1 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition"
                                                title="Cancel"
                                            >
                                                <X className="w-3.5 h-3.5" />
                                            </button>
                                        </div>
                                    </div>
                                )}

                                {/* Interactive SVG Overlay (Vibration-free Dragging) */}
                                <svg
                                    ref={svgRef}
                                    onClick={handleSvgClick}
                                    viewBox="0 0 100 100"
                                    preserveAspectRatio="none"
                                    className="absolute inset-0 w-full h-full cursor-crosshair z-20"
                                >
                                    {/* Shaded polygon zone */}
                                    {polygonPoints.length >= 3 && (
                                        <polygon
                                            points={polygonSvgPoints}
                                            fill="rgba(59, 130, 246, 0.25)"
                                            stroke="#3b82f6"
                                            strokeWidth="0.8"
                                            strokeDasharray="2,1"
                                        />
                                    )}

                                    {/* Lines between points if fewer than 3 */}
                                    {polygonPoints.length === 2 && (
                                        <line
                                            x1={polygonPoints[0][0] * 100}
                                            y1={polygonPoints[0][1] * 100}
                                            x2={polygonPoints[1][0] * 100}
                                            y2={polygonPoints[1][1] * 100}
                                            stroke="#3b82f6"
                                            strokeWidth="0.8"
                                            strokeDasharray="2,1"
                                        />
                                    )}

                                    {/* Draggable Vertex Circles with Selection Ring */}
                                    {polygonPoints.map(([x, y], idx) => {
                                        const isBeingDragged = dragIdx === idx;
                                        const isSelected = selectedVertexIdx === idx;
                                        return (
                                            <g key={idx}>
                                                {/* Selected Halo Ring */}
                                                {isSelected && (
                                                    <circle
                                                        cx={x * 100}
                                                        cy={y * 100}
                                                        r={4.5}
                                                        fill="none"
                                                        stroke="#f43f5e"
                                                        strokeWidth={0.8}
                                                        strokeDasharray="1,1"
                                                    />
                                                )}
                                                <circle
                                                    cx={x * 100}
                                                    cy={y * 100}
                                                    r={isBeingDragged ? 2.8 : isSelected ? 3.0 : 2.2}
                                                    fill={isSelected ? '#f43f5e' : isBeingDragged ? '#60a5fa' : '#ffffff'}
                                                    stroke={isSelected ? '#ffffff' : '#2563eb'}
                                                    strokeWidth={0.9}
                                                    className="cursor-pointer"
                                                    onPointerDown={e => handleVertexPointerDown(idx, e)}
                                                    onContextMenu={e => {
                                                        e.preventDefault();
                                                        handleRemovePoint(idx, e);
                                                    }}
                                                />
                                                <text
                                                    x={x * 100 + 3}
                                                    y={y * 100 - 3}
                                                    fill={isSelected ? '#fda4af' : '#bfdbfe'}
                                                    fontSize="3"
                                                    fontWeight="bold"
                                                    pointerEvents="none"
                                                >
                                                    P{idx + 1}
                                                </text>
                                            </g>
                                        );
                                    })}
                                </svg>
                            </div>

                            {captureError && (
                                <div className="p-2.5 rounded-xl bg-rose-50 border border-rose-200 text-rose-700 text-xs flex items-center gap-2">
                                    <AlertCircle className="w-4 h-4 flex-shrink-0" />
                                    <span>{captureError}</span>
                                </div>
                            )}

                            {/* Polygon Coordinates Snippet with Selected Vertex Actions */}
                            <div className="p-3.5 rounded-xl bg-gray-50 border border-gray-200 text-[11px] font-mono text-gray-600 space-y-2">
                                <div className="flex justify-between text-gray-500">
                                    <span>Normalized Coordinates (0.00 - 1.00)</span>
                                    <span className={polygonPoints.length >= 3 ? 'text-emerald-600 font-semibold' : 'text-amber-600'}>
                                        {polygonPoints.length >= 3 ? `${polygonPoints.length} Vertices · Valid Geofence` : 'Incomplete (Min 3 Points)'}
                                    </span>
                                </div>
                                <div className="truncate text-blue-700 font-semibold">
                                    {JSON.stringify(polygonPoints)}
                                </div>

                                {selectedVertexIdx !== null && polygonPoints[selectedVertexIdx] && (
                                    <div className="flex items-center justify-between pt-2 border-t border-gray-200 text-xs font-sans">
                                        <span className="text-gray-700 font-medium flex items-center gap-1.5">
                                            <span className="w-2 h-2 rounded-full bg-rose-500" />
                                            Selected: <strong className="text-gray-900">Vertex P{selectedVertexIdx + 1}</strong> ({polygonPoints[selectedVertexIdx][0].toFixed(3)}, {polygonPoints[selectedVertexIdx][1].toFixed(3)})
                                        </span>
                                        <div className="flex items-center gap-2">
                                            <button
                                                type="button"
                                                onClick={() => setSelectedVertexIdx(null)}
                                                className="px-2 py-1 text-gray-500 hover:text-gray-700 text-xs font-medium"
                                            >
                                                Deselect
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => handleRemovePoint(selectedVertexIdx)}
                                                className="px-2.5 py-1 rounded-lg bg-rose-50 hover:bg-rose-100 border border-rose-200 text-rose-700 font-semibold text-xs flex items-center gap-1 transition"
                                            >
                                                <Trash2 className="w-3.5 h-3.5" /> Delete Vertex
                                            </button>
                                        </div>
                                    </div>
                                )}
                            </div>
                        </div>
                    </div>

                    {/* Footer Actions */}
                    <div className="pt-5 border-t border-gray-100 flex items-center justify-between">
                        <label className="flex items-center gap-2.5 text-xs text-gray-700 cursor-pointer">
                            <input
                                type="checkbox"
                                checked={isActive}
                                onChange={e => setIsActive(e.target.checked)}
                                className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                            />
                            <span>Workstation is Active (Enabled for Live Vision Monitoring)</span>
                        </label>

                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                onClick={onClose}
                                className="px-5 py-2.5 rounded-xl bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium text-xs transition"
                            >
                                Cancel
                            </button>
                            <button
                                type="submit"
                                disabled={isSubmitting}
                                className="px-6 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs shadow-sm hover:shadow-md flex items-center gap-2 transition disabled:opacity-50"
                            >
                                {isSubmitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                                {isEdit ? 'Save Workstation Changes' : 'Create Workstation'}
                            </button>
                        </div>
                    </div>
                </form>
            </div>
        </div>
    );
}
