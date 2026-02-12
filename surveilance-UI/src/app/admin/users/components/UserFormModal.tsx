'use client';

import { useState, useEffect } from 'react';
import { X } from 'lucide-react';
import { createUser, updateUser } from '@/lib/api/users';
import type { UserResponse, TenantResponse } from '@/types/admin';

interface UserFormModalProps {
    user?: UserResponse | null;
    tenants: TenantResponse[];
    onClose: () => void;
}

export default function UserFormModal({ user, tenants, onClose }: UserFormModalProps) {
    const [formData, setFormData] = useState({
        tenantId: '',
        fullName: '',
        email: '',
        phoneNumber: '',
        employeeCode: '',
        designation: '',
        password: '',
    });
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (user) {
            setFormData({
                tenantId: user.tenantId || '',
                fullName: user.fullName,
                email: user.email,
                phoneNumber: user.phoneNumber,
                employeeCode: user.employeeCode || '',
                designation: user.designation || '',
                password: '',
            });
        }
    }, [user]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);

        try {
            if (user) {
                // Update existing user
                await updateUser(user.id, {
                    fullName: formData.fullName,
                    phoneNumber: formData.phoneNumber,
                    employeeCode: formData.employeeCode,
                    designation: formData.designation,
                });
                alert('User updated successfully');
            } else {
                // Create new user
                await createUser({
                    ...formData,
                    tenantId: formData.tenantId || undefined,
                    roleIds: [], // TODO: Add role selection
                });
                alert('User created successfully');
            }

            onClose();
        } catch (error: any) {
            console.error('Failed to save user:', error);
            alert(error.message || 'Failed to save user');
        } finally {
            setLoading(false);
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto border border-white/20">
                {/* Header */}
                <div className="flex justify-between items-center p-6 border-b border-gray-200/50">
                    <h3 className="text-2xl font-bold text-gray-900">
                        {user ? 'Edit User' : 'Add New User'}
                    </h3>
                    <button onClick={onClose} className="text-gray-500 hover:text-gray-700 transition-colors bg-gray-100/50 hover:bg-gray-100 p-2 rounded-full">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Tenant {!user && '*'}
                        </label>
                        <div className="relative">
                            <select
                                name="tenantId"
                                value={formData.tenantId}
                                onChange={handleChange}
                                required={!user}
                                disabled={!!user}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 font-medium transition-all hover:border-gray-300 disabled:bg-gray-50 disabled:text-gray-500 appearance-none"
                            >
                                <option value="">SuperAdmin (No Tenant)</option>
                                {tenants.map(tenant => (
                                    <option key={tenant.id} value={tenant.id}>
                                        {tenant.tenantName}
                                    </option>
                                ))}
                            </select>
                            <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-4 text-gray-500">
                                <svg className="h-4 w-4 fill-current" viewBox="0 0 20 20">
                                    <path d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" />
                                </svg>
                            </div>
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Full Name *
                            </label>
                            <input
                                type="text"
                                name="fullName"
                                value={formData.fullName}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="John Doe"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Email *
                            </label>
                            <input
                                type="email"
                                name="email"
                                value={formData.email}
                                onChange={handleChange}
                                required
                                disabled={!!user}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300 disabled:bg-gray-50 disabled:text-gray-500"
                                placeholder="john@example.com"
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Phone Number *
                            </label>
                            <input
                                type="tel"
                                name="phoneNumber"
                                value={formData.phoneNumber}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="+1 (555) 000-0000"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Employee Code
                            </label>
                            <input
                                type="text"
                                name="employeeCode"
                                value={formData.employeeCode}
                                onChange={handleChange}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="EMP-001"
                            />
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Designation
                        </label>
                        <input
                            type="text"
                            name="designation"
                            value={formData.designation}
                            onChange={handleChange}
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                            placeholder="e.g. Senior Manager"
                        />
                    </div>

                    {!user && (
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Password *
                            </label>
                            <input
                                type="password"
                                name="password"
                                value={formData.password}
                                onChange={handleChange}
                                required
                                minLength={8}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="••••••••"
                            />
                            <p className="text-xs text-gray-500 mt-2 ml-1">
                                Minimum 8 characters
                            </p>
                        </div>
                    )}

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
                            {loading ? 'Saving...' : user ? 'Update User' : 'Create User'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
