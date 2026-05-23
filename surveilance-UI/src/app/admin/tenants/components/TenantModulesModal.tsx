'use client';

import { useState, useEffect } from 'react';
import { X, CheckCircle2, Circle } from 'lucide-react';
import { getTenantModules, updateTenantModule } from '@/lib/api/tenants';

interface TenantModulesModalProps {
    tenantId: string;
    tenantName: string;
    onClose: () => void;
}

const AVAILABLE_MODULES = [
    { key: 'heatmaps', label: 'Aisle Heatmaps', description: 'Customer movement tracking' },
    { key: 'planograms', label: 'Planogram AI', description: 'Shelf availability monitoring' },
    { key: 'construction', label: 'Construction Safety', description: 'Hard hat and vest detection' },
    { key: 'restaurant', label: 'Kitchen Hygiene', description: 'Hairnet and mask compliance' },
    { key: 'logistics', label: 'Factory Logistics', description: 'Forklift and path safety' },
];

export default function TenantModulesModal({ tenantId, tenantName, onClose }: TenantModulesModalProps) {
    const [enabledModules, setEnabledModules] = useState<string[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState<string | null>(null);

    useEffect(() => {
        const loadModules = async () => {
            try {
                const data = await getTenantModules(tenantId);
                setEnabledModules(data.filter(m => m.isEnabled).map(m => m.moduleKey));
            } catch (error) {
                console.error('Failed to load modules:', error);
            } finally {
                setLoading(false);
            }
        };
        loadModules();
    }, [tenantId]);

    const handleToggle = async (moduleKey: string) => {
        const isCurrentlyEnabled = enabledModules.includes(moduleKey);
        setSaving(moduleKey);
        
        try {
            await updateTenantModule(tenantId, moduleKey, !isCurrentlyEnabled);
            if (isCurrentlyEnabled) {
                setEnabledModules(prev => prev.filter(k => k !== moduleKey));
            } else {
                setEnabledModules(prev => [...prev, moduleKey]);
            }
        } catch (error) {
            console.error('Failed to update module:', error);
            alert('Failed to update module');
        } finally {
            setSaving(null);
        }
    };

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[60] backdrop-blur-sm">
            <div className="bg-white rounded-[2rem] w-full max-w-2xl shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
                <div className="p-8 border-b border-gray-100 flex justify-between items-center bg-gray-50/50">
                    <div>
                        <h2 className="text-2xl font-bold text-gray-900">Module Access</h2>
                        <p className="text-sm text-gray-500">Managing modules for <span className="font-semibold text-blue-600">{tenantName}</span></p>
                    </div>
                    <button onClick={onClose} className="p-2 hover:bg-white rounded-full transition-colors shadow-sm border border-transparent hover:border-gray-100">
                        <X className="w-6 h-6 text-gray-400" />
                    </button>
                </div>

                <div className="p-8 overflow-y-auto space-y-4">
                    {loading ? (
                        <div className="py-20 text-center text-gray-500">Loading configurations...</div>
                    ) : (
                        AVAILABLE_MODULES.map((module) => {
                            const isEnabled = enabledModules.includes(module.key);
                            const isSaving = saving === module.key;

                            return (
                                <button
                                    key={module.key}
                                    disabled={isSaving}
                                    onClick={() => handleToggle(module.key)}
                                    className={`w-full flex items-center gap-4 p-5 rounded-2xl border-2 transition-all text-left group
                                        ${isEnabled 
                                            ? 'border-blue-100 bg-blue-50/30 ring-2 ring-blue-500/5' 
                                            : 'border-gray-100 hover:border-gray-200 bg-white'
                                        } ${isSaving ? 'opacity-50 cursor-not-allowed' : ''}`}
                                >
                                    <div className={`p-3 rounded-xl transition-colors ${isEnabled ? 'bg-blue-100 text-blue-600' : 'bg-gray-100 text-gray-400 group-hover:bg-gray-200'}`}>
                                        {isEnabled ? <CheckCircle2 className="w-6 h-6" /> : <Circle className="w-6 h-6" />}
                                    </div>
                                    <div className="flex-1">
                                        <h3 className={`font-bold text-lg transition-colors ${isEnabled ? 'text-blue-900' : 'text-gray-900'}`}>
                                            {module.label}
                                        </h3>
                                        <p className="text-sm text-gray-500">{module.description}</p>
                                    </div>
                                    <div className={`text-xs font-bold uppercase tracking-wider px-3 py-1 rounded-full ${isEnabled ? 'bg-blue-600 text-white' : 'bg-gray-200 text-gray-500'}`}>
                                        {isEnabled ? 'Enabled' : 'Disabled'}
                                    </div>
                                </button>
                            );
                        })
                    )}
                </div>

                <div className="p-8 border-t border-gray-100 bg-gray-50/50 flex justify-end">
                    <button
                        onClick={onClose}
                        className="px-8 py-3 bg-gray-900 text-white font-bold rounded-2xl hover:bg-black transition-all shadow-lg shadow-gray-200"
                    >
                        Done
                    </button>
                </div>
            </div>
        </div>
    );
}
