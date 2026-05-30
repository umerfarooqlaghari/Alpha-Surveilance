'use client';

import { useState, useEffect } from 'react';
import { Link2, Link2Off, AlertTriangle } from 'lucide-react';
import { getCameras } from '@/lib/api/cameras';
import { getDevices, assignCameraToDevice, unassignCameraFromDevice } from '@/lib/api/devices';
import type { CameraResponse } from '@/types/admin';
import type { EdgeDeviceResponse } from '@/types/device';
import type { TenantResponse } from '@/types/admin';

interface Props {
    tenants: TenantResponse[];
    selectedTenantId: string;
}

export default function AssignmentsTab({ tenants, selectedTenantId }: Props) {
    const [cameras, setCameras] = useState<CameraResponse[]>([]);
    const [devices, setDevices] = useState<EdgeDeviceResponse[]>([]);
    const [loading, setLoading] = useState(false);
    const [pendingCamera, setPendingCamera] = useState<CameraResponse | null>(null);
    const [pendingDevice, setPendingDevice] = useState<string>('');
    const [locationWarning, setLocationWarning] = useState<string | null>(null);
    const [confirmPending, setConfirmPending] = useState(false);
    const [saving, setSaving] = useState(false);

    const load = async () => {
        if (!selectedTenantId) return;
        setLoading(true);
        try {
            const [cams, devs] = await Promise.all([
                getCameras(selectedTenantId),
                getDevices(selectedTenantId),
            ]);
            setCameras(cams);
            setDevices(devs);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, [selectedTenantId]);

    const deviceById = (id?: string | null) =>
        id ? devices.find(d => d.id === id) : undefined;

    /** Check if assigning `camera` to `deviceId` would mix locations. */
    const checkLocationMismatch = (camera: CameraResponse, deviceId: string): string | null => {
        if (!camera.locationId) return null;
        const device = devices.find(d => d.id === deviceId);
        if (!device) return null;
        const existing = cameras.filter(c => c.deviceId === deviceId && c.id !== camera.id);
        const existingLocations = new Set(existing.map(c => c.locationId).filter(Boolean));
        if (existingLocations.size > 0 && !existingLocations.has(camera.locationId)) {
            return `This camera's location differs from other cameras on this device. If the RTSP stream isn't publicly routable, the device may not be able to reach it — make sure the network allows cross-location access before proceeding.`;
        }
        return null;
    };

    const handleAssign = (camera: CameraResponse, deviceId: string) => {
        const warning = checkLocationMismatch(camera, deviceId);
        setPendingCamera(camera);
        setPendingDevice(deviceId);
        setLocationWarning(warning);
        setConfirmPending(true);
    };

    const confirmAssign = async () => {
        if (!pendingCamera) return;
        setSaving(true);
        try {
            if (pendingDevice) {
                await assignCameraToDevice(pendingDevice, pendingCamera.id);
            } else {
                const current = pendingCamera.deviceId;
                if (current) await unassignCameraFromDevice(current, pendingCamera.id);
            }
            setConfirmPending(false);
            load();
        } catch (e: any) {
            alert(e.message || 'Failed to update assignment');
        } finally {
            setSaving(false);
        }
    };

    if (!selectedTenantId) {
        return (
            <div className="p-8 text-center text-gray-500">
                Select a tenant to manage camera-device assignments.
            </div>
        );
    }

    return (
        <div>
            <p className="text-sm text-gray-500 mb-4">
                Assign cameras to specific edge devices. Unassigned cameras are served to all devices
                for this tenant (shared pool).
            </p>

            {loading ? (
                <div className="p-8 text-center text-gray-400">Loading…</div>
            ) : (
                <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200 text-xs font-medium text-gray-500 uppercase tracking-wider">
                            <tr>
                                <th className="px-6 py-3 text-left">Camera</th>
                                <th className="px-6 py-3 text-left">Location</th>
                                <th className="px-6 py-3 text-left">Assigned Device</th>
                                <th className="px-6 py-3 text-left">Assign to</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {cameras.map(camera => {
                                const assignedDeviceId = camera.deviceId ?? null;
                                const assignedDevice = deviceById(assignedDeviceId);
                                return (
                                    <tr key={camera.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4">
                                            <div className="font-medium text-sm text-gray-900">{camera.name}</div>
                                            <div className="text-xs text-gray-400">{camera.cameraId}</div>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-500">
                                            {camera.locationName || <span className="italic text-gray-300">—</span>}
                                        </td>
                                        <td className="px-6 py-4">
                                            {assignedDevice ? (
                                                <div className="flex items-center gap-2">
                                                    <Link2 className="w-4 h-4 text-blue-500" />
                                                    <span className="text-sm text-blue-700 font-medium">{assignedDevice.displayName}</span>
                                                    <button
                                                        onClick={() => handleAssign(camera, '')}
                                                        className="ml-2 text-xs text-red-400 hover:text-red-600 flex items-center gap-1"
                                                        title="Remove from device (move to shared pool)"
                                                    >
                                                        <Link2Off className="w-3.5 h-3.5" /> Unassign
                                                    </button>
                                                </div>
                                            ) : (
                                                <span className="text-xs text-gray-400 italic">Shared pool</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4">
                                            <select
                                                className="text-sm border border-gray-300 rounded-lg px-3 py-1.5 focus:ring-2 focus:ring-blue-500 text-gray-700 min-w-[180px]"
                                                value={assignedDeviceId ?? ''}
                                                onChange={e => {
                                                    const val = e.target.value;
                                                    if (val !== (assignedDeviceId ?? '')) {
                                                        handleAssign(camera, val);
                                                    }
                                                }}
                                            >
                                                <option value="">— Shared pool —</option>
                                                {devices.filter(d => d.status === 'Active').map(d => (
                                                    <option key={d.id} value={d.id}>{d.displayName}</option>
                                                ))}
                                            </select>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Confirm dialog */}
            {confirmPending && pendingCamera && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-xl w-full max-w-md p-6 space-y-4">
                        <h3 className="text-base font-semibold text-gray-900">
                            {pendingDevice
                                ? `Assign "${pendingCamera.name}" to "${deviceById(pendingDevice)?.displayName}"?`
                                : `Move "${pendingCamera.name}" to shared pool?`}
                        </h3>

                        {locationWarning && (
                            <div className="flex gap-3 bg-amber-50 border border-amber-200 rounded-lg p-3 text-sm text-amber-800">
                                <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0 text-amber-500" />
                                <span>{locationWarning}</span>
                            </div>
                        )}

                        <div className="flex justify-end gap-3 pt-1">
                            <button
                                onClick={() => setConfirmPending(false)}
                                className="px-4 py-2 text-sm border border-gray-300 rounded-lg hover:bg-gray-50 text-gray-700"
                            >Cancel</button>
                            <button
                                onClick={confirmAssign}
                                disabled={saving}
                                className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
                            >{saving ? 'Saving…' : 'Confirm'}</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
