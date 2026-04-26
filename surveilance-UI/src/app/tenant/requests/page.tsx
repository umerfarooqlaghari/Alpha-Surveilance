'use client';

import { useState, useEffect, useCallback } from 'react';
import { getTenantAvailableSops, getMySopRequests, requestSopViolation } from '@/lib/api/tenant/sops';
import type { SopResponse } from '@/types/admin';
import type { TenantViolationRequestResponse } from '@/lib/api/requests';
import { Search, ChevronDown, ChevronRight, CheckCircle2, Clock, Send, ShieldPlus, AlertCircle } from 'lucide-react';

export default function TenantRequestsPage() {
    const [sops, setSops] = useState<SopResponse[]>([]);
    const [myRequests, setMyRequests] = useState<TenantViolationRequestResponse[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isSubmitting, setIsSubmitting] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState<string | null>(null);
    const [searchTerm, setSearchTerm] = useState('');
    const [expandedSops, setExpandedSops] = useState<Set<string>>(new Set());

    const loadData = useCallback(async () => {
        try {
            setIsLoading(true);
            const [sopsData, requestsData] = await Promise.all([
                getTenantAvailableSops(),
                getMySopRequests()
            ]);
            setSops(sopsData);
            setMyRequests(requestsData);
            setError(null);
        } catch (err) {
            setError((err as Error).message || 'Failed to load catalog');
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleRequest = async (violationTypeId: string, name: string) => {
        setIsSubmitting(violationTypeId);
        setError(null);
        setSuccess(null);
        try {
            await requestSopViolation(violationTypeId);
            setSuccess(`Request for "${name}" submitted successfully!`);
            const updatedRequests = await getMySopRequests();
            setMyRequests(updatedRequests);
        } catch (err) {
            setError((err as Error).message || 'Failed to submit request');
        } finally {
            setIsSubmitting(null);
        }
    };

    const toggleSop = (id: string) => {
        const next = new Set(expandedSops);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        setExpandedSops(next);
    };

    const getRequestStatus = (violationTypeId: string) => {
        return myRequests.find(r => r.sopViolationTypeId === violationTypeId);
    };

    const filteredSops = sops.filter(sop =>
        sop.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
        sop.violationTypes?.some(v => v.name.toLowerCase().includes(searchTerm.toLowerCase()))
    );

    if (isLoading) return <div className="text-center py-20 text-gray-500">Loading catalog...</div>;

    return (
        <div className="max-w-5xl mx-auto space-y-8 animate-in fade-in duration-500 text-black">
            <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
                <div>
                    <h1 className="text-3xl font-bold text-gray-900 tracking-tight">AI Model Library</h1>
                    <p className="text-gray-500 mt-1">Discover and request access to standardized safety & security detection models.</p>
                </div>
                <div className="relative w-full md:w-72">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                    <input
                        type="text"
                        placeholder="Search models..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 outline-none transition-all placeholder:text-gray-400"
                    />
                </div>
            </header>

            {(error || success) && (
                <div className={`p-4 rounded-xl border flex items-center gap-3 animate-in slide-in-from-top-2 ${error ? 'bg-red-50 border-red-100 text-red-700' : 'bg-emerald-50 border-emerald-100 text-emerald-700'
                    }`}>
                    {error ? <AlertCircle className="w-5 h-5" /> : <CheckCircle2 className="w-5 h-5" />}
                    <span className="font-medium">{error || success}</span>
                </div>
            )}

            <div className="grid gap-4">
                {filteredSops.length === 0 ? (
                    <div className="text-center py-20 bg-white rounded-3xl border border-dashed border-gray-200">
                        <ShieldPlus className="w-12 h-12 text-gray-300 mx-auto mb-4" />
                        <p className="text-gray-500 font-medium">No procedures found Matching your search.</p>
                    </div>
                ) : (
                    filteredSops.map(sop => (
                        <div key={sop.id} className="bg-white rounded-2xl border border-gray-200 overflow-hidden shadow-sm hover:shadow-md transition-all duration-300 group">
                            <div
                                className="p-5 flex items-center justify-between cursor-pointer select-none bg-white"
                                onClick={() => toggleSop(sop.id)}
                            >
                                <div className="flex items-center gap-4">
                                    <div className={`p-2 rounded-lg transition-colors ${expandedSops.has(sop.id) ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-500'}`}>
                                        <AlertCircle className="w-5 h-5" />
                                    </div>
                                    <div>
                                        <h3 className="text-lg font-bold text-gray-900">{sop.name}</h3>
                                        <p className="text-sm text-gray-500 line-clamp-1">{sop.description}</p>
                                    </div>
                                </div>
                                <div className="text-gray-400 group-hover:text-blue-600 transition-colors">
                                    {expandedSops.has(sop.id) ? <ChevronDown /> : <ChevronRight />}
                                </div>
                            </div>

                            {expandedSops.has(sop.id) && (
                                <div className="border-t border-gray-100 bg-gray-50/30 p-5 pt-2">
                                    <h4 className="text-[10px] font-bold text-gray-400 uppercase tracking-widest mb-4 mt-2 px-1">Available Detection Models</h4>
                                    <div className="grid gap-3">
                                        {sop.violationTypes?.map(v => {
                                            const request = getRequestStatus(v.id);
                                            const isPending = request?.status === 0;
                                            const isApproved = request?.status === 1;

                                            return (
                                                <div key={v.id} className="flex items-center justify-between p-4 bg-white rounded-xl border border-gray-100 shadow-sm">
                                                    <div>
                                                        <h5 className="font-bold text-gray-900">{v.name}</h5>
                                                        <p className="text-xs text-gray-500 mt-1 max-w-lg">{v.description}</p>
                                                        <div className="mt-2 flex items-center gap-2 flex-wrap">
                                                            <span className="text-[10px] font-mono px-2 py-0.5 bg-gray-100 text-gray-600 rounded border border-gray-200 uppercase">
                                                                {v.modelIdentifier}
                                                            </span>
                                                            {v.triggerLabels && v.triggerLabels.split(',').map(l => l.trim()).filter(Boolean).map(label => (
                                                                <span key={label} className="text-[10px] font-mono px-1.5 py-0.5 bg-blue-50 text-blue-700 rounded border border-blue-100 font-bold">
                                                                    {label}
                                                                </span>
                                                            ))}
                                                        </div>
                                                    </div>

                                                    <div className="flex-shrink-0 ml-4">
                                                        {isApproved ? (
                                                            <div className="flex items-center gap-2 px-4 py-2 bg-emerald-50 text-emerald-700 rounded-lg border border-emerald-100 font-bold text-sm">
                                                                <CheckCircle2 className="w-4 h-4" />
                                                                Approved
                                                            </div>
                                                        ) : isPending ? (
                                                            <div className="flex items-center gap-2 px-4 py-2 bg-amber-50 text-amber-700 rounded-lg border border-amber-100 font-bold text-sm">
                                                                <Clock className="w-4 h-4" />
                                                                Pending
                                                            </div>
                                                        ) : (
                                                            <button
                                                                onClick={() => handleRequest(v.id, v.name)}
                                                                disabled={isSubmitting === v.id}
                                                                className="flex items-center gap-2 px-5 py-2.5 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all font-bold text-sm shadow-lg shadow-blue-500/20 disabled:opacity-50 active:scale-[0.98]"
                                                            >
                                                                {isSubmitting === v.id ? <Clock className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                                                                Request Access
                                                            </button>
                                                        )}
                                                    </div>
                                                </div>
                                            );
                                        })}
                                        {(!sop.violationTypes || sop.violationTypes.length === 0) && (
                                            <p className="text-sm text-gray-400 italic px-2">No violation models available for this SOP yet.</p>
                                        )}
                                    </div>
                                </div>
                            )}
                        </div>
                    ))
                )}
            </div>
        </div>
    );
}
