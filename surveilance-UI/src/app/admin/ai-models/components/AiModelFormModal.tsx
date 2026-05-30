'use client';

import { useState, useEffect } from 'react';
import { X, Brain } from 'lucide-react';
import { registerAiModel, updateAiModel } from '@/lib/api/aiModels';
import type { AiModelResponse, AiModelType, RegisterAiModelRequest } from '@/types/admin';

interface Props {
    model?: AiModelResponse;
    onClose: () => void;
    onSaved: () => void;
}

const MODEL_TYPES: { value: AiModelType; label: string; hint: string }[] = [
    { value: 'YoloLocal',     label: 'YOLO Local',       hint: '.pt file downloaded to the edge device' },
    { value: 'YoloFineTuned', label: 'YOLO Fine-Tuned',  hint: 'Custom-trained .pt — same loading path as Local' },
    { value: 'RoboflowCloud', label: 'Roboflow Cloud',   hint: 'Inference via Roboflow hosted API — no file download' },
];

export default function AiModelFormModal({ model, onClose, onSaved }: Props) {
    const isEdit = !!model;

    const [form, setForm] = useState<RegisterAiModelRequest>({
        modelKey:      model?.modelKey      ?? '',
        displayName:   model?.displayName   ?? '',
        description:   model?.description   ?? '',
        modelType:     model?.modelType     ?? 'YoloLocal',
        downloadUrl:   model?.downloadUrl   ?? '',
        s3Bucket:      model?.s3Bucket      ?? '',
        s3Key:         model?.s3Key         ?? '',
        localPath:     model?.localPath     ?? '',
        version:       model?.version       ?? '',
        sha256Checksum: model?.sha256Checksum ?? '',
    });

    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const isLocalModel = form.modelType !== 'RoboflowCloud';

    const set = (key: keyof RegisterAiModelRequest, value: string | AiModelType) =>
        setForm(prev => ({ ...prev, [key]: value }));

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setSaving(true);
        setError(null);
        try {
            const payload: RegisterAiModelRequest = {
                ...form,
                downloadUrl:    form.downloadUrl    || undefined,
                s3Bucket:       form.s3Bucket       || undefined,
                s3Key:          form.s3Key          || undefined,
                localPath:      form.localPath      || undefined,
                version:        form.version        || undefined,
                sha256Checksum: form.sha256Checksum || undefined,
            };
            if (isEdit) {
                await updateAiModel(model!.id, payload);
            } else {
                await registerAiModel(payload);
            }
            onSaved();
        } catch (err) {
            setError((err as Error).message);
        } finally {
            setSaving(false);
        }
    };

    // Close on Escape
    useEffect(() => {
        const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', handler);
        return () => window.removeEventListener('keydown', handler);
    }, [onClose]);

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
            <div className="bg-white rounded-2xl shadow-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                {/* Header */}
                <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100">
                    <div className="flex items-center gap-2">
                        <Brain className="w-5 h-5 text-blue-500" />
                        <h2 className="text-lg font-semibold text-gray-900">
                            {isEdit ? 'Edit AI Model' : 'Register AI Model'}
                        </h2>
                    </div>
                    <button onClick={onClose} className="p-1.5 rounded-lg text-gray-400 hover:bg-gray-100 transition-colors">
                        <X className="w-4 h-4" />
                    </button>
                </div>

                {/* Body */}
                <form onSubmit={handleSubmit} className="px-6 py-5 space-y-5">
                    {error && (
                        <div className="bg-red-50 border border-red-200 text-red-700 rounded-xl px-4 py-3 text-sm">
                            {error}
                        </div>
                    )}

                    {/* Model Key + Display Name */}
                    <div className="grid grid-cols-2 gap-4">
                        <Field label="Model Key *" hint="Unique slug — must match SopViolationType identifier">
                            <input
                                required
                                disabled={isEdit}
                                value={form.modelKey}
                                onChange={e => set('modelKey', e.target.value)}
                                placeholder="e.g. restaurant-ppe-v1"
                                className="input-base disabled:bg-gray-50 disabled:text-gray-400"
                            />
                        </Field>
                        <Field label="Display Name *">
                            <input
                                required
                                value={form.displayName}
                                onChange={e => set('displayName', e.target.value)}
                                placeholder="e.g. Restaurant PPE YOLO v2"
                                className="input-base"
                            />
                        </Field>
                    </div>

                    {/* Description */}
                    <Field label="Description">
                        <textarea
                            value={form.description}
                            onChange={e => set('description', e.target.value)}
                            rows={2}
                            placeholder="Brief description of what this model detects."
                            className="input-base resize-none"
                        />
                    </Field>

                    {/* Model Type */}
                    <Field label="Model Type *">
                        <div className="grid grid-cols-3 gap-2">
                            {MODEL_TYPES.map(t => (
                                <button
                                    type="button"
                                    key={t.value}
                                    onClick={() => set('modelType', t.value)}
                                    className={`text-left border rounded-xl px-3 py-2.5 transition-colors ${
                                        form.modelType === t.value
                                            ? 'border-blue-400 bg-blue-50 text-blue-700'
                                            : 'border-gray-200 text-gray-600 hover:border-gray-300'
                                    }`}
                                >
                                    <p className="text-sm font-medium">{t.label}</p>
                                    <p className="text-xs text-gray-400 mt-0.5">{t.hint}</p>
                                </button>
                            ))}
                        </div>
                    </Field>

                    {/* Local-model fields */}
                    {isLocalModel && (
                        <>
                            <div className="border-t border-gray-100 pt-4">
                                <p className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">Download Configuration</p>
                                <div className="space-y-4">
                                    <Field label="Direct Download URL" hint="HTTPS or presigned S3 URL the edge device downloads from">
                                        <input
                                            value={form.downloadUrl ?? ''}
                                            onChange={e => set('downloadUrl', e.target.value)}
                                            placeholder="https://…/model.pt"
                                            className="input-base"
                                        />
                                    </Field>
                                    <div className="grid grid-cols-2 gap-4">
                                        <Field label="S3 Bucket" hint="Alternative to direct URL">
                                            <input
                                                value={form.s3Bucket ?? ''}
                                                onChange={e => set('s3Bucket', e.target.value)}
                                                placeholder="my-models-bucket"
                                                className="input-base"
                                            />
                                        </Field>
                                        <Field label="S3 Key">
                                            <input
                                                value={form.s3Key ?? ''}
                                                onChange={e => set('s3Key', e.target.value)}
                                                placeholder="models/restaurant-ppe-v2.pt"
                                                className="input-base"
                                            />
                                        </Field>
                                    </div>
                                    <Field label="Local Path on Edge Device" hint="Absolute path where the file is saved">
                                        <input
                                            value={form.localPath ?? ''}
                                            onChange={e => set('localPath', e.target.value)}
                                            placeholder="/tmp/models/restaurant-ppe-v2.pt"
                                            className="input-base"
                                        />
                                    </Field>
                                </div>
                            </div>
                        </>
                    )}

                    {/* Version + Checksum */}
                    <div className="grid grid-cols-2 gap-4">
                        <Field label="Version">
                            <input
                                value={form.version ?? ''}
                                onChange={e => set('version', e.target.value)}
                                placeholder="2.1.0"
                                className="input-base"
                            />
                        </Field>
                        <Field label="SHA-256 Checksum" hint="Optional integrity check">
                            <input
                                value={form.sha256Checksum ?? ''}
                                onChange={e => set('sha256Checksum', e.target.value)}
                                placeholder="64-char hex"
                                className="input-base font-mono text-xs"
                            />
                        </Field>
                    </div>

                    {/* Footer */}
                    <div className="flex justify-end gap-3 pt-2 border-t border-gray-100">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={saving}
                            className="px-5 py-2 bg-blue-500 hover:bg-blue-600 text-white text-sm font-medium rounded-xl shadow transition-colors disabled:opacity-60"
                        >
                            {saving ? 'Saving…' : isEdit ? 'Save Changes' : 'Register Model'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}

// Tiny Field wrapper
function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
    return (
        <div className="space-y-1.5">
            <label className="block text-sm font-medium text-gray-700">{label}</label>
            {hint && <p className="text-xs text-gray-400">{hint}</p>}
            {children}
        </div>
    );
}
