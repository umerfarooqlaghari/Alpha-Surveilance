"use client";

import { useViolationHub } from "../hooks/useViolationHub";
import { useEffect, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { getAuthHeaders } from "@/lib/utils/auth";

export const LiveFeed = () => {
    const { tenant } = useAuth();
    const tenantId = tenant?.id || "";
    const { notifications } = useViolationHub();
    const [history, setHistory] = useState<any[]>([]);

    useEffect(() => {
        const fetchHistory = async () => {
            if (!tenantId) return;
            try {
                const res = await fetch('/api/dashboard/violations/recent', {
                    headers: {
                        ...getAuthHeaders(),
                        'X-Tenant-Id': tenantId
                    }
                });
                if (res.ok) {
                    const data = await res.json();
                    setHistory(data);
                }
            } catch (err) {
                console.error("Failed to fetch history", err);
            }
        };
        fetchHistory();
    }, [tenantId]);

    // Merge and Deduplicate (by ID) just in case
    const allNotifications = [...notifications, ...history].filter((v, i, a) => a.findIndex(t => t.id === v.id) === i);
    // Sort by timestamp desc (newest first)
    allNotifications.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

    return (
        <div className="w-full max-w-4xl p-6 bg-white dark:bg-zinc-900 rounded-lg shadow-lg">
            <h2 className="text-2xl font-bold mb-4 text-zinc-800 dark:text-zinc-100 flex items-center gap-2">
                <span className="relative flex h-3 w-3">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-400 opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-red-500"></span>
                </span>
                Live Violation Feed
            </h2>

            <div className="space-y-4">
                {allNotifications.length === 0 && (
                    <p className="text-zinc-500 italic">Waiting for violations...</p>
                )}

                {allNotifications.map((n, idx) => (
                    <div
                        key={`${n.id}-${idx}`}
                        className="flex items-start gap-4 p-4 border rounded-md border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-950 animate-in slide-in-from-top-2 fade-in"
                    >
                        <div className={`w-2 h-full rounded-full ${n.severity === 'Critical' ? 'bg-red-600' : 'bg-yellow-500'}`} />

                        <div className="flex-1">
                            <div className="flex justify-between items-start">
                                <h3 className="font-semibold text-lg text-zinc-900 dark:text-zinc-100">
                                    {n.type}
                                </h3>
                                <span className="text-xs text-zinc-500">{new Date(n.timestamp).toLocaleTimeString()}</span>
                            </div>
                            <p className="text-sm text-zinc-600 dark:text-zinc-400 mt-1">
                                Camera: <span className="font-mono bg-zinc-200 dark:bg-zinc-800 px-1 rounded">{n.cameraId}</span>
                            </p>
                            <p className="text-xs text-zinc-400 mt-2">ID: {n.id}</p>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
};
