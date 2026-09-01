import React from 'react';
import { ArrowDownRight, ArrowUpRight, ArrowLeftRight, Minus } from 'lucide-react';

interface AttendanceBadgeProps {
    mode?: string | number | null;
    className?: string;
    showLabel?: boolean;
}

export default function AttendanceBadge({ mode, className = '', showLabel = true }: AttendanceBadgeProps) {
    const m = String(mode || 'None').toLowerCase();

    if (m === 'markin' || m === '1') {
        return (
            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-emerald-50 text-emerald-800 border border-emerald-200/80 shadow-xs ${className}`}>
                <ArrowDownRight className="w-3.5 h-3.5 text-emerald-600 stroke-[2.2]" />
                {showLabel && <span>Mark In</span>}
            </span>
        );
    }

    if (m === 'markout' || m === '2') {
        return (
            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-orange-50 text-orange-800 border border-orange-200/80 shadow-xs ${className}`}>
                <ArrowUpRight className="w-3.5 h-3.5 text-orange-600 stroke-[2.2]" />
                {showLabel && <span>Mark Out</span>}
            </span>
        );
    }

    if (m === 'bidirectional' || m === '3') {
        return (
            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium bg-purple-50 text-purple-800 border border-purple-200/80 shadow-xs ${className}`}>
                <ArrowLeftRight className="w-3.5 h-3.5 text-purple-600 stroke-[2.2]" />
                {showLabel && <span>Bidirectional</span>}
            </span>
        );
    }

    return (
        <span className={`inline-flex items-center gap-1 text-xs text-neutral-400 font-medium ${className}`}>
            <Minus className="w-3 h-3 text-neutral-300 stroke-[2]" />
            {showLabel && <span className="italic">None</span>}
        </span>
    );
}
