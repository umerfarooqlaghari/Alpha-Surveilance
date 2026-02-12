export default function AdminDashboard() {
    return (
        <div>
            <h2 className="text-3xl font-bold text-gray-900 mb-6">Dashboard</h2>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Tenants</h3>
                    <p className="text-4xl font-bold text-blue-600">-</p>
                </div>
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Users</h3>
                    <p className="text-4xl font-bold text-green-600">-</p>
                </div>
                <div className="bg-white p-6 rounded-lg shadow-sm border border-gray-200">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">Total Cameras</h3>
                    <p className="text-4xl font-bold text-purple-600">-</p>
                </div>
            </div>
        </div>
    );
}
