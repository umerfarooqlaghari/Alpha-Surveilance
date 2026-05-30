'use client';

import { useState, useEffect, useCallback } from 'react';
import { Plus, Edit, Trash2, Server } from 'lucide-react';
import { getDevices, createDevice, updateDevice, deleteDevice } from '@/lib/api/devices';
import type { EdgeDeviceResponse, CreateEdgeDeviceRequest, DeviceOnlineStatus } from '@/types/device';
import { getOnlineStatus } from '@/types/device';
import type { TenantResponse } from '@/types/admin';
import type { Location } from '@/types/location';
import { getLocations } from '@/lib/api/locations';

interface Props {
    tenants: TenantResponse[];
    selectedTenantId: string;
}

const STATUS_DOT: Record<DeviceOnlineStatus, string> = {
    online: 'bg-green-500',
    idle: 'bg-yellow-400',
    offline: 'bg-gray-400',
    unknown: 'bg-gray-300',
};

const STATUS_LABEL: Record<DeviceOnlineStatus, string> = {
    online: 'Online',
    idle: 'Idle',
    offline: 'Offline',
    unknown: 'Never seen',
};

const STATUS_CODE = {
    Active: 0,
    Disabled: 1,
} as const;

const getErrorMessage = (error: unknown, fallback: string): string => {
    return error instanceof Error ? error.message : fallback;
};

const DevicesTab = ({ selectedTenantId }: Props) => {
    const [devices, setDevices] = useState<EdgeDeviceResponse[]>([]);
    const [locations, setLocations] = useState<Location[]>([]);
    const [loading, setLoading] = useState(false);
    const [showModal, setShowModal] = useState(false);
    const [editing, setEditing] = useState<EdgeDeviceResponse | null>(null);
    const [form, setForm] = useState<Partial<CreateEdgeDeviceRequest>>({});
    const [saving, setSaving] = useState(false);

    const load = useCallback(async () => {
        if (!selectedTenantId) return;
        setLoading(true);
        try {
            const [devs, locs] = await Promise.all([
                getDevices(selectedTenantId),
                getLocations(selectedTenantId),
            ]);
            setDevices(devs);
            setLocations(locs);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    }, [selectedTenantId]);

    useEffect(() => { load(); }, [load]);

    const openCreate = () => {
        setEditing(null);
        setForm({ tenantId: selectedTenantId });
        setShowModal(true);
    };

    const openEdit = (d: EdgeDeviceResponse) => {
        setEditing(d);
        setForm({
            tenantId: d.tenantId,
            locationId: d.locationId,
            deviceIdentifier: d.deviceIdentifier,
            displayName: d.displayName,
            hostname: d.hostname,
        });
        setShowModal(true);
    };

    const handleSave = async () => {
        if (!form.deviceIdentifier?.trim() || !form.displayName?.trim()) {
            alert('Device identifier and display name are required.');
            return;
        }
        setSaving(true);
        try {
            if (editing) {
                await updateDevice(editing.id, {
                    displayName: form.displayName,
                    hostname: form.hostname,
                    locationId: form.locationId || undefined,
                });
            } else {
                await createDevice({
                    tenantId: selectedTenantId,
                    deviceIdentifier: form.deviceIdentifier!,
                    displayName: form.displayName!,
                    hostname: form.hostname,
                    locationId: form.locationId,
                });
            }
            setShowModal(false);
            load();
        } catch (e: unknown) {
            alert(getErrorMessage(e, 'Failed to save device'));
        } finally {
            setSaving(false);
        }
    };

    const handleDelete = async (d: EdgeDeviceResponse) => {
        if (!confirm(`Delete device "${d.displayName}"? Its cameras will move to the shared pool.`)) return;
        try {
            await deleteDevice(d.id);
            load();
        } catch (e: unknown) {
            alert(getErrorMessage(e, 'Failed to delete device'));
        }
    };

    const handleToggleStatus = async (d: EdgeDeviceResponse) => {
        const nextStatus = d.status === 'Active' ? 'Disabled' : 'Active';
        const newStatus = STATUS_CODE[nextStatus];
        try {
            await updateDevice(d.id, { status: newStatus });
            load();
        } catch (e: unknown) {
            alert(getErrorMessage(e, 'Failed to update status'));
        }
    };

    if (!selectedTenantId) {
        return (
            <div className="p-8 text-center text-gray-500">
                Select a tenant to manage its edge devices.
            </div>
        );
    }

    return (
        <div>
            <div className="flex justify-between items-center mb-4">
                <p className="text-sm text-gray-500">
                    Edge devices running the vision inference service for this tenant.
                    Cameras assigned to a device are served exclusively to it.
                    Unassigned cameras are shared across all devices for the tenant.
                </p>
                <button
                    onClick={openCreate}
                    className="flex items-center gap-2 bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 transition-colors whitespace-nowrap ml-4"
                >
                    <Plus className="w-4 h-4" /> Add Device
                </button>
            </div>

            {loading ? (
                <div className="p-8 text-center text-gray-400">Loading…</div>
            ) : devices.length === 0 ? (
                <div className="p-8 text-center text-gray-400">
                    <Server className="w-10 h-10 mx-auto mb-2 opacity-30" />
                    No devices registered for this tenant yet.
                </div>
            ) : (
                <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200 text-xs font-medium text-gray-500 uppercase tracking-wider">
                            <tr>
                                <th className="px-6 py-3 text-left">Device</th>
                                <th className="px-6 py-3 text-left">Identifier</th>
                                <th className="px-6 py-3 text-left">Location</th>
                                <th className="px-6 py-3 text-left">Cameras</th>
                                <th className="px-6 py-3 text-left">Status</th>
                                <th className="px-6 py-3 text-left">Last Seen</th>
                                <th className="px-6 py-3 text-right">Actions</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {devices.map(d => {
                                const online = getOnlineStatus(d.lastSeenAt);
                                const hasMixedLocations = d.distinctLocationIds.length > 1;
                                return (
                                    <tr key={d.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4">
                                            <div className="font-medium text-gray-900 text-sm">{d.displayName}</div>
                                            <div className="text-xs text-gray-400">{d.hostname}</div>
                                        </td>
                                        <td className="px-6 py-4 text-xs text-gray-500 font-mono">
                                            {d.deviceIdentifier.length > 18
                                                ? `${d.deviceIdentifier.slice(0, 8)}…${d.deviceIdentifier.slice(-6)}`
                                                : d.deviceIdentifier}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-500">
                                            {d.locationName ?? <span className="italic text-gray-300">—</span>}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-700">
                                            <span className="font-medium">{d.cameraCount}</span>
                                            {hasMixedLocations && (
                                                <span
                                                    className="ml-2 text-amber-500 cursor-help"
                                                    title="Cameras from multiple locations are assigned to this device."
                                                >⚠</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4">
                                            <div className="flex items-center gap-2">
                                                <span className={`w-2 h-2 rounded-full ${STATUS_DOT[online]}`} />
                                                <span className="text-xs text-gray-600">{STATUS_LABEL[online]}</span>
                                            </div>
                                            {d.status === 'Disabled' && (
                                                <span className="text-xs text-red-500 mt-0.5 block">Disabled</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 text-xs text-gray-400">
                                            {d.lastSeenAt
                                                ? new Date(d.lastSeenAt).toLocaleString()
                                                : '—'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                                            <button
                                                onClick={() => openEdit(d)}
                                                className="text-blue-600 hover:text-blue-800 mr-3"
                                                title="Edit"
                                            ><Edit className="w-4 h-4 inline" /></button>
                                            <button
                                                onClick={() => handleToggleStatus(d)}
                                                className={`mr-3 text-xs font-medium ${d.status === 'Active' ? 'text-yellow-600 hover:text-yellow-800' : 'text-green-600 hover:text-green-800'}`}
                                            >{d.status === 'Active' ? 'Disable' : 'Enable'}</button>
                                            <button
                                                onClick={() => handleDelete(d)}
                                                className="text-red-500 hover:text-red-700"
                                                title="Delete"
                                            ><Trash2 className="w-4 h-4 inline" /></button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Modal */}
            {showModal && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-xl w-full max-w-md p-6 space-y-4">
                        <h3 className="text-lg font-semibold text-gray-900">
                            {editing ? 'Edit Device' : 'Add Edge Device'}
                        </h3>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Display Name *</label>
                            <input
                                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 text-gray-900"
                                placeholder="e.g. Kitchen Floor Device"
                                value={form.displayName ?? ''}
                                onChange={e => setForm(p => ({ ...p, displayName: e.target.value }))}
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Device Identifier *
                                {editing && <span className="ml-1 text-xs text-gray-400">(read-only)</span>}
                            </label>
                            <input
                                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 text-gray-900 disabled:bg-gray-50 disabled:text-gray-400"
                                placeholder="Paste from DEVICE_ID env var or device UUID file"
                                value={form.deviceIdentifier ?? ''}
                                disabled={!!editing}
                                onChange={e => setForm(p => ({ ...p, deviceIdentifier: e.target.value }))}
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Hostname</label>
                            <input
                                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 text-gray-900"
                                placeholder="device-hostname (optional)"
                                value={form.hostname ?? ''}
                                onChange={e => setForm(p => ({ ...p, hostname: e.target.value }))}
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Primary Location (optional)</label>
                            <select
                                className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 text-gray-700"
                                value={form.locationId ?? ''}
                                onChange={e => setForm(p => ({ ...p, locationId: e.target.value || undefined }))}
                            >
                                <option value="">— No location —</option>
                                {locations.map(l => (
                                    <option key={l.id} value={l.id}>{l.name} ({l.code})</option>
                                ))}
                            </select>
                            <p className="text-xs text-gray-400 mt-1">
                                Used as a reference hint only. Cameras from other locations can still be assigned.
                            </p>
                        </div>

                        <div className="flex justify-end gap-3 pt-2">
                            <button
                                onClick={() => setShowModal(false)}
                                className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50 text-gray-700"
                            >Cancel</button>
                            <button
                                onClick={handleSave}
                                disabled={saving}
                                className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
                            >{saving ? 'Saving…' : editing ? 'Update' : 'Create'}</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default DevicesTab;
