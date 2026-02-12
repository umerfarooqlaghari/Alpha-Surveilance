'use client';

import { useState, useEffect } from 'react';
import { Plus, Search, Edit, Trash2 } from 'lucide-react';
import { getCameras, deleteCamera, updateCameraStatus, createCamera, updateCamera } from '@/lib/api/cameras';
import { getTenants } from '@/lib/api/tenants';
import type { CameraResponse, TenantResponse } from '@/types/admin';
import CameraFormModal from './components/CameraFormModal';

export default function CamerasPage() {
    const [cameras, setCameras] = useState<CameraResponse[]>([]);
    const [tenants, setTenants] = useState<TenantResponse[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedTenant, setSelectedTenant] = useState<string>('');
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingCamera, setEditingCamera] = useState<CameraResponse | null>(null);

    const loadData = async () => {
        try {
            setLoading(true);
            const tenantsData = await getTenants(1, 100);
            setTenants(tenantsData.tenants);

            if (selectedTenant) {
                const camerasData = await getCameras(selectedTenant);
                setCameras(camerasData);
            } else {
                setCameras([]);
            }
        } catch (error) {
            console.error('Failed to load data:', error);
            alert('Failed to load data');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, [selectedTenant]);

    const handleDelete = async (id: string, name: string) => {
        if (!confirm(`Are you sure you want to delete "${name}"?`)) return;

        try {
            await deleteCamera(id);
            alert('Camera deleted successfully');
            loadData();
        } catch (error) {
            console.error('Failed to delete camera:', error);
            alert('Failed to delete camera');
        }
    };

    const handleEdit = (camera: CameraResponse) => {
        setEditingCamera(camera);
        setIsModalOpen(true);
    };

    const handleModalClose = () => {
        setIsModalOpen(false);
        setEditingCamera(null);
        loadData();
    };

    const filteredCameras = cameras.filter(camera =>
        camera.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
        camera.cameraId.toLowerCase().includes(searchTerm.toLowerCase()) ||
        camera.location.toLowerCase().includes(searchTerm.toLowerCase())
    );

    const getStatusColor = (status: string) => {
        switch (status.toLowerCase()) {
            case 'active': return 'bg-green-100 text-green-800';
            case 'inactive': return 'bg-gray-100 text-gray-800';
            case 'maintenance': return 'bg-yellow-100 text-yellow-800';
            case 'error': return 'bg-red-100 text-red-800';
            default: return 'bg-gray-100 text-gray-800';
        }
    };

    return (
        <div>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-3xl font-bold text-gray-900">Cameras Management</h2>
                <button
                    onClick={() => setIsModalOpen(true)}
                    disabled={!selectedTenant}
                    className="flex items-center gap-2 bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    <Plus className="w-5 h-5" />
                    Add Camera
                </button>
            </div>

            {/* Filters */}
            <div className="mb-6 grid grid-cols-2 gap-4">
                <div className="relative">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-5 h-5" />
                    <input
                        type="text"
                        placeholder="Search cameras..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900"
                    />
                </div>
                <select
                    value={selectedTenant}
                    onChange={(e) => setSelectedTenant(e.target.value)}
                    className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-400"
                >
                    <option value="">Select a Tenant</option>
                    {tenants.map(tenant => (
                        <option key={tenant.id} value={tenant.id}>
                            {tenant.tenantName}
                        </option>
                    ))}
                </select>
            </div>

            {/* Table */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 overflow-hidden">
                {!selectedTenant ? (
                    <div className="p-8 text-center text-gray-500">
                        Please select a tenant to view cameras
                    </div>
                ) : loading ? (
                    <div className="p-8 text-center text-gray-500">Loading...</div>
                ) : (
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Camera ID
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Name
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Location
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Status
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Services
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Actions
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {filteredCameras.map((camera) => (
                                <tr key={camera.id} className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                        {camera.cameraId}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                        {camera.name}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {camera.location}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        <span className={`px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${getStatusColor(camera.status)}`}>
                                            {camera.status}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-500">
                                        <div className="flex flex-wrap gap-1">
                                            {camera.enableSafetyViolations && (
                                                <span className="px-2 py-1 bg-blue-100 text-blue-800 text-xs rounded">Safety</span>
                                            )}
                                            {camera.enableSecurityViolations && (
                                                <span className="px-2 py-1 bg-purple-100 text-purple-800 text-xs rounded">Security</span>
                                            )}
                                            {camera.enableOperationalViolations && (
                                                <span className="px-2 py-1 bg-green-100 text-green-800 text-xs rounded">Operational</span>
                                            )}
                                            {camera.enableComplianceViolations && (
                                                <span className="px-2 py-1 bg-yellow-100 text-yellow-800 text-xs rounded">Compliance</span>
                                            )}
                                        </div>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                                        <button
                                            onClick={() => handleEdit(camera)}
                                            className="text-blue-600 hover:text-blue-900 mr-4"
                                        >
                                            <Edit className="w-4 h-4 inline" />
                                        </button>
                                        <button
                                            onClick={() => handleDelete(camera.id, camera.name)}
                                            className="text-red-600 hover:text-red-900"
                                        >
                                            <Trash2 className="w-4 h-4 inline" />
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}

                {!loading && selectedTenant && filteredCameras.length === 0 && (
                    <div className="p-8 text-center text-gray-500">
                        No cameras found for this tenant
                    </div>
                )}
            </div>

            {/* Modal */}
            {isModalOpen && selectedTenant && (
                <CameraFormModal
                    camera={editingCamera}
                    tenantId={selectedTenant}
                    onClose={handleModalClose}
                    onCreate={createCamera}
                    onUpdate={updateCamera}
                />
            )}
        </div>
    );
}
