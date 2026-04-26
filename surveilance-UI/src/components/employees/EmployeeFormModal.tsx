'use client';

import { useEffect } from 'react';
import { useForm, useFieldArray } from 'react-hook-form';
import { Loader2, X, Plus, Trash2 } from 'lucide-react';
import { Employee, EmployeeRequest } from '@/types/employee';
import { createEmployee, updateEmployee } from '@/lib/api/tenant/employees';

interface EmployeeFormModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
    employee?: Employee | null;
}

export default function EmployeeFormModal({ isOpen, onClose, onSuccess, employee }: EmployeeFormModalProps) {
    const { register, control, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<EmployeeRequest & { metadataList: { key: string; value: string }[] }>({
        defaultValues: {
            firstName: '',
            lastName: '',
            email: '',
            employeeId: '',
            metadataList: []
        }
    });

    const { fields, append, remove } = useFieldArray({
        control,
        name: "metadataList"
    });

    useEffect(() => {
        if (isOpen) {
            if (employee) {
                const metadataList = employee.metadata
                    ? Object.entries(employee.metadata).map(([key, value]) => ({ key, value: String(value) }))
                    : [];

                reset({
                    firstName: employee.firstName,
                    lastName: employee.lastName,
                    email: employee.email,
                    employeeId: employee.employeeId,
                    number: employee.number || '',
                    companyName: employee.companyName || '',
                    designation: employee.designation || '',
                    department: employee.department || '',
                    tenure: employee.tenure || '',
                    grade: employee.grade || '',
                    gender: employee.gender || '',
                    managerId: employee.managerId || '',
                    metadataList
                });
            } else {
                reset({
                    firstName: '',
                    lastName: '',
                    email: '',
                    employeeId: '',
                    metadataList: []
                });
            }
        }
    }, [isOpen, employee, reset]);

    if (!isOpen) return null;

    const onSubmit = async (data: any) => {
        try {
            const metadata: Record<string, any> = {};
            data.metadataList?.forEach((item: any) => {
                if (item.key) metadata[item.key] = item.value;
            });

            const payload: EmployeeRequest = {
                firstName: data.firstName,
                lastName: data.lastName,
                email: data.email,
                employeeId: data.employeeId,
                number: data.number,
                companyName: data.companyName,
                designation: data.designation,
                department: data.department,
                tenure: data.tenure,
                grade: data.grade,
                gender: data.gender,
                managerId: data.managerId,
                metadata
            };

            if (employee) {
                await updateEmployee(employee.id, payload);
            } else {
                await createEmployee(payload);
            }
            onSuccess();
            onClose();
        } catch (error) {
            console.error(error);
            alert('Failed to save employee. Check console for details.');
        }
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm transition-all duration-300 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-3xl p-8 relative max-h-[90vh] overflow-y-auto border border-white/20 ring-1 ring-black/5">
                <button
                    onClick={onClose}
                    className="absolute top-4 right-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100/50 rounded-full transition-colors"
                >
                    <X className="w-5 h-5" />
                </button>

                <h2 className="text-2xl font-bold mb-8 bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">
                    {employee ? 'Edit Employee' : 'Add New Employee'}
                </h2>

                <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-gray-700 ml-1">First Name <span className="text-red-500">*</span></label>
                            <input
                                {...register('firstName', { required: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="John"
                            />
                            {errors.firstName && <span className="text-red-500 text-xs ml-1">Required</span>}
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-gray-700 ml-1">Last Name <span className="text-red-500">*</span></label>
                            <input
                                {...register('lastName', { required: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="Doe"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-gray-700 ml-1">Email <span className="text-red-500">*</span></label>
                            <input
                                type="email"
                                {...register('email', { required: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="john.doe@company.com"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-gray-700 ml-1">Employee ID <span className="text-red-500">*</span></label>
                            <input
                                {...register('employeeId', { required: true })}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="EMP-001"
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Department</label>
                            <input
                                {...register('department')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="Engineering"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Designation</label>
                            <input
                                {...register('designation')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="Senior Engineer"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Company</label>
                            <input
                                {...register('companyName')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                                placeholder="ACME Corp"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Phone</label>
                            <input
                                {...register('number')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Grade</label>
                            <input
                                {...register('grade')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                            />
                        </div>
                        <div className="space-y-2">
                            <label className="text-sm font-medium text-gray-600 ml-1">Tenure</label>
                            <input
                                {...register('tenure')}
                                className="w-full px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-black"
                            />
                        </div>
                    </div>

                    <div className="border-t border-gray-100 pt-6">
                        <div className="flex justify-between items-center mb-4">
                            <h3 className="text-base font-semibold text-gray-900">Additional Attributes</h3>
                            <button
                                type="button"
                                onClick={() => append({ key: '', value: '' })}
                                className="group flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-blue-600 bg-blue-50 hover:bg-blue-100 rounded-lg transition-colors text-black"
                            >
                                <Plus className="w-4 h-4 transition-transform group-hover:rotate-90" /> Add Field
                            </button>
                        </div>

                        <div className="space-y-3">
                            {fields.map((field, index) => (
                                <div key={field.id} className="flex gap-3 group">
                                    <input
                                        {...register(`metadataList.${index}.key`)}
                                        placeholder="Key (e.g. Skills)"
                                        className="flex-1 px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-sm text-black"
                                    />
                                    <input
                                        {...register(`metadataList.${index}.value`)}
                                        placeholder="Value (e.g. React, Node.js)"
                                        className="flex-[2] px-4 py-2.5 bg-gray-50/50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all outline-none text-sm text-black"
                                    />
                                    <button
                                        type="button"
                                        onClick={() => remove(index)}
                                        className="p-2.5 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded-xl transition-all opacity-0 group-hover:opacity-100 text-black"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </button>
                                </div>
                            ))}
                            {fields.length === 0 && (
                                <div className="text-center py-6 border-2 border-dashed border-gray-200 rounded-xl">
                                    <p className="text-sm text-gray-500">No additional attributes added.</p>
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="flex justify-end gap-3 pt-6 border-t border-gray-100">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-6 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-xl transition-colors"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={isSubmitting}
                            className="px-6 py-2.5 text-sm font-medium bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 shadow-lg shadow-blue-500/30 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 transition-all hover:scale-[1.02] active:scale-[0.98]"
                        >
                            {isSubmitting && <Loader2 className="w-4 h-4 animate-spin" />} Save Employee
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
