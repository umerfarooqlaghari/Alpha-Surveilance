'use client';

import { useState, useEffect } from 'react';
import { Loader2, X, Search, User } from 'lucide-react';
import { Employee } from '@/types/employee';
import { getEmployees } from '@/lib/api/tenant/employees';

interface SelectEmployeesModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSelect: (selectedIds: string[]) => void;
    initialSelectedIds?: string[];
}

export default function SelectEmployeesModal({ isOpen, onClose, onSelect, initialSelectedIds = [] }: SelectEmployeesModalProps) {
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [loading, setLoading] = useState(false);
    const [search, setSearch] = useState('');
    const [selectedIds, setSelectedIds] = useState<string[]>(initialSelectedIds);

    // Filters
    const [designation, setDesignation] = useState('');
    const [department, setDepartment] = useState('');
    const [filterOptions, setFilterOptions] = useState<{ designations: string[], departments: string[] }>({ designations: [], departments: [] });

    useEffect(() => {
        if (isOpen) {
            fetchEmployees();
            setSelectedIds(initialSelectedIds);
            fetchFilterOptions();
        }
    }, [isOpen]);

    const fetchFilterOptions = async () => {
        try {
            const res = await getEmployees({ page: 1, pageSize: 200 });
            const emps = Array.isArray(res) ? res : (res as any).data || [];

            const uniqueDesignations = Array.from(new Set(emps.map((e: Employee) => e.designation).filter(Boolean))) as string[];
            const uniqueDepartments = Array.from(new Set(emps.map((e: Employee) => e.department).filter(Boolean))) as string[];

            setFilterOptions({
                designations: uniqueDesignations.sort(),
                departments: uniqueDepartments.sort()
            });
        } catch (error) {
            console.error('Failed to fetch filter options', error);
        }
    };

    const fetchEmployees = async () => {
        setLoading(true);
        try {
            const res = await getEmployees({
                page: 1,
                pageSize: 100,
                search,
                designation: designation || undefined,
                department: department || undefined
            });
            if (Array.isArray(res)) {
                setEmployees(res);
            } else {
                // @ts-ignore
                setEmployees(res.data || res);
            }
        } catch (error) {
            console.error('Failed to fetch employees', error);
        } finally {
            setLoading(false);
        }
    };

    // Debounce search and filters
    useEffect(() => {
        const timer = setTimeout(() => {
            if (isOpen) fetchEmployees();
        }, 500);
        return () => clearTimeout(timer);
    }, [search, designation, department]);

    const toggleSelection = (id: string) => {
        setSelectedIds(prev =>
            prev.includes(id)
                ? prev.filter(x => x !== id)
                : [...prev, id]
        );
    };

    const handleConfirm = () => {
        onSelect(selectedIds);
        onClose();
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
            <div className="bg-white rounded-2xl shadow-xl w-full max-w-2xl flex flex-col max-h-[90vh]">
                <div className="p-6 border-b border-gray-100 flex justify-between items-center">
                    <h2 className="text-xl font-bold text-gray-900">Select Recipients</h2>
                    <button onClick={onClose} className="p-2 text-gray-400 hover:text-gray-600 rounded-full hover:bg-gray-100">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <div className="p-6 border-b border-gray-100 space-y-3">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 w-5 h-5" />
                        <input
                            type="text"
                            placeholder="Search employees..."
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            className="w-full pl-10 pr-4 py-2 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-black"
                        />
                    </div>
                    <div className="flex gap-3">
                        <select
                            value={department}
                            onChange={(e) => setDepartment(e.target.value)}
                            className="flex-1 px-3 py-2 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-sm text-black bg-white"
                        >
                            <option value="">All Departments</option>
                            {filterOptions.departments.map(d => (
                                <option key={d} value={d}>{d}</option>
                            ))}
                        </select>
                        <select
                            value={designation}
                            onChange={(e) => setDesignation(e.target.value)}
                            className="flex-1 px-3 py-2 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none text-sm text-black bg-white"
                        >
                            <option value="">All Designations</option>
                            {filterOptions.designations.map(d => (
                                <option key={d} value={d}>{d}</option>
                            ))}
                        </select>
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto p-2">
                    {loading ? (
                        <div className="flex justify-center p-8"><Loader2 className="w-6 h-6 animate-spin text-blue-600" /></div>
                    ) : employees.length === 0 ? (
                        <div className="text-center p-8 text-gray-500">No employees found.</div>
                    ) : (
                        <div className="space-y-1">
                            {employees.map(emp => (
                                <div
                                    key={emp.id}
                                    onClick={() => toggleSelection(emp.id)}
                                    className={`flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-colors ${selectedIds.includes(emp.id) ? 'bg-blue-50 border border-blue-200' : 'hover:bg-gray-50 border border-transparent'}`}
                                >
                                    <input
                                        type="checkbox"
                                        checked={selectedIds.includes(emp.id)}
                                        readOnly
                                        className="w-4 h-4 text-blue-600 rounded border-gray-300 focus:ring-blue-500"
                                    />
                                    <div className="w-8 h-8 rounded-full bg-gray-100 flex items-center justify-center text-gray-600 text-xs font-bold">
                                        {(emp.firstName?.[0] || '')}{(emp.lastName?.[0] || '')}
                                    </div>
                                    <div className="flex-1">
                                        <div className="flex justify-between items-start">
                                            <div>
                                                <p className="text-sm font-medium text-gray-900">{emp.firstName} {emp.lastName}</p>
                                                <p className="text-xs text-gray-500">{emp.email}</p>
                                            </div>
                                            {(emp.designation || emp.department) && (
                                                <div className="flex flex-col items-end gap-1">
                                                    {emp.designation && <span className="text-[10px] bg-blue-50 text-blue-700 px-1.5 py-0.5 rounded border border-blue-100 whitespace-nowrap">{emp.designation}</span>}
                                                    {emp.department && <span className="text-[10px] bg-gray-100 text-gray-600 px-1.5 py-0.5 rounded border border-gray-200 whitespace-nowrap">{emp.department}</span>}
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>

                <div className="p-6 border-t border-gray-100 flex justify-between items-center bg-gray-50/50 rounded-b-2xl">
                    <span className="text-sm text-gray-500">{selectedIds.length} selected</span>
                    <div className="flex gap-3">
                        <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-200/50 rounded-lg">Cancel</button>
                        <button
                            onClick={handleConfirm}
                            className="px-4 py-2 text-sm font-medium bg-blue-600 text-white rounded-lg hover:bg-blue-700 shadow-lg shadow-blue-500/30"
                        >
                            Confirm Selection
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}
