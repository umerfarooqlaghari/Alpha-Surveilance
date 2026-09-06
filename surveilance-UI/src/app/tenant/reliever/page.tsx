'use client';

import React, { useEffect, useState, useMemo } from 'react';
import { 
    Users, 
    Clock, 
    Shield, 
    AlertTriangle, 
    CheckCircle2, 
    Layers, 
    Activity, 
    BarChart3, 
    RefreshCw, 
    Plus, 
    Camera, 
    Search, 
    Filter, 
    Timer, 
    ArrowUpRight, 
    FileSpreadsheet, 
    Eye, 
    Edit2, 
    Trash2, 
    ChevronRight, 
    Play, 
    Sliders,
    Zap
} from 'lucide-react';
import { 
    getWorkstations, 
    getWorkstationById,
    getLiveBoard, 
    getReliefSessions, 
    getReliefAnalytics, 
    deleteWorkstation, 
    WorkstationResponse, 
    WorkstationLiveBoardItem, 
    ReliefSessionResponse, 
    ReliefAnalyticsSummary 
} from '@/lib/api/tenant/reliever';
import WorkstationFormModal from './components/WorkstationFormModal';

export default function RelieverSystemPage() {
    const [activeTab, setActiveTab] = useState<'live' | 'analytics' | 'workstations' | 'history'>('live');

    // Data States
    const [workstations, setWorkstations] = useState<WorkstationResponse[]>([]);
    const [liveBoard, setLiveBoard] = useState<WorkstationLiveBoardItem[]>([]);
    const [sessions, setSessions] = useState<ReliefSessionResponse[]>([]);
    const [analytics, setAnalytics] = useState<ReliefAnalyticsSummary | null>(null);

    const [isLoading, setIsLoading] = useState(true);
    const [isRefreshing, setIsRefreshing] = useState(false);

    // Modal State
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingWorkstation, setEditingWorkstation] = useState<WorkstationResponse | null>(null);

    // Filters
    const [searchTerm, setSearchTerm] = useState('');
    const [statusFilter, setStatusFilter] = useState('ALL');
    const [historyDate, setHistoryDate] = useState<string>(new Date().toISOString().split('T')[0]);

    const fetchData = async (silent = false) => {
        if (!silent) setIsLoading(true);
        else setIsRefreshing(true);

        try {
            const [wsData, boardData, sessData, anaData] = await Promise.all([
                getWorkstations(),
                getLiveBoard(),
                getReliefSessions({ startDate: historyDate, endDate: historyDate }),
                getReliefAnalytics()
            ]);

            setWorkstations(wsData);
            setLiveBoard(boardData);
            setSessions(sessData);
            setAnalytics(anaData);
        } catch (err) {
            console.error('Failed to fetch reliever data:', err);
        } finally {
            setIsLoading(false);
            setIsRefreshing(false);
        }
    };

    useEffect(() => {
        fetchData();
        // Auto-refresh live board every 10 seconds
        const interval = setInterval(() => {
            getLiveBoard().then(data => setLiveBoard(data)).catch(() => {});
        }, 10000);
        return () => clearInterval(interval);
    }, [historyDate]);

    const handleDeleteWorkstation = async (id: string) => {
        if (!confirm('Are you sure you want to delete this workstation? Active monitoring will be stopped.')) return;
        try {
            await deleteWorkstation(id);
            fetchData(true);
        } catch (err) {
            alert('Failed to delete workstation');
        }
    };

    const handleOpenEdit = async (ws: WorkstationResponse | string) => {
        const id = typeof ws === 'string' ? ws : ws.id;
        const initialWs = typeof ws === 'string' ? workstations.find(w => w.id === id) || null : ws;
        setEditingWorkstation(initialWs);
        setIsModalOpen(true);
        try {
            const detail = await getWorkstationById(id);
            if (detail) {
                setEditingWorkstation(detail);
            }
        } catch (err) {
            console.error('Failed to load workstation details:', err);
        }
    };

    const handleOpenCreate = () => {
        setEditingWorkstation(null);
        setIsModalOpen(true);
    };

    // Live Board status aggregations
    const liveStats = useMemo(() => {
        const total = liveBoard.length;
        const staffed = liveBoard.filter(w => w.status === 'Staffed').length;
        const underRelief = liveBoard.filter(w => w.status === 'UnderRelief').length;
        const pendingHandover = liveBoard.filter(w => w.status === 'PendingHandover').length;
        const violations = liveBoard.filter(w => w.status === 'UnattendedViolation').length;
        return { total, staffed, underRelief, pendingHandover, violations };
    }, [liveBoard]);

    const workstationMap = useMemo(() => {
        return new Map(workstations.map(w => [w.id, w]));
    }, [workstations]);

    const getWorkstationShiftStatus = (operatingScheduleJson?: string) => {
        if (!operatingScheduleJson) {
            return { isContinuous: true, isActive: true, label: '24/7 Continuous' };
        }
        try {
            const windows = JSON.parse(operatingScheduleJson);
            if (!Array.isArray(windows) || windows.length === 0) {
                return { isContinuous: true, isActive: true, label: '24/7 Continuous' };
            }

            const now = new Date();
            const currentDay = now.getDay();
            const currentMinutes = now.getHours() * 60 + now.getMinutes();

            for (const win of windows) {
                if (!win.isActive) continue;
                if (Array.isArray(win.daysOfWeek) && win.daysOfWeek.length > 0 && !win.daysOfWeek.includes(currentDay)) {
                    continue;
                }
                if (!win.startTime || !win.endTime) continue;
                const [sH, sM] = win.startTime.split(':').map(Number);
                const [eH, eM] = win.endTime.split(':').map(Number);
                const startTotal = sH * 60 + sM;
                const endTotal = eH * 60 + eM;

                const isMatch = startTotal <= endTotal
                    ? (currentMinutes >= startTotal && currentMinutes <= endTotal)
                    : (currentMinutes >= startTotal || currentMinutes <= endTotal);

                if (isMatch) {
                    return {
                        isContinuous: false,
                        isActive: true,
                        label: win.label || 'Active Shift',
                        time: `${win.startTime} - ${win.endTime}`
                    };
                }
            }

            return {
                isContinuous: false,
                isActive: false,
                label: 'Off-Duty (Outside Shift)'
            };
        } catch {
            return { isContinuous: true, isActive: true, label: '24/7 Continuous' };
        }
    };

    const formatSeconds = (sec?: number) => {
        if (!sec || sec <= 0) return '0s';
        const mins = Math.floor(sec / 60);
        const remaining = sec % 60;
        if (mins === 0) return `${remaining}s`;
        return `${mins}m ${remaining}s`;
    };

    const exportSessionsCsv = () => {
        if (sessions.length === 0) return;
        const headers = ['Workstation', 'Camera', 'Primary Worker', 'Reliever', 'Primary Left', 'Reliever Arrived', 'Handover Latency (s)', 'Relief Duration (s)', 'Status'];
        const rows = sessions.map(s => [
            s.workstationName,
            s.cameraName,
            s.primaryEmployeeName || s.primaryEmployeeExternalId || 'N/A',
            s.relieverEmployeeName || s.relieverEmployeeExternalId || 'N/A',
            new Date(s.primaryLeftAt).toLocaleTimeString(),
            s.relieverArrivedAt ? new Date(s.relieverArrivedAt).toLocaleTimeString() : 'N/A',
            s.handoverLatencySeconds ?? 'N/A',
            s.reliefDurationSeconds ?? 'N/A',
            s.status
        ]);
        const csvContent = 'data:text/csv;charset=utf-8,' + [headers.join(','), ...rows.map(e => e.join(','))].join('\n');
        const encodedUri = encodeURI(csvContent);
        const link = document.createElement('a');
        link.setAttribute('href', encodedUri);
        link.setAttribute('download', `relief_sessions_${historyDate}.csv`);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };

    return (
        <div className="p-8 space-y-8 max-w-[1650px] mx-auto min-h-screen text-gray-900">
            {/* Header Banner */}
            <div className="bg-white rounded-3xl p-6 sm:p-8 border border-gray-100 shadow-sm flex flex-col md:flex-row md:items-center md:justify-between gap-6">
                <div className="space-y-2">
                    <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-blue-50 text-blue-700 text-xs font-semibold uppercase tracking-wider border border-blue-200/60">
                        <Users className="w-3.5 h-3.5" /> Factory Production Line Safeguard
                    </div>
                    <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight text-gray-900">Factory Worker Reliever System</h1>
                    <p className="text-gray-500 text-sm max-w-2xl">
                        Monitor workstation geofences in real-time, enforce primary operator presence, track reliever takeover latency, and prevent unmanned line downtime violations.
                    </p>
                </div>

                <div className="flex items-center gap-3">
                    <button
                        onClick={() => fetchData(true)}
                        disabled={isRefreshing}
                        className="px-4 py-2.5 rounded-2xl bg-gray-50 hover:bg-gray-100 text-gray-700 border border-gray-200 font-semibold text-xs flex items-center gap-2 transition shadow-xs"
                    >
                        <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
                        Refresh Board
                    </button>
                    {workstations.length > 0 && (
                        <select
                            onChange={(e) => {
                                if (e.target.value) {
                                    handleOpenEdit(e.target.value);
                                    e.target.value = '';
                                }
                            }}
                            defaultValue=""
                            className="px-3.5 py-2.5 rounded-2xl bg-gray-50 hover:bg-gray-100 text-gray-700 border border-gray-200 font-semibold text-xs transition cursor-pointer focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                        >
                            <option value="" disabled>Edit Workstation...</option>
                            {workstations.map(w => (
                                <option key={w.id} value={w.id}>
                                    Edit: {w.code} - {w.name}
                                </option>
                            ))}
                        </select>
                    )}
                    <button
                        onClick={handleOpenCreate}
                        className="px-5 py-2.5 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs shadow-sm hover:shadow-md flex items-center gap-2 transition"
                    >
                        <Plus className="w-4 h-4" /> Add Workstation
                    </button>
                </div>
            </div>

            {/* Quick Metrics KPI Bar */}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
                <div className="bg-white border border-gray-100 p-5 rounded-2xl shadow-sm space-y-1 hover:shadow-md transition-shadow">
                    <div className="flex items-center justify-between text-xs text-gray-500 font-medium">
                        <span>Total Workstations</span>
                        <Layers className="w-4 h-4 text-blue-600" />
                    </div>
                    <div className="text-2xl font-bold text-gray-900">{liveStats.total}</div>
                    <div className="text-[11px] text-gray-400">Monitored Line Stations</div>
                </div>

                <div className="bg-white border border-gray-100 p-5 rounded-2xl shadow-sm space-y-1 hover:shadow-md transition-shadow">
                    <div className="flex items-center justify-between text-xs text-emerald-600 font-medium">
                        <span>Staffed / Normal</span>
                        <CheckCircle2 className="w-4 h-4 text-emerald-600" />
                    </div>
                    <div className="text-2xl font-bold text-emerald-600">{liveStats.staffed}</div>
                    <div className="text-[11px] text-gray-400">Primary operators present</div>
                </div>

                <div className="bg-white border border-gray-100 p-5 rounded-2xl shadow-sm space-y-1 hover:shadow-md transition-shadow">
                    <div className="flex items-center justify-between text-xs text-blue-600 font-medium">
                        <span>Under Relief</span>
                        <Activity className="w-4 h-4 text-blue-600" />
                    </div>
                    <div className="text-2xl font-bold text-blue-600">{liveStats.underRelief}</div>
                    <div className="text-[11px] text-gray-400">Relievers on active duty</div>
                </div>

                <div className="bg-white border border-gray-100 p-5 rounded-2xl shadow-sm space-y-1 hover:shadow-md transition-shadow">
                    <div className="flex items-center justify-between text-xs text-amber-600 font-medium">
                        <span>Pending Handover</span>
                        <Timer className="w-4 h-4 text-amber-600" />
                    </div>
                    <div className="text-2xl font-bold text-amber-600">{liveStats.pendingHandover}</div>
                    <div className="text-[11px] text-gray-400">Grace timers ticking</div>
                </div>

                <div className="bg-white border border-gray-100 p-5 rounded-2xl shadow-sm space-y-1 hover:shadow-md transition-shadow">
                    <div className="flex items-center justify-between text-xs text-rose-600 font-medium">
                        <span>Unattended Alerts</span>
                        <AlertTriangle className="w-4 h-4 text-rose-600" />
                    </div>
                    <div className="text-2xl font-bold text-rose-600">{liveStats.violations}</div>
                    <div className="text-[11px] text-gray-400">Threshold expired</div>
                </div>
            </div>

            {/* Tab Navigation */}
            <div className="flex items-center gap-1.5 p-1.5 bg-gray-100/80 rounded-2xl border border-gray-200/60 overflow-x-auto">
                <button
                    onClick={() => setActiveTab('live')}
                    className={`px-4 py-2.5 rounded-xl font-semibold text-xs transition flex items-center gap-2 ${
                        activeTab === 'live'
                            ? 'bg-white text-gray-900 shadow-sm'
                            : 'text-gray-500 hover:text-gray-900'
                    }`}
                >
                    <Activity className="w-4 h-4 text-blue-600" /> Live Workstation Board ({liveBoard.length})
                </button>
                <button
                    onClick={() => setActiveTab('analytics')}
                    className={`px-4 py-2.5 rounded-xl font-semibold text-xs transition flex items-center gap-2 ${
                        activeTab === 'analytics'
                            ? 'bg-white text-gray-900 shadow-sm'
                            : 'text-gray-500 hover:text-gray-900'
                    }`}
                >
                    <BarChart3 className="w-4 h-4 text-blue-600" /> Reliever Analytics & KPIs
                </button>
                <button
                    onClick={() => setActiveTab('workstations')}
                    className={`px-4 py-2.5 rounded-xl font-semibold text-xs transition flex items-center gap-2 ${
                        activeTab === 'workstations'
                            ? 'bg-white text-gray-900 shadow-sm'
                            : 'text-gray-500 hover:text-gray-900'
                    }`}
                >
                    <Sliders className="w-4 h-4 text-blue-600" /> Workstations & Geofences ({workstations.length})
                </button>
                <button
                    onClick={() => setActiveTab('history')}
                    className={`px-4 py-2.5 rounded-xl font-semibold text-xs transition flex items-center gap-2 ${
                        activeTab === 'history'
                            ? 'bg-white text-gray-900 shadow-sm'
                            : 'text-gray-500 hover:text-gray-900'
                    }`}
                >
                    <Clock className="w-4 h-4 text-blue-600" /> Session Audit Log ({sessions.length})
                </button>
            </div>

            {/* TAB 1: LIVE WORKSTATION BOARD */}
            {activeTab === 'live' && (
                <div className="space-y-6">
                    {liveBoard.length === 0 ? (
                        <div className="text-center py-20 bg-white rounded-3xl border border-gray-100 shadow-sm space-y-3">
                            <Layers className="w-12 h-12 mx-auto text-gray-300" />
                            <h3 className="text-lg font-bold text-gray-800">No Workstations Configured Yet</h3>
                            <p className="text-xs text-gray-500 max-w-sm mx-auto">
                                Click &quot;Add Workstation&quot; above to draw your first workstation polygon geofence on a camera feed.
                            </p>
                            <button
                                onClick={handleOpenCreate}
                                className="px-5 py-2.5 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs shadow-sm hover:shadow-md transition"
                            >
                                Setup Workstation
                            </button>
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                            {liveBoard.map(station => {
                                const isStaffed = station.status === 'Staffed';
                                const isRelieved = station.status === 'UnderRelief';
                                const isPending = station.status === 'PendingHandover';
                                const isViolation = station.status === 'UnattendedViolation';

                                return (
                                    <div
                                        key={station.workstationId}
                                        className={`rounded-3xl border p-6 transition shadow-sm hover:shadow-md relative overflow-hidden flex flex-col justify-between ${
                                            isViolation
                                                ? 'bg-rose-50/50 border-rose-200'
                                                : isPending
                                                ? 'bg-amber-50/50 border-amber-200'
                                                : isRelieved
                                                ? 'bg-blue-50/50 border-blue-200'
                                                : 'bg-white border-gray-200'
                                        }`}
                                    >
                                        <div className="space-y-4">
                                            {/* Station Header */}
                                            <div className="flex items-start justify-between">
                                                <div>
                                                    <div className="flex items-center gap-2 flex-wrap">
                                                        <span className="px-2.5 py-0.5 rounded-lg bg-gray-100 text-blue-700 font-mono text-xs font-bold uppercase">
                                                            {station.code}
                                                        </span>
                                                        <span className="text-xs text-gray-500 flex items-center gap-1">
                                                            <Camera className="w-3 h-3" /> {station.cameraName}
                                                        </span>
                                                    </div>
                                                    <h3 className="text-lg font-bold text-gray-900 mt-1">
                                                        {station.name}
                                                    </h3>
                                                    <div className="mt-1">
                                                        {(() => {
                                                            const wsData = workstationMap.get(station.workstationId);
                                                            const shiftStatus = getWorkstationShiftStatus(wsData?.operatingScheduleJson);
                                                            return shiftStatus.isActive ? (
                                                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-emerald-50 text-emerald-700 text-[11px] font-semibold border border-emerald-200">
                                                                    <Clock className="w-3 h-3 text-emerald-600" />
                                                                    {shiftStatus.isContinuous ? '24/7 Continuous' : `${shiftStatus.label} (${shiftStatus.time})`}
                                                                </span>
                                                            ) : (
                                                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-amber-50 text-amber-700 text-[11px] font-medium border border-amber-200">
                                                                    <Clock className="w-3 h-3 text-amber-500" /> {shiftStatus.label}
                                                                </span>
                                                            );
                                                        })()}
                                                    </div>
                                                </div>

                                                {/* Status Badge & Edit Action */}
                                                <div className="flex items-center gap-2">
                                                    <span
                                                        className={`px-3 py-1 rounded-full text-xs font-bold uppercase tracking-wider flex items-center gap-1.5 ${
                                                            isViolation
                                                                ? 'bg-rose-50 text-rose-700 border border-rose-200/60 animate-pulse'
                                                                : isPending
                                                                ? 'bg-amber-50 text-amber-700 border border-amber-200/60'
                                                                : isRelieved
                                                                ? 'bg-blue-50 text-blue-700 border border-blue-200/60'
                                                                : 'bg-emerald-50 text-emerald-700 border border-emerald-200/60'
                                                        }`}
                                                    >
                                                        {isViolation && <AlertTriangle className="w-3.5 h-3.5" />}
                                                        {isPending && <Timer className="w-3.5 h-3.5 animate-spin" />}
                                                        {isRelieved && <Activity className="w-3.5 h-3.5" />}
                                                        {isStaffed && <CheckCircle2 className="w-3.5 h-3.5" />}
                                                        {station.status}
                                                    </span>
                                                    <button
                                                        type="button"
                                                        onClick={() => handleOpenEdit(station.workstationId)}
                                                        title="Edit Workstation Configuration & Geofence"
                                                        className="p-1.5 rounded-xl bg-white hover:bg-gray-100 text-gray-500 hover:text-blue-600 border border-gray-200 transition shadow-2xs"
                                                    >
                                                        <Edit2 className="w-3.5 h-3.5" />
                                                    </button>
                                                </div>
                                            </div>

                                            {/* Real-time Timers & Countdown */}
                                            {isPending && (
                                                <div className="p-3.5 rounded-2xl bg-amber-50/80 border border-amber-200 space-y-1.5">
                                                    <div className="flex justify-between text-xs text-amber-900 font-semibold">
                                                        <span>Grace Period Countdown</span>
                                                        <span>{formatSeconds(station.remainingGraceSeconds)} remaining</span>
                                                    </div>
                                                    <div className="w-full h-2 rounded-full bg-amber-100 overflow-hidden">
                                                        <div
                                                            className="h-full bg-amber-500 rounded-full transition-all duration-1000"
                                                            style={{
                                                                width: `${Math.min(100, ((station.remainingGraceSeconds ?? 0) / station.handoverThresholdSeconds) * 100)}%`
                                                            }}
                                                        />
                                                    </div>
                                                </div>
                                            )}

                                            {isRelieved && (
                                                <div className="p-3.5 rounded-2xl bg-blue-50/80 border border-blue-200 space-y-1">
                                                    <div className="flex justify-between text-xs text-blue-900 font-semibold">
                                                        <span>Active Relief Duty</span>
                                                        <span>{formatSeconds(station.activeReliefDurationSeconds)} active</span>
                                                    </div>
                                                    <div className="text-[11px] text-blue-700">
                                                        Covered by: <strong>{station.activeReliever || 'Designated Reliever'}</strong>
                                                    </div>
                                                </div>
                                            )}

                                            {isViolation && (
                                                <div className="p-3.5 rounded-2xl bg-rose-50/80 border border-rose-200 text-rose-800 text-xs space-y-1">
                                                    <div className="font-bold flex items-center gap-1.5 text-rose-700">
                                                        <AlertTriangle className="w-4 h-4 text-rose-600" />
                                                        Unattended Workstation Alert
                                                    </div>
                                                    <p className="text-[11px] text-rose-600">
                                                        Grace threshold ({station.handoverThresholdSeconds}s) expired without a reliever takeover.
                                                    </p>
                                                </div>
                                            )}

                                            {/* Station Info Grid */}
                                            {(() => {
                                                const wsData = workstationMap.get(station.workstationId);
                                                return (
                                                    <div className="grid grid-cols-2 gap-2 text-xs text-gray-600 pt-3 border-t border-gray-100">
                                                        <div>
                                                            <span className="text-gray-400 text-[11px] block">Primary Worker:</span>
                                                            <div className="font-semibold text-gray-900 truncate">
                                                                {station.activePrimaryWorker || 'Assigned'}
                                                            </div>
                                                        </div>
                                                        <div>
                                                            <span className="text-gray-400 text-[11px] block">Rotating Worker Pool:</span>
                                                            <div className="font-semibold text-gray-900 flex items-center gap-1">
                                                                <Users className="w-3 h-3 text-blue-600" />
                                                                {wsData ? `${wsData.primaryWorkersCount} Primaries · ${wsData.relieversCount} Relievers` : 'Configured'}
                                                            </div>
                                                        </div>
                                                        <div>
                                                            <span className="text-gray-400 text-[11px] block">Required Presence:</span>
                                                            <div className="font-semibold text-gray-900">
                                                                {station.requiredPrimaryWorkers} Primary (Max {wsData?.maxRelievers ?? 1} Reliever)
                                                            </div>
                                                        </div>
                                                        <div>
                                                            <span className="text-gray-400 text-[11px] block">Today&apos;s Reliefs:</span>
                                                            <div className="font-semibold text-gray-900">
                                                                {station.activeReliefsTodayCount} events
                                                            </div>
                                                        </div>
                                                    </div>
                                                );
                                            })()}
                                        </div>

                                        {/* Bottom Footer */}
                                        <div className="mt-5 pt-3 border-t border-gray-100 flex items-center justify-between text-[11px] text-gray-500">
                                            <span>Uptime: <strong className="text-gray-900">{station.todayUptimePercentage}%</strong></span>
                                            <div className="flex items-center gap-3">
                                                <span className="font-mono text-gray-400">Grace: {station.handoverThresholdSeconds}s</span>
                                                <button
                                                    type="button"
                                                    onClick={() => handleOpenEdit(station.workstationId)}
                                                    className="text-blue-600 hover:text-blue-800 font-semibold flex items-center gap-1 hover:underline text-xs"
                                                >
                                                    <Edit2 className="w-3 h-3" /> Edit Workstation
                                                </button>
                                            </div>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </div>
            )}

            {/* TAB 2: RELIEVER ANALYTICS & KPIS */}
            {activeTab === 'analytics' && analytics && (
                <div className="space-y-8">
                    {/* Top Analytics KPI Row */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
                        <div className="p-6 rounded-3xl bg-white border border-gray-100 shadow-sm space-y-2">
                            <div className="text-xs text-gray-400 font-semibold uppercase">Overall Compliance Rate</div>
                            <div className="text-4xl font-extrabold text-emerald-600">
                                {analytics.overallStaffingComplianceRate}%
                            </div>
                            <p className="text-xs text-gray-500">Percentage of monitored hours fully staffed</p>
                        </div>

                        <div className="p-6 rounded-3xl bg-white border border-gray-100 shadow-sm space-y-2">
                            <div className="text-xs text-gray-400 font-semibold uppercase">Avg Handover Latency</div>
                            <div className="text-4xl font-extrabold text-blue-600">
                                {analytics.averageHandoverLatencySeconds}s
                            </div>
                            <p className="text-xs text-gray-500">Average response time for reliever takeover</p>
                        </div>

                        <div className="p-6 rounded-3xl bg-white border border-gray-100 shadow-sm space-y-2">
                            <div className="text-xs text-gray-400 font-semibold uppercase">Total Relief Cycles</div>
                            <div className="text-4xl font-extrabold text-teal-600">
                                {analytics.totalReliefEventsCount}
                            </div>
                            <p className="text-xs text-gray-500">Avg duration: {analytics.averageReliefDurationMinutes} mins</p>
                        </div>

                        <div className="p-6 rounded-3xl bg-white border border-gray-100 shadow-sm space-y-2">
                            <div className="text-xs text-gray-400 font-semibold uppercase">Total Unattended Downtime</div>
                            <div className="text-4xl font-extrabold text-rose-600">
                                {analytics.totalDowntimeMinutes}m
                            </div>
                            <p className="text-xs text-gray-500">{analytics.totalUnattendedViolationsCount} unstaffed violations</p>
                        </div>
                    </div>

                    {/* Reliever Workload Breakdown & Workstation Compliance Tables */}
                    <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
                        {/* Top Relievers Leaderboard */}
                        <div className="lg:col-span-6 bg-white border border-gray-100 rounded-3xl p-6 space-y-4 shadow-sm">
                            <div className="flex items-center justify-between">
                                <h3 className="text-base font-bold text-gray-900 flex items-center gap-2">
                                    <Users className="w-4 h-4 text-blue-600" /> Reliever Utilization & Workload
                                </h3>
                                <span className="text-xs text-gray-500">{analytics.topRelievers.length} active relievers</span>
                            </div>

                            <div className="overflow-x-auto">
                                <table className="w-full text-left text-xs text-gray-700">
                                    <thead className="bg-gray-50 text-gray-500 font-semibold uppercase text-[10px]">
                                        <tr>
                                            <th className="p-3 rounded-l-xl">Reliever</th>
                                            <th className="p-3">Total Reliefs</th>
                                            <th className="p-3">Relief Time</th>
                                            <th className="p-3 rounded-r-xl">Avg Response</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-100">
                                        {analytics.topRelievers.length === 0 ? (
                                            <tr>
                                                <td colSpan={4} className="p-6 text-center text-gray-400 italic">
                                                    No reliever activity logged for this period.
                                                </td>
                                            </tr>
                                        ) : (
                                            analytics.topRelievers.map(r => (
                                                <tr key={r.employeeId || r.employeeName} className="hover:bg-gray-50/60 transition">
                                                    <td className="p-3 font-semibold text-gray-900">
                                                        {r.employeeName}
                                                    </td>
                                                    <td className="p-3 font-mono font-bold text-blue-600">
                                                        {r.reliefCount}
                                                    </td>
                                                    <td className="p-3 font-mono">
                                                        {r.totalReliefMinutes} mins
                                                    </td>
                                                    <td className="p-3 font-mono text-emerald-600 font-semibold">
                                                        {r.averageResponseLatencySeconds}s
                                                    </td>
                                                </tr>
                                            ))
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </div>

                        {/* Workstation Compliance Breakdown */}
                        <div className="lg:col-span-6 bg-white border border-gray-100 rounded-3xl p-6 space-y-4 shadow-sm">
                            <div className="flex items-center justify-between">
                                <h3 className="text-base font-bold text-gray-900 flex items-center gap-2">
                                    <Layers className="w-4 h-4 text-blue-600" /> Station Compliance & Uptime
                                </h3>
                                <span className="text-xs text-gray-500">{analytics.workstationMetrics.length} stations</span>
                            </div>

                            <div className="overflow-x-auto">
                                <table className="w-full text-left text-xs text-gray-700">
                                    <thead className="bg-gray-50 text-gray-500 font-semibold uppercase text-[10px]">
                                        <tr>
                                            <th className="p-3 rounded-l-xl">Station</th>
                                            <th className="p-3">Compliance</th>
                                            <th className="p-3">Reliefs</th>
                                            <th className="p-3 rounded-r-xl">Violations</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-100">
                                        {analytics.workstationMetrics.length === 0 ? (
                                            <tr>
                                                <td colSpan={4} className="p-6 text-center text-gray-400 italic">
                                                    No workstation metrics recorded yet.
                                                </td>
                                            </tr>
                                        ) : (
                                            analytics.workstationMetrics.map(w => (
                                                <tr key={w.workstationId} className="hover:bg-gray-50/60 transition">
                                                    <td className="p-3">
                                                        <span className="font-semibold text-gray-900">{w.workstationName}</span>
                                                        <span className="text-gray-400 ml-1.5 font-mono text-[10px]">({w.workstationCode})</span>
                                                    </td>
                                                    <td className="p-3 font-bold text-emerald-600">
                                                        {w.staffingCompliancePercentage}%
                                                    </td>
                                                    <td className="p-3 font-mono">
                                                        {w.totalReliefsReceived}
                                                    </td>
                                                    <td className="p-3 font-mono text-rose-600 font-semibold">
                                                        {w.totalViolations}
                                                    </td>
                                                </tr>
                                            ))
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* TAB 3: WORKSTATIONS MANAGEMENT */}
            {activeTab === 'workstations' && (
                <div className="space-y-6">
                    <div className="flex items-center justify-between">
                        <div className="text-sm text-gray-500">
                            Configure workstation polygon boundaries, operator counts, and threshold timers.
                        </div>
                        <button
                            onClick={handleOpenCreate}
                            className="px-5 py-2.5 rounded-2xl bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs shadow-sm hover:shadow-md transition flex items-center gap-2"
                        >
                            <Plus className="w-4 h-4" /> Create Workstation
                        </button>
                    </div>

                    <div className="bg-white border border-gray-100 rounded-3xl overflow-hidden shadow-sm">
                        <table className="w-full text-left text-xs text-gray-700">
                            <thead className="bg-gray-50 text-gray-500 font-semibold uppercase text-[10px]">
                                <tr>
                                    <th className="p-4">Station Code & Name</th>
                                    <th className="p-4">Camera Feed</th>
                                    <th className="p-4">Required On-Duty</th>
                                    <th className="p-4">Rotating Worker Pool</th>
                                    <th className="p-4">Handover Grace</th>
                                    <th className="p-4">Max Relief Time</th>
                                    <th className="p-4">Operating Shifts</th>
                                    <th className="p-4">Status</th>
                                    <th className="p-4 text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {workstations.length === 0 ? (
                                    <tr>
                                        <td colSpan={9} className="p-8 text-center text-gray-400 italic">
                                            No workstations configured yet.
                                        </td>
                                    </tr>
                                ) : (
                                    workstations.map(ws => (
                                        <tr key={ws.id} className="hover:bg-gray-50/50 transition">
                                            <td className="p-4">
                                                <div className="font-bold text-gray-900 text-sm">{ws.name}</div>
                                                <div className="font-mono text-blue-600 text-[11px]">{ws.code}</div>
                                            </td>
                                            <td className="p-4">
                                                <div className="text-gray-900 font-medium">{ws.cameraName}</div>
                                                <div className="text-[10px] text-gray-400 font-mono">{ws.cameraExternalId}</div>
                                            </td>
                                            <td className="p-4">
                                                <div className="text-gray-900 font-semibold">{ws.requiredPrimaryWorkers} Primary{ws.requiredPrimaryWorkers > 1 ? 's' : ''}</div>
                                                <div className="text-gray-400 text-[11px]">Max Relievers: {ws.maxRelievers}</div>
                                            </td>
                                            <td className="p-4">
                                                <div className="space-y-1">
                                                    <div className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-blue-50 text-blue-700 text-xs font-semibold border border-blue-100">
                                                        <Users className="w-3 h-3 text-blue-600" />
                                                        {ws.primaryWorkersCount} Primary Pool
                                                    </div>
                                                    <div className="text-[11px] text-teal-700 font-medium">
                                                        {ws.relieversCount} Authorized Reliever{ws.relieversCount !== 1 ? 's' : ''}
                                                    </div>
                                                </div>
                                            </td>
                                            <td className="p-4 font-mono font-bold text-amber-600">
                                                {ws.handoverThresholdSeconds}s
                                            </td>
                                            <td className="p-4 font-mono text-gray-600">
                                                {Math.round(ws.maxReliefDurationSeconds / 60)} mins ({ws.maxReliefDurationSeconds}s)
                                            </td>
                                            <td className="p-4">
                                                {(() => {
                                                    if (!ws.operatingScheduleJson) {
                                                        return (
                                                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-lg bg-gray-100 text-gray-600 text-[11px] font-medium">
                                                                <Clock className="w-3 h-3 text-gray-400" /> 24/7 Continuous
                                                            </span>
                                                        );
                                                    }
                                                    try {
                                                        const scheds = JSON.parse(ws.operatingScheduleJson);
                                                        if (!Array.isArray(scheds) || scheds.length === 0) {
                                                            return (
                                                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-lg bg-gray-100 text-gray-600 text-[11px] font-medium">
                                                                    <Clock className="w-3 h-3 text-gray-400" /> 24/7 Continuous
                                                                </span>
                                                            );
                                                        }
                                                        return (
                                                            <div className="space-y-1">
                                                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-lg bg-blue-50 text-blue-700 text-[11px] font-semibold border border-blue-200">
                                                                    <Clock className="w-3 h-3 text-blue-500" /> {scheds.length} {scheds.length === 1 ? 'Shift' : 'Shifts'}
                                                                </span>
                                                                <div className="text-[10px] text-gray-500 font-mono">
                                                                    {scheds.slice(0, 2).map((s: any) => `${s.label || 'Shift'}: ${s.startTime}-${s.endTime}`).join(' | ')}
                                                                    {scheds.length > 2 && ' +more'}
                                                                </div>
                                                            </div>
                                                        );
                                                    } catch {
                                                        return <span className="text-gray-400 text-xs">Standard</span>;
                                                    }
                                                })()}
                                            </td>
                                            <td className="p-4">
                                                <span className={`px-2.5 py-1 rounded-full text-[10px] font-bold uppercase ${
                                                    ws.isActive ? 'bg-emerald-50 text-emerald-700 border border-emerald-200/60' : 'bg-gray-100 text-gray-500 border border-gray-200/60'
                                                }`}>
                                                    {ws.isActive ? 'Active' : 'Disabled'}
                                                </span>
                                            </td>
                                             <td className="p-4 text-right space-x-2">
                                                <button
                                                    onClick={() => handleOpenEdit(ws)}
                                                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-blue-50 hover:bg-blue-100 text-blue-700 font-semibold text-xs border border-blue-200/60 transition shadow-2xs"
                                                    title="Edit Workstation & Polygon"
                                                >
                                                    <Edit2 className="w-3.5 h-3.5" /> Edit
                                                </button>
                                                <button
                                                    onClick={() => handleDeleteWorkstation(ws.id)}
                                                    className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-rose-50 hover:bg-rose-100 text-rose-600 transition shadow-2xs text-xs font-semibold"
                                                    title="Delete Workstation"
                                                >
                                                    <Trash2 className="w-3.5 h-3.5" /> Delete
                                                </button>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {/* TAB 4: SESSION AUDIT LOG */}
            {activeTab === 'history' && (
                <div className="space-y-6">
                    {/* Filter bar */}
                    <div className="flex flex-wrap items-center justify-between gap-4 bg-white p-4 rounded-2xl border border-gray-100 shadow-sm">
                        <div className="flex items-center gap-3">
                            <input
                                type="date"
                                value={historyDate}
                                onChange={e => setHistoryDate(e.target.value)}
                                className="px-3 py-1.5 rounded-xl bg-gray-50 border border-gray-200 text-xs text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-500"
                            />
                            <span className="text-xs text-gray-500">
                                Showing sessions for <strong className="text-gray-900">{historyDate}</strong>
                            </span>
                        </div>

                        <button
                            onClick={exportSessionsCsv}
                            disabled={sessions.length === 0}
                            className="px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-semibold flex items-center gap-2 transition shadow-sm disabled:opacity-50"
                        >
                            <FileSpreadsheet className="w-4 h-4" /> Export CSV
                        </button>
                    </div>

                    <div className="bg-white border border-gray-100 rounded-3xl overflow-hidden shadow-sm">
                        <table className="w-full text-left text-xs text-gray-700">
                            <thead className="bg-gray-50 text-gray-500 font-semibold uppercase text-[10px]">
                                <tr>
                                    <th className="p-4">Workstation</th>
                                    <th className="p-4">Primary Left</th>
                                    <th className="p-4">Reliever Arrived</th>
                                    <th className="p-4">Handover Latency</th>
                                    <th className="p-4">Duty Duration</th>
                                    <th className="p-4">Status</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {sessions.length === 0 ? (
                                    <tr>
                                        <td colSpan={6} className="p-8 text-center text-gray-400 italic">
                                            No relief sessions logged on {historyDate}.
                                        </td>
                                    </tr>
                                ) : (
                                    sessions.map(s => (
                                        <tr key={s.id} className="hover:bg-gray-50/50 transition">
                                            <td className="p-4">
                                                <div className="font-semibold text-gray-900">{s.workstationName}</div>
                                                <div className="text-[10px] text-gray-400 font-mono">{s.workstationCode} · {s.cameraName}</div>
                                            </td>
                                            <td className="p-4">
                                                <div className="text-gray-900 font-medium">{new Date(s.primaryLeftAt).toLocaleTimeString()}</div>
                                                <div className="text-[10px] text-gray-400">{s.primaryEmployeeName || 'Primary Worker'}</div>
                                            </td>
                                            <td className="p-4">
                                                {s.relieverArrivedAt ? (
                                                    <div>
                                                        <div className="text-blue-700 font-medium">{new Date(s.relieverArrivedAt).toLocaleTimeString()}</div>
                                                        <div className="text-[10px] text-blue-500">{s.relieverEmployeeName || 'Reliever'}</div>
                                                    </div>
                                                ) : (
                                                    <span className="text-gray-400 italic">—</span>
                                                )}
                                            </td>
                                            <td className="p-4 font-mono font-bold text-amber-600">
                                                {s.handoverLatencySeconds !== undefined && s.handoverLatencySeconds !== null
                                                    ? `${s.handoverLatencySeconds}s`
                                                    : '—'}
                                            </td>
                                            <td className="p-4 font-mono text-gray-700">
                                                {s.reliefDurationSeconds ? formatSeconds(s.reliefDurationSeconds) : '—'}
                                            </td>
                                            <td className="p-4">
                                                <span className={`px-2.5 py-1 rounded-full text-[10px] font-bold uppercase ${
                                                    s.status === 'Completed'
                                                        ? 'bg-emerald-50 text-emerald-700 border border-emerald-200/60'
                                                        : s.status === 'ActiveRelief'
                                                        ? 'bg-blue-50 text-blue-700 border border-blue-200/60'
                                                        : s.status === 'UnattendedViolation'
                                                        ? 'bg-rose-50 text-rose-700 border border-rose-200/60'
                                                        : 'bg-amber-50 text-amber-700 border border-amber-200/60'
                                                }`}>
                                                    {s.status}
                                                </span>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {/* Modal */}
            <WorkstationFormModal
                isOpen={isModalOpen}
                onClose={() => setIsModalOpen(false)}
                onSuccess={() => fetchData(true)}
                workstation={editingWorkstation}
            />
        </div>
    );
}
