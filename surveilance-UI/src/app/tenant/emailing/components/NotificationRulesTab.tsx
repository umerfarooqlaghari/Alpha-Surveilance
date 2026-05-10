'use client';

import { useState, useEffect } from 'react';
import { Plus, Trash2, Edit2, AlertCircle, Clock, MapPin, Video, ShieldAlert, Tag, Building2 } from 'lucide-react';
import { NotificationRule, NotificationRuleRequest, getNotificationRules, createNotificationRule, updateNotificationRule, deleteNotificationRule } from '@/lib/api/tenant/notificationRules';
import { getCameras } from '@/lib/api/tenant/cameras';
import { getSopViolationTypes, SopViolationType } from '@/lib/api/tenant/sops';
import { getEmployees } from '@/lib/api/tenant/employees';
import { getLocations } from '@/lib/api/tenant/locations';

export default function NotificationRulesTab() {
    const [rules, setRules] = useState<NotificationRule[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const [cameras, setCameras] = useState<{ id: string; name: string }[]>([]);
    const [violationTypes, setViolationTypes] = useState<SopViolationType[]>([]);
    const [employees, setEmployees] = useState<{ id: string; name: string; email: string; department?: string }[]>([]);
    const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
    const [departments, setDepartments] = useState<string[]>([]);

    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingId, setEditingId] = useState<string | null>(null);

    const [employeeSearch, setEmployeeSearch] = useState('');
    const [showEmployeeDropdown, setShowEmployeeDropdown] = useState(false);

    const [formData, setFormData] = useState<NotificationRuleRequest>({
        name: '',
        targetEmails: [],
        filterLocationIds: [],
        filterCameraIds: [],
        filterViolationTypeIds: [],
        filterSeverities: [],
        filterDepartments: [],
        timeIntervals: [],
        isActive: true
    });
    
    const [emailInput, setEmailInput] = useState('');

    useEffect(() => {
        loadData();
    }, []);

    const loadData = async () => {
        try {
            setLoading(true);
            const [rulesData, camerasData, typesData, employeesData, locsData] = await Promise.all([
                getNotificationRules(),
                getCameras(),
                getSopViolationTypes(),
                getEmployees({ pageSize: 1000 }),
                getLocations()
            ]);
            setRules(rulesData);
            setCameras(camerasData.map((c: any) => ({ id: c.id, name: c.name })));
            setViolationTypes(typesData);
            
            const emps = employeesData.map((e: any) => ({ id: e.id, name: `${e.firstName} ${e.lastName}`, email: e.email, department: e.department }));
            setEmployees(emps);
            
            const depts = Array.from(new Set(emps.map(e => e.department).filter(Boolean))) as string[];
            setDepartments(depts);
            
            setLocations(locsData.map((l: any) => ({ id: l.id, name: l.name })));
        } catch (e: any) {
            setError(e.message || 'Failed to load data');
        } finally {
            setLoading(false);
        }
    };

    const handleOpenModal = (rule?: NotificationRule) => {
        if (rule) {
            setEditingId(rule.id);
            setFormData({
                name: rule.name,
                targetEmails: rule.targetEmails || [],
                filterLocationIds: rule.filterLocationIds || [],
                filterCameraIds: rule.filterCameraIds || [],
                filterViolationTypeIds: rule.filterViolationTypeIds || [],
                filterSeverities: rule.filterSeverities || [],
                filterDepartments: rule.filterDepartments || [],
                timeIntervals: rule.timeIntervals || [],
                isActive: rule.isActive
            });
        } else {
            setEditingId(null);
            setFormData({
                name: '',
                targetEmails: [],
                filterLocationIds: [],
                filterCameraIds: [],
                filterViolationTypeIds: [],
                filterSeverities: [],
                filterDepartments: [],
                timeIntervals: [],
                isActive: true
            });
        }
        setIsModalOpen(true);
    };

    const handleSave = async () => {
        try {
            if (!formData.name.trim() || formData.targetEmails.length === 0) {
                alert('Name and at least one email are required.');
                return;
            }

            // Ensure intervals have HH:mm:ss format
            const payload = {
                ...formData,
                timeIntervals: formData.timeIntervals.map(t => ({
                    start: t.start.length === 5 ? `${t.start}:00` : t.start,
                    end: t.end.length === 5 ? `${t.end}:00` : t.end,
                }))
            };

            if (editingId) {
                await updateNotificationRule(editingId, payload);
            } else {
                await createNotificationRule(payload);
            }
            
            setIsModalOpen(false);
            loadData();
        } catch (e: any) {
            alert(e.message || 'Failed to save rule');
        }
    };

    const handleDelete = async (id: string) => {
        if (confirm('Are you sure you want to delete this rule?')) {
            try {
                await deleteNotificationRule(id);
                loadData();
            } catch (e: any) {
                alert(e.message || 'Failed to delete rule');
            }
        }
    };

    const toggleActive = async (rule: NotificationRule) => {
        try {
            await updateNotificationRule(rule.id, {
                ...rule,
                isActive: !rule.isActive
            });
            loadData();
        } catch (e: any) {
            alert(e.message || 'Failed to update rule');
        }
    };

    // Helper for multi-select arrays
    const addToArray = (field: keyof NotificationRuleRequest, value: any) => {
        if (!value) return;
        const current = formData[field] as any[];
        if (!current.includes(value)) {
            setFormData({ ...formData, [field]: [...current, value] });
        }
    };

    const removeFromArray = (field: keyof NotificationRuleRequest, value: any) => {
        const current = formData[field] as any[];
        setFormData({ ...formData, [field]: current.filter(item => item !== value) });
    };

    if (loading) return <div className="p-8 text-center text-gray-500 animate-pulse">Loading notification rules...</div>;
    if (error) return <div className="p-8 text-center text-red-500 bg-red-50 rounded-xl">{error}</div>;

    return (
        <div className="space-y-6">
            <div className="flex justify-between items-center bg-white p-6 rounded-2xl shadow-sm border border-gray-100">
                <div>
                    <h2 className="text-lg font-bold text-gray-900">Notification Rules</h2>
                    <p className="text-sm text-gray-500 mt-1">Configure conditional alerts for specific scenarios.</p>
                </div>
                <button
                    onClick={() => handleOpenModal()}
                    className="flex items-center gap-2 px-4 py-2.5 bg-black text-white text-sm font-semibold rounded-xl hover:bg-gray-800 transition-colors shadow-sm"
                >
                    <Plus className="w-4 h-4" />
                    Create Rule
                </button>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {rules.map(rule => (
                    <div key={rule.id} className="bg-white rounded-2xl border border-gray-100 shadow-sm overflow-hidden flex flex-col">
                        <div className="p-5 border-b border-gray-50 flex justify-between items-start">
                            <div>
                                <h3 className="font-bold text-gray-900">{rule.name}</h3>
                                <p className="text-xs text-gray-500 mt-1 flex items-center gap-1">
                                    <Tag className="w-3 h-3" />
                                    {rule.targetEmails?.length || 0} recipient(s)
                                </p>
                            </div>
                            <label className="relative inline-flex items-center cursor-pointer">
                                <input
                                    type="checkbox"
                                    className="sr-only peer"
                                    checked={rule.isActive}
                                    onChange={() => toggleActive(rule)}
                                />
                                <div className="w-9 h-5 bg-gray-200 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-green-500"></div>
                            </label>
                        </div>

                        <div className="p-5 flex-1 space-y-3 bg-gray-50/50">
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <MapPin className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Location:</span>
                                {rule.filterLocationIds?.length > 0 ? `${rule.filterLocationIds.length} selected` : 'Any'}
                            </div>
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <Video className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Camera:</span>
                                {rule.filterCameraIds?.length > 0 ? `${rule.filterCameraIds.length} selected` : 'Any'}
                            </div>
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <ShieldAlert className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Violation:</span>
                                {rule.filterViolationTypeIds?.length > 0 ? `${rule.filterViolationTypeIds.length} selected` : 'Any'}
                            </div>
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <AlertCircle className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Severity:</span>
                                {rule.filterSeverities?.length > 0 ? rule.filterSeverities.join(', ') : 'Any'}
                            </div>
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <Building2 className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Department:</span>
                                {rule.filterDepartments?.length > 0 ? rule.filterDepartments.join(', ') : 'Any'}
                            </div>
                            <div className="flex items-center gap-2 text-sm text-gray-600">
                                <Clock className="w-4 h-4 text-gray-400" />
                                <span className="font-medium text-gray-900 mr-2">Timing:</span>
                                {rule.timeIntervals?.length > 0 ? `${rule.timeIntervals.length} interval(s)` : 'Any Time'}
                            </div>
                        </div>

                        <div className="p-4 border-t border-gray-100 flex justify-end gap-2 bg-white">
                            <button
                                onClick={() => handleOpenModal(rule)}
                                className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"
                            >
                                <Edit2 className="w-4 h-4" />
                            </button>
                            <button
                                onClick={() => handleDelete(rule.id)}
                                className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                            >
                                <Trash2 className="w-4 h-4" />
                            </button>
                        </div>
                    </div>
                ))}

                {rules.length === 0 && (
                    <div className="col-span-full py-12 text-center text-gray-500 border-2 border-dashed border-gray-200 rounded-2xl bg-gray-50">
                        <AlertCircle className="w-8 h-8 mx-auto text-gray-400 mb-3" />
                        <p>No notification rules configured.</p>
                        <p className="text-sm text-gray-400 mt-1">Create a rule to selectively send alerts based on conditions.</p>
                    </div>
                )}
            </div>

            {isModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm">
                    <div className="bg-white rounded-2xl w-full max-w-4xl max-h-[90vh] overflow-y-auto shadow-2xl">
                        <div className="p-6 border-b border-gray-100">
                            <h2 className="text-xl font-bold text-gray-900">
                                {editingId ? 'Edit Rule' : 'Create Rule'}
                            </h2>
                        </div>

                        <div className="p-6 space-y-6">
                            {/* General */}
                            <div>
                                <label className="block text-sm font-semibold text-gray-700 mb-2">Rule Name *</label>
                                <input
                                    type="text"
                                    value={formData.name}
                                    onChange={e => setFormData({ ...formData, name: e.target.value })}
                                    placeholder="e.g. Night Shift Alerts"
                                    className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-black focus:border-black outline-none text-black"
                                />
                            </div>

                            {/* Recipients */}
                            <div>
                                <label className="block text-sm font-semibold text-gray-700 mb-2">Recipients *</label>
                                
                                <div className="mb-2 relative">
                                    <input
                                        type="text"
                                        placeholder="Search Employee Directory..."
                                        value={employeeSearch}
                                        onChange={(e) => {
                                            setEmployeeSearch(e.target.value);
                                            setShowEmployeeDropdown(true);
                                        }}
                                        onFocus={() => setShowEmployeeDropdown(true)}
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none text-sm text-black"
                                    />
                                    {showEmployeeDropdown && employeeSearch && (
                                        <div className="absolute z-10 w-full mt-1 bg-white border border-gray-200 rounded-xl shadow-lg max-h-48 overflow-y-auto">
                                            {employees
                                                .filter(emp => emp.name.toLowerCase().includes(employeeSearch.toLowerCase()) || emp.email.toLowerCase().includes(employeeSearch.toLowerCase()))
                                                .map(emp => (
                                                    <div 
                                                        key={emp.id} 
                                                        className="px-4 py-2 hover:bg-gray-50 cursor-pointer text-sm text-black"
                                                        onClick={() => {
                                                            addToArray('targetEmails', emp.email);
                                                            setEmployeeSearch('');
                                                            setShowEmployeeDropdown(false);
                                                        }}
                                                    >
                                                        <div className="font-medium">{emp.name}</div>
                                                        <div className="text-gray-500 text-xs">{emp.email} {emp.department ? `- ${emp.department}` : ''}</div>
                                                    </div>
                                                ))}
                                            {employees.filter(emp => emp.name.toLowerCase().includes(employeeSearch.toLowerCase()) || emp.email.toLowerCase().includes(employeeSearch.toLowerCase())).length === 0 && (
                                                <div className="px-4 py-2 text-sm text-gray-500">No employees found</div>
                                            )}
                                        </div>
                                    )}
                                </div>

                                <div className="w-full min-h-[46px] p-2 bg-gray-50 border border-gray-200 rounded-xl focus-within:ring-2 focus-within:ring-black flex flex-wrap gap-2 text-black">
                                    {formData.targetEmails.map(email => (
                                        <span key={email} className="inline-flex items-center gap-1 px-3 py-1 bg-black text-white text-sm rounded-lg">
                                            {email}
                                            <button onClick={() => removeFromArray('targetEmails', email)} className="hover:text-red-300 ml-1">&times;</button>
                                        </span>
                                    ))}
                                    <input
                                        type="email"
                                        value={emailInput}
                                        onChange={e => setEmailInput(e.target.value)}
                                        onKeyDown={(e) => {
                                            if (e.key === 'Enter' || e.key === ',') {
                                                e.preventDefault();
                                                const val = emailInput.trim();
                                                if (val && /^\S+@\S+\.\S+$/.test(val)) {
                                                    addToArray('targetEmails', val);
                                                    setEmailInput('');
                                                }
                                            }
                                        }}
                                        placeholder="Add custom email..."
                                        className="flex-1 bg-transparent border-none outline-none min-w-[150px] px-2"
                                    />
                                </div>
                            </div>

                            <hr className="border-gray-100" />
                            <h3 className="text-sm font-bold text-gray-900 uppercase tracking-wider text-black">Conditions (Leave empty for 'Any')</h3>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6 text-gray-800">
                                
                                {/* Location */}
                                <div>
                                    <label className="block text-sm font-semibold text-gray-700 mb-2">Locations</label>
                                    <select
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none mb-2"
                                        onChange={(e) => { addToArray('filterLocationIds', e.target.value); e.target.value = ''; }}
                                    >
                                        <option value="">+ Add Location...</option>
                                        {locations.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
                                    </select>
                                    <div className="flex flex-wrap gap-2">
                                        {formData.filterLocationIds.map(id => (
                                            <span key={id} className="inline-flex items-center gap-1 px-3 py-1 bg-gray-100 text-gray-800 text-xs rounded-lg border border-gray-200">
                                                {locations.find(l => l.id === id)?.name || id}
                                                <button onClick={() => removeFromArray('filterLocationIds', id)} className="hover:text-red-500 ml-1">&times;</button>
                                            </span>
                                        ))}
                                    </div>
                                </div>

                                {/* Camera */}
                                <div>
                                    <label className="block text-sm font-semibold text-gray-700 mb-2">Cameras</label>
                                    <select
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none mb-2"
                                        onChange={(e) => { addToArray('filterCameraIds', e.target.value); e.target.value = ''; }}
                                    >
                                        <option value="">+ Add Camera...</option>
                                        {cameras.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                                    </select>
                                    <div className="flex flex-wrap gap-2">
                                        {formData.filterCameraIds.map(id => (
                                            <span key={id} className="inline-flex items-center gap-1 px-3 py-1 bg-gray-100 text-gray-800 text-xs rounded-lg border border-gray-200">
                                                {cameras.find(c => c.id === id)?.name || id}
                                                <button onClick={() => removeFromArray('filterCameraIds', id)} className="hover:text-red-500 ml-1">&times;</button>
                                            </span>
                                        ))}
                                    </div>
                                </div>

                                {/* Violation Types */}
                                <div>
                                    <label className="block text-sm font-semibold text-gray-700 mb-2">Violation Types</label>
                                    <select
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none mb-2"
                                        onChange={(e) => { addToArray('filterViolationTypeIds', e.target.value); e.target.value = ''; }}
                                    >
                                        <option value="">+ Add Violation Type...</option>
                                        {violationTypes.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
                                    </select>
                                    <div className="flex flex-wrap gap-2">
                                        {formData.filterViolationTypeIds.map(id => (
                                            <span key={id} className="inline-flex items-center gap-1 px-3 py-1 bg-gray-100 text-gray-800 text-xs rounded-lg border border-gray-200">
                                                {violationTypes.find(v => v.id === id)?.name || id}
                                                <button onClick={() => removeFromArray('filterViolationTypeIds', id)} className="hover:text-red-500 ml-1">&times;</button>
                                            </span>
                                        ))}
                                    </div>
                                </div>

                                {/* Severities */}
                                <div>
                                    <label className="block text-sm font-semibold text-gray-700 mb-2">Severities</label>
                                    <select
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none mb-2"
                                        onChange={(e) => { addToArray('filterSeverities', e.target.value); e.target.value = ''; }}
                                    >
                                        <option value="">+ Add Severity...</option>
                                        {['Critical', 'High', 'Medium', 'Low'].map(s => <option key={s} value={s}>{s}</option>)}
                                    </select>
                                    <div className="flex flex-wrap gap-2">
                                        {formData.filterSeverities.map(s => (
                                            <span key={s} className="inline-flex items-center gap-1 px-3 py-1 bg-gray-100 text-gray-800 text-xs rounded-lg border border-gray-200">
                                                {s}
                                                <button onClick={() => removeFromArray('filterSeverities', s)} className="hover:text-red-500 ml-1">&times;</button>
                                            </span>
                                        ))}
                                    </div>
                                </div>

                                {/* Departments */}
                                <div>
                                    <label className="block text-sm font-semibold text-gray-700 mb-2">Violator Departments</label>
                                    <select
                                        className="w-full px-4 py-2.5 bg-gray-50 border border-gray-200 rounded-xl outline-none mb-2"
                                        onChange={(e) => { addToArray('filterDepartments', e.target.value); e.target.value = ''; }}
                                    >
                                        <option value="">+ Add Department...</option>
                                        {departments.map(d => <option key={d} value={d}>{d}</option>)}
                                    </select>
                                    <div className="flex flex-wrap gap-2">
                                        {formData.filterDepartments.map(d => (
                                            <span key={d} className="inline-flex items-center gap-1 px-3 py-1 bg-gray-100 text-gray-800 text-xs rounded-lg border border-gray-200">
                                                {d}
                                                <button onClick={() => removeFromArray('filterDepartments', d)} className="hover:text-red-500 ml-1">&times;</button>
                                            </span>
                                        ))}
                                    </div>
                                </div>

                                {/* Time Intervals */}
                                <div className="md:col-span-2">
                                    <div className="flex justify-between items-center mb-2">
                                        <label className="block text-sm font-semibold text-gray-700">Time Intervals</label>
                                        <button 
                                            onClick={() => setFormData({...formData, timeIntervals: [...formData.timeIntervals, { start: '08:00', end: '17:00' }]})}
                                            className="text-xs text-blue-600 hover:text-blue-800 font-bold"
                                        >
                                            + Add Interval
                                        </button>
                                    </div>
                                    <div className="space-y-2">
                                        {formData.timeIntervals.length === 0 ? (
                                            <p className="text-xs text-gray-400 italic">Any time (24/7)</p>
                                        ) : (
                                            formData.timeIntervals.map((interval, idx) => (
                                                <div key={idx} className="flex items-center gap-2">
                                                    <input 
                                                        type="time" 
                                                        value={interval.start.substring(0,5)}
                                                        onChange={(e) => {
                                                            const newIntervals = [...formData.timeIntervals];
                                                            newIntervals[idx].start = e.target.value;
                                                            setFormData({...formData, timeIntervals: newIntervals});
                                                        }}
                                                        className="px-3 py-2 bg-gray-50 border border-gray-200 rounded-lg outline-none flex-1"
                                                    />
                                                    <span className="text-gray-400">to</span>
                                                    <input 
                                                        type="time" 
                                                        value={interval.end.substring(0,5)}
                                                        onChange={(e) => {
                                                            const newIntervals = [...formData.timeIntervals];
                                                            newIntervals[idx].end = e.target.value;
                                                            setFormData({...formData, timeIntervals: newIntervals});
                                                        }}
                                                        className="px-3 py-2 bg-gray-50 border border-gray-200 rounded-lg outline-none flex-1"
                                                    />
                                                    <button 
                                                        onClick={() => {
                                                            const newIntervals = formData.timeIntervals.filter((_, i) => i !== idx);
                                                            setFormData({...formData, timeIntervals: newIntervals});
                                                        }}
                                                        className="p-2 text-gray-400 hover:text-red-500 rounded-lg"
                                                    >
                                                        <Trash2 className="w-4 h-4" />
                                                    </button>
                                                </div>
                                            ))
                                        )}
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div className="p-6 border-t border-gray-100 flex justify-end gap-3 bg-gray-50 rounded-b-2xl">
                            <button
                                onClick={() => setIsModalOpen(false)}
                                className="px-5 py-2.5 text-sm font-semibold text-gray-600 hover:text-gray-900 transition-colors"
                            >
                                Cancel
                            </button>
                            <button
                                onClick={handleSave}
                                className="px-6 py-2.5 bg-black text-white text-sm font-bold rounded-xl hover:bg-gray-800 transition-colors shadow-sm"
                            >
                                Save Rule
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
