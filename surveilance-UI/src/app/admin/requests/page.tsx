'use client';

import { useState, useEffect, useCallback } from 'react';
import { CheckCircle2, XCircle, Search, Filter } from 'lucide-react';
import { getPendingRequests, resolveRequest } from '@/lib/api/requests';
import type { TenantViolationRequestResponse } from '@/lib/api/requests';

export default function RequestsPage() {
    const [requests, setRequests] = useState<TenantViolationRequestResponse[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const [isProcessing, setIsProcessing] = useState<string | null>(null); // Stores ID of request being processed

    const loadRequests = useCallback(async () => {
        try {
            setIsLoading(true);
            const data = await getPendingRequests();
            setRequests(data);
            setError(null);
        } catch (err) {
            setError((err as Error).message || 'Failed to load pending requests');
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => {
        loadRequests();
    }, [loadRequests]);

    const handleResolve = async (id: string, status: number) => {
        setIsProcessing(id);
        try {
            await resolveRequest(id, status);
            // Remove the resolved request from the UI locally rather than reloading everything
            setRequests(prev => prev.filter(r => r.id !== id));
        } catch (err) {
            alert((err as Error).message || 'Failed to process request');
        } finally {
            setIsProcessing(null);
        }
    };

    return (
        <div className="space-y-6 text-black">
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">Tenant Requests</h1>
                    <p className="text-gray-500 mt-1">Review and approve tenant requests for specific Standard AI Models.</p>
                </div>
            </div>

            {error && (
                <div className="p-4 bg-red-50 text-red-700 rounded-lg border border-red-100">
                    {error}
                </div>
            )}

            {isLoading ? (
                <div className="text-center py-12 text-gray-500">Loading pending requests...</div>
            ) : requests.length === 0 ? (
                <div className="text-center py-12 bg-white rounded-xl border border-gray-200 shadow-sm flex flex-col items-center">
                    <div className="bg-gray-50 p-4 rounded-full mb-4">
                        <CheckCircle2 className="w-8 h-8 text-emerald-500" />
                    </div>
                    <h3 className="text-lg font-medium text-gray-900">All caught up!</h3>
                    <p className="text-gray-500 mt-1">There are no pending tenant requests waiting for approval.</p>
                </div>
            ) : (
                <div className="bg-white border text-black border-gray-200 rounded-xl overflow-hidden shadow-sm">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Requested
                                </th>
                                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Tenant (Client)
                                </th>
                                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Requested Standard (SOP)
                                </th>
                                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Violation Type (Model)
                                </th>
                                <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Action
                                </th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {requests.map((request) => (
                                <tr key={request.id} className="hover:bg-gray-50 transition-colors">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {new Date(request.requestedAt).toLocaleDateString()} <br />
                                        <span className="text-xs text-gray-400">{new Date(request.requestedAt).toLocaleTimeString()}</span>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <div className="text-sm font-medium text-gray-900">
                                            {request.tenantName || 'Unknown Tenant'}
                                        </div>
                                        <div className="text-[10px] text-gray-400 font-mono">
                                            {request.tenantId}
                                        </div>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <div className="text-sm font-medium text-gray-900">
                                            {request.sopName || 'Unknown SOP'}
                                        </div>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <span className="px-2.5 py-1 text-xs font-medium text-blue-700 bg-blue-50 rounded-lg border border-blue-100">
                                            {request.violationTypeName || 'Unknown Model'}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                        <div className="flex justify-end gap-2">
                                            <button
                                                onClick={() => handleResolve(request.id, 2)}
                                                disabled={isProcessing === request.id}
                                                className="flex items-center gap-1.5 px-3 py-1.5 text-red-700 bg-red-50 hover:bg-red-100 rounded-lg transition-colors border border-red-200 disabled:opacity-50"
                                            >
                                                <XCircle className="w-4 h-4" />
                                                Reject
                                            </button>
                                            <button
                                                onClick={() => handleResolve(request.id, 1)}
                                                disabled={isProcessing === request.id}
                                                className="flex items-center gap-1.5 px-3 py-1.5 text-emerald-700 bg-emerald-50 hover:bg-emerald-100 rounded-lg transition-colors border border-emerald-200 disabled:opacity-50"
                                            >
                                                <CheckCircle2 className="w-4 h-4" />
                                                Approve
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}
