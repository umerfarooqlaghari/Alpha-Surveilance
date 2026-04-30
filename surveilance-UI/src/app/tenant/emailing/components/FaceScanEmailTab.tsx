'use client';

import { useState, useEffect } from 'react';
import { getEmployees, sendFaceScanInvites } from '@/lib/api/tenant/employees';
import { Employee } from '@/types/employee';
import { Loader2, Send, Clock, UserX } from 'lucide-react';
import { formatDistanceToNow } from 'date-fns';

export default function FaceScanEmailTab() {
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [loading, setLoading] = useState(true);
    const [isSending, setIsSending] = useState(false);
    const [selectedNotAssigned, setSelectedNotAssigned] = useState<string[]>([]);
    const [selectedPending, setSelectedPending] = useState<string[]>([]);

    const fetchEmployees = async () => {
        setLoading(true);
        try {
            const res = await getEmployees({ pageSize: 1000 });
            // @ts-ignore
            setEmployees(Array.isArray(res) ? res : res.data || res);
        } catch (error) {
            console.error('Failed to fetch employees', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchEmployees();
    }, []);

    const notAssigned = employees.filter(e => e.faceScanStatus === 'NotAssigned');
    const pending = employees.filter(e => e.faceScanStatus === 'Pending');

    const handleSend = async (ids: string[], setSelection: (v: string[]) => void) => {
        if (!ids.length) return;
        setIsSending(true);
        try {
            await sendFaceScanInvites(ids);
            alert(`Successfully sent ${ids.length} invites.`);
            setSelection([]);
            fetchEmployees();
        } catch (error) {
            alert('Failed to send invites.');
        } finally {
            setIsSending(false);
        }
    };

    if (loading) {
        return (
            <div className="flex flex-col items-center justify-center p-20 bg-white rounded-2xl shadow-sm border border-gray-100">
                <Loader2 className="w-8 h-8 animate-spin text-blue-600 mb-4" />
                <p className="text-gray-500 font-medium">Loading employee status...</p>
            </div>
        );
    }

    return (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
            {/* Section 1: First-Time Invites */}
            <div className="bg-white rounded-2xl shadow-sm border border-gray-100 overflow-hidden flex flex-col h-[600px]">
                <div className="p-6 border-b border-gray-100 bg-gray-50/50 flex justify-between items-center">
                    <div>
                        <h2 className="text-lg font-bold text-gray-900 flex items-center gap-2">
                            <UserX className="w-5 h-5 text-blue-600" /> Need Enrollment
                        </h2>
                        <p className="text-sm text-gray-500 mt-1">Employees who have never received an invite.</p>
                    </div>
                    <span className="bg-blue-100 text-blue-800 text-xs font-bold px-3 py-1 rounded-full">
                        {notAssigned.length}
                    </span>
                </div>
                
                <div className="flex-1 overflow-y-auto p-4">
                    {notAssigned.length === 0 ? (
                        <p className="text-center text-gray-500 mt-10">All employees have been invited!</p>
                    ) : (
                        <ul className="space-y-2">
                            {notAssigned.map(emp => (
                                <li key={emp.id} className="flex items-center justify-between p-3 hover:bg-gray-50 rounded-lg border border-transparent hover:border-gray-100 transition-colors">
                                    <label className="flex items-center gap-3 cursor-pointer flex-1">
                                        <input
                                            type="checkbox"
                                            checked={selectedNotAssigned.includes(emp.id)}
                                            onChange={(e) => {
                                                if (e.target.checked) setSelectedNotAssigned(prev => [...prev, emp.id]);
                                                else setSelectedNotAssigned(prev => prev.filter(id => id !== emp.id));
                                            }}
                                            className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                                        />
                                        <div>
                                            <p className="font-semibold text-gray-900 text-sm">{emp.firstName} {emp.lastName}</p>
                                            <p className="text-xs text-gray-500">{emp.email}</p>
                                        </div>
                                    </label>
                                </li>
                            ))}
                        </ul>
                    )}
                </div>

                <div className="p-4 border-t border-gray-100 bg-gray-50/50 flex justify-between items-center">
                    <button 
                        onClick={() => setSelectedNotAssigned(notAssigned.length === selectedNotAssigned.length ? [] : notAssigned.map(e => e.id))}
                        className="text-sm text-blue-600 font-medium hover:text-blue-800"
                    >
                        {notAssigned.length > 0 && selectedNotAssigned.length === notAssigned.length ? 'Deselect All' : 'Select All'}
                    </button>
                    <button
                        onClick={() => handleSend(selectedNotAssigned, setSelectedNotAssigned)}
                        disabled={selectedNotAssigned.length === 0 || isSending}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors"
                    >
                        {isSending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                        Send First Invites ({selectedNotAssigned.length})
                    </button>
                </div>
            </div>

            {/* Section 2: Reminders */}
            <div className="bg-white rounded-2xl shadow-sm border border-gray-100 overflow-hidden flex flex-col h-[600px]">
                <div className="p-6 border-b border-gray-100 bg-gray-50/50 flex justify-between items-center">
                    <div>
                        <h2 className="text-lg font-bold text-gray-900 flex items-center gap-2">
                            <Clock className="w-5 h-5 text-amber-500" /> Pending Reminders
                        </h2>
                        <p className="text-sm text-gray-500 mt-1">Invites sent but scans not yet completed.</p>
                    </div>
                    <span className="bg-amber-100 text-amber-800 text-xs font-bold px-3 py-1 rounded-full">
                        {pending.length}
                    </span>
                </div>
                
                <div className="flex-1 overflow-y-auto p-4">
                    {pending.length === 0 ? (
                        <p className="text-center text-gray-500 mt-10">No pending enrollments!</p>
                    ) : (
                        <ul className="space-y-2">
                            {pending.map(emp => (
                                <li key={emp.id} className="flex items-center justify-between p-3 hover:bg-gray-50 rounded-lg border border-transparent hover:border-gray-100 transition-colors">
                                    <label className="flex items-center gap-3 cursor-pointer flex-1">
                                        <input
                                            type="checkbox"
                                            checked={selectedPending.includes(emp.id)}
                                            onChange={(e) => {
                                                if (e.target.checked) setSelectedPending(prev => [...prev, emp.id]);
                                                else setSelectedPending(prev => prev.filter(id => id !== emp.id));
                                            }}
                                            className="rounded border-gray-300 text-amber-600 focus:ring-amber-500"
                                        />
                                        <div>
                                            <p className="font-semibold text-gray-900 text-sm">{emp.firstName} {emp.lastName}</p>
                                            <p className="text-xs text-gray-500">Sent {emp.faceScanInviteSentAt ? formatDistanceToNow(new Date(emp.faceScanInviteSentAt), { addSuffix: true }) : 'unknown'}</p>
                                        </div>
                                    </label>
                                </li>
                            ))}
                        </ul>
                    )}
                </div>

                <div className="p-4 border-t border-gray-100 bg-gray-50/50 flex justify-between items-center">
                    <button 
                        onClick={() => setSelectedPending(pending.length === selectedPending.length ? [] : pending.map(e => e.id))}
                        className="text-sm text-amber-600 font-medium hover:text-amber-800"
                    >
                        {pending.length > 0 && selectedPending.length === pending.length ? 'Deselect All' : 'Select All'}
                    </button>
                    <button
                        onClick={() => handleSend(selectedPending, setSelectedPending)}
                        disabled={selectedPending.length === 0 || isSending}
                        className="flex items-center gap-2 px-4 py-2 bg-amber-600 text-white text-sm font-medium rounded-lg hover:bg-amber-700 disabled:opacity-50 transition-colors"
                    >
                        {isSending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                        Send Reminders ({selectedPending.length})
                    </button>
                </div>
            </div>
        </div>
    );
}
