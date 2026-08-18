'use client';

import { useEffect, useState } from 'react';
import { 
    Clock, 
    Users, 
    Sun, 
    Moon, 
    Calendar, 
    Search, 
    Filter, 
    CheckCircle2, 
    AlertCircle, 
    Timer, 
    ArrowUpRight,
    RefreshCw,
    LogIn,
    LogOut,
    Building
} from 'lucide-react';
import { 
    getAttendanceRecords, 
    getAttendanceSummary, 
    AttendanceRecordResponse, 
    AttendanceSummaryResponse 
} from '@/lib/api/tenant/attendance';

export default function AttendancePage() {
    const [records, setRecords] = useState<AttendanceRecordResponse[]>([]);
    const [summary, setSummary] = useState<AttendanceSummaryResponse | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Filters
    const [shiftDate, setShiftDate] = useState<string>(new Date().toISOString().split('T')[0]);
    const [employeeIdFilter, setEmployeeIdFilter] = useState('');
    const [statusFilter, setStatusFilter] = useState('ALL');

    const fetchData = async () => {
        setIsLoading(true);
        setError(null);
        try {
            const [recordsData, summaryData] = await Promise.all([
                getAttendanceRecords({
                    shiftDate: shiftDate || undefined,
                    employeeExternalId: employeeIdFilter || undefined,
                    status: statusFilter !== 'ALL' ? statusFilter : undefined,
                    page: 1,
                    pageSize: 100
                }),
                getAttendanceSummary({ shiftDate: shiftDate || undefined })
            ]);

            setRecords(recordsData);
            setSummary(summaryData);
        } catch (err: any) {
            console.error('Failed to fetch attendance data:', err);
            setError(err.message || 'Failed to load attendance records');
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        fetchData();
    }, [shiftDate, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        fetchData();
    };

    const formatMinutesToHours = (mins: number) => {
        if (!mins || mins <= 0) return '0m';
        const hours = Math.floor(mins / 60);
        const remainingMins = mins % 60;
        if (hours === 0) return `${remainingMins}m`;
        return `${hours}h ${remainingMins}m`;
    };

    const formatTime = (timeStr?: string) => {
        if (!timeStr) return '--:--';
        try {
            return new Date(timeStr).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        } catch {
            return timeStr;
        }
    };

    return (
        <div className="p-8 space-y-8 max-w-[1600px] mx-auto">
            {/* Header Title Section */}
            <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4 bg-gradient-to-r from-slate-900 via-indigo-950 to-slate-900 p-8 rounded-3xl text-white shadow-xl relative overflow-hidden">
                <div className="absolute top-0 right-0 w-96 h-96 bg-indigo-500/10 rounded-full blur-3xl pointer-events-none" />
                <div className="space-y-2 relative z-10">
                    <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-indigo-500/20 text-indigo-300 text-xs font-semibold uppercase tracking-wider border border-indigo-500/30">
                        <Clock className="w-3.5 h-3.5" /> First-In, Last-Out (FILO) Engine
                    </div>
                    <h1 className="text-3xl font-extrabold tracking-tight">Attendance & Shift Records</h1>
                    <p className="text-slate-400 text-sm max-w-xl">
                        Automated multi-camera entry and exit tracking with 16-hour shift aggregation and night-shift detection.
                    </p>
                </div>

                <div className="flex items-center gap-3 relative z-10">
                    <div className="bg-slate-800/80 border border-slate-700/60 rounded-2xl px-4 py-2.5 flex items-center gap-3">
                        <Calendar className="w-4 h-4 text-indigo-400" />
                        <input
                            type="date"
                            value={shiftDate}
                            onChange={(e) => setShiftDate(e.target.value)}
                            className="bg-transparent text-sm font-semibold text-white focus:outline-none cursor-pointer"
                        />
                    </div>
                    <button
                        onClick={fetchData}
                        disabled={isLoading}
                        className="bg-indigo-600 hover:bg-indigo-500 text-white p-3 rounded-2xl transition-all shadow-lg hover:shadow-indigo-500/25 active:scale-95 disabled:opacity-50"
                    >
                        <RefreshCw className={`w-5 h-5 ${isLoading ? 'animate-spin' : ''}`} />
                    </button>
                </div>
            </div>

            {/* KPI Summary Cards Grid */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-5">
                {/* Total Present */}
                <div className="bg-white rounded-3xl p-6 border border-slate-100 shadow-sm hover:shadow-md transition-shadow relative overflow-hidden group">
                    <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-400 uppercase tracking-wider">Total Present Today</span>
                        <div className="w-10 h-10 rounded-2xl bg-emerald-50 border border-emerald-100 flex items-center justify-center text-emerald-600">
                            <Users className="w-5 h-5" />
                        </div>
                    </div>
                    <div className="mt-4 flex items-baseline gap-2">
                        <span className="text-3xl font-black text-slate-900">{summary?.totalPresent ?? 0}</span>
                        <span className="text-xs font-semibold text-emerald-600">Employees</span>
                    </div>
                    <div className="mt-2 text-xs text-slate-500 flex items-center gap-1">
                        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" /> Verified via Camera Re-ID
                    </div>
                </div>

                {/* Day Shift */}
                <div className="bg-white rounded-3xl p-6 border border-slate-100 shadow-sm hover:shadow-md transition-shadow relative overflow-hidden group">
                    <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-400 uppercase tracking-wider">Day Shift Count</span>
                        <div className="w-10 h-10 rounded-2xl bg-amber-50 border border-amber-100 flex items-center justify-center text-amber-600">
                            <Sun className="w-5 h-5" />
                        </div>
                    </div>
                    <div className="mt-4 flex items-baseline gap-2">
                        <span className="text-3xl font-black text-slate-900">{summary?.dayShiftCount ?? 0}</span>
                        <span className="text-xs font-semibold text-amber-600">Standard Shifts</span>
                    </div>
                    <div className="mt-2 text-xs text-slate-500 flex items-center gap-1">
                        First entry after 04:00 AM
                    </div>
                </div>

                {/* Night Shift */}
                <div className="bg-white rounded-3xl p-6 border border-slate-100 shadow-sm hover:shadow-md transition-shadow relative overflow-hidden group">
                    <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-400 uppercase tracking-wider">Night Shift Count</span>
                        <div className="w-10 h-10 rounded-2xl bg-purple-50 border border-purple-100 flex items-center justify-center text-purple-600">
                            <Moon className="w-5 h-5" />
                        </div>
                    </div>
                    <div className="mt-4 flex items-baseline gap-2">
                        <span className="text-3xl font-black text-slate-900">{summary?.nightShiftCount ?? 0}</span>
                        <span className="text-xs font-semibold text-purple-600">Overnight Shifts</span>
                    </div>
                    <div className="mt-2 text-xs text-slate-500 flex items-center gap-1">
                        Bound to previous calendar day
                    </div>
                </div>

                {/* Average Work Minutes */}
                <div className="bg-white rounded-3xl p-6 border border-slate-100 shadow-sm hover:shadow-md transition-shadow relative overflow-hidden group">
                    <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-slate-400 uppercase tracking-wider">Avg Shift Duration</span>
                        <div className="w-10 h-10 rounded-2xl bg-blue-50 border border-blue-100 flex items-center justify-center text-blue-600">
                            <Timer className="w-5 h-5" />
                        </div>
                    </div>
                    <div className="mt-4 flex items-baseline gap-2">
                        <span className="text-3xl font-black text-slate-900">
                            {formatMinutesToHours(summary?.averageWorkMinutes ?? 0)}
                        </span>
                        <span className="text-xs font-semibold text-blue-600">On-Site</span>
                    </div>
                    <div className="mt-2 text-xs text-slate-500 flex items-center gap-1">
                        FILO First-In to Last-Out delta
                    </div>
                </div>
            </div>

            {/* Filter & Search Bar */}
            <div className="bg-white p-4 rounded-3xl border border-slate-100 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-4">
                <form onSubmit={handleSearchSubmit} className="flex items-center gap-3 flex-1 max-w-md">
                    <div className="relative flex-1">
                        <Search className="w-4 h-4 text-slate-400 absolute left-4 top-1/2 -translate-y-1/2" />
                        <input
                            type="text"
                            placeholder="Search by Employee ID (e.g. EMP-001)..."
                            value={employeeIdFilter}
                            onChange={(e) => setEmployeeIdFilter(e.target.value)}
                            className="w-full bg-slate-50 border border-slate-200 rounded-2xl pl-11 pr-4 py-2.5 text-sm font-medium text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 transition-all"
                        />
                    </div>
                    <button
                        type="submit"
                        className="bg-slate-900 hover:bg-slate-800 text-white px-4 py-2.5 rounded-2xl text-xs font-semibold transition-colors"
                    >
                        Search
                    </button>
                </form>

                <div className="flex items-center gap-3">
                    <div className="flex items-center gap-2 bg-slate-50 border border-slate-200 rounded-2xl px-3 py-1.5">
                        <Filter className="w-3.5 h-3.5 text-slate-400" />
                        <span className="text-xs font-bold text-slate-500">Status:</span>
                        <select
                            value={statusFilter}
                            onChange={(e) => setStatusFilter(e.target.value)}
                            className="bg-transparent text-xs font-semibold text-slate-800 focus:outline-none cursor-pointer"
                        >
                            <option value="ALL">All Statuses</option>
                            <option value="InShift">In Shift (Active)</option>
                            <option value="Completed">Completed (Exited)</option>
                            <option value="Incomplete">Incomplete</option>
                        </select>
                    </div>
                </div>
            </div>

            {/* Attendance Records Table */}
            <div className="bg-white rounded-3xl border border-slate-100 shadow-sm overflow-hidden">
                <div className="p-6 border-b border-slate-100 flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-bold text-slate-900">Shift Log & Duration</h2>
                        <p className="text-xs text-slate-400 mt-0.5">Showing daily aggregated First-In and Last-Out camera timestamps</p>
                    </div>
                    <span className="px-3 py-1 rounded-full bg-slate-100 text-slate-600 text-xs font-semibold">
                        {records.length} Records
                    </span>
                </div>

                {isLoading ? (
                    <div className="p-12 text-center text-slate-400 font-medium animate-pulse flex flex-col items-center gap-2">
                        <RefreshCw className="w-6 h-6 animate-spin text-indigo-500" />
                        Fetching FILO attendance records...
                    </div>
                ) : error ? (
                    <div className="p-12 text-center text-rose-500 font-medium flex flex-col items-center gap-2">
                        <AlertCircle className="w-6 h-6" />
                        {error}
                    </div>
                ) : records.length === 0 ? (
                    <div className="p-16 text-center text-slate-400 flex flex-col items-center gap-3">
                        <div className="w-12 h-12 rounded-full bg-slate-100 flex items-center justify-center text-slate-400">
                            <Clock className="w-6 h-6" />
                        </div>
                        <div>
                            <p className="text-sm font-semibold text-slate-700">No Attendance Records Found</p>
                            <p className="text-xs text-slate-400 mt-0.5">No check-in or check-out events match the selected filter criteria.</p>
                        </div>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-left border-collapse">
                            <thead>
                                <tr className="bg-slate-50/60 border-b border-slate-100 text-[11px] font-bold text-slate-400 uppercase tracking-wider">
                                    <th className="py-4 px-6">Employee</th>
                                    <th className="py-4 px-6">Shift Type</th>
                                    <th className="py-4 px-6">First In (Check-In)</th>
                                    <th className="py-4 px-6">Last Out (Check-Out)</th>
                                    <th className="py-4 px-6">Work Duration</th>
                                    <th className="py-4 px-6">Status</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100 text-sm">
                                {records.map((rec) => (
                                    <tr key={rec.id} className="hover:bg-slate-50/50 transition-colors">
                                        {/* Employee ID & Name */}
                                        <td className="py-4 px-6">
                                            <div className="flex items-center gap-3">
                                                <div className="w-9 h-9 rounded-2xl bg-indigo-50 border border-indigo-100 flex items-center justify-center text-indigo-600 font-bold text-xs">
                                                    {rec.employeeExternalId.substring(0, 3)}
                                                </div>
                                                <div>
                                                    <span className="font-semibold text-slate-900 block">{rec.employeeName || rec.employeeExternalId}</span>
                                                    <span className="text-xs font-mono text-slate-400">{rec.employeeExternalId}</span>
                                                </div>
                                            </div>
                                        </td>

                                        {/* Shift Type */}
                                        <td className="py-4 px-6">
                                            {rec.shiftType === 'NightShift' ? (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-purple-50 text-purple-700 border border-purple-100 text-xs font-semibold">
                                                    <Moon className="w-3 h-3 text-purple-500" /> Night Shift
                                                </span>
                                            ) : (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-amber-50 text-amber-700 border border-amber-100 text-xs font-semibold">
                                                    <Sun className="w-3 h-3 text-amber-500" /> Day Shift
                                                </span>
                                            )}
                                        </td>

                                        {/* First In Time */}
                                        <td className="py-4 px-6">
                                            <div className="flex items-center gap-2 text-slate-700">
                                                <LogIn className="w-4 h-4 text-emerald-500" />
                                                <div>
                                                    <span className="font-bold text-slate-900 block">{formatTime(rec.firstInTime)}</span>
                                                    <span className="text-[11px] text-slate-400">{rec.firstInCameraName || 'Entry Gate'}</span>
                                                </div>
                                            </div>
                                        </td>

                                        {/* Last Out Time */}
                                        <td className="py-4 px-6">
                                            <div className="flex items-center gap-2 text-slate-700">
                                                <LogOut className="w-4 h-4 text-rose-500" />
                                                <div>
                                                    <span className="font-bold text-slate-900 block">{formatTime(rec.lastOutTime)}</span>
                                                    <span className="text-[11px] text-slate-400">{rec.lastOutCameraName || 'Exit Gate'}</span>
                                                </div>
                                            </div>
                                        </td>

                                        {/* Work Duration */}
                                        <td className="py-4 px-6">
                                            <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-xl bg-slate-100 text-slate-800 text-xs font-bold font-mono">
                                                <Timer className="w-3.5 h-3.5 text-indigo-500" />
                                                {formatMinutesToHours(rec.totalWorkMinutes)}
                                            </div>
                                        </td>

                                        {/* Status */}
                                        <td className="py-4 px-6">
                                            {rec.status === 'Completed' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-emerald-50 text-emerald-700 border border-emerald-200 text-xs font-semibold">
                                                    <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" /> Shift Ended
                                                </span>
                                            )}
                                            {rec.status === 'InShift' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-blue-50 text-blue-700 border border-blue-200 text-xs font-semibold animate-pulse">
                                                    <Clock className="w-3.5 h-3.5 text-blue-500" /> On-Site
                                                </span>
                                            )}
                                            {rec.status === 'Incomplete' && (
                                                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-slate-100 text-slate-600 border border-slate-200 text-xs font-semibold">
                                                    Incomplete
                                                </span>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
}
