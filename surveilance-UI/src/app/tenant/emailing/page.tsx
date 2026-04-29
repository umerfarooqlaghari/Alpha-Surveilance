'use client';

import { useState } from 'react';
import { Mail, FileText, Paperclip, Users, AlertTriangle, Send, Loader2, X, Bell, User } from 'lucide-react';
import SelectEmployeesModal from '@/components/emailing/SelectEmployeesModal';
import SelectViolationsModal from '@/components/emailing/SelectViolationsModal';
import TemplateManagerModal from '@/components/emailing/TemplateManagerModal';
import NotificationEmailsTab from './components/NotificationEmailsTab';
import FaceScanEmailTab from './components/FaceScanEmailTab';
import { emailingApi } from '@/lib/api/tenant/emailing';
import { EmailTemplate } from '@/types/emailing';

type Tab = 'compose' | 'notifications' | 'facescan';

export default function EmailingPage() {
    const [activeTab, setActiveTab] = useState<Tab>('compose');

    // Compose Form State
    const [subject, setSubject] = useState('');
    const [body, setBody] = useState('');
    const [selectedEmployeeIds, setSelectedEmployeeIds] = useState<string[]>([]);
    const [selectedViolationIds, setSelectedViolationIds] = useState<string[]>([]);
    const [auditAttachments, setAuditAttachments] = useState<File[]>([]);
    const [isSending, setIsSending] = useState(false);

    // Modals
    const [isEmployeesModalOpen, setIsEmployeesModalOpen] = useState(false);
    const [isViolationsModalOpen, setIsViolationsModalOpen] = useState(false);
    const [isTemplatesModalOpen, setIsTemplatesModalOpen] = useState(false);

    const handleTemplateSelect = (template: EmailTemplate) => {
        setSubject(template.subject);
        setBody(template.body);
    };

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files) {
            setAuditAttachments(prev => [...prev, ...Array.from(e.target.files!)]);
        }
    };

    const removeFile = (index: number) => {
        setAuditAttachments(prev => prev.filter((_, i) => i !== index));
    };

    const handleSend = async () => {
        if (!selectedEmployeeIds.length) return alert('Please select at least one recipient.');
        if (!subject) return alert('Subject is required.');
        if (!body) return alert('Body is required.');

        setIsSending(true);
        try {
            await emailingApi.sendEmail({
                employeeIds: selectedEmployeeIds,
                violationIds: selectedViolationIds,
                subject,
                body,
                attachments: auditAttachments
            });
            alert('Email sent successfully!');
            setSubject('');
            setBody('');
            setSelectedEmployeeIds([]);
            setSelectedViolationIds([]);
            setAuditAttachments([]);
        } catch (error: any) {
            alert(`Failed to send email: ${error.message}`);
        } finally {
            setIsSending(false);
        }
    };

    return (
        <div className="p-8 max-w-[1600px] mx-auto">
            {/* Page Header */}
            <div className="flex justify-between items-end mb-6">
                <div>
                    <h1 className="text-3xl font-bold bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">
                        Emailing
                    </h1>
                    <p className="text-gray-500 mt-1">Compose emails and manage violation alert recipients</p>
                </div>
                {activeTab === 'compose' && (
                    <button
                        onClick={() => setIsTemplatesModalOpen(true)}
                        className="flex items-center gap-2 px-5 py-2.5 bg-white border border-gray-200 rounded-xl hover:bg-gray-50 text-gray-700 font-medium transition-all shadow-sm"
                    >
                        <FileText className="w-4 h-4" /> Manage Templates
                    </button>
                )}
            </div>

            {/* Sub-tabs */}
            <div className="flex gap-1 mb-8 bg-gray-100 rounded-xl p-1 w-fit">
                <button
                    onClick={() => setActiveTab('compose')}
                    className={`flex items-center gap-2 px-5 py-2.5 rounded-lg font-semibold text-sm transition-all ${activeTab === 'compose'
                        ? 'bg-white text-gray-900 shadow-sm'
                        : 'text-gray-500 hover:text-gray-700'}`}
                >
                    <Mail className="w-4 h-4" />
                    Compose
                </button>
                <button
                    onClick={() => setActiveTab('notifications')}
                    className={`flex items-center gap-2 px-5 py-2.5 rounded-lg font-semibold text-sm transition-all ${activeTab === 'notifications'
                        ? 'bg-white text-gray-900 shadow-sm'
                        : 'text-gray-500 hover:text-gray-700'}`}
                >
                    <Bell className="w-4 h-4" />
                    Notification Emails
                </button>
                <button
                    onClick={() => setActiveTab('facescan')}
                    className={`flex items-center gap-2 px-5 py-2.5 rounded-lg font-semibold text-sm transition-all ${activeTab === 'facescan'
                        ? 'bg-white text-gray-900 shadow-sm'
                        : 'text-gray-500 hover:text-gray-700'}`}
                >
                    <User className="w-4 h-4" />
                    Face Scan Invites
                </button>
            </div>

            {/* Face Scan Tab */}
            {activeTab === 'facescan' && <FaceScanEmailTab />}

            {/* Notification Emails Tab */}
            {activeTab === 'notifications' && <NotificationEmailsTab />}

            {/* Compose Tab */}
            {activeTab === 'compose' && (
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
                    {/* Main Compose Area */}
                    <div className="lg:col-span-2 space-y-6">
                        {/* Subject */}
                        <div className="bg-white p-6 rounded-2xl shadow-sm border border-gray-100">
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Subject</label>
                            <input
                                value={subject}
                                onChange={(e) => setSubject(e.target.value)}
                                className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none transition-all text-black"
                                placeholder="Enter email subject..."
                            />
                        </div>

                        {/* Body */}
                        <div className="bg-white p-6 rounded-2xl shadow-sm border border-gray-100 flex flex-col h-[500px]">
                            <label className="block text-sm font-semibold text-gray-700 mb-2">Message Body</label>
                            <textarea
                                value={body}
                                onChange={(e) => setBody(e.target.value)}
                                className="flex-1 w-full p-4 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 outline-none transition-all resize-none font-mono text-sm text-black"
                                placeholder="Type your message here... (HTML is supported)"
                            />
                            <p className="text-xs text-gray-400 mt-2 text-right">Supports basic HTML tags</p>
                        </div>
                    </div>

                    {/* Sidebar Controls */}
                    <div className="space-y-6">
                        {/* Recipients */}
                        <div className="bg-white p-6 rounded-2xl shadow-sm border border-gray-100">
                            <div className="flex justify-between items-center mb-4">
                                <h3 className="font-semibold text-gray-900 flex items-center gap-2">
                                    <Users className="w-5 h-5 text-gray-400" /> Recipients
                                </h3>
                                <button
                                    onClick={() => setIsEmployeesModalOpen(true)}
                                    className="text-sm text-blue-600 hover:text-blue-700 font-medium"
                                >
                                    Select
                                </button>
                            </div>
                            {selectedEmployeeIds.length === 0 ? (
                                <p className="text-sm text-gray-500 italic">No recipients selected</p>
                            ) : (
                                <div className="flex flex-wrap gap-2">
                                    <span className="px-3 py-1 bg-blue-50 text-blue-700 rounded-full text-sm font-medium border border-blue-100 text-black">
                                        {selectedEmployeeIds.length} Recipients
                                    </span>
                                    <button onClick={() => setSelectedEmployeeIds([])} className="text-xs text-gray-400 hover:text-red-500 underline">Clear</button>
                                </div>
                            )}
                        </div>

                        {/* Attachments */}
                        <div className="bg-white p-6 rounded-2xl shadow-sm border border-gray-100">
                            <h3 className="font-semibold text-gray-900 flex items-center gap-2 mb-4">
                                <Paperclip className="w-5 h-5 text-gray-400" /> Attachments
                            </h3>

                            {/* Violations */}
                            <div className="mb-4">
                                <div className="flex justify-between items-center mb-2">
                                    <label className="text-sm font-medium text-gray-700 flex items-center gap-1">
                                        <AlertTriangle className="w-3 h-3 text-orange-500" /> Violations
                                    </label>
                                    <button
                                        onClick={() => setIsViolationsModalOpen(true)}
                                        className="text-xs text-blue-600 hover:text-blue-700 font-medium"
                                    >
                                        Select
                                    </button>
                                </div>
                                {selectedViolationIds.length > 0 && (
                                    <div className="p-3 bg-orange-50 rounded-lg border border-orange-100 text-sm text-orange-800 flex justify-between items-center text-black">
                                        <span>{selectedViolationIds.length} Violations Selected</span>
                                        <button onClick={() => setSelectedViolationIds([])} className="text-orange-400 hover:text-orange-600"><X className="w-4 h-4" /></button>
                                    </div>
                                )}
                            </div>

                            {/* Files */}
                            <div>
                                <label className="text-sm font-medium text-gray-700 mb-2 block">Files</label>
                                <input
                                    type="file"
                                    multiple
                                    onChange={handleFileChange}
                                    className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-full file:border-0 file:text-sm file:font-semibold file:bg-blue-50 file:text-blue-700 hover:file:bg-blue-100 transition-all"
                                />
                                {auditAttachments.length > 0 && (
                                    <div className="mt-3 space-y-2">
                                        {auditAttachments.map((file, i) => (
                                            <div key={i} className="flex justify-between items-center p-2 bg-gray-50 rounded-lg text-sm text-black">
                                                <span className="truncate max-w-[150px]">{file.name}</span>
                                                <button onClick={() => removeFile(i)} className="text-gray-400 hover:text-red-500"><X className="w-4 h-4" /></button>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                        {/* Send Button */}
                        <button
                            onClick={handleSend}
                            disabled={isSending}
                            className="w-full py-4 bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 shadow-lg shadow-blue-500/30 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2 transition-all hover:scale-[1.02] active:scale-[0.98] font-bold text-lg"
                        >
                            {isSending ? (
                                <><Loader2 className="w-5 h-5 animate-spin" /> Sending...</>
                            ) : (
                                <><Send className="w-5 h-5" /> Send Email</>
                            )}
                        </button>
                    </div>
                </div>
            )}

            {/* Modals */}
            <SelectEmployeesModal
                isOpen={isEmployeesModalOpen}
                onClose={() => setIsEmployeesModalOpen(false)}
                onSelect={(ids) => setSelectedEmployeeIds(ids)}
                initialSelectedIds={selectedEmployeeIds}
            />
            <SelectViolationsModal
                isOpen={isViolationsModalOpen}
                onClose={() => setIsViolationsModalOpen(false)}
                onSelect={(ids) => setSelectedViolationIds(ids)}
                initialSelectedIds={selectedViolationIds}
            />
            <TemplateManagerModal
                isOpen={isTemplatesModalOpen}
                onClose={() => setIsTemplatesModalOpen(false)}
                onSelect={handleTemplateSelect}
            />
        </div>
    );
}
