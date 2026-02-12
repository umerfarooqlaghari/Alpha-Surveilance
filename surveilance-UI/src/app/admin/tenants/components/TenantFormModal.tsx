'use client';

import { useState, useEffect } from 'react';
import { X } from 'lucide-react';
import { createTenant, updateTenant, uploadTenantLogo } from '@/lib/api/tenants';
import type { TenantResponse } from '@/types/admin';

interface TenantFormModalProps {
    tenant?: TenantResponse | null;
    onClose: () => void;
}

export default function TenantFormModal({ tenant, onClose }: TenantFormModalProps) {
    const [formData, setFormData] = useState({
        tenantName: '',
        slug: '',
        employeeCount: 0,
        address: '',
        city: '',
        country: '',
        industry: '',
    });
    const [logoFile, setLogoFile] = useState<File | null>(null);
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (tenant) {
            setFormData({
                tenantName: tenant.tenantName,
                slug: tenant.slug,
                employeeCount: tenant.employeeCount,
                address: tenant.address,
                city: tenant.city,
                country: tenant.country,
                industry: tenant.industry,
            });
        }
    }, [tenant]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);

        try {
            if (tenant) {
                // Update existing tenant
                await updateTenant(tenant.id, formData);

                // Upload logo if selected
                if (logoFile) {
                    await uploadTenantLogo(tenant.id, logoFile);
                }

                alert('Tenant updated successfully');
            } else {
                // Create new tenant
                const newTenant = await createTenant(formData);

                // Upload logo if selected
                if (logoFile) {
                    await uploadTenantLogo(newTenant.id, logoFile);
                }

                alert('Tenant created successfully');
            }

            onClose();
        } catch (error: any) {
            console.error('Failed to save tenant:', error);
            alert(error.message || 'Failed to save tenant');
        } finally {
            setLoading(false);
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setFormData(prev => ({
            ...prev,
            [name]: name === 'employeeCount' ? parseInt(value) || 0 : value
        }));
    };

    const handleLogoChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files[0]) {
            setLogoFile(e.target.files[0]);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto border border-white/20">
                {/* Header */}
                <div className="flex justify-between items-center p-6 border-b border-gray-200/50">
                    <h3 className="text-2xl font-bold text-gray-900">
                        {tenant ? 'Edit Tenant' : 'Add New Tenant'}
                    </h3>
                    <button
                        onClick={onClose}
                        className="text-gray-500 hover:text-gray-700 transition-colors bg-gray-100/50 hover:bg-gray-100 p-2 rounded-full"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Tenant Name *
                            </label>
                            <input
                                type="text"
                                name="tenantName"
                                value={formData.tenantName}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="e.g. Acme Corp"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Slug *
                            </label>
                            <input
                                type="text"
                                name="slug"
                                value={formData.slug}
                                onChange={handleChange}
                                required
                                disabled={!!tenant}
                                pattern="[a-z0-9-]+"
                                title="Only lowercase letters, numbers, and hyphens"
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300 disabled:bg-gray-50 disabled:text-gray-500"
                                placeholder="e.g. acme-corp"
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Employee Count *
                            </label>
                            <input
                                type="number"
                                name="employeeCount"
                                value={formData.employeeCount}
                                onChange={handleChange}
                                required
                                min="1"
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Industry *
                            </label>
                            <input
                                type="text"
                                name="industry"
                                value={formData.industry}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="e.g. Technology"
                            />
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Address *
                        </label>
                        <input
                            type="text"
                            name="address"
                            value={formData.address}
                            onChange={handleChange}
                            required
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                            placeholder="Full street address"
                        />
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                City *
                            </label>
                            <input
                                type="text"
                                name="city"
                                value={formData.city}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="City name"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Country *
                            </label>
                            <input
                                type="text"
                                name="country"
                                value={formData.country}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="Country name"
                            />
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Logo
                        </label>
                        <div className="mt-1 flex justify-center px-6 pt-5 pb-6 border-2 border-gray-300 border-dashed rounded-xl hover:border-blue-500 transition-colors bg-gray-50/50">
                            <div className="space-y-1 text-center">
                                <svg
                                    className="mx-auto h-12 w-12 text-gray-400"
                                    stroke="currentColor"
                                    fill="none"
                                    viewBox="0 0 48 48"
                                    aria-hidden="true"
                                >
                                    <path
                                        d="M28 8H12a4 4 0 00-4 4v20m32-12v8m0 0v8a4 4 0 01-4 4H12a4 4 0 01-4-4v-4m32-4l-3.172-3.172a4 4 0 00-5.656 0L28 28M8 32l9.172-9.172a4 4 0 015.656 0L28 28m0 0l4 4m4-24h8m-4-4v8m-12 4h.02"
                                        strokeWidth={2}
                                        strokeLinecap="round"
                                        strokeLinejoin="round"
                                    />
                                </svg>
                                <div className="flex text-sm text-gray-600 justify-center">
                                    <label
                                        htmlFor="file-upload"
                                        className="relative cursor-pointer bg-white rounded-md font-medium text-blue-600 hover:text-blue-500 focus-within:outline-none focus-within:ring-2 focus-within:ring-offset-2 focus-within:ring-blue-500"
                                    >
                                        <span>Upload a file</span>
                                        <input
                                            id="file-upload"
                                            name="file-upload"
                                            type="file"
                                            className="sr-only"
                                            accept="image/*"
                                            onChange={handleLogoChange}
                                        />
                                    </label>
                                    <p className="pl-1">or drag and drop</p>
                                </div>
                                <p className="text-xs text-gray-500">
                                    PNG, JPG, GIF up to 5MB
                                </p>
                                {logoFile && (
                                    <p className="text-sm text-green-600 font-medium mt-2">
                                        Selected: {logoFile.name}
                                    </p>
                                )}
                            </div>
                        </div>
                    </div>

                    {/* Actions */}
                    <div className="flex justify-end gap-3 pt-6 border-t border-gray-200/50">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-6 py-2.5 text-gray-700 bg-white border border-gray-300 rounded-xl hover:bg-gray-50 hover:text-gray-900 transition-all font-medium shadow-sm"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={loading}
                            className="px-6 py-2.5 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all font-medium shadow-md shadow-blue-500/20 disabled:opacity-50 disabled:shadow-none"
                        >
                            {loading ? 'Saving...' : tenant ? 'Update Tenant' : 'Create Tenant'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
