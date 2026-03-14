'use client';

import { useState, useEffect } from 'react';
import { Loader2, X, Plus, Edit2, Trash2, FileText, Check } from 'lucide-react';
import { emailingApi } from '@/lib/api/tenant/emailing';
import { EmailTemplate } from '@/types/emailing';

interface TemplateManagerModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSelect: (template: EmailTemplate) => void;
}

export default function TemplateManagerModal({ isOpen, onClose, onSelect }: TemplateManagerModalProps) {
    const [templates, setTemplates] = useState<EmailTemplate[]>([]);
    const [loading, setLoading] = useState(false);
    const [view, setView] = useState<'list' | 'edit'>('list');
    const [editingTemplate, setEditingTemplate] = useState<Partial<EmailTemplate>>({});
    const [isSaving, setIsSaving] = useState(false);

    useEffect(() => {
        if (isOpen) {
            fetchTemplates();
            setView('list');
        }
    }, [isOpen]);

    const fetchTemplates = async () => {
        setLoading(true);
        try {
            const data = await emailingApi.getTemplates();
            setTemplates(data);
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    };

    const handleCreate = () => {
        setEditingTemplate({ name: '', subject: '', body: '' });
        setView('edit');
    };

    const handleEdit = (tmpl: EmailTemplate) => {
        setEditingTemplate({ ...tmpl });
        setView('edit');
    };

    const handleDelete = async (id: string) => {
        if (!confirm('Area you sure?')) return;
        try {
            await emailingApi.deleteTemplate(id);
            fetchTemplates();
        } catch (error) {
            console.error(error);
        }
    };

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            if (editingTemplate.id) {
                await emailingApi.updateTemplate({
                    id: editingTemplate.id,
                    name: editingTemplate.name!,
                    subject: editingTemplate.subject!,
                    body: editingTemplate.body!
                });
            } else {
                await emailingApi.createTemplate({
                    name: editingTemplate.name!,
                    subject: editingTemplate.subject!,
                    body: editingTemplate.body!
                });
            }
            fetchTemplates();
            setView('list');
        } catch (error) {
            console.error(error);
            alert('Failed to save template');
        } finally {
            setIsSaving(false);
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
            <div className="bg-white rounded-2xl shadow-xl w-full max-w-4xl flex flex-col h-[80vh]">
                <div className="p-6 border-b border-gray-100 flex justify-between items-center">
                    <h2 className="text-xl font-bold text-gray-900">
                        {view === 'list' ? 'Email Templates' : editingTemplate.id ? 'Edit Template' : 'New Template'}
                    </h2>
                    <button onClick={onClose} className="p-2 text-gray-400 hover:text-gray-600 rounded-full hover:bg-gray-100">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <div className="flex-1 overflow-hidden flex">
                    {/* List View */}
                    {view === 'list' && (
                        <div className="flex-1 flex flex-col">
                            <div className="p-6 border-b border-gray-100 bg-gray-50/30">
                                <button
                                    onClick={handleCreate}
                                    className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 shadow-md shadow-blue-500/20 text-sm font-medium"
                                >
                                    <Plus className="w-4 h-4" /> Create New Template
                                </button>
                            </div>
                            <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 md:grid-cols-2 gap-4">
                                {loading ? (
                                    <div className="col-span-2 flex justify-center"><Loader2 className="w-8 h-8 animate-spin text-blue-600" /></div>
                                ) : templates.length === 0 ? (
                                    <div className="col-span-2 text-center text-gray-500 py-10">No templates found. Create one!</div>
                                ) : (
                                    templates.map(tmpl => (
                                        <div key={tmpl.id} className="border border-gray-200 rounded-xl p-5 hover:border-blue-300 hover:shadow-md transition-all group bg-white">
                                            <div className="flex justify-between items-start mb-3">
                                                <div className="flex items-center gap-2">
                                                    <div className="p-2 bg-blue-50 text-blue-600 rounded-lg">
                                                        <FileText className="w-5 h-5" />
                                                    </div>
                                                    <h3 className="font-semibold text-gray-900">{tmpl.name}</h3>
                                                </div>
                                                <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                                    <button onClick={() => handleEdit(tmpl)} className="p-1.5 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded"><Edit2 className="w-4 h-4" /></button>
                                                    <button onClick={() => handleDelete(tmpl.id)} className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded"><Trash2 className="w-4 h-4" /></button>
                                                </div>
                                            </div>
                                            <p className="text-sm text-gray-500 mb-4 line-clamp-1">Subject: {tmpl.subject}</p>
                                            <button
                                                onClick={() => { onSelect(tmpl); onClose(); }}
                                                className="w-full py-2 text-sm font-medium text-blue-600 bg-blue-50 hover:bg-blue-100 rounded-lg transition-colors flex items-center justify-center gap-2"
                                            >
                                                Use Template
                                            </button>
                                        </div>
                                    ))
                                )}
                            </div>
                        </div>
                    )}

                    {/* Edit View */}
                    {view === 'edit' && (
                        <div className="flex-1 flex flex-col overflow-y-auto p-6">
                            <form id="template-form" onSubmit={handleSave} className="space-y-6 max-w-2xl mx-auto w-full">
                                <div className="space-y-2">
                                    <label className="text-sm font-semibold text-gray-700">Template Name</label>
                                    <input
                                        required
                                        value={editingTemplate.name}
                                        onChange={e => setEditingTemplate({ ...editingTemplate, name: e.target.value })}
                                        className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 text-black"
                                        placeholder="e.g. Monthly Violation Report"
                                    />
                                </div>
                                <div className="space-y-2">
                                    <label className="text-sm font-semibold text-gray-700">Email Subject</label>
                                    <input
                                        required
                                        value={editingTemplate.subject}
                                        onChange={e => setEditingTemplate({ ...editingTemplate, subject: e.target.value })}
                                        className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 text-black"
                                        placeholder="Subject line..."
                                    />
                                </div>
                                <div className="space-y-2">
                                    <label className="text-sm font-semibold text-gray-700">Email Body (HTML supported)</label>
                                    <textarea
                                        required
                                        rows={12}
                                        value={editingTemplate.body}
                                        onChange={e => setEditingTemplate({ ...editingTemplate, body: e.target.value })}
                                        className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 font-mono text-sm text-black"
                                        placeholder="<p>Hello,</p>..."
                                    />
                                    <p className="text-xs text-gray-500">Supports simple HTML tags.</p>
                                </div>
                                <div className="flex gap-3 pt-4">
                                    <button
                                        type="button"
                                        onClick={() => setView('list')}
                                        className="px-6 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg text-black"
                                    >
                                        Back to List
                                    </button>
                                    <button
                                        type="submit"
                                        disabled={isSaving}
                                        className="px-6 py-2.5 text-sm font-medium bg-blue-600 text-white rounded-lg hover:bg-blue-700 shadow-lg shadow-blue-500/30 flex items-center gap-2 text-black"
                                    >
                                        {isSaving && <Loader2 className="w-4 h-4 animate-spin" />} Save Template
                                    </button>
                                </div>
                            </form>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
