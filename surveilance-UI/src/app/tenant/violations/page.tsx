'use client';

import { useState, useEffect, useRef } from 'react';
import { Search, Eye, Filter, ExternalLink } from 'lucide-react';
import { getViolations } from '@/lib/api/tenant/violations';
import type { Violation } from '@/lib/api/tenant/violations';
import { useAuth } from '@/contexts/AuthContext';
import { useViolationHub } from '@/hooks/useViolationHub';
import Image from 'next/image';

export default function TenantViolationsPage() {
    const { tenant } = useAuth();
    const { notifications } = useViolationHub();
    const [violations, setViolations] = useState<Violation[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedModel, setSelectedModel] = useState<string>('all');
    const [selectedSeverity, setSelectedSeverity] = useState<string>('all');
    const [selectedCamera, setSelectedCamera] = useState<string>('all');
    const [selectedStatus, setSelectedStatus] = useState<string>('all');
    const [dateFrom, setDateFrom] = useState<string>('');
    const [dateTo, setDateTo] = useState<string>('');
    const [currentPage, setCurrentPage] = useState(1);
    const PAGE_SIZE = 25;

    const loadData = async (showSpinner = true) => {
        try {
            if (showSpinner) setLoading(true);
            const data = await getViolations();
            setViolations(data);
        } catch (error) {
            console.error('Failed to load violations:', error);
            // alert('Failed to load violations');
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

    const filteredViolations = violations.filter(v => {
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

    const uniqueCameras = Array.from(new Set(violations.map(v => v.cameraName).filter(Boolean)));
    const uniqueModels = Array.from(new Set(violations.map(v => v.modelIdentifier).filter(Boolean)));
    const statuses = ['Pending', 'Audited', 'FailedAudit'];

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
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        #
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Frame URL
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
                                    <tr key={violation.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {(currentPage - 1) * PAGE_SIZE + index + 1}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-blue-600">
                                            {violation.framePath ? (
                                                <a
                                                    href={violation.framePath}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className="flex items-center gap-1 hover:text-blue-800"
                                                    title={violation.framePath}
                                                >
                                                    <ExternalLink className="w-4 h-4" />
                                                    <span className="truncate max-w-[150px]">Open Link</span>
                                                </a>
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
                                                const isPersonRelated = violation.metadataJson?.includes('"person_box"');
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
                                            <span className={`px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${getStatusColor(violation.status)}`}>
                                                {violation.status === '0' ? 'Pending' : violation.status === '1' ? 'Audited' : violation.status === '2' ? 'FailedAudit' : (violation.status || 'Pending')}
                                            </span>
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
            )}        </div>
    );
}
