'use client';

import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Loader2, X } from 'lucide-react';
import type { Location, CreateLocationRequest, UpdateLocationRequest } from '@/types/location';
import { createLocation, updateLocation } from '@/lib/api/tenant/locations';

interface LocationFormModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
    location?: Location | null;
}

type FormValues = {
    name: string;
    code: string;
    address?: string;
    city?: string;
    country?: string;
    timezone?: string;
    status?: number;
};

export default function LocationFormModal({ isOpen, onClose, onSuccess, location }: LocationFormModalProps) {
    const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<FormValues>({
        defaultValues: { name: '', code: '' },
    });
    const [serverError, setServerError] = useState<string | null>(null);

    useEffect(() => {
        if (!isOpen) return;
        if (location) {
            reset({
                name: location.name,
                code: location.code,
                address: location.address ?? '',
                city: location.city ?? '',
                country: location.country ?? '',
                timezone: location.timezone ?? '',
                status: location.status === 'Inactive' ? 1 : 0,
            });
        } else {
            reset({ name: '', code: '', address: '', city: '', country: '', timezone: '', status: 0 });
        }
        setServerError(null);
    }, [isOpen, location, reset]);

    if (!isOpen) return null;

    const onSubmit = async (data: FormValues) => {
        setServerError(null);
        try {
            if (location) {
                const payload: UpdateLocationRequest = {
                    name: data.name,
                    code: data.code,
                    address: data.address,
                    city: data.city,
                    country: data.country,
                    timezone: data.timezone,
                    status: data.status,
                };
                await updateLocation(location.id, payload);
            } else {
                const payload: CreateLocationRequest = {
                    name: data.name,
                    code: data.code,
                    address: data.address,
                    city: data.city,
                    country: data.country,
                    timezone: data.timezone,
                };
                await createLocation(payload);
            }
            onSuccess();
            onClose();
        } catch (e: any) {
            setServerError(e?.message || 'Failed to save location');
        }
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-2xl p-8 relative max-h-[90vh] overflow-y-auto border border-white/20">
                <button onClick={onClose} className="absolute top-4 right-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100/50 rounded-full">
                    <X className="w-5 h-5" />
                </button>

                <h2 className="text-2xl font-bold mb-6 bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">
                    {location ? 'Edit Location' : 'Add New Location'}
                </h2>

                <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                        <div className="space-y-1.5">
                            <label className="text-sm font-semibold text-gray-700">Name <span className="text-red-500">*</span></label>
                            <input
                                {...register('name', { required: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                                placeholder="HQ — North Wing"
                            />
                            {errors.name && <span className="text-red-500 text-xs">Required</span>}
                        </div>
                        <div className="space-y-1.5">
                            <label className="text-sm font-semibold text-gray-700">Code <span className="text-red-500">*</span></label>
                            <input
                                {...register('code', { required: true, maxLength: 50 })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black font-mono"
                                placeholder="HQ-N"
                            />
                            {errors.code && <span className="text-red-500 text-xs">Required (max 50)</span>}
                        </div>
                    </div>

                    <div className="space-y-1.5">
                        <label className="text-sm font-medium text-gray-600">Address</label>
                        <input
                            {...register('address')}
                            className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                            placeholder="123 Main St."
                        />
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
                        <div className="space-y-1.5">
                            <label className="text-sm font-medium text-gray-600">City</label>
                            <input
                                {...register('city')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                            />
                        </div>
                        <div className="space-y-1.5">
                            <label className="text-sm font-medium text-gray-600">Country</label>
                            <input
                                {...register('country')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                            />
                        </div>
                        <div className="space-y-1.5">
                            <label className="text-sm font-medium text-gray-600">Timezone</label>
                            <input
                                {...register('timezone')}
                                placeholder="Asia/Karachi"
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                            />
                        </div>
                    </div>

                    {location && (
                        <div className="space-y-1.5">
                            <label className="text-sm font-medium text-gray-600">Status</label>
                            <select
                                {...register('status', { valueAsNumber: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                            >
                                <option value={0}>Active</option>
                                <option value={1}>Inactive</option>
                            </select>
                        </div>
                    )}

                    {serverError && (
                        <div className="px-4 py-3 bg-red-50 border border-red-100 text-red-700 text-sm rounded-xl">
                            {serverError}
                        </div>
                    )}

                    <div className="flex justify-end gap-3 pt-4 border-t border-gray-100">
                        <button type="button" onClick={onClose} className="px-6 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-xl">
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={isSubmitting}
                            className="px-6 py-2.5 text-sm font-medium bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 shadow-lg shadow-blue-500/30 disabled:opacity-50 flex items-center gap-2"
                        >
                            {isSubmitting && <Loader2 className="w-4 h-4 animate-spin" />}
                            {location ? 'Update Location' : 'Create Location'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
