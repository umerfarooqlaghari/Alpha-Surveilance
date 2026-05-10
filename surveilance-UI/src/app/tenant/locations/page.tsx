'use client';

import { useEffect, useState } from 'react';
import { Plus, Search, Edit, Trash2, MapPin, Loader2 } from 'lucide-react';
import { useAuth } from '@/contexts/AuthContext';
import { getLocations, deleteLocation } from '@/lib/api/tenant/locations';
import type { Location } from '@/types/location';
import LocationFormModal from './components/LocationFormModal';

export default function TenantLocationsPage() {
    const { tenant } = useAuth();
    const [locations, setLocations] = useState<Location[]>([]);
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState('');
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editing, setEditing] = useState<Location | null>(null);

    const load = async () => {
        try {
            setLoading(true);
            const data = await getLocations({ search: search || undefined });
            setLocations(data);
        } catch (e) {
            console.error('Failed to load locations', e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (tenant?.id) load();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [tenant?.id, search]);

    const handleEdit = (loc: Location) => {
        setEditing(loc);
        setIsModalOpen(true);
    };

    const handleAdd = () => {
        setEditing(null);
        setIsModalOpen(true);
    };

    const handleDelete = async (loc: Location) => {
        if (!confirm(`Delete location "${loc.name}"? This cannot be undone.`)) return;
        try {
            await deleteLocation(loc.id);
            load();
        } catch (e: any) {
            alert(e?.message || 'Failed to delete location');
        }
    };

    return (
        <div className="p-8 max-w-[1600px] mx-auto">
            <div className="flex justify-between items-end mb-8">
                <div>
                    <h1 className="text-3xl font-bold bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">Locations</h1>
                    <p className="text-gray-500 mt-1">Buildings, sites or sub-tenants. Cameras and employees can be assigned to a location.</p>
                </div>
                <button
                    onClick={handleAdd}
                    className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 transition-all shadow-lg shadow-blue-500/30 font-medium"
                >
                    <Plus className="w-4 h-4" /> Add Location
                </button>
            </div>

            <div className="bg-white p-1 rounded-2xl shadow-sm border border-gray-100 mb-8 max-w-lg">
                <div className="relative">
                    <Search className="absolute left-4 top-3.5 w-5 h-5 text-gray-400" />
                    <input
                        type="text"
                        placeholder="Search by name, code or city..."
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="w-full pl-12 pr-4 py-3 border-none rounded-xl focus:ring-0 text-gray-700 placeholder-gray-400 bg-transparent"
                    />
                </div>
            </div>

            <div className="bg-white rounded-2xl shadow-xl shadow-gray-100/50 border border-gray-100 overflow-hidden">
                {loading ? (
                    <div className="p-20 flex flex-col items-center justify-center gap-4">
                        <Loader2 className="w-10 h-10 animate-spin text-blue-600" />
                        <p className="text-gray-500">Loading locations...</p>
                    </div>
                ) : locations.length === 0 ? (
                    <div className="p-20 text-center text-gray-500 flex flex-col items-center">
                        <div className="w-20 h-20 bg-gray-50 rounded-full flex items-center justify-center mb-4">
                            <MapPin className="w-10 h-10 text-gray-300" />
                        </div>
                        <h3 className="text-lg font-semibold text-gray-900">No locations yet</h3>
                        <p className="max-w-xs mx-auto mt-2">Create your first location, then assign cameras and employees to it.</p>
                        <button onClick={handleAdd} className="mt-6 text-blue-600 hover:text-blue-700 font-medium hover:underline">
                            Add your first location
                        </button>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-left text-sm">
                            <thead className="bg-gray-50/80 text-gray-500 font-medium border-b border-gray-200 uppercase tracking-wider text-xs">
                                <tr>
                                    <th className="px-6 py-4">Name</th>
                                    <th className="px-6 py-4">Code</th>
                                    <th className="px-6 py-4">City / Country</th>
                                    <th className="px-6 py-4">Cameras</th>
                                    <th className="px-6 py-4">Status</th>
                                    <th className="px-6 py-4 text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {locations.map((loc) => (
                                    <tr key={loc.id} className="hover:bg-blue-50/30 transition-colors group">
                                        <td className="px-6 py-4">
                                            <div className="flex items-center gap-3">
                                                <div className="w-9 h-9 rounded-full bg-gradient-to-br from-blue-100 to-indigo-100 flex items-center justify-center text-blue-700">
                                                    <MapPin className="w-4 h-4" />
                                                </div>
                                                <div>
                                                    <p className="font-semibold text-gray-900">{loc.name}</p>
                                                    {loc.address && <p className="text-gray-500 text-xs">{loc.address}</p>}
                                                </div>
                                            </div>
                                        </td>
                                        <td className="px-6 py-4">
                                            <span className="font-mono text-xs bg-gray-100 text-gray-600 px-2 py-1 rounded-md border border-gray-200">
                                                {loc.code}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-gray-600">
                                            {[loc.city, loc.country].filter(Boolean).join(', ') || <span className="text-gray-400">-</span>}
                                        </td>
                                        <td className="px-6 py-4 text-gray-700 font-medium">{loc.cameraCount}</td>
                                        <td className="px-6 py-4">
                                            <span className={`inline-flex items-center px-3 py-1 rounded-full text-xs font-semibold border ${
                                                loc.status === 'Inactive'
                                                    ? 'bg-gray-100 text-gray-600 border-gray-200'
                                                    : 'bg-green-50 text-green-700 border-green-200'
                                            }`}>
                                                {loc.status}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-right">
                                            <div className="flex justify-end gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                                                <button
                                                    onClick={() => handleEdit(loc)}
                                                    className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-lg border border-transparent hover:border-blue-100"
                                                    title="Edit"
                                                >
                                                    <Edit className="w-4 h-4" />
                                                </button>
                                                <button
                                                    onClick={() => handleDelete(loc)}
                                                    className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg border border-transparent hover:border-red-100"
                                                    title="Delete"
                                                >
                                                    <Trash2 className="w-4 h-4" />
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

            <LocationFormModal
                isOpen={isModalOpen}
                onClose={() => { setIsModalOpen(false); setEditing(null); }}
                onSuccess={load}
                location={editing}
            />
        </div>
    );
}
