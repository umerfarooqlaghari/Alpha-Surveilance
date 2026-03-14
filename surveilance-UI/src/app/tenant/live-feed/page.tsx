'use client';

import { useState, useEffect } from 'react';
import { LayoutDashboard, Camera, FileText, AlertTriangle } from 'lucide-react';
import { getStats, getRecentViolations, DashboardStats } from '@/lib/api/tenant/dashboard';
import { getCameras, updateCamera } from '@/lib/api/tenant/cameras';
import type { CameraResponse } from '@/types/admin';
import { useViolationHub } from '@/hooks/useViolationHub';
import WebRTCPlayer from '@/components/cameras/WebRTCPlayer';

// Define the shape of a real-time violation Notification
interface NotificationPayload {
    id: string;
    type: string;
    severity: string;
    timestamp: string;
    framePath: string;
    cameraId: string;
    cameraName?: string;
    sopName?: string;
    violationTypeName?: string;
}

export default function TenantLiveFeed() {
    const [stats, setStats] = useState<DashboardStats>({
        totalCameras: 0,
        activeViolations: 0,
        resolvedToday: 0
    });
    const [loading, setLoading] = useState(true);
    const [activities, setActivities] = useState<NotificationPayload[]>([]);
    const [streams, setStreams] = useState<CameraResponse[]>([]);

    // Connect to SignalR
    const { connection, notifications } = useViolationHub();

    const loadData = async () => {
        try {
            setLoading(true);
            const [statsData, recent, cameras] = await Promise.all([getStats(), getRecentViolations(), getCameras()]);
            setStats(statsData);
            setStreams(cameras.filter(c => c.whepUrl));
            // Only show violations from the last 12 hours
            const cutoff = new Date(Date.now() - 12 * 60 * 60 * 1000);
            setActivities((recent as any[]).filter(v => new Date(v.timestamp) >= cutoff));
        } catch (error) {
            console.error('Failed to load live feed data:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleToggleStream = async (cameraId: string, isStreaming: boolean) => {
        try {
            await updateCamera(cameraId, {
                isStreaming: isStreaming
            });

            // Optimistically update local UI state
            setStreams(prev => prev.map(c =>
                c.id === cameraId ? { ...c, isStreaming } : c
            ));
        } catch (error) {
            console.error('Failed to toggle stream:', error);
            // Optionally could pop a toast here
            alert('Failed to update live stream state.');
        }
    };

    useEffect(() => {
        loadData();
    }, []);

    // Sync activity feed with SignalR hook notifications
    useEffect(() => {
        if (notifications.length > 0) {
            // Prepend new real-time notification, keep feed within 12-hour window
            const cutoff = new Date(Date.now() - 12 * 60 * 60 * 1000);
            setActivities(prev => {
                const combined = [...notifications, ...prev];
                return combined.filter(v => new Date(v.timestamp) >= cutoff).slice(0, 50);
            });
            // Refresh stats from server so Active/Resolved counts are authoritative
            getStats().then(setStats).catch(() => { });
        }
    }, [notifications]);

    return (
        <div>
            <h2 className="text-3xl font-bold text-gray-900 mb-6 flex items-center gap-3">
                Live Feed
                {connection && <span className="text-xs px-2 py-1 bg-green-100 text-green-700 rounded-full font-medium animate-pulse">Live</span>}
            </h2>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <div className="flex items-center justify-between mb-4">
                        <h3 className="text-lg font-semibold text-gray-700">Total Cameras</h3>
                        <div className="p-2 bg-purple-50 rounded-lg">
                            <Camera className="w-6 h-6 text-purple-600" />
                        </div>
                    </div>
                    <p className="text-4xl font-bold text-purple-600">
                        {loading ? '-' : stats.totalCameras}
                    </p>
                </div>

                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <div className="flex items-center justify-between mb-4">
                        <h3 className="text-lg font-semibold text-gray-700">Active Violations</h3>
                        <div className="p-2 bg-red-50 rounded-lg">
                            <FileText className="w-6 h-6 text-red-600" />
                        </div>
                    </div>
                    <p className="text-4xl font-bold text-red-600">
                        {loading ? '-' : stats.activeViolations}
                    </p>
                    <p className="text-sm text-gray-500 mt-2">Requires attention</p>
                </div>

                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <div className="flex items-center justify-between mb-4">
                        <h3 className="text-lg font-semibold text-gray-700">Resolved Today</h3>
                        <div className="p-2 bg-green-50 rounded-lg">
                            <LayoutDashboard className="w-6 h-6 text-green-600" />
                        </div>
                    </div>
                    <p className="text-4xl font-bold text-green-600">
                        {loading ? '-' : stats.resolvedToday}
                    </p>
                    <p className="text-sm text-gray-500 mt-2">Audited & closed</p>
                </div>
            </div>

            {/* Live Camera Streams */}
            <div className="mb-8">
                <h3 className="text-xl font-bold text-gray-900 mb-4 flex items-center gap-2">
                    <Camera className="w-5 h-5 text-gray-500" />
                    Security Wall
                </h3>

                <div className="bg-gray-900 rounded-xl p-6 shadow-sm border border-gray-800">
                    {streams.length === 0 ? (
                        <div className="bg-gray-800 p-8 rounded-lg text-center border-2 border-dashed border-gray-700">
                            <p className="text-gray-400">No WebRTC cameras configured for this tenant.</p>
                            <p className="text-sm text-gray-500 mt-2">Go to the Cameras tab to set up Cloudflare streaming links.</p>
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-2 xl:grid-cols-3 gap-6">
                            {streams.map(cam => (
                                <WebRTCPlayer
                                    key={cam.id}
                                    camera={cam}
                                    onToggleStream={handleToggleStream}
                                />
                            ))}
                        </div>
                    )}
                </div>
            </div>

            {/* Recent Activity Feed */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
                <h3 className="text-lg font-bold text-gray-900 mb-4">Live Activity Feed</h3>

                {activities.length === 0 ? (
                    <div className="text-gray-500 text-center py-8">
                        Waiting for real-time events...
                    </div>
                ) : (
                    <div className="space-y-4">
                        {activities.map((act) => (
                            <div key={act.id} className="flex items-start gap-4 p-4 border rounded-lg bg-red-50 border-red-100 animate-in fade-in slide-in-from-top-2">
                                <div className="p-2 bg-red-100 rounded-full">
                                    <AlertTriangle className="w-5 h-5 text-red-600" />
                                </div>
                                <div className="flex-1">
                                    <div className="flex justify-between items-start">
                                        <h4 className="font-semibold text-gray-900">{act.sopName || 'Violation'} Detected</h4>
                                        <span className="text-xs text-gray-500">{new Date(act.timestamp).toLocaleTimeString()}</span>
                                    </div>
                                    <p className="text-sm text-gray-600 mt-1">
                                        Type: <span className="font-medium text-red-600">{act.violationTypeName || act.type}</span> • Camera: <span className="font-medium text-gray-900">{act.cameraName || 'Unknown Camera'}</span>
                                        {act.cameraId && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(act.cameraId) && act.cameraId !== act.cameraName && (
                                            <span className="text-xs text-gray-400 ml-1">({act.cameraId})</span>
                                        )}
                                    </p>
                                    {act.framePath && (
                                        <div className="mt-3">
                                            <a href={act.framePath} target="_blank" rel="noopener noreferrer" className="text-xs text-blue-600 hover:underline">
                                                View Evidence Frame
                                            </a>
                                        </div>
                                    )}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
