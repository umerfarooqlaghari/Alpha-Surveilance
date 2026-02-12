'use client';

import { BarChart3 } from 'lucide-react';

export default function TenantAnalyticsPage() {
    return (
        <div>
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-3xl font-bold text-gray-900">Analytics</h2>
            </div>

            <div className="bg-white rounded-lg shadow-sm border border-gray-200 p-8 text-center">
                <div className="flex justify-center mb-4">
                    <div className="p-4 bg-purple-50 rounded-full">
                        <BarChart3 className="w-12 h-12 text-purple-600" />
                    </div>
                </div>
                <h3 className="text-xl font-semibold text-gray-900 mb-2">Analytics Dashboard Coming Soon</h3>
                <p className="text-gray-500 max-w-md mx-auto">
                    Detailed insights, violation trends, and camera performance metrics will be available here.
                </p>
            </div>
        </div>
    );
}
