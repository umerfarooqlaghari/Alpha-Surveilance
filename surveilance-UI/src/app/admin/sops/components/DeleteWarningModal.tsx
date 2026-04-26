import { AlertTriangle, X } from 'lucide-react';

interface DeleteWarningModalProps {
    isOpen: boolean;
    onClose: () => void;
    onConfirm: () => void;
    title: string;
    itemName: string;
    type: 'SOP' | 'Violation';
}

export default function DeleteWarningModal({ isOpen, onClose, onConfirm, title, itemName, type }: DeleteWarningModalProps) {
    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-[60] flex items-center justify-center p-4">
            <div
                className="absolute inset-0 bg-black/60 backdrop-blur-md transition-opacity"
                onClick={onClose}
            />

            <div className="relative bg-white rounded-2xl shadow-2xl w-full max-w-md overflow-hidden transform transition-all text-black border border-red-100">
                <div className="p-6">
                    <div className="flex items-center justify-center w-12 h-12 rounded-full bg-red-100 text-red-600 mb-4 mx-auto">
                        <AlertTriangle className="w-6 h-6" />
                    </div>

                    <h2 className="text-xl font-bold text-center text-gray-900 mb-2">
                        {title}
                    </h2>

                    <div className="bg-red-50 p-4 rounded-xl border border-red-100 mb-6">
                        <p className="text-sm text-red-800 leading-relaxed font-medium">
                            Warning: Deleting the {type === 'SOP' ? 'SOP' : 'violation'} <span className="underline decoration-red-300 decoration-2 underline-offset-2">"{itemName}"</span> will have cascading effects:
                        </p>
                        <ul className="mt-3 space-y-2 text-sm text-red-700">
                            {type === 'SOP' ? (
                                <>
                                    <li className="flex items-start gap-2">
                                        <div className="mt-1.5 w-1.5 h-1.5 rounded-full bg-red-400 flex-shrink-0" />
                                        <span>All <strong>Violation Types</strong> under this SOP will be soft-deleted.</span>
                                    </li>
                                    <li className="flex items-start gap-2">
                                        <div className="mt-1.5 w-1.5 h-1.5 rounded-full bg-red-400 flex-shrink-0" />
                                        <span><strong>Tenant access</strong> to these violations will be revoked immediately.</span>
                                    </li>
                                </>
                            ) : (
                                <li className="flex items-start gap-2">
                                    <div className="mt-1.5 w-1.5 h-1.5 rounded-full bg-red-400 flex-shrink-0" />
                                    <span><strong>Tenant access</strong> to this specific violation will be revoked.</span>
                                </li>
                            )}
                            <li className="flex items-start gap-2">
                                <div className="mt-1.5 w-1.5 h-1.5 rounded-full bg-red-400 flex-shrink-0" />
                                <span>All <strong>Cameras</strong> using these violations will stop detecting them.</span>
                            </li>
                        </ul>
                        <p className="mt-4 text-xs font-semibold text-red-900 uppercase tracking-wider">
                            This action is a soft-delete and can be audited.
                        </p>
                    </div>

                    <div className="flex flex-col gap-2">
                        <button
                            onClick={onConfirm}
                            className="w-full py-3 px-4 bg-red-600 text-white font-semibold rounded-xl hover:bg-red-700 transition-all shadow-lg shadow-red-200 active:scale-[0.98]"
                        >
                            Yes, Delete and Disconnect
                        </button>
                        <button
                            onClick={onClose}
                            className="w-full py-3 px-4 bg-white text-gray-700 font-semibold rounded-xl border border-gray-200 hover:bg-gray-50 transition-all active:scale-[0.98]"
                        >
                            Cancel
                        </button>
                    </div>
                </div>

                <button
                    onClick={onClose}
                    className="absolute top-4 right-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-full transition-all"
                >
                    <X className="w-5 h-5" />
                </button>
            </div>
        </div>
    );
}
