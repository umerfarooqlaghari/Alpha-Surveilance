'use client';

import { useState, useEffect } from 'react';
import { LayoutDashboard, Camera, FileText } from 'lucide-react';
import { getStats, getRecentViolations, DashboardStats } from '@/lib/api/tenant/dashboard';

export default function TenantDashboard() {
    const [stats, setStats] = useState<DashboardStats>({
        totalCameras: 0,
        activeViolations: 0,
        resolvedToday: 0
    });
    const [loading, setLoading] = useState(true);

    const loadData = async () => {
        try {
            setLoading(true);
            const statsData = await getStats();
            setStats(statsData);
        } catch (error) {
            console.error('Failed to load dashboard data:', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, []);

    return (
        <div>
            <h2 className="text-3xl font-bold text-gray-900 mb-6">Dashboard</h2>

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

            {/* Placeholder for Recent Activity or Charts */}
            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-6">
                <h3 className="text-lg font-bold text-gray-900 mb-4">Recent Activity</h3>
                <div className="text-gray-500 text-center py-8">
                    Real-time activity feed coming soon...
                </div>
            </div>
        </div>
    );
}
