'use client';

import { useState, useEffect } from 'react';
import { X } from 'lucide-react';
import type { CameraResponse, CreateCameraRequest, UpdateCameraRequest } from '@/types/admin';

interface CameraFormModalProps {
    camera?: CameraResponse | null;
    tenantId: string;
    onClose: () => void;
    onCreate: (data: CreateCameraRequest) => Promise<CameraResponse>;
    onUpdate: (id: string, data: UpdateCameraRequest) => Promise<CameraResponse>;
}

export default function CameraFormModal({
    camera,
    tenantId,
    onClose,
    onCreate,
    onUpdate
}: CameraFormModalProps) {
    const [formData, setFormData] = useState({
        cameraId: '',
        name: '',
        location: '',
        rtspUrl: '',
        enableSafetyViolations: true,
        enableSecurityViolations: true,
        enableOperationalViolations: true,
        enableComplianceViolations: true,
    });
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (camera) {
            setFormData({
                cameraId: camera.cameraId,
                name: camera.name,
                location: camera.location,
                rtspUrl: '', // Don't show RTSP URL for security
                enableSafetyViolations: camera.enableSafetyViolations,
                enableSecurityViolations: camera.enableSecurityViolations,
                enableOperationalViolations: camera.enableOperationalViolations,
                enableComplianceViolations: camera.enableComplianceViolations,
            });
        }
    }, [camera]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);

        try {
            if (camera) {
                // Update existing camera
                await onUpdate(camera.id, {
                    name: formData.name,
                    location: formData.location,
                    rtspUrl: formData.rtspUrl || undefined,
                    enableSafetyViolations: formData.enableSafetyViolations,
                    enableSecurityViolations: formData.enableSecurityViolations,
                    enableOperationalViolations: formData.enableOperationalViolations,
                    enableComplianceViolations: formData.enableComplianceViolations,
                });
                alert('Camera updated successfully');
            } else {
                // Create new camera
                await onCreate({
                    ...formData,
                    tenantId,
                });
                alert('Camera created successfully');
            }

            onClose();
        } catch (error: any) {
            console.error('Failed to save camera:', error);
            alert(error.message || 'Failed to save camera');
        } finally {
            setLoading(false);
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value, type, checked } = e.target;
        setFormData(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }));
    };

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto border border-white/20">
                {/* Header */}
                <div className="flex justify-between items-center p-6 border-b border-gray-200/50">
                    <h3 className="text-2xl font-bold text-gray-900">
                        {camera ? 'Edit Camera' : 'Add New Camera'}
                    </h3>
                    <button onClick={onClose} className="text-gray-500 hover:text-gray-700 transition-colors bg-gray-100/50 hover:bg-gray-100 p-2 rounded-full">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Camera ID *
                            </label>
                            <input
                                type="text"
                                name="cameraId"
                                value={formData.cameraId}
                                onChange={handleChange}
                                required
                                disabled={!!camera}
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300 disabled:bg-gray-50 disabled:text-gray-500"
                                placeholder="CAM-001"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-semibold text-gray-700 mb-2">
                                Name *
                            </label>
                            <input
                                type="text"
                                name="name"
                                value={formData.name}
                                onChange={handleChange}
                                required
                                className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                                placeholder="Front Entrance"
                            />
                        </div>
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            Location *
                        </label>
                        <input
                            type="text"
                            name="location"
                            value={formData.location}
                            onChange={handleChange}
                            required
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                            placeholder="Building A, Floor 1"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-semibold text-gray-700 mb-2">
                            RTSP URL {!camera && '*'}
                        </label>
                        <input
                            type="text"
                            name="rtspUrl"
                            value={formData.rtspUrl}
                            onChange={handleChange}
                            required={!camera}
                            placeholder={camera ? 'Leave empty to keep existing URL' : 'rtsp://username:password@host:port/path'}
                            className="w-full px-4 py-3 bg-white border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900 placeholder-gray-400 font-medium transition-all hover:border-gray-300"
                        />
                        <p className="text-xs text-gray-500 mt-2 ml-1">
                            RTSP URL will be encrypted before storage
                        </p>
                    </div>

                    <div className="border-t border-gray-200/50 pt-6">
                        <label className="block text-sm font-semibold text-gray-700 mb-4">
                            Enabled Violation Services
                        </label>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <label className="flex items-center p-3 border border-gray-200 rounded-xl hover:bg-gray-50 transition-colors cursor-pointer">
                                <input
                                    type="checkbox"
                                    name="enableSafetyViolations"
                                    checked={formData.enableSafetyViolations}
                                    onChange={handleChange}
                                    className="w-5 h-5 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
                                />
                                <span className="ml-3 text-sm font-medium text-gray-900">Safety Violations</span>
                            </label>
                            <label className="flex items-center p-3 border border-gray-200 rounded-xl hover:bg-gray-50 transition-colors cursor-pointer">
                                <input
                                    type="checkbox"
                                    name="enableSecurityViolations"
                                    checked={formData.enableSecurityViolations}
                                    onChange={handleChange}
                                    className="w-5 h-5 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
                                />
                                <span className="ml-3 text-sm font-medium text-gray-900">Security Violations</span>
                            </label>
                            <label className="flex items-center p-3 border border-gray-200 rounded-xl hover:bg-gray-50 transition-colors cursor-pointer">
                                <input
                                    type="checkbox"
                                    name="enableOperationalViolations"
                                    checked={formData.enableOperationalViolations}
                                    onChange={handleChange}
                                    className="w-5 h-5 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
                                />
                                <span className="ml-3 text-sm font-medium text-gray-900">Operational Violations</span>
                            </label>
                            <label className="flex items-center p-3 border border-gray-200 rounded-xl hover:bg-gray-50 transition-colors cursor-pointer">
                                <input
                                    type="checkbox"
                                    name="enableComplianceViolations"
                                    checked={formData.enableComplianceViolations}
                                    onChange={handleChange}
                                    className="w-5 h-5 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
                                />
                                <span className="ml-3 text-sm font-medium text-gray-900">Compliance Violations</span>
                            </label>
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
                            {loading ? 'Saving...' : camera ? 'Update Camera' : 'Create Camera'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
