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

    // Form state
    const [selectedTenantId, setSelectedTenantId] = useState<string>('');
    const [selectedSopId, setSelectedSopId] = useState<string>('');
    const [selectedViolationTypeId, setSelectedViolationTypeId] = useState<string>('');

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

        if (!selectedTenantId || !selectedViolationTypeId) {
            setError('Please select both a tenant and a violation type.');
            return;
        }

        setIsSubmitting(true);
        try {
            await assignProactiveRequest(selectedTenantId, selectedViolationTypeId);

            // Refresh associations
            const updatedAssociations = await getAllRequests();
            setAssociations(updatedAssociations.filter(a => a.status === 1));

            const tenantObj = tenants.find(t => t.id === selectedTenantId);
            const sopObj = sops.find(s => s.id === selectedSopId);
            const violationObj = sopObj?.violationTypes?.find(v => v.id === selectedViolationTypeId);

            setSuccessMessage(`Successfully granted ${tenantObj?.tenantName} access to the ${violationObj?.name} AI Model!`);

            // Reset fields
            setSelectedTenantId('');
            setSelectedSopId('');
            setSelectedViolationTypeId('');
        } catch (err) {
            setError((err as Error).message || 'Failed to map violation to tenant');
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

    const activeSop = sops.find(s => s.id === selectedSopId);
    const availableViolations = activeSop?.violationTypes || [];

    const isDuplicate = associations.some(a =>
        a.tenantId === selectedTenantId && a.sopViolationTypeId === selectedViolationTypeId
    );

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

                                    {/* SOP Selection */}
                                    <div className="pt-4 border-t border-gray-100">
                                        <label className="block text-sm font-medium text-gray-700 mb-1">
                                            Standard Operating Procedure
                                        </label>
                                        <select
                                            required
                                            value={selectedSopId}
                                            onChange={(e) => {
                                                setSelectedSopId(e.target.value);
                                                setSelectedViolationTypeId(''); // Reset violation dropdown
                                            }}
                                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                                        >
                                            <option value="" disabled>Filter by SOP Category...</option>
                                            {sops.map(sop => (
                                                <option key={sop.id} value={sop.id}>
                                                    {sop.name}
                                                </option>
                                            ))}
                                        </select>
                                    </div>

                                    {/* Violation Type Selection */}
                                    {selectedSopId && (
                                        <div>
                                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                                AI Violation Model
                                            </label>
                                            <select
                                                required
                                                value={selectedViolationTypeId}
                                                onChange={(e) => setSelectedViolationTypeId(e.target.value)}
                                                className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                                            >
                                                <option value="" disabled>Select a specific violation...</option>
                                                {availableViolations.map(violation => (
                                                    <option key={violation.id} value={violation.id}>
                                                        {violation.name} ({violation.modelIdentifier})
                                                    </option>
                                                ))}
                                            </select>
                                            {availableViolations.length === 0 && (
                                                <p className="text-xs text-red-500 mt-2">There are no violations configured under this SOP yet.</p>
                                            )}
                                        </div>
                                    )}

                                    <div className="pt-6">
                                        <button
                                            type="submit"
                                            disabled={isSubmitting || !selectedTenantId || !selectedViolationTypeId || isDuplicate}
                                            className="w-full flex justify-center items-center gap-2 px-4 py-3 text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed font-medium shadow-sm hover:shadow"
                                        >
                                            <Link2 className="w-5 h-5" />
                                            {isSubmitting ? 'Assigning...' : isDuplicate ? 'Already Assigned' : 'Assign AI Model'}
                                        </button>
                                        <p className="text-xs text-center text-gray-500 mt-3">
                                            {isDuplicate
                                                ? 'This tenant already has access to this AI model.'
                                                : 'This will grant the tenant immediate access to apply this model on their cameras.'}
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
