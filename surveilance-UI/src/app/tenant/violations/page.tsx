'use client';

import { useState, useEffect } from 'react';
import { Search, Eye, Filter, ExternalLink } from 'lucide-react';
import { getViolations } from '@/lib/api/tenant/violations';
import type { Violation } from '@/lib/api/tenant/violations';
import { useAuth } from '@/contexts/AuthContext';
import Image from 'next/image';

export default function TenantViolationsPage() {
    const { tenant } = useAuth();
    const [violations, setViolations] = useState<Violation[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedModel, setSelectedModel] = useState<string>('all');
    const [selectedSeverity, setSelectedSeverity] = useState<string>('all');
    const [selectedCamera, setSelectedCamera] = useState<string>('all');
    const [selectedStatus, setSelectedStatus] = useState<string>('all');

    const loadData = async () => {
        try {
            setLoading(true);
            const data = await getViolations();
            setViolations(data);
        } catch (error) {
            console.error('Failed to load violations:', error);
            // alert('Failed to load violations');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, []);

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

        return matchesSearch && matchesModel && matchesSeverity && matchesCamera && matchesStatus;
    });

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
            <div className="mb-6 grid grid-cols-1 md:grid-cols-3 lg:grid-cols-5 gap-4">
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
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Actions
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {filteredViolations.map((violation, index) => (
                                    <tr key={violation.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {index + 1}
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
                                            {violation.employee ? (
                                                <div>
                                                    <div className="font-medium text-gray-900">{violation.employee.firstName} {violation.employee.lastName}</div>
                                                    <div className="text-xs text-gray-500">{violation.employee.employeeId}</div>
                                                </div>
                                            ) : violation.metadataJson && violation.metadataJson.includes('"isUnauthorized": true') ? (
                                                <span className="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-red-100 text-red-800">
                                                    ⚠️ Unauthorized
                                                </span>
                                            ) : (
                                                <span className="text-gray-400">Unknown</span>
                                            )}
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
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                            <button className="text-blue-600 hover:text-blue-900">
                                                <Eye className="w-5 h-5" />
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
}
