'use client';

import { useState, useEffect } from 'react';
import { getTenants } from '@/lib/api/tenants';
import { getUsers } from '@/lib/api/users';
import { Loader2 } from 'lucide-react';

export default function AdminDashboard() {
    const [stats, setStats] = useState({ tenants: 0, users: 0, cameras: 0 });
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const loadStats = async () => {
            try {
                const [tenantsRes, usersRes] = await Promise.all([
                    getTenants(1, 1000),
                    getUsers()
                ]);

                const totalCameras = tenantsRes.tenants.reduce((sum, t) => sum + (t.cameraCount || 0), 0);

                setStats({
                    tenants: tenantsRes.totalCount || 0,
                    users: usersRes.length || 0,
                    cameras: totalCameras
                });
            } catch (err) {
                console.error("Failed to load admin stats:", err);
            } finally {
                setLoading(false);
            }
        };

        loadStats();
    }, []);

    return (
        <div>
            <h2 className="text-3xl font-bold text-gray-900 mb-6">Dashboard</h2>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Tenants</h3>
                    <p className="text-4xl font-bold text-blue-600">
                        {loading ? <Loader2 className="w-6 h-6 animate-spin text-blue-600 inline-block" /> : stats.tenants}
                    </p>
                </div>
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Users</h3>
                    <p className="text-4xl font-bold text-green-600">
                        {loading ? <Loader2 className="w-6 h-6 animate-spin text-green-600 inline-block" /> : stats.users}
                    </p>
                </div>
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Cameras</h3>
                    <p className="text-4xl font-bold text-purple-600">
                        {loading ? <Loader2 className="w-6 h-6 animate-spin text-purple-600 inline-block" /> : stats.cameras}
                    </p>
                </div>
            </div>
        </div>
    );
}
