'use client';

import { useState, useEffect, useCallback } from 'react';
import { getTenants } from '@/lib/api/tenants';
import { getSops } from '@/lib/api/sops';
import { assignProactiveRequest, getAllRequests, unassignRequest, type TenantViolationRequestResponse } from '@/lib/api/requests';
import type { TenantResponse, SopResponse } from '@/types/admin';
import { CheckCircle2, Link2, Trash2, ShieldCheck, Building2, Zap } from 'lucide-react';

export default function AssociationsPage() {
    const [tenants, setTenants] = useState<TenantResponse[]>([]);
    const [sops, setSops] = useState<SopResponse[]>([]);
    const [associations, setAssociations] = useState<TenantViolationRequestResponse[]>([]);

    const [isLoading, setIsLoading] = useState(true);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);

    // Form state — now support multi-select
    const [selectedTenantId, setSelectedTenantId] = useState<string>('');
    const [selectedSopIds, setSelectedSopIds] = useState<Set<string>>(new Set());
    const [selectedViolationTypeIds, setSelectedViolationTypeIds] = useState<Set<string>>(new Set());

    const loadData = useCallback(async () => {
        try {
            setIsLoading(true);
            const [tenantsData, sopsData, associationsData] = await Promise.all([
                getTenants(1, 1000), // Get all tenants
                getSops(),
                getAllRequests()
            ]);
            setTenants(tenantsData.tenants);
            setSops(sopsData);
            setAssociations(associationsData.filter(a => a.status === 1)); // Only show approved/active associations
            setError(null);
        } catch (err) {
            setError((err as Error).message || 'Failed to load underlying data');
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setSuccessMessage(null);

        if (!selectedTenantId || selectedViolationTypeIds.size === 0) {
            setError('Please select both a tenant and at least one violation type.');
            return;
        }

        setIsSubmitting(true);
        try {
            // Assign all selected violations to the tenant
            const assignments = Array.from(selectedViolationTypeIds);
            const results = await Promise.all(
                assignments.map(violationId => assignProactiveRequest(selectedTenantId, violationId))
            );

            // Refresh associations
            const updatedAssociations = await getAllRequests();
            setAssociations(updatedAssociations.filter(a => a.status === 1));

            const tenantObj = tenants.find(t => t.id === selectedTenantId);
            setSuccessMessage(`Successfully granted ${tenantObj?.tenantName} access to ${assignments.length} AI Model(s)!`);

            // Reset fields
            setSelectedTenantId('');
            setSelectedSopIds(new Set());
            setSelectedViolationTypeIds(new Set());
        } catch (err) {
            setError((err as Error).message || 'Failed to map violations to tenant');
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleUnassign = async (id: string) => {
        if (!confirm('Are you sure you want to unassign this violation from the tenant? This will also disable it for any cameras using it.')) {
            return;
        }

        try {
            await unassignRequest(id);
            setAssociations(prev => prev.filter(a => a.id !== id));
            setSuccessMessage('Association removed successfully.');
            setTimeout(() => setSuccessMessage(null), 3000);
        } catch (err) {
            setError((err as Error).message || 'Failed to remove association');
        }
    };

    // Get violations from all selected SOPs
    const availableViolations = sops
        .filter(s => selectedSopIds.has(s.id))
        .flatMap(s => (s.violationTypes || []).map(v => ({ ...v, sopName: s.name })));
    
    // Get violation IDs already assigned to the selected tenant
    const tenantAssignedViolationIds = new Set(
        associations
            .filter(a => a.tenantId === selectedTenantId)
            .map(a => a.sopViolationTypeId)
    );
    
    // Filter SOPs to exclude those fully assigned or where all violations are assigned to this tenant
    const availableSops = sops.filter(sop => {
        const sopViolations = sop.violationTypes || [];
        if (sopViolations.length === 0) return true; // Show SOPs with no violations yet
        // Show SOP if at least one violation is NOT assigned to the selected tenant
        return sopViolations.some(v => !tenantAssignedViolationIds.has(v.id));
    });
    
    // Filter available violations to exclude those already assigned to the tenant
    const unassignedAvailableViolations = availableViolations.filter(v => !tenantAssignedViolationIds.has(v.id));
    
    const toggleSopSelection = (sopId: string) => {
        const newSet = new Set(selectedSopIds);
        if (newSet.has(sopId)) {
            newSet.delete(sopId);
            // Clear violations from deselected SOP
            const deselectedSop = sops.find(s => s.id === sopId);
            if (deselectedSop) {
                deselectedSop.violationTypes?.forEach(v => selectedViolationTypeIds.delete(v.id));
            }
        } else {
            newSet.add(sopId);
        }
        setSelectedSopIds(newSet);
    };
    
    const toggleViolationSelection = (violationId: string) => {
        const newSet = new Set(selectedViolationTypeIds);
        if (newSet.has(violationId)) {
            newSet.delete(violationId);
        } else {
            newSet.add(violationId);
        }
        setSelectedViolationTypeIds(newSet);
    };

    return (
        <div className="space-y-6 text-black">
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">Tenant Associations</h1>
                    <p className="text-gray-500 mt-1">Directly bind a violation detection model to a specific tenant.</p>
                </div>
            </div>

            {error && (
                <div className="p-4 bg-red-50 text-red-700 rounded-lg border border-red-100">
                    {error}
                </div>
            )}

            {successMessage && (
                <div className="p-4 bg-emerald-50 text-emerald-700 rounded-lg border border-emerald-100 flex items-center gap-2">
                    <CheckCircle2 className="w-5 h-5" />
                    {successMessage}
                </div>
            )}

            {isLoading ? (
                <div className="text-center py-12 text-gray-500">Loading catalog...</div>
            ) : (
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
                    {/* Assignment Form */}
                    <div className="lg:col-span-1">
                        <div className="bg-white border text-black border-gray-200 rounded-xl overflow-hidden shadow-sm sticky top-6">
                            <div className="p-6">
                                <h2 className="text-lg font-semibold mb-6 flex items-center gap-2">
                                    <Zap className="w-5 h-5 text-blue-500" />
                                    New Association
                                </h2>
                                <form onSubmit={handleSubmit} className="space-y-6">
                                    {/* Tenant Selection */}
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">
                                            Target Tenant
                                        </label>
                                        <select
                                            required
                                            value={selectedTenantId}
                                            onChange={(e) => setSelectedTenantId(e.target.value)}
                                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                                        >
                                            <option value="" disabled>Select a tenant...</option>
                                            {tenants.map(tenant => (
                                                <option key={tenant.id} value={tenant.id}>
                                                    {tenant.tenantName} ({tenant.slug})
                                                </option>
                                            ))}
                                        </select>
                                    </div>

                                    {/* SOP Selection (Multi) */}
                                    <div className="pt-4 border-t border-gray-100">
                                        <label className="block text-sm font-medium text-gray-700 mb-2">
                                            Select SOPs (Multiple)
                                        </label>
                                        <div className="border border-gray-300 rounded-lg p-3 max-h-48 overflow-y-auto bg-white">
                                            {selectedTenantId === '' ? (
                                                <p className="text-sm text-gray-500">Select a tenant first</p>
                                            ) : availableSops.length === 0 ? (
                                                <p className="text-sm text-gray-500">All SOPs already assigned to this tenant</p>
                                            ) : (
                                                availableSops.map(sop => (
                                                    <label key={sop.id} className="flex items-center gap-3 py-2 cursor-pointer hover:bg-gray-50 px-2 rounded">
                                                        <input
                                                            type="checkbox"
                                                            checked={selectedSopIds.has(sop.id)}
                                                            onChange={() => toggleSopSelection(sop.id)}
                                                            className="w-4 h-4 text-blue-600 rounded"
                                                        />
                                                        <span className="text-sm font-medium text-gray-700">{sop.name}</span>
                                                    </label>
                                                ))
                                            )}
                                        </div>
                                        <p className="text-xs text-gray-500 mt-1">Select one or more SOPs</p>
                                    </div>

                                    {/* Violation Type Selection (Multi) */}
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-2">
                                            Select Violation Types (Multiple)
                                        </label>
                                        <div className="border border-gray-300 rounded-lg p-3 max-h-48 overflow-y-auto bg-white">
                                            {unassignedAvailableViolations.length === 0 ? (
                                                <p className="text-sm text-gray-500">Select an SOP first or all violations already assigned</p>
                                            ) : (
                                                unassignedAvailableViolations.map(v => (
                                                    <label key={v.id} className="flex items-center gap-3 py-2 cursor-pointer hover:bg-gray-50 px-2 rounded">
                                                        <input
                                                            type="checkbox"
                                                            checked={selectedViolationTypeIds.has(v.id)}
                                                            onChange={() => toggleViolationSelection(v.id)}
                                                            className="w-4 h-4 text-blue-600 rounded"
                                                        />
                                                        <div>
                                                            <span className="text-xs text-gray-500">{v.sopName}</span>
                                                            <p className="text-sm font-medium text-gray-700">{v.name}</p>
                                                        </div>
                                                    </label>
                                                ))
                                            )}
                                        </div>
                                        <p className="text-xs text-gray-500 mt-1">Select one or more violation types</p>
                                    </div>

                                    <div className="pt-6">
                                        <button
                                            type="submit"
                                            disabled={isSubmitting || !selectedTenantId || selectedViolationTypeIds.size === 0}
                                            className="w-full flex justify-center items-center gap-2 px-4 py-3 text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed font-medium shadow-sm hover:shadow"
                                        >
                                            <Link2 className="w-5 h-5" />
                                            {isSubmitting ? 'Assigning...' : `Assign ${selectedViolationTypeIds.size} Model(s)`}
                                        </button>
                                        <p className="text-xs text-center text-gray-500 mt-3">
                                            This will grant the tenant immediate access to apply these models on their cameras.
                                        </p>
                                    </div>
                                </form>
                            </div>
                        </div>
                    </div>

                    {/* Associations List */}
                    <div className="lg:col-span-2">
                        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
                            <div className="p-6 border-b border-gray-100">
                                <h2 className="text-lg font-semibold flex items-center gap-2">
                                    <ShieldCheck className="w-5 h-5 text-emerald-500" />
                                    Active Tenant Associations
                                </h2>
                            </div>
                            <div className="overflow-x-auto">
                                <table className="w-full text-left">
                                    <thead>
                                        <tr className="bg-gray-50 border-b border-gray-100">
                                            <th className="px-6 py-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">Tenant</th>
                                            <th className="px-6 py-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">SOP / Violation</th>
                                            <th className="px-6 py-4 text-xs font-semibold text-gray-600 uppercase tracking-wider">Assigned At</th>
                                            <th className="px-6 py-4 text-xs font-semibold text-gray-600 uppercase tracking-wider text-right">Action</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-100">
                                        {associations.length === 0 ? (
                                            <tr>
                                                <td colSpan={4} className="px-6 py-12 text-center text-gray-500">
                                                    No active associations found.
                                                </td>
                                            </tr>
                                        ) : (
                                            associations.map((assoc) => (
                                                <tr key={assoc.id} className="hover:bg-gray-50/50 transition-colors">
                                                    <td className="px-6 py-4">
                                                        <div className="flex items-center gap-3">
                                                            <div className="p-2 bg-blue-50 rounded-lg">
                                                                <Building2 className="w-4 h-4 text-blue-600" />
                                                            </div>
                                                            <div>
                                                                <p className="font-medium text-gray-900">{assoc.tenantName}</p>
                                                                <p className="text-xs text-gray-500 font-mono">{assoc.tenantId.split('-')[0]}...</p>
                                                            </div>
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <div>
                                                            <p className="text-xs font-semibold text-blue-600 uppercase mb-0.5">{assoc.sopName}</p>
                                                            <p className="font-medium text-gray-900">{assoc.violationTypeName}</p>
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <p className="text-sm text-gray-600">
                                                            {new Date(assoc.requestedAt).toLocaleDateString()}
                                                        </p>
                                                    </td>
                                                    <td className="px-6 py-4 text-right">
                                                        <button
                                                            onClick={() => handleUnassign(assoc.id)}
                                                            className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-all"
                                                            title="Unassign"
                                                        >
                                                            <Trash2 className="w-5 h-5" />
                                                        </button>
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
        </div>
    );
}
