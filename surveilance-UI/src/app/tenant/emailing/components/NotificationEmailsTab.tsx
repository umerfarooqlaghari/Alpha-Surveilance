'use client';

import { useState, useEffect } from 'react';
import { Plus, Trash2, ToggleLeft, ToggleRight, Bell, Loader2, X, MailCheck, Users } from 'lucide-react';
import {
    getNotificationEmails,
    addNotificationEmail,
    toggleNotificationEmail,
    deleteNotificationEmail,
    type NotificationEmailEntry,
} from '@/lib/api/notificationEmails';
import ImportEmployeeModal from './ImportEmployeeModal';

export default function NotificationEmailsTab() {
    const [emails, setEmails] = useState<NotificationEmailEntry[]>([]);
    const [loading, setLoading] = useState(true);
    const [newEmail, setNewEmail] = useState('');
    const [newLabel, setNewLabel] = useState('');
    const [isAdding, setIsAdding] = useState(false);
    const [showForm, setShowForm] = useState(false);
    const [showImport, setShowImport] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const load = async () => {
        try {
            setLoading(true);
            setEmails(await getNotificationEmails());
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, []);

    const handleAdd = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setIsAdding(true);
        try {
            const entry = await addNotificationEmail(newEmail.trim(), newLabel.trim() || undefined);
            setEmails(prev => [...prev, entry]);
            setNewEmail('');
            setNewLabel('');
            setShowForm(false);
        } catch (err) {
            setError((err as Error).message);
        } finally {
            setIsAdding(false);
        }
    };

    const handleToggle = async (id: string) => {
        try {
            const updated = await toggleNotificationEmail(id);
            setEmails(prev => prev.map(e => e.id === id ? { ...e, isActive: updated.isActive } : e));
        } catch (err) {
            setError((err as Error).message);
        }
    };

    const handleDelete = async (id: string, email: string) => {
        if (!confirm(`Remove ${email} from violation notifications?`)) return;
        try {
            await deleteNotificationEmail(id);
            setEmails(prev => prev.filter(e => e.id !== id));
        } catch (err) {
            setError((err as Error).message);
        }
    };

    return (
        <div className="max-w-2xl">
            {/* Header */}
            <div className="flex items-start justify-between mb-6">
                <div>
                    <h2 className="text-xl font-bold text-gray-900 flex items-center gap-2">
                        <Bell className="w-5 h-5 text-blue-600" />
                        Violation Alert Recipients
                    </h2>
                    <p className="text-sm text-gray-500 mt-1">
                        These email addresses will receive an automatic alert whenever a <strong>High</strong> or
                        critical severity violation is detected by a camera on your account.
                    </p>
                </div>
                <div className="flex items-center gap-2 flex-shrink-0">
                    <button
                        onClick={() => setShowImport(true)}
                        className="flex items-center gap-2 px-4 py-2 bg-white text-gray-700 border border-gray-200 rounded-xl hover:bg-gray-50 transition-all font-semibold text-sm"
                    >
                        <Users className="w-4 h-4" />
                        Import
                    </button>
                    <button
                        onClick={() => setShowForm(v => !v)}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-all font-semibold text-sm shadow-sm shadow-blue-500/20"
                    >
                        <Plus className="w-4 h-4" />
                        Add Email
                    </button>
                </div>
            </div>

            {/* Error */}
            {error && (
                <div className="mb-4 p-3 bg-red-50 border border-red-100 rounded-xl text-sm text-red-700 flex justify-between items-center">
                    {error}
                    <button onClick={() => setError(null)}><X className="w-4 h-4" /></button>
                </div>
            )}

            {/* Add Form */}
            {showForm && (
                <form onSubmit={handleAdd} className="mb-6 p-5 bg-blue-50 border border-blue-100 rounded-2xl space-y-4">
                    <h3 className="font-semibold text-gray-900 text-sm">New Notification Recipient</h3>
                    <div className="grid gap-3 sm:grid-cols-2">
                        <div>
                            <label className="block text-xs font-semibold text-gray-600 mb-1">Email Address *</label>
                            <input
                                type="email"
                                required
                                value={newEmail}
                                onChange={e => setNewEmail(e.target.value)}
                                placeholder="manager@company.com"
                                className="w-full px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm text-black focus:ring-2 focus:ring-blue-400 outline-none"
                            />
                        </div>
                        <div>
                            <label className="block text-xs font-semibold text-gray-600 mb-1">
                                Label <span className="font-normal text-gray-400">(optional)</span>
                            </label>
                            <input
                                type="text"
                                value={newLabel}
                                onChange={e => setNewLabel(e.target.value)}
                                placeholder="e.g. Security Manager"
                                className="w-full px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm text-black focus:ring-2 focus:ring-blue-400 outline-none"
                            />
                        </div>
                    </div>
                    <div className="flex gap-2 justify-end">
                        <button type="button" onClick={() => setShowForm(false)}
                            className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800 bg-white border border-gray-200 rounded-lg transition-colors">
                            Cancel
                        </button>
                        <button type="submit" disabled={isAdding}
                            className="px-4 py-2 text-sm font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg transition-colors disabled:opacity-50 flex items-center gap-2">
                            {isAdding && <Loader2 className="w-3 h-3 animate-spin" />}
                            {isAdding ? 'Adding...' : 'Add Recipient'}
                        </button>
                    </div>
                </form>
            )}

            {/* Email List */}
            {loading ? (
                <div className="flex items-center gap-2 text-sm text-gray-500 py-8 justify-center">
                    <Loader2 className="w-4 h-4 animate-spin" /> Loading recipients...
                </div>
            ) : emails.length === 0 ? (
                <div className="text-center py-12 px-6 bg-gray-50 rounded-2xl border border-dashed border-gray-200">
                    <MailCheck className="w-10 h-10 text-gray-300 mx-auto mb-3" />
                    <p className="text-sm font-semibold text-gray-500">No notification recipients yet</p>
                    <p className="text-xs text-gray-400 mt-1">Add an email address to start receiving violation alerts.</p>
                </div>
            ) : (
                <div className="space-y-3">
                    {emails.map(entry => (
                        <div key={entry.id}
                            className={`flex items-center justify-between p-4 rounded-xl border transition-all ${entry.isActive
                                ? 'bg-white border-gray-200 shadow-sm'
                                : 'bg-gray-50 border-gray-100 opacity-60'}`}>
                            <div className="min-w-0">
                                <p className="text-sm font-semibold text-gray-900 truncate">{entry.email}</p>
                                {entry.label && (
                                    <p className="text-xs text-gray-500 mt-0.5">{entry.label}</p>
                                )}
                                {!entry.isActive && (
                                    <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wide">Paused</span>
                                )}
                            </div>
                            <div className="flex items-center gap-2 flex-shrink-0 ml-4">
                                <button
                                    onClick={() => handleToggle(entry.id)}
                                    title={entry.isActive ? 'Pause alerts' : 'Resume alerts'}
                                    className={`transition-colors ${entry.isActive ? 'text-blue-500 hover:text-blue-700' : 'text-gray-400 hover:text-blue-500'}`}
                                >
                                    {entry.isActive
                                        ? <ToggleRight className="w-6 h-6" />
                                        : <ToggleLeft className="w-6 h-6" />}
                                </button>
                                <button
                                    onClick={() => handleDelete(entry.id, entry.email)}
                                    className="text-gray-400 hover:text-red-500 transition-colors p-1"
                                >
                                    <Trash2 className="w-4 h-4" />
                                </button>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            <p className="text-xs text-gray-400 mt-5 leading-relaxed">
                <strong>Note:</strong> Email sending is subject to a 5-minute cooldown per camera+violation type to prevent alert flooding.
                Alerts are only sent for <strong>High</strong> severity detections.
            </p>

            {showImport && (
                <ImportEmployeeModal
                    existingEmails={emails.map(e => e.email)}
                    onClose={() => setShowImport(false)}
                    onImported={(entries) => setEmails(prev => [...prev, ...entries])}
                />
            )}
        </div>
    );
}
