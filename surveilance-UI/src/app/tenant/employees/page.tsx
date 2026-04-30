'use client';

import { useState, useEffect } from 'react';
import { useAuth } from '@/contexts/AuthContext';
import { getEmployees, deleteEmployee, sendFaceScanInvites } from '@/lib/api/tenant/employees';
import { Employee } from '@/types/employee';
import { Loader2, Plus, Upload, Search, Edit2, Trash2, User, Send, CheckCircle2, AlertCircle } from 'lucide-react';
import EmployeeFormModal from '@/components/employees/EmployeeFormModal';
import BulkUploadModal from '@/components/employees/BulkUploadModal';

export default function EmployeesPage() {
    const { token } = useAuth();
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [loading, setLoading] = useState(true);
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const [search, setSearch] = useState('');

    // Modals
    const [isFormOpen, setIsFormOpen] = useState(false);
    const [isBulkOpen, setIsBulkOpen] = useState(false);
    const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null);
    const [selectedIds, setSelectedIds] = useState<string[]>([]);
    const [isSending, setIsSending] = useState(false);

    const fetchEmployees = async () => {
        setLoading(true);
        try {
            const res = await getEmployees({ page, search, pageSize: 10 });
            // API returns array directly based on client implementation, or response object.
            // Adjusting based on standard axios response structure if 'api.get' returns 'AxiosResponse'
            // If api.get returns T directly, then:
            if (Array.isArray(res)) {
                setEmployees(res);
                // Headers might be lost if api.get returns body directly. 
                // We might need to adjust api client later if pagination headers are vital.
            } else {
                // @ts-ignore
                setEmployees(res.data || res);
                // @ts-ignore
                if (res.headers && res.headers['x-total-count']) {
                    // @ts-ignore
                    setTotal(parseInt(res.headers['x-total-count']));
                }
            }
        } catch (error) {
            console.error('Failed to fetch employees', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (token) fetchEmployees();
    }, [token, page, search]);

    const handleDelete = async (id: string) => {
        if (confirm('Are you sure you want to delete this employee?')) {
            try {
                await deleteEmployee(id);
                fetchEmployees();
            } catch (error) {
                console.error(error);
                alert('Failed to delete');
            }
        }
    };

    const handleEdit = (employee: Employee) => {
        setSelectedEmployee(employee);
        setIsFormOpen(true);
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    const handleAdd = () => {
        setSelectedEmployee(null);
        setIsFormOpen(true);
    };

    const handleSendInvites = async (ids: string[]) => {
        setIsSending(true);
        try {
            await sendFaceScanInvites(ids);
            alert(`Successfully sent ${ids.length} invites.`);
            setSelectedIds([]);
            fetchEmployees();
        } catch (error) {
            alert('Failed to send invites.');
        } finally {
            setIsSending(false);
        }
    };

    const toggleSelectAll = () => {
        if (selectedIds.length === employees.length) {
            setSelectedIds([]);
        } else {
            setSelectedIds(employees.map(e => e.id));
        }
    };

    const toggleSelect = (id: string) => {
        setSelectedIds(prev => 
            prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
        );
    };

    return (
        <div className="p-8 max-w-[1600px] mx-auto">
            <div className="flex justify-between items-end mb-8">
                <div>
                    <h1 className="text-3xl font-bold bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">Employees</h1>
                    <p className="text-gray-500 mt-1">Manage and monitor your workforce metrics</p>
                </div>
                <div className="flex gap-4">
                    <button
                        onClick={() => setIsBulkOpen(true)}
                        className="flex items-center gap-2 px-5 py-2.5 bg-white border border-gray-200 rounded-xl hover:bg-gray-50 text-gray-700 font-medium transition-all hover:border-gray-300 shadow-sm"
                    >
                        <Upload className="w-4 h-4" /> Bulk Import
                    </button>
                    <button
                        onClick={handleAdd}
                        className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 transition-all shadow-lg shadow-blue-500/30 hover:shadow-blue-500/40 hover:scale-[1.02] active:scale-[0.98] font-medium"
                    >
                        <Plus className="w-4 h-4" /> Add Employee
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white p-1 rounded-2xl shadow-sm border border-gray-100 mb-8 max-w-lg">
                <div className="relative">
                    <Search className="absolute left-4 top-3.5 w-5 h-5 text-gray-400" />
                    <input
                        type="text"
                        placeholder="Search by name, email or ID..."
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        className="w-full pl-12 pr-4 py-3 border-none rounded-xl focus:ring-0 text-gray-700 placeholder-gray-400 bg-transparent"
                    />
                </div>
            </div>

            {/* Bulk Actions Toolbar */}
            {selectedIds.length > 0 && (
                <div className="bg-white p-4 rounded-2xl shadow-lg border border-indigo-100 mb-8 max-w-full flex items-center justify-between sticky top-4 z-10 animate-fade-in-up">
                    <div className="flex items-center gap-3">
                        <span className="flex items-center justify-center bg-indigo-100 text-indigo-700 font-bold rounded-full w-8 h-8 text-sm">
                            {selectedIds.length}
                        </span>
                        <span className="text-gray-700 font-medium">employees selected</span>
                    </div>
                    <div className="flex gap-3">
                        <button
                            onClick={() => handleSendInvites(selectedIds)}
                            disabled={isSending}
                            className="flex items-center gap-2 px-5 py-2.5 bg-indigo-600 text-white rounded-xl hover:bg-indigo-700 transition-all shadow-md disabled:opacity-50 font-medium"
                        >
                            {isSending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                            Send Face Scan Invites
                        </button>
                        <button
                            onClick={() => setSelectedIds([])}
                            className="px-5 py-2.5 bg-gray-100 text-gray-600 rounded-xl hover:bg-gray-200 transition-all font-medium"
                        >
                            Cancel
                        </button>
                    </div>
                </div>
            )}

            {/* Table */}
            <div className="bg-white rounded-2xl shadow-xl shadow-gray-100/50 border border-gray-100 overflow-hidden">
                {loading ? (
                    <div className="p-20 flex flex-col items-center justify-center gap-4">
                        <Loader2 className="w-10 h-10 animate-spin text-blue-600" />
                        <p className="text-gray-500 animate-pulse">Loading workforce data...</p>
                    </div>
                ) : employees.length === 0 ? (
                    <div className="p-20 text-center text-gray-500 flex flex-col items-center">
                        <div className="w-20 h-20 bg-gray-50 rounded-full flex items-center justify-center mb-4">
                            <User className="w-10 h-10 text-gray-300" />
                        </div>
                        <h3 className="text-lg font-semibold text-gray-900">No employees found</h3>
                        <p className="max-w-xs mx-auto mt-2">Get started by adding a new employee manually or importing a CSV.</p>
                        <button
                            onClick={handleAdd}
                            className="mt-6 text-blue-600 hover:text-blue-700 font-medium hover:underline"
                        >
                            Add your first employee
                        </button>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-left text-sm">
                            <thead className="bg-gray-50/80 text-gray-500 font-medium border-b border-gray-200 uppercase tracking-wider text-xs">
                                <tr>
                                    <th className="px-6 py-4 w-12">
                                        <input 
                                            type="checkbox" 
                                            checked={employees.length > 0 && selectedIds.length === employees.length}
                                            onChange={toggleSelectAll}
                                            className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                        />
                                    </th>
                                    <th className="px-8 py-4">Employee</th>
                                    <th className="px-6 py-4">Employee ID</th>
                                    <th className="px-6 py-4">Role</th>
                                    <th className="px-6 py-4">Department</th>
                                    <th className="px-6 py-4">Face Scan</th>
                                    <th className="px-6 py-4 text-right">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {employees.map((emp) => (
                                    <tr key={emp.id} className="hover:bg-blue-50/30 transition-colors group">
                                        <td className="px-6 py-4 w-12">
                                            <input 
                                                type="checkbox" 
                                                checked={selectedIds.includes(emp.id)}
                                                onChange={() => toggleSelect(emp.id)}
                                                className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                            />
                                        </td>
                                        <td className="px-8 py-4">
                                            <div className="flex items-center gap-3">
                                                <div className="w-10 h-10 rounded-full bg-gradient-to-br from-blue-100 to-indigo-100 flex items-center justify-center text-blue-700 font-bold text-sm">
                                                    {emp.firstName[0]}{emp.lastName[0]}
                                                </div>
                                                <div>
                                                    <p className="font-semibold text-gray-900">{emp.firstName} {emp.lastName}</p>
                                                    <p className="text-gray-500 text-xs">{emp.email}</p>
                                                </div>
                                            </div>
                                        </td>
                                        <td className="px-6 py-4">
                                            <span className="font-mono text-xs bg-gray-100 text-gray-600 px-2 py-1 rounded-md border border-gray-200">
                                                {emp.employeeId}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-gray-600 font-medium">{emp.designation || '-'}</td>
                                        <td className="px-6 py-4">
                                            {emp.department ? (
                                                <span className="inline-flex items-center px-3 py-1 rounded-full text-xs font-semibold bg-blue-50 text-blue-700 border border-blue-100">
                                                    {emp.department}
                                                </span>
                                            ) : <span className="text-gray-400">-</span>}
                                        </td>
                                        <td className="px-6 py-4">
                                            {emp.faceScanStatus === 'Completed' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium bg-green-50 text-green-700 border border-green-200">
                                                    <CheckCircle2 className="w-3.5 h-3.5" /> Completed
                                                </span>
                                            )}
                                            {emp.faceScanStatus === 'Pending' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium bg-amber-50 text-amber-700 border border-amber-200">
                                                    <AlertCircle className="w-3.5 h-3.5" /> Pending
                                                </span>
                                            )}
                                            {emp.faceScanStatus === 'NotAssigned' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium bg-gray-100 text-gray-600 border border-gray-200">
                                                    Not Assigned
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-6 py-4 text-right">
                                            <div className="flex justify-end gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                                                {emp.faceScanStatus !== 'Completed' && (
                                                    <button
                                                        onClick={() => handleSendInvites([emp.id])}
                                                        className="p-2 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors border border-transparent hover:border-indigo-100"
                                                        title="Send Invite"
                                                    >
                                                        <Send className="w-4 h-4" />
                                                    </button>
                                                )}
                                                <button
                                                    onClick={() => handleEdit(emp)}
                                                    className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors border border-transparent hover:border-blue-100"
                                                    title="Edit"
                                                >
                                                    <Edit2 className="w-4 h-4" />
                                                </button>
                                                <button
                                                    onClick={() => handleDelete(emp.id)}
                                                    className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors border border-transparent hover:border-red-100"
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

                {/* Pagination */}
                <div className="px-8 py-5 border-t border-gray-100 flex justify-between items-center bg-gray-50/30">
                    <span className="text-sm text-gray-500">
                        Page <span className="font-semibold text-gray-900">{page}</span>
                    </span>
                    <div className="flex gap-3">
                        <button
                            disabled={page <= 1}
                            onClick={() => setPage(p => p - 1)}
                            className="px-4 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors shadow-sm text-black"
                        >
                            Previous
                        </button>
                        <button
                            onClick={() => setPage(p => p + 1)}
                            className="px-4 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors shadow-sm text-black"
                        >
                            Next
                        </button>
                    </div>
                </div>
            </div>

            <EmployeeFormModal
                isOpen={isFormOpen}
                onClose={() => setIsFormOpen(false)}
                onSuccess={fetchEmployees}
                employee={selectedEmployee}
            />

            <BulkUploadModal
                isOpen={isBulkOpen}
                onClose={() => setIsBulkOpen(false)}
                onSuccess={fetchEmployees}
            />
        </div>
    );
}
