'use client';

import AnalyticsDashboard from '@/components/analytics/AnalyticsDashboard';

export default function TenantAnalyticsPage() {
    return (
        <div>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-3xl font-bold text-gray-900">Analytics</h2>
            </div>
            <AnalyticsDashboard />
        </div>
    );
}
