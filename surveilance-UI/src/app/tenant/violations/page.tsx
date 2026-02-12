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
    const [selectedType, setSelectedType] = useState<string>('all');
    const [selectedSeverity, setSelectedSeverity] = useState<string>('all');

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
            v.type.toLowerCase().includes(searchTerm.toLowerCase());

        const matchesType = selectedType === 'all' || v.type === selectedType;
        const matchesSeverity = selectedSeverity === 'all' || v.severity?.toString() === selectedSeverity;

        return matchesSearch && matchesType && matchesSeverity;
    });

    const getSeverityColor = (severity?: string | number) => {
        if (severity === undefined || severity === null) return 'bg-gray-100 text-gray-800';

        const severityStr = severity.toString().toLowerCase();

        switch (severityStr) {
            case 'high':
            case '2':
                return 'bg-red-100 text-red-800';
            case 'critical':
            case '3':
                return 'bg-red-900 text-white';
            case 'medium':
            case '1':
                return 'bg-yellow-100 text-yellow-800';
            case 'low':
            case '0':
                return 'bg-blue-100 text-blue-800';
            default: return 'bg-gray-100 text-gray-800';
        }
    };

    return (
        <div>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-3xl font-bold text-gray-900">Violations</h2>
            </div>

            {/* Filters */}
            <div className="mb-6 grid grid-cols-1 md:grid-cols-4 gap-4">
                <div className="relative md:col-span-2">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-5 h-5" />
                    <input
                        type="text"
                        placeholder="Search violations..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900"
                    />
                </div>
                <select
                    value={selectedType}
                    onChange={(e) => setSelectedType(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-700"
                >
                    <option value="all">All Types</option>
                    <option value="Safety">Safety</option>
                    <option value="Security">Security</option>
                    <option value="Operational">Operational</option>
                    <option value="Compliance">Compliance</option>
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
                                        Snapshot
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Frame URL
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        Type
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
                                {filteredViolations.map((violation) => (
                                    <tr key={violation.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            {violation.framePath ? (
                                                <div className="h-12 w-20 relative rounded overflow-hidden">
                                                    {/* Ideally use image hosting URL here */}
                                                    <div className="absolute inset-0 bg-gray-200 flex items-center justify-center text-xs text-gray-500">
                                                        Img
                                                    </div>
                                                </div>
                                            ) : (
                                                <div className="h-12 w-20 bg-gray-200 rounded flex items-center justify-center text-xs text-gray-500">
                                                    No Img
                                                </div>
                                            )}
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
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                            {violation.type}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className={`px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${getSeverityColor(violation.severity)}`}>
                                                {violation.severity || 'Unknown'}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {violation.cameraId || 'N/A'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {new Date(violation.timestamp).toLocaleString()}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap">
                                            <span className="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-yellow-100 text-yellow-800">
                                                {violation.status || 'Pending'}
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
