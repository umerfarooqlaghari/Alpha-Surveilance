'use client';

import { useState, useEffect, useRef, useMemo } from 'react';
import { Search, Eye, Filter, ExternalLink, AlertTriangle, RotateCcw, X } from 'lucide-react';
import {
    getViolations,
    getFalsePositiveViolations,
    markViolationsFalsePositive,
    unmarkViolationsFalsePositive,
} from '@/lib/api/tenant/violations';
import type { Violation } from '@/lib/api/tenant/violations';
import { useAuth } from '@/contexts/AuthContext';
import { useViolationHub } from '@/hooks/useViolationHub';
import Image from 'next/image';

export default function TenantViolationsPage() {
    const { tenant } = useAuth();
    const { notifications } = useViolationHub();
    const [violations, setViolations] = useState<Violation[]>([]);
    const [falsePositives, setFalsePositives] = useState<Violation[]>([]);
    const [tab, setTab] = useState<'active' | 'falsePositive'>('active');
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
    const [bulkBusy, setBulkBusy] = useState(false);
    const [showMarkModal, setShowMarkModal] = useState(false);
    const [markReason, setMarkReason] = useState('');
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedModel, setSelectedModel] = useState<string>('all');
    const [selectedSeverity, setSelectedSeverity] = useState<string>('all');
    const [selectedCamera, setSelectedCamera] = useState<string>('all');
    const [selectedStatus, setSelectedStatus] = useState<string>('all');
    const [dateFrom, setDateFrom] = useState<string>('');
    const [dateTo, setDateTo] = useState<string>('');
    const [currentPage, setCurrentPage] = useState(1);
    const [frameModalUrl, setFrameModalUrl] = useState<string | null>(null);
    const PAGE_SIZE = 25;

    const loadData = async (showSpinner = true) => {
        try {
            if (showSpinner) setLoading(true);
            // Fetch both lists in parallel — counts are needed for the tab badges
            // regardless of which tab is currently visible.
            const [active, fps] = await Promise.all([
                getViolations(),
                getFalsePositiveViolations().catch(() => [] as Violation[]),
            ]);
            setViolations(active);
            setFalsePositives(fps);
        } catch (error) {
            console.error('Failed to load violations:', error);
        } finally {
            if (showSpinner) setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, []);

    // Refetch the enriched violations list whenever a real-time
    // notification arrives over SignalR. Using the count avoids
    // re-running on identity changes of the array reference alone.
    const lastSeenCount = useRef(0);
    useEffect(() => {
        if (notifications.length > lastSeenCount.current) {
            lastSeenCount.current = notifications.length;
            loadData(false);
        }
    }, [notifications.length]);

    // Reset to page 1 whenever any filter changes
    useEffect(() => { setCurrentPage(1); }, [searchTerm, selectedModel, selectedSeverity, selectedCamera, selectedStatus, dateFrom, dateTo]);

    // Clear selection whenever the user switches tab or changes any filter —
    // the selected IDs may no longer be visible on screen.
    useEffect(() => { setSelectedIds(new Set()); setCurrentPage(1); }, [tab]);
    useEffect(() => { setSelectedIds(new Set()); }, [searchTerm, selectedModel, selectedSeverity, selectedCamera, selectedStatus, dateFrom, dateTo]);

    // Active list = whichever tab is showing. All downstream filtering, pagination,
    // and stats reference `currentList` so the two tabs share the same code path.
    const currentList: Violation[] = tab === 'active' ? violations : falsePositives;

    const filteredViolations = currentList.filter(v => {
        const matchesSearch =
            v.id.toLowerCase().includes(searchTerm.toLowerCase()) ||
            v.cameraId?.toLowerCase().includes(searchTerm.toLowerCase()) ||
            v.cameraName?.toLowerCase().includes(searchTerm.toLowerCase()) ||
            v.violationTypeName?.toLowerCase().includes(searchTerm.toLowerCase());

        const matchesModel = selectedModel === 'all' || v.modelIdentifier === selectedModel;
        const matchesSeverity = selectedSeverity === 'all' || v.severity?.toString() === selectedSeverity;
        const matchesCamera = selectedCamera === 'all' || v.cameraName === selectedCamera;
        const matchesStatus = selectedStatus === 'all' || v.status === selectedStatus || (v.status === '0' && selectedStatus === 'Pending') || (v.status === '1' && selectedStatus === 'Audited') || (v.status === '2' && selectedStatus === 'FailedAudit');

        const ts = new Date(v.timestamp);
        const matchesDateFrom = !dateFrom || ts >= new Date(dateFrom);
        const matchesDateTo   = !dateTo   || ts <= new Date(dateTo);

        return matchesSearch && matchesModel && matchesSeverity && matchesCamera && matchesStatus && matchesDateFrom && matchesDateTo;
    });

    const totalPages = Math.max(1, Math.ceil(filteredViolations.length / PAGE_SIZE));
    const paginatedViolations = filteredViolations.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

    const uniqueCameras = Array.from(new Set(currentList.map(v => v.cameraName).filter(Boolean)));
    const uniqueModels = Array.from(new Set(currentList.map(v => v.modelIdentifier).filter(Boolean)));
    const statuses = ['Pending', 'Audited', 'FailedAudit'];

    // Selection helpers — operate on the current page so "select all" doesn't
    // accidentally pull in rows the user can't see.
    const pageIds = useMemo(() => paginatedViolations.map(v => v.id), [paginatedViolations]);
    const allOnPageSelected = pageIds.length > 0 && pageIds.every(id => selectedIds.has(id));
    const toggleRow = (id: string) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    };
    const togglePage = () => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (allOnPageSelected) pageIds.forEach(id => next.delete(id));
            else pageIds.forEach(id => next.add(id));
            return next;
        });
    };

    const handleMark = async () => {
        if (selectedIds.size === 0) return;
        try {
            setBulkBusy(true);
            await markViolationsFalsePositive(Array.from(selectedIds), markReason.trim() || undefined);
            setShowMarkModal(false);
            setMarkReason('');
            setSelectedIds(new Set());
            await loadData(false);
        } catch (e) {
            console.error('Mark failed', e);
            alert('Failed to mark as false positive');
        } finally {
            setBulkBusy(false);
        }
    };

    const handleUnmark = async () => {
        if (selectedIds.size === 0) return;
        if (!confirm(`Restore ${selectedIds.size} violation(s) back to the active list?`)) return;
        try {
            setBulkBusy(true);
            await unmarkViolationsFalsePositive(Array.from(selectedIds));
            setSelectedIds(new Set());
            await loadData(false);
        } catch (e) {
            console.error('Unmark failed', e);
            alert('Failed to restore violations');
        } finally {
            setBulkBusy(false);
        }
    };

    const getStatusColor = (status?: string) => {
        if (!status) return 'bg-yellow-100 text-yellow-800';
        switch (status) {
            case 'Audited':
            case '1': 
                return 'bg-green-100 text-green-800';
            case 'FailedAudit':
            case '2':
                return 'bg-red-100 text-red-800';
            case 'Pending':
            case '0':
                return 'bg-yellow-100 text-yellow-800';
            default: return 'bg-gray-100 text-gray-800';
        }
    };

    return (
        <div>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-3xl font-bold text-gray-900">Violations</h2>
            </div>

            {/* Tabs — Active vs False Positives. Counts come from the full unfiltered lists
                so users can see how many FPs exist even when filters hide them. */}
            <div className="mb-4 border-b border-gray-200">
                <nav className="flex gap-6" aria-label="Tabs">
                    <button
                        onClick={() => setTab('active')}
                        className={`pb-3 -mb-px text-sm font-medium border-b-2 transition-colors ${
                            tab === 'active'
                                ? 'border-blue-600 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                        }`}
                    >
                        Active
                        <span className="ml-2 inline-flex items-center justify-center px-2 py-0.5 rounded-full text-xs bg-gray-100 text-gray-700">
                            {violations.length}
                        </span>
                    </button>
                    <button
                        onClick={() => setTab('falsePositive')}
                        className={`pb-3 -mb-px text-sm font-medium border-b-2 transition-colors ${
                            tab === 'falsePositive'
                                ? 'border-blue-600 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                        }`}
                    >
                        False Positives
                        <span className="ml-2 inline-flex items-center justify-center px-2 py-0.5 rounded-full text-xs bg-red-100 text-red-700">
                            {falsePositives.length}
                        </span>
                    </button>
                </nav>
            </div>

            {/* Bulk action bar — only visible when at least one row is selected. */}
            {selectedIds.size > 0 && (
                <div className="mb-4 flex items-center justify-between gap-3 px-4 py-2 rounded-lg bg-blue-50 border border-blue-200">
                    <div className="text-sm text-blue-900">
                        <span className="font-medium">{selectedIds.size}</span> selected
                    </div>
                    <div className="flex items-center gap-2">
                        <button
                            onClick={() => setSelectedIds(new Set())}
                            className="text-xs text-gray-600 hover:text-gray-800 underline"
                        >
                            Clear
                        </button>
                        {tab === 'active' ? (
                            <button
                                onClick={() => setShowMarkModal(true)}
                                disabled={bulkBusy}
                                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg bg-yellow-500 hover:bg-yellow-600 text-white text-xs font-medium disabled:opacity-50"
                            >
                                <AlertTriangle className="w-4 h-4" />
                                Mark as False Positive
                            </button>
                        ) : (
                            <button
                                onClick={handleUnmark}
                                disabled={bulkBusy}
                                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg bg-green-600 hover:bg-green-700 text-white text-xs font-medium disabled:opacity-50"
                            >
                                <RotateCcw className="w-4 h-4" />
                                Restore
                            </button>
                        )}
                    </div>
                </div>
            )}

            {/* Filters */}
            <div className="mb-6 space-y-3">
            <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-5 gap-3">
                <div className="relative lg:col-span-1">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-5 h-5" />
                    <input
                        type="text"
                        placeholder="Search..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900"
                    />
                </div>
                <select
                    value={selectedModel}
                    onChange={(e) => setSelectedModel(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-700"
                >
                    <option value="all">All Models</option>
                    {uniqueModels.map(model => (
                        <option key={model} value={model}>{model}</option>
                    ))}
                </select>
                <select
                    value={selectedCamera}
                    onChange={(e) => setSelectedCamera(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-700"
                >
                    <option value="all">All Cameras</option>
                    {uniqueCameras.map(cam => (
                        <option key={cam} value={cam}>{cam}</option>
                    ))}
                </select>
                <select
                    value={selectedStatus}
                    onChange={(e) => setSelectedStatus(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-700"
                >
                    <option value="all">All Statuses</option>
                    {statuses.map(s => (
                        <option key={s} value={s}>{s}</option>
                    ))}
                </select>
                <select
                    value={selectedSeverity}
                    onChange={(e) => setSelectedSeverity(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-700"
                >
                    <option value="all">All Severities</option>
                    <option value="Low">Low</option>
                    <option value="Medium">Medium</option>
                    <option value="High">High</option>
                    <option value="Critical">Critical</option>
                </select>
            </div>

            {/* Date / Time range row */}
            <div className="flex flex-wrap items-center gap-3">
                <div className="flex items-center gap-2">
                    <Filter className="w-4 h-4 text-gray-400 shrink-0" />
                    <span className="text-sm text-gray-500 whitespace-nowrap">Date range:</span>
                </div>
                <div className="flex items-center gap-2 border border-gray-300 rounded-lg px-3 py-2 bg-white">
                    <span className="text-xs text-gray-400">From</span>
                    <input
                        type="datetime-local"
                        value={dateFrom}
                        onChange={(e) => setDateFrom(e.target.value)}
                        className="text-sm outline-none text-gray-700 bg-transparent"
                    />
                </div>
                <div className="flex items-center gap-2 border border-gray-300 rounded-lg px-3 py-2 bg-white">
                    <span className="text-xs text-gray-400">To</span>
                    <input
                        type="datetime-local"
                        value={dateTo}
                        onChange={(e) => setDateTo(e.target.value)}
                        className="text-sm outline-none text-gray-700 bg-transparent"
                    />
                </div>
                {(dateFrom || dateTo) && (
                    <button
                        onClick={() => { setDateFrom(''); setDateTo(''); }}
                        className="text-xs text-blue-600 hover:text-blue-800 underline"
                    >
                        Clear dates
                    </button>
                )}
            </div>
            </div>

            {/* Grid */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
                {loading ? (
                    <div className="p-8 text-center text-gray-500">Loading...</div>
                ) : filteredViolations.length === 0 ? (
                    <div className="p-8 text-center text-gray-500">No violations found</div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full">
                            <thead className="bg-gray-50 border-b border-gray-200">
                                <tr>
                                    <th className="px-4 py-3 text-left w-10">
                                        <input
                                            type="checkbox"
                                            aria-label="Select all on page"
                                            checked={allOnPageSelected}
                                            onChange={togglePage}
                                            className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500 cursor-pointer"
                                        />
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        #
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Frame
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Type
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Person
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Severity
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Camera
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Time
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Status
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {paginatedViolations.map((violation, index) => (
                                    <tr key={violation.id} className={`hover:bg-gray-50 ${selectedIds.has(violation.id) ? 'bg-blue-50/40' : ''}`}>
                                        <td className="px-4 py-4 w-10">
                                            <input
                                                type="checkbox"
                                                aria-label={`Select violation ${violation.id}`}
                                                checked={selectedIds.has(violation.id)}
                                                onChange={() => toggleRow(violation.id)}
                                                className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500 cursor-pointer"
                                            />
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {(currentPage - 1) * PAGE_SIZE + index + 1}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm">
                                            {violation.framePath ? (
                                                <button
                                                    onClick={() => setFrameModalUrl(violation.framePath!)}
                                                    className="flex items-center gap-1 text-blue-600 hover:text-blue-800"
                                                    title="View frame"
                                                >
                                                    <Eye className="w-4 h-4" />
                                                    <span>View</span>
                                                </button>
                                            ) : (
                                                <span className="text-gray-400">N/A</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                            <div className="font-medium">{violation.violationTypeName || violation.type}</div>
                                            <div className="text-xs text-gray-500">{violation.sopName || 'Security'}</div>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm">
                                            {(() => {
                                                if (violation.employee) {
                                                    return (
                                                        <div>
                                                            <div className="font-medium text-gray-900">{violation.employee.firstName} {violation.employee.lastName}</div>
                                                            <div className="text-xs text-gray-500">{violation.employee.employeeId}</div>
                                                        </div>
                                                    );
                                                }
                                                // Person-related violation detected (vision service attached a person_box)
                                                // but face was not recognized against the employee DB.
                                                // Use structural JSON parsing — substring matching can produce false
                                                // positives if the string "person_box" appears in a label or comment.
                                                const isPersonRelated = (() => {
                                                    try {
                                                        const meta = violation.metadataJson ? JSON.parse(violation.metadataJson) : null;
                                                        return meta !== null && typeof meta === 'object' && 'person_box' in meta;
                                                    } catch {
                                                        return false;
                                                    }
                                                })();
                                                if (isPersonRelated) {
                                                    return (
                                                        <span className="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-yellow-100 text-yellow-800">
                                                            Unrecognized
                                                        </span>
                                                    );
                                                }
                                                // Non-human violation (e.g. dirty floor, equipment issue) — no person involved.
                                                return <span className="text-gray-400">N/A</span>;
                                            })()}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {violation.severity || 'Unknown'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            <div className="font-medium text-gray-900">{violation.cameraName || 'Unknown Camera'}</div>
                                            {violation.cameraId && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(violation.cameraId) && violation.cameraId !== violation.cameraName && (
                                                <div className="text-xs text-gray-400">{violation.cameraId}</div>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {new Date(violation.timestamp).toLocaleString()}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            {tab === 'falsePositive' ? (
                                                <div className="text-xs">
                                                    <span className="px-2 py-1 inline-flex leading-5 font-semibold rounded-full bg-red-100 text-red-800">
                                                        False Positive
                                                    </span>
                                                    {(violation.falsePositiveMarkedBy || violation.falsePositiveMarkedAt) && (
                                                        <div className="text-[11px] text-gray-500 mt-1" title={violation.falsePositiveReason || ''}>
                                                            {violation.falsePositiveMarkedBy ? `by ${violation.falsePositiveMarkedBy}` : ''}
                                                            {violation.falsePositiveMarkedAt ? ` · ${new Date(violation.falsePositiveMarkedAt).toLocaleDateString()}` : ''}
                                                        </div>
                                                    )}
                                                </div>
                                            ) : (
                                                <span className={`px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${getStatusColor(violation.status)}`}>
                                                    {violation.status === '0' ? 'Pending' : violation.status === '1' ? 'Audited' : violation.status === '2' ? 'FailedAudit' : (violation.status || 'Pending')}
                                                </span>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
            {/* Pagination controls */}
            {filteredViolations.length > 0 && (
                <div className="flex items-center justify-between mt-4 px-1">
                    <p className="text-sm text-gray-500">
                        Showing <span className="font-medium text-gray-700">{(currentPage - 1) * PAGE_SIZE + 1}</span>–<span className="font-medium text-gray-700">{Math.min(currentPage * PAGE_SIZE, filteredViolations.length)}</span> of <span className="font-medium text-gray-700">{filteredViolations.length}</span> violations
                    </p>
                    <div className="flex items-center gap-1">
                        <button
                            onClick={() => setCurrentPage(1)}
                            disabled={currentPage === 1}
                            className="px-2 py-1.5 text-xs font-medium rounded-lg border border-gray-300 text-gray-600 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                        >«</button>
                        <button
                            onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
                            disabled={currentPage === 1}
                            className="px-3 py-1.5 text-xs font-medium rounded-lg border border-gray-300 text-gray-600 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                        >Prev</button>
                        {Array.from({ length: totalPages }, (_, i) => i + 1)
                            .filter(p => p === 1 || p === totalPages || Math.abs(p - currentPage) <= 2)
                            .reduce<(number | '...')[]>((acc, p, i, arr) => {
                                if (i > 0 && p - (arr[i - 1] as number) > 1) acc.push('...');
                                acc.push(p);
                                return acc;
                            }, [])
                            .map((item, i) =>
                                item === '...' ? (
                                    <span key={`ellipsis-${i}`} className="px-2 py-1.5 text-xs text-gray-400">…</span>
                                ) : (
                                    <button
                                        key={item}
                                        onClick={() => setCurrentPage(item as number)}
                                        className={`px-3 py-1.5 text-xs font-medium rounded-lg border transition-colors ${
                                            currentPage === item
                                                ? 'bg-blue-600 border-blue-600 text-white'
                                                : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                                        }`}
                                    >{item}</button>
                                )
                            )
                        }
                        <button
                            onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
                            disabled={currentPage === totalPages}
                            className="px-3 py-1.5 text-xs font-medium rounded-lg border border-gray-300 text-gray-600 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                        >Next</button>
                        <button
                            onClick={() => setCurrentPage(totalPages)}
                            disabled={currentPage === totalPages}
                            className="px-2 py-1.5 text-xs font-medium rounded-lg border border-gray-300 text-gray-600 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                        >»</button>
                    </div>
                </div>
            )}

            {/* Frame image modal */}
            {frameModalUrl && (
                <div
                    className="fixed inset-0 z-50 flex items-center justify-center bg-black/70"
                    onClick={() => setFrameModalUrl(null)}
                >
                    <div
                        className="relative bg-white rounded-xl shadow-2xl max-w-4xl w-full mx-4 overflow-hidden"
                        onClick={e => e.stopPropagation()}
                    >
                        {/* Header */}
                        <div className="flex items-center justify-between px-5 py-3 border-b border-gray-200">
                            <h3 className="text-base font-semibold text-gray-900">Violation Frame</h3>
                            <div className="flex items-center gap-3">
                                <a
                                    href={frameModalUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800"
                                >
                                    <ExternalLink className="w-3.5 h-3.5" />
                                    Open full size
                                </a>
                                <button
                                    onClick={() => setFrameModalUrl(null)}
                                    className="text-gray-400 hover:text-gray-600 rounded-full p-1 hover:bg-gray-100"
                                    aria-label="Close"
                                >
                                    <X className="w-5 h-5" />
                                </button>
                            </div>
                        </div>
                        {/* Image */}
                        <div className="bg-gray-950 flex items-center justify-center" style={{ maxHeight: '75vh' }}>
                            {/* eslint-disable-next-line @next/next/no-img-element */}
                            <img
                                src={frameModalUrl}
                                alt="Violation frame"
                                className="object-contain w-full"
                                style={{ maxHeight: '75vh' }}
                            />
                        </div>
                    </div>
                </div>
            )}

            {/* Mark-as-false-positive confirmation modal. Reason is optional but encouraged
                so future maintainers can see WHY a violation was suppressed. */}
            {showMarkModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
                    <div className="bg-white rounded-lg shadow-xl w-full max-w-md mx-4">
                        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200">
                            <h3 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
                                <AlertTriangle className="w-5 h-5 text-yellow-500" />
                                Mark as False Positive
                            </h3>
                            <button
                                onClick={() => { setShowMarkModal(false); setMarkReason(''); }}
                                className="text-gray-400 hover:text-gray-600"
                            >
                                <X className="w-5 h-5" />
                            </button>
                        </div>
                        <div className="px-5 py-4 space-y-3">
                            <p className="text-sm text-gray-600">
                                You are about to flag <span className="font-semibold">{selectedIds.size}</span> violation(s) as false positives.
                                They will be hidden from analytics, compliance and reports, but can be restored later.
                            </p>
                            <div>
                                <label className="block text-xs font-medium text-gray-700 mb-1">
                                    Reason <span className="text-gray-400">(optional)</span>
                                </label>
                                <textarea
                                    value={markReason}
                                    onChange={(e) => setMarkReason(e.target.value)}
                                    rows={3}
                                    placeholder="e.g. mirror reflection, mannequin, test footage…"
                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm text-gray-800 focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                />
                            </div>
                        </div>
                        <div className="px-5 py-3 bg-gray-50 border-t border-gray-200 flex justify-end gap-2 rounded-b-lg">
                            <button
                                onClick={() => { setShowMarkModal(false); setMarkReason(''); }}
                                disabled={bulkBusy}
                                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                            >
                                Cancel
                            </button>
                            <button
                                onClick={handleMark}
                                disabled={bulkBusy}
                                className="px-4 py-2 text-sm font-medium text-white bg-yellow-500 hover:bg-yellow-600 rounded-lg disabled:opacity-50"
                            >
                                {bulkBusy ? 'Marking…' : 'Mark as False Positive'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}