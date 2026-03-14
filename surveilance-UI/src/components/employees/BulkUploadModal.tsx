'use client';

import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Loader2, Upload, AlertCircle, CheckCircle, FileText, X } from 'lucide-react';
import { bulkImportEmployees, downloadTemplate } from '@/lib/api/tenant/employees';
import { BulkImportResponse } from '@/types/employee';

interface BulkUploadModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

export default function BulkUploadModal({ isOpen, onClose, onSuccess }: BulkUploadModalProps) {
    const [file, setFile] = useState<File | null>(null);
    const [uploading, setUploading] = useState(false);
    const [result, setResult] = useState<BulkImportResponse | null>(null);
    const [error, setError] = useState<string | null>(null);

    if (!isOpen) return null;

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files[0]) {
            setFile(e.target.files[0]);
            setError(null);
            setResult(null);
        }
    };

    const handleUpload = async () => {
        if (!file) return;

        setUploading(true);
        setError(null);

        try {
            const response = await bulkImportEmployees(file);
            // In typical axios, response.data holds the body. If the response IS the body, this needs to be tweaked.
            const data: BulkImportResponse = (response as any).data ? (response as any).data : response;
            setResult(data);
            if (data && data.successCount > 0) {
                onSuccess(); // Refresh list background
            }
        } catch (err: any) {
            console.error(err);
            setError(err.message || 'Failed to upload CSV');
        } finally {
            setUploading(false);
        }
    };

    const handleClose = () => {
        setFile(null);
        setResult(null);
        setError(null);
        onClose();
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm transition-all duration-300 p-4">
            <div className="bg-white/95 backdrop-blur-md rounded-2xl shadow-2xl w-full max-w-lg p-6 relative border border-white/20 ring-1 ring-black/5 flex flex-col max-h-[90vh]">
                <button
                    onClick={handleClose}
                    className="absolute top-4 right-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100/50 rounded-full transition-colors"
                >
                    <X className="w-5 h-5" />
                </button>

                <h2 className="text-xl font-bold mb-5 bg-gradient-to-r from-gray-900 to-gray-600 bg-clip-text text-transparent">
                    Bulk Import Employees
                </h2>

                <div className="flex-1 overflow-y-auto pr-1">
                    {!result ? (
                        <div className="space-y-6">
                            <div className="p-4 bg-blue-50/50 border border-blue-100 rounded-xl">
                                <div className="flex justify-between items-start mb-2">
                                    <h3 className="font-semibold text-blue-900 flex items-center gap-2 text-sm">
                                        <FileText className="w-4 h-4" /> Instructions
                                    </h3>
                                    <button
                                        onClick={downloadTemplate}
                                        className="text-white bg-blue-600 hover:bg-blue-700 px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-1 transition-colors shadow-sm"
                                    >
                                        <FileText className="w-3 h-3" /> Template
                                    </button>
                                </div>
                                <ul className="list-disc list-inside text-xs text-blue-800 space-y-1 ml-1 opacity-80">
                                    <li>Use the CSV template.</li>
                                    <li><strong className="font-semibold">Email</strong> is required & unique.</li>
                                    <li>Extra columns = metadata.</li>
                                </ul>
                            </div>

                            <div className="relative group">
                                <div className="absolute -inset-0.5 bg-gradient-to-r from-blue-500 to-indigo-500 rounded-xl opacity-20 group-hover:opacity-40 transition duration-300 blur"></div>
                                <div className="relative bg-white border-2 border-dashed border-gray-300 group-hover:border-blue-400 rounded-xl p-6 text-center transition-all">
                                    <input
                                        type="file"
                                        accept=".csv"
                                        onChange={handleFileChange}
                                        className="hidden"
                                        id="csv-upload"
                                    />
                                    <label htmlFor="csv-upload" className="cursor-pointer flex flex-col items-center">
                                        <div className="w-12 h-12 bg-blue-50 text-blue-600 rounded-full flex items-center justify-center mb-3 group-hover:scale-110 transition-transform duration-300">
                                            <Upload className="w-6 h-6" />
                                        </div>
                                        <span className="text-base font-semibold text-gray-900">Click to upload CSV</span>
                                        <span className="text-xs text-gray-500 mt-1">or drag and drop here</span>
                                    </label>
                                    {file && (
                                        <div className="mt-4 inline-flex items-center gap-2 text-xs font-medium text-blue-700 bg-blue-50 py-1.5 px-3 rounded-full border border-blue-100 animate-in fade-in slide-in-from-bottom-2">
                                            <FileText className="w-3 h-3" />
                                            {file.name}
                                        </div>
                                    )}
                                </div>
                            </div>

                            {error && (
                                <div className="p-3 bg-red-50 text-red-700 rounded-xl border border-red-100 flex items-start gap-3 text-xs animate-in fade-in slide-in-from-top-2">
                                    <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                                    <div>
                                        <p className="font-semibold">Upload Failed</p>
                                        <p>{error}</p>
                                    </div>
                                </div>
                            )}

                            <div className="flex justify-end gap-3 pt-2">
                                <button
                                    onClick={handleClose}
                                    className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-xl transition-colors"
                                >
                                    Cancel
                                </button>
                                <button
                                    onClick={handleUpload}
                                    disabled={!file || uploading}
                                    className="px-4 py-2 text-sm font-medium bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl hover:from-blue-700 hover:to-indigo-700 shadow-lg shadow-blue-500/30 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 transition-all hover:scale-[1.02] active:scale-[0.98]"
                                >
                                    {uploading && <Loader2 className="w-3 h-3 animate-spin" />}
                                    Upload
                                </button>
                            </div>
                        </div>
                    ) : (
                        <div className="space-y-6 animate-in fade-in zoom-in-95 duration-300">
                            <div className="flex flex-col items-center text-center p-5 bg-green-50/50 text-green-800 rounded-2xl border border-green-100">
                                <div className="w-12 h-12 bg-green-100 rounded-full flex items-center justify-center mb-3">
                                    <CheckCircle className="w-6 h-6 text-green-600" />
                                </div>
                                <h3 className="text-lg font-bold text-gray-900">Processing Complete</h3>
                                <p className="text-gray-500 text-sm mt-1">Found {result.totalProcessed} records.</p>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                                <div className="p-4 bg-white border border-gray-100 rounded-2xl shadow-sm text-center">
                                    <p className="text-2xl font-bold text-green-600 mb-0.5">{result.successCount}</p>
                                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Successful</p>
                                </div>
                                <div className="p-4 bg-white border border-gray-100 rounded-2xl shadow-sm text-center">
                                    <p className="text-2xl font-bold text-red-600 mb-0.5">{result.failureCount}</p>
                                    <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Failed</p>
                                </div>
                            </div>

                            {result.failures.length > 0 && (
                                <div className="border border-gray-200 rounded-xl overflow-hidden shadow-sm">
                                    <div className="bg-gray-50 px-4 py-2 border-b border-gray-200 font-semibold text-xs text-gray-700 flex justify-between items-center">
                                        <span>Error Details</span>
                                        <span className="font-normal text-gray-500">{result.failures.length} errors</span>
                                    </div>
                                    <div className="max-h-40 overflow-y-auto">
                                        <table className="w-full text-xs text-left">
                                            <thead className="bg-gray-50/50 sticky top-0">
                                                <tr>
                                                    <th className="px-4 py-2 font-medium text-gray-500">Row</th>
                                                    <th className="px-4 py-2 font-medium text-gray-500">Email</th>
                                                    <th className="px-4 py-2 font-medium text-gray-500">Reason</th>
                                                </tr>
                                            </thead>
                                            <tbody className="divide-y divide-gray-100 bg-white">
                                                {result.failures.map((fail, i) => (
                                                    <tr key={i} className="hover:bg-red-50/30 transition-colors">
                                                        <td className="px-4 py-2 text-gray-500 font-mono">{fail.rowIndex}</td>
                                                        <td className="px-4 py-2 text-gray-900">{fail.email || '-'}</td>
                                                        <td className="px-4 py-2 text-red-600 break-words">{fail.reason}</td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>
                                </div>
                            )}

                            <div className="flex justify-end pt-2">
                                <button
                                    onClick={handleClose}
                                    className="px-5 py-2 bg-gray-900 text-white text-sm font-medium rounded-xl hover:bg-gray-800 shadow-lg shadow-gray-200 transition-all hover:scale-[1.02] active:scale-[0.98]"
                                >
                                    Close
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
