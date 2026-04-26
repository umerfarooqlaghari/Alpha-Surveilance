'use client';

import { useState, useEffect } from 'react';
import { X, Search, Users, Loader2, CheckCircle2, UserCheck } from 'lucide-react';
import { getEmployees } from '@/lib/api/tenant/employees';
import { addNotificationEmail, getNotificationEmails, type NotificationEmailEntry } from '@/lib/api/notificationEmails';
import type { Employee } from '@/types/employee';

interface ImportEmployeeModalProps {
    existingEmails: string[];                        // already-added emails to grey out
    onClose: () => void;
    onImported: (entries: NotificationEmailEntry[]) => void;
}

export default function ImportEmployeeModal({ existingEmails, onClose, onImported }: ImportEmployeeModalProps) {
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [filtered, setFiltered] = useState<Employee[]>([]);
    const [search, setSearch] = useState('');
    const [loading, setLoading] = useState(true);
    const [selected, setSelected] = useState<Set<string>>(new Set()); // keyed by email
    const [importing, setImporting] = useState(false);
    const [error, setError] = useState('');

    useEffect(() => {
        getEmployees({ pageSize: 500 })
            .then(data => {
                // Only show employees that have an email
                const withEmail = data.filter((e: Employee) => !!e.email);
                setEmployees(withEmail);
                setFiltered(withEmail);
            })
            .catch(e => setError((e as Error).message))
            .finally(() => setLoading(false));
    }, []);

    useEffect(() => {
        const q = search.toLowerCase();
        const fullName = (e: Employee) => `${e.firstName} ${e.lastName}`.toLowerCase();
        setFiltered(employees.filter(e =>
            fullName(e).includes(q) ||
            e.email?.toLowerCase().includes(q) ||
            e.department?.toLowerCase().includes(q)
        ));
    }, [search, employees]);

    const toggle = (email: string) => {
        if (existingEmails.includes(email)) return; // already added
        setSelected(prev => {
            const next = new Set(prev);
            next.has(email) ? next.delete(email) : next.add(email);
            return next;
        });
    };

    const handleImport = async () => {
        if (!selected.size) return;
        setImporting(true);
        setError('');
        const added: NotificationEmailEntry[] = [];
        try {
            for (const email of selected) {
                const emp = employees.find(e => e.email === email);
                const label = emp ? `${emp.firstName} ${emp.lastName}`.trim() || emp.department || undefined : undefined;
                const entry = await addNotificationEmail(email, label);
                added.push(entry);
            }
            onImported(added);
            onClose();
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setImporting(false);
        }
    };

    const alreadyCount = employees.filter(e => existingEmails.includes(e.email!)).length;

    return (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg flex flex-col max-h-[85vh]">
                {/* Header */}
                <div className="flex items-center justify-between p-5 border-b border-gray-100 flex-shrink-0">
                    <div>
                        <h3 className="text-base font-bold text-gray-900 flex items-center gap-2">
                            <UserCheck className="w-5 h-5 text-blue-600" /> Import from Employees
                        </h3>
                        <p className="text-xs text-gray-500 mt-0.5">
                            Select employees to add as violation alert recipients
                        </p>
                    </div>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600 p-1 rounded-full hover:bg-gray-100">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Search */}
                <div className="px-5 pt-4 pb-2 flex-shrink-0">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                        <input
                            autoFocus
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            placeholder="Search by name, email, or department..."
                            className="w-full pl-9 pr-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl text-sm text-black outline-none focus:ring-2 focus:ring-blue-400"
                        />
                    </div>
                    {alreadyCount > 0 && (
                        <p className="text-[11px] text-gray-400 mt-1.5">
                            {alreadyCount} employee{alreadyCount !== 1 ? 's' : ''} already added (greyed out)
                        </p>
                    )}
                </div>

                {/* Employee list */}
                <div className="flex-1 overflow-y-auto px-5 py-2 min-h-0">
                    {loading ? (
                        <div className="flex items-center justify-center h-32 gap-2 text-sm text-gray-400">
                            <Loader2 className="w-4 h-4 animate-spin" /> Loading employees...
                        </div>
                    ) : error ? (
                        <p className="text-sm text-red-600 text-center py-8">{error}</p>
                    ) : filtered.length === 0 ? (
                        <div className="text-center py-10">
                            <Users className="w-10 h-10 text-gray-200 mx-auto mb-2" />
                            <p className="text-sm text-gray-500">No employees found</p>
                        </div>
                    ) : (
                        <div className="space-y-1.5">
                            {filtered.map(emp => {
                                const alreadyAdded = existingEmails.includes(emp.email!);
                                const isSelected = selected.has(emp.email!);
                                return (
                                    <button
                                        key={emp.id}
                                        onClick={() => toggle(emp.email!)}
                                        disabled={alreadyAdded}
                                        className={`w-full flex items-center gap-3 p-3 rounded-xl text-left transition-all border
                                            ${alreadyAdded
                                                ? 'opacity-40 cursor-not-allowed bg-gray-50 border-gray-100'
                                                : isSelected
                                                    ? 'bg-blue-50 border-blue-200 shadow-sm'
                                                    : 'bg-white border-gray-100 hover:border-blue-200 hover:bg-blue-50/50'
                                            }`}
                                    >
                                        {/* Avatar */}
                                        <div className={`w-9 h-9 rounded-full flex items-center justify-center font-bold text-sm flex-shrink-0
                                            ${isSelected ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-600'}`}>
                                            {alreadyAdded
                                                ? <CheckCircle2 className="w-4 h-4 text-green-500" />
                                                : (`${emp.firstName[0] ?? emp.email?.[0] ?? '?'}`).toUpperCase()
                                            }
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <p className="text-sm font-semibold text-gray-900 truncate">
                                                {`${emp.firstName} ${emp.lastName}`.trim() || '—'}
                                                {alreadyAdded && <span className="ml-2 text-[10px] font-bold text-green-600 bg-green-50 px-1.5 py-0.5 rounded-full">Added</span>}
                                            </p>
                                            <p className="text-xs text-gray-500 truncate">{emp.email}</p>
                                            {emp.department && <p className="text-[10px] text-gray-400">{emp.department}</p>}
                                        </div>
                                        {isSelected && !alreadyAdded && (
                                            <CheckCircle2 className="w-5 h-5 text-blue-600 flex-shrink-0" />
                                        )}
                                    </button>
                                );
                            })}
                        </div>
                    )}
                </div>

                {/* Footer */}
                <div className="flex items-center justify-between p-5 border-t border-gray-100 flex-shrink-0">
                    <p className="text-sm text-gray-500">
                        {selected.size > 0
                            ? <span className="font-semibold text-blue-600">{selected.size} selected</span>
                            : 'Select employees above'
                        }
                    </p>
                    <div className="flex gap-2">
                        <button onClick={onClose}
                            className="px-4 py-2 text-sm font-semibold text-gray-600 border border-gray-200 rounded-xl hover:bg-gray-50">
                            Cancel
                        </button>
                        <button
                            onClick={handleImport}
                            disabled={!selected.size || importing}
                            className="px-5 py-2 text-sm font-bold text-white bg-blue-600 rounded-xl hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
                        >
                            {importing && <Loader2 className="w-4 h-4 animate-spin" />}
                            {importing ? 'Adding...' : `Add ${selected.size || ''} Recipient${selected.size !== 1 ? 's' : ''}`}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
}
