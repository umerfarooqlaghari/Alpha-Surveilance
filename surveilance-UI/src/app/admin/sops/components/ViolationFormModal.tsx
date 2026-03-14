'use client';

import { useState, useEffect, KeyboardEvent } from 'react';
import { X, Tag } from 'lucide-react';
import { createViolationType, updateViolationType } from '@/lib/api/sops';
import type { SopViolationTypeResponse } from '@/types/admin';

interface ViolationFormModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
    sopId: string;
    initialData?: SopViolationTypeResponse;
}

export default function ViolationFormModal({ isOpen, onClose, onSuccess, sopId, initialData }: ViolationFormModalProps) {
    const isEditing = !!initialData;
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [labelInput, setLabelInput] = useState('');

    const [formData, setFormData] = useState({
        name: '',
        modelIdentifier: '',
        description: '',
        triggerLabels: [] as string[],
    });

    useEffect(() => {
        if (isOpen) {
            const existingLabels = initialData?.triggerLabels
                ? initialData.triggerLabels.split(',').map(l => l.trim()).filter(Boolean)
                : [];
            setFormData({
                name: initialData?.name || '',
                modelIdentifier: initialData?.modelIdentifier || '',
                description: initialData?.description || '',
                triggerLabels: existingLabels,
            });
            setLabelInput('');
            setError(null);
        }
    }, [isOpen, initialData]);

    if (!isOpen) return null;

    const addLabel = () => {
        const trimmed = labelInput.trim().toLowerCase();
        if (trimmed && !formData.triggerLabels.includes(trimmed)) {
            setFormData(prev => ({ ...prev, triggerLabels: [...prev.triggerLabels, trimmed] }));
        }
        setLabelInput('');
    };

    const removeLabel = (label: string) => {
        setFormData(prev => ({ ...prev, triggerLabels: prev.triggerLabels.filter(l => l !== label) }));
    };

    const handleLabelKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Enter' || e.key === ',') {
            e.preventDefault();
            addLabel();
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setIsLoading(true);

        const payload = {
            ...formData,
            triggerLabels: formData.triggerLabels.join(', '),
        };

        try {
            if (isEditing) {
                await updateViolationType(initialData.id, payload);
            } else {
                await createViolationType(sopId, payload);
            }
            onSuccess();
        } catch (err) {
            setError((err as Error).message || 'An error occurred while saving the violation type');
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
            <div
                className="absolute inset-0 bg-black/50 backdrop-blur-sm transition-opacity"
                onClick={onClose}
            />

            <div className="relative bg-white rounded-xl shadow-xl w-full max-w-md overflow-hidden transform transition-all text-black">
                <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50/50">
                    <h2 className="text-xl font-semibold text-gray-900">
                        {isEditing ? 'Edit Violation Type' : 'Create New Violation Type'}
                    </h2>
                    <button
                        onClick={onClose}
                        className="p-2 text-gray-400 hover:text-gray-500 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <X className="w-5 h-5" />
                    </button>
                </div>

                <form onSubmit={handleSubmit} className="p-6 space-y-4">
                    {error && (
                        <div className="p-3 bg-red-50 text-red-700 rounded-lg text-sm border border-red-100">
                            {error}
                        </div>
                    )}

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            Violation Name
                        </label>
                        <input
                            type="text"
                            required
                            value={formData.name}
                            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                            placeholder="e.g. Hardhat Missing"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            Model Identifier
                        </label>
                        <input
                            type="text"
                            required
                            value={formData.modelIdentifier}
                            onChange={(e) => setFormData({ ...formData, modelIdentifier: e.target.value.replace(/\s+/g, '') })}
                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all font-mono"
                            placeholder="e.g. hardhat_v1"
                        />
                        <p className="text-xs text-gray-500 mt-1">The key that tells the Vision Engine which AI model to run.</p>
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            Description
                        </label>
                        <textarea
                            required
                            value={formData.description}
                            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                            rows={2}
                            className="w-full px-4 py-2 bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                            placeholder="Description of the specific violation..."
                        />
                    </div>

                    {/* Trigger Labels */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1 flex items-center gap-1.5">
                            <Tag className="w-3.5 h-3.5 text-blue-500" />
                            Trigger Labels
                        </label>
                        <div className="w-full min-h-[42px] px-3 py-2 bg-white border border-gray-300 rounded-lg focus-within:ring-2 focus-within:ring-blue-500 flex flex-wrap gap-1.5 items-center">
                            {formData.triggerLabels.map(label => (
                                <span key={label} className="flex items-center gap-1 px-2 py-0.5 bg-blue-100 text-blue-800 text-xs font-mono font-bold rounded border border-blue-200">
                                    {label}
                                    <button type="button" onClick={() => removeLabel(label)} className="text-blue-400 hover:text-blue-700">
                                        <X className="w-3 h-3" />
                                    </button>
                                </span>
                            ))}
                            <input
                                type="text"
                                value={labelInput}
                                onChange={(e) => setLabelInput(e.target.value)}
                                onKeyDown={handleLabelKeyDown}
                                onBlur={addLabel}
                                className="flex-1 min-w-[120px] outline-none text-sm bg-transparent"
                                placeholder={formData.triggerLabels.length === 0 ? "Type a label, press Enter (e.g. helmet)" : "Add more..."}
                            />
                        </div>
                        <p className="text-xs text-gray-500 mt-1">AI detection labels that must match to trigger this violation. Empty = match all detections from this model.</p>
                    </div>

                    <div className="flex gap-3 pt-4 border-t border-gray-100">
                        <button
                            type="button"
                            onClick={onClose}
                            className="flex-1 px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={isLoading}
                            className="flex-1 px-4 py-2 text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50"
                        >
                            {isLoading ? 'Saving...' : (isEditing ? 'Save Changes' : 'Add Violation')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
