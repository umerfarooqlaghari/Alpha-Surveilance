import { useEffect, useRef, useState } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  BarChart, Bar, PieChart, Pie, Cell, Legend,
  Radar, RadarChart, PolarGrid, PolarAngleAxis, PolarRadiusAxis,
} from 'recharts';
import {
  Loader2, Calendar, Video, RefreshCw, CheckCircle,
} from 'lucide-react';
import { getAnalytics, AnalyticsResponse, getViolations, Violation } from '@/lib/api/tenant/violations';
import { getCameras } from '@/lib/api/tenant/cameras';
import LocationSelect from '@/components/locations/LocationSelect';

// --- Professional Theme Constants ---
const THEME = {
  colors: {
    primary: '#4F46E5',
    success: '#10B981',
    warning: '#F59E0B',
    danger: '#EF4444',
    neutral: '#6B7280',
    axisLabel: '#374151',  // darker for legibility
    grid: '#E5E7EB',
  },
  charts: {
    palette: ['#4F46E5', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#EC4899', '#6366F1'],
  },
};

const CustomTooltip = ({ active, payload, label }: any) => {
  if (active && payload && payload.length) {
    return (
      <div className="bg-white p-3 border border-gray-100 shadow-lg rounded-lg text-xs">
        <p className="font-semibold text-gray-800 mb-1">{label}</p>
        {payload.map((entry: any, index: number) => (
          <div key={index} className="flex items-center gap-2 mb-1 last:mb-0">
            <div className="w-2 h-2 rounded-full" style={{ backgroundColor: entry.color }} />
            <span className="text-gray-500 capitalize">{entry.name}:</span>
            <span className="font-mono font-medium text-gray-900">{entry.value}</span>
          </div>
        ))}
      </div>
    );
  }
  return null;
};

// --- Reusable Dashboard Card ---
interface DashboardCardProps {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  action?: React.ReactNode;
  className?: string;
}

const DashboardCard = ({ title, subtitle, children, action, className = '' }: DashboardCardProps) => (
  <div className={`bg-white rounded-xl border border-gray-200 shadow-sm ${className}`}>
    <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-start">
      <div>
        <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
        {subtitle && <p className="text-xs text-gray-500 mt-0.5">{subtitle}</p>}
      </div>
      {action && <div className="flex items-center gap-2">{action}</div>}
    </div>
    <div className="p-6">{children}</div>
  </div>
);

// --- KPI Card ---
const KPICard = ({ title, value, trend, color }: any) => (
  <div className="bg-white p-5 rounded-xl border border-gray-200 shadow-sm flex items-start justify-between">
    <div>
      <p className="text-xs font-medium text-gray-500 uppercase tracking-wide">{title}</p>
      <h3 className="text-2xl font-bold text-gray-900 mt-1">{value?.toLocaleString() || 0}</h3>
    </div>
    <div className={`text-xs font-medium px-2 py-1 rounded-full bg-opacity-10 ${color}`}>
      {trend}
    </div>
  </div>
);

// --- Main Component ---
export default function AnalyticsDashboard() {
  const [data, setData] = useState<AnalyticsResponse | null>(null);
  const [recentViolations, setRecentViolations] = useState<Violation[]>([]);
  const [loading, setLoading] = useState(true);

  // Helper to get local date string YYYY-MM-DD
  const getLocalDateString = (date: Date) => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  };

  // Convert a local YYYY-MM-DD date string + time to a UTC ISO string for the API.
  // Timestamps are stored in UTC; PKT = UTC+5, so subtract 5h when sending start,
  // and add enough to cover the end of the day.
  const toUtcIso = (localDate: string, endOfDay = false) => {
    if (!localDate) return undefined;
    // Treat input as local PKT (UTC+5) — shift to UTC by subtracting 5 hours
    const [year, month, day] = localDate.split('-').map(Number);
    const localMs = endOfDay
      ? Date.UTC(year, month - 1, day, 23, 59, 59) - 5 * 60 * 60 * 1000  // end of PKT day → UTC
      : Date.UTC(year, month - 1, day, 0,  0,  0)  - 5 * 60 * 60 * 1000; // start of PKT day → UTC
    return new Date(localMs).toISOString();
  };

  // Global Filters
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [selectedCamera, setSelectedCamera] = useState('');
  const [selectedLocation, setSelectedLocation] = useState<string | null>(null);
  const [cameras, setCameras] = useState<{ id: string; name: string }[]>([]);
  const [cameraMap, setCameraMap] = useState<Record<string, string>>({});

  // Local Chart State
  const [trendRange, setTrendRange] = useState<'7d' | '30d'>('30d');
  const [cameraLimit, setCameraLimit] = useState<number>(5);
  const [trendApiData, setTrendApiData] = useState<any[]>([]);
  const [heatmapDate, setHeatmapDate] = useState('');
  const [heatmapRawData, setHeatmapRawData] = useState<any[]>([]);

  const isTrendFirstRender = useRef(true);
  const isHeatmapFirstRender = useRef(true);

  const fetchTrendData = async (range: '7d' | '30d', cameraId?: string) => {
    const today = new Date();
    const endD = getLocalDateString(today);
    const days = range === '7d' ? 6 : 29;
    const startD = getLocalDateString(new Date(today.getTime() - days * 86400000));
    try {
      const response = await getAnalytics({
        startDate: toUtcIso(startD, false),
        endDate: toUtcIso(endD, true),
        cameraId: cameraId ?? (selectedCamera || undefined),
        locationId: selectedLocation || undefined,
      });
      setTrendApiData(response.dailyTrends ?? []);
    } catch (e) {
      console.error('Failed to fetch trend data', e);
    }
  };

  const fetchHeatmapData = async (date: string) => {
    try {
      // When no date is selected, fetch all historical data (no date filters)
      const response = await getAnalytics(
        date
          ? { startDate: toUtcIso(date, false), endDate: toUtcIso(date, true), locationId: selectedLocation || undefined }
          : { locationId: selectedLocation || undefined }
      );
      setHeatmapRawData(response.hourlyHeatmap ?? []);
    } catch (e) {
      console.error('Failed to fetch heatmap data', e);
    }
  };

  useEffect(() => {
    loadInitialData();
  }, []);

  const loadInitialData = async () => {
    try {
      const cams = await getCameras();
      const camList = cams.map((c: any) => ({ id: c.cameraId, name: c.name }));
      setCameras(camList);
      const map: Record<string, string> = {};
      cams.forEach((c: any) => { map[c.cameraId] = c.name; });
      setCameraMap(map);
      const violations = await getViolations();
      setRecentViolations(violations?.slice(0, 10) ?? []);
      // initial analytics load — no date filter by default (show all data)
      await fetchAnalyticsData({});
      await fetchTrendData('30d');
      // heatmap with no date selected → show all historical data
      await fetchHeatmapData('');
    } catch (error) {
      console.error('Initialization failed', error);
      setLoading(false);
    }
  };

  const fetchAnalyticsData = async (
    filters: { startDate?: string; endDate?: string; cameraId?: string; locationId?: string } = {}
  ) => {
    setLoading(true);
    try {
      const response = await getAnalytics({
        startDate: filters.startDate,
        endDate: filters.endDate,
        cameraId: filters.cameraId,
        locationId: filters.locationId,
      });
      setData(response);
    } catch (error) {
      console.error('Failed to fetch analytics', error);
    } finally {
      setLoading(false);
    }
  };

  // Re-fetch when any filter changes (skip initial mount — loadInitialData handles that)
  const isFirstRender = useRef(true);
  useEffect(() => {
    if (isFirstRender.current) { isFirstRender.current = false; return; }
    fetchAnalyticsData({
      startDate: toUtcIso(startDate, false),
      endDate:   toUtcIso(endDate, true),
      cameraId: selectedCamera || undefined,
      locationId: selectedLocation || undefined,
    });
  }, [startDate, endDate, selectedCamera, selectedLocation]);

  // Re-fetch trend when range or camera/location filter changes (independent of global date range)
  useEffect(() => {
    if (isTrendFirstRender.current) { isTrendFirstRender.current = false; return; }
    fetchTrendData(trendRange);
  }, [trendRange, selectedCamera, selectedLocation]);

  // Re-fetch heatmap when focus date changes (independent of global filters).
  // Empty date → fetch all historical data.
  useEffect(() => {
    if (isHeatmapFirstRender.current) { isHeatmapFirstRender.current = false; return; }
    fetchHeatmapData(heatmapDate);
  }, [heatmapDate, selectedLocation]);

  if (loading && !data) {
    return (
      <div className="flex justify-center items-center h-screen">
        <Loader2 className="w-8 h-8 animate-spin text-indigo-600" />
      </div>
    );
  }

  if (!data) return <div className="text-center p-8 text-gray-500">Failed to load analytics data.</div>;

  const allTrends = data.dailyTrends ?? [];
  const allCameras = data.byCamera ?? [];
  const bySeverity = data.bySeverity ?? [];
  const byCategory = data.byCategory ?? [];
  const byStatus = data.byStatus ?? [];
  
  // Adjusted for timezone offset (5 hours)
  const heatmapData = heatmapRawData.map(h => ({
    ...h,
    hour: (h.hour + 5) % 24
  }));

  const heatmapRows = Array.from(new Map(
    heatmapData
      .filter(h => h.cameraName)
      .map(h => {
        const idPart = h.cameraId || h.cameraName;
        const key = `${idPart}|${h.isDeleted ? '1' : '0'}`;
        return [key, {
          key,
          cameraId: h.cameraId,
          cameraName: h.cameraName as string,
          isDeleted: Boolean(h.isDeleted),
        }] as const;
      })
  ).values());
  const heatmapHours = Array.from({ length: 24 }, (_, i) => i);

  const trendData = trendApiData;
  const cameraData = allCameras.slice(0, cameraLimit).map(c => ({
    ...c,
    cameraLabel: c.isDeleted ? `${c.cameraName} (Deleted)` : c.cameraName,
  }));

  return (
    <div className="space-y-6 pb-12 bg-gray-50/50 min-h-screen">

      {/* Header & Filters */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-white p-4 rounded-xl border border-gray-200 shadow-sm">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Analytical Overview</h1>
          <p className="text-sm text-gray-500">Monitor violations and system performance</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex items-center gap-2 bg-gray-50 px-3 py-2 rounded-lg border border-gray-200">
            <Calendar className="w-4 h-4 text-gray-400" />
            <input
              type="date"
              className="bg-transparent text-sm outline-none w-32 text-black"
              value={startDate}
              onChange={e => setStartDate(e.target.value)}
            />
            <span className="text-gray-300">|</span>
            <input
              type="date"
              className="bg-transparent text-sm outline-none w-32 text-black"
              value={endDate}
              onChange={e => setEndDate(e.target.value)}
            />
          </div>
          <div className="flex items-center gap-2 bg-gray-50 px-3 py-2 rounded-lg border border-gray-200">
            <Video className="w-4 h-4 text-gray-400" />
            <select
              className="bg-transparent text-sm outline-none min-w-[120px] text-black"
              value={selectedCamera}
              onChange={e => setSelectedCamera(e.target.value)}
            >
              <option value="">All Cameras</option>
              {cameras.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <div className="min-w-[180px]">
            <LocationSelect
              value={selectedLocation}
              onChange={setSelectedLocation}
              unassignedLabel="All Locations"
            />
          </div>
          <button onClick={() => fetchAnalyticsData({
            startDate: toUtcIso(startDate, false),
            endDate:   toUtcIso(endDate, true),
            cameraId: selectedCamera || undefined,
            locationId: selectedLocation || undefined,
          })} className="p-2 hover:bg-gray-100 rounded-lg text-gray-600">
            <RefreshCw className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <KPICard title="Total Violations" value={data.summary.totalViolations} trend="+12%" color="text-indigo-600" />
        <KPICard title="Active Cases" value={data.summary.activeViolations} trend="-5%" color="text-amber-600" />
        <KPICard title="Resolved" value={data.summary.resolvedViolations} trend="+8%" color="text-emerald-600" />
        <KPICard title="Critical Alerts" value={data.summary.criticalViolations} trend="+2%" color="text-red-600" />
      </div>

      <DashboardCard title="Recent Activity" subtitle="Latest 10 detected violations">
        <div className="overflow-x-auto">
          <table className="w-full text-sm text-left">
            <thead className="text-xs text-gray-500 uppercase bg-gray-50/50 border-b border-gray-100">
              <tr>
                <th className="px-4 py-3 font-medium">Type</th>
                <th className="px-4 py-3 font-medium">Camera</th>
                <th className="px-4 py-3 font-medium">Severity</th>
                <th className="px-4 py-3 font-medium">Time</th>
                <th className="px-4 py-3 font-medium">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {(recentViolations ?? []).map(v => (
                <tr key={v.id} className="hover:bg-gray-50/50 transition-colors">
                  <td className="px-4 py-3 font-medium text-gray-900">{v.violationTypeName || v.type || 'Detection'}</td>
                  <td className="px-4 py-3 text-gray-700 font-medium">
                    <div className="flex items-center gap-2">
                      <span>{cameraMap[v.cameraId ?? ''] || v.cameraName || v.cameraId || '—'}</span>
                      {v.cameraDeleted && (
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-100 text-amber-800 border border-amber-200">
                          Deleted (historical)
                        </span>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${v.severity === 'Critical' || v.severity === 2 ? 'bg-red-50 text-red-700' :
                      v.severity === 'High' || v.severity === 1 ? 'bg-amber-50 text-amber-700' :
                        'bg-green-50 text-green-700'
                      }`}>
                      {v.severity === 'Critical' || v.severity === 2 ? 'Critical' :
                        v.severity === 'High' || v.severity === 1 ? 'High' : 'Low'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-gray-500 font-mono text-xs">
                    {new Date(v.timestamp).toLocaleString()}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium border ${v.status === 'Resolved' || v.status === '2'
                      ? 'bg-green-50 text-green-700 border-green-100'
                      : 'bg-gray-50 text-gray-600 border-gray-100'
                      }`}>
                      {v.status === 'Resolved' || v.status === '2'
                        ? <><CheckCircle className="w-3 h-3" /> Resolved</>
                        : <><Loader2 className="w-3 h-3" /> Pending</>}
                    </span>
                  </td>
                </tr>
              ))}
              {recentViolations.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-gray-500">No recent activity found.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </DashboardCard>

      {/* Main Charts Row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">

        {/* Trend Chart (2/3 width) */}
        <DashboardCard
          title="Violation Trends"
          subtitle="Daily violation volume over time"
          className="lg:col-span-2"
          action={
            <select
              className="text-xs border-none bg-gray-100 rounded-md px-2 py-1 outline-none font-medium text-gray-600"
              value={trendRange}
              onChange={(e: any) => setTrendRange(e.target.value)}
            >
              <option value="7d">Last 7 Days</option>
              <option value="30d">Last 30 Days</option>
            </select>
          }
        >
          <svg style={{ height: 0, width: 0, position: 'absolute' }}>
            <defs>
              <linearGradient id="radarGradient" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0%" stopColor="#4F46E5" stopOpacity={0.8} />
                <stop offset="100%" stopColor="#818CF8" stopOpacity={0.3} />
              </linearGradient>
            </defs>
          </svg>
          <ResponsiveContainer width="100%" height={300}>
            <AreaChart data={trendData}>
              <defs>
                <linearGradient id="colorCount" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={THEME.colors.primary} stopOpacity={0.1} />
                  <stop offset="95%" stopColor={THEME.colors.primary} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={THEME.colors.grid} />
              <XAxis
                dataKey="date"
                tickFormatter={val => new Date(val).toLocaleDateString(undefined, { day: '2-digit', month: 'short' })}
                stroke={THEME.colors.axisLabel} fontSize={11} tickLine={false} axisLine={false} dy={10}
              />
              <YAxis stroke={THEME.colors.axisLabel} fontSize={11} tickLine={false} axisLine={false} dx={-10} />
              <Tooltip content={<CustomTooltip />} cursor={{ stroke: THEME.colors.primary, strokeDasharray: '3 3' }} />
              <Area type="monotone" dataKey="count" stroke={THEME.colors.primary} strokeWidth={2} fill="url(#colorCount)" name="Violations" />
            </AreaChart>
          </ResponsiveContainer>
        </DashboardCard>

        {/* Model Distribution (Radar Chart) */}
        <DashboardCard title="Detection Model Analysis" subtitle="AI detection performance across categories">
          <ResponsiveContainer width="100%" height={320}>
            <RadarChart cx="50%" cy="50%" outerRadius="80%" data={byCategory}>
              <PolarGrid gridType="circle" stroke="#E2E8F0" strokeDasharray="3 3" />
              <PolarAngleAxis 
                dataKey="type" 
                tick={{ fill: '#64748B', fontSize: 10, fontWeight: 500 }} 
              />
              <PolarRadiusAxis 
                angle={30} 
                domain={[0, 'auto']} 
                tick={false} 
                axisLine={false} 
              />
              <Radar
                name="Detection Count"
                dataKey="count"
                stroke="#4F46E5"
                strokeWidth={3}
                fill="url(#radarGradient)"
                fillOpacity={0.7}
                dot={{ r: 4, fill: '#4F46E5', strokeWidth: 2, stroke: '#fff' }}
              />
              <Tooltip content={<CustomTooltip />} />
            </RadarChart>
          </ResponsiveContainer>
        </DashboardCard>
      </div>

      {/* Secondary Charts Row */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

        {/* Top Cameras */}
        <DashboardCard title="Camera Distribution">
          <ResponsiveContainer width="100%" height={280}>
            <PieChart>
              <Pie
                data={cameraData}
                cx="50%" cy="50%"
                innerRadius={60} outerRadius={80}
                paddingAngle={5}
                dataKey="count" nameKey="cameraLabel"
              >
                {cameraData.map((_, index) => (
                  <Cell key={`cell-${index}`} fill={THEME.charts.palette[index % THEME.charts.palette.length]} strokeWidth={0} />
                ))}
              </Pie>
              <Tooltip content={<CustomTooltip />} />
              <Legend
                verticalAlign="bottom" height={36} iconType="circle"
                formatter={(val, entry: any) => (
                  <span className="text-xs font-medium text-gray-600 ml-1 truncate max-w-[140px] inline-flex items-center gap-1 align-middle">
                    <span className="truncate max-w-[90px] inline-block">{val}</span>
                    {entry?.payload?.isDeleted && (
                      <span className="px-1.5 py-0.5 rounded-full text-[9px] font-semibold bg-amber-100 text-amber-800 border border-amber-200">
                        Deleted
                      </span>
                    )}
                  </span>
                )}
              />
            </PieChart>
          </ResponsiveContainer>
        </DashboardCard>

        {/* Status Distribution */}
        <DashboardCard title="Audit Status Distribution">
          <ResponsiveContainer width="100%" height={280}>
            <PieChart>
              <Pie
                data={byStatus}
                cx="50%" cy="50%"
                innerRadius={60} outerRadius={80}
                paddingAngle={5}
                dataKey="count" nameKey="status"
              >
                {byStatus.map((entry, index) => (
                  <Cell 
                    key={`cell-${index}`} 
                    fill={entry.status === 'Audited' ? THEME.colors.success : THEME.colors.warning} 
                    strokeWidth={0} 
                  />
                ))}
              </Pie>
              <Tooltip content={<CustomTooltip />} />
              <Legend
                verticalAlign="bottom" height={36} iconType="circle"
                formatter={val => <span className="text-xs font-medium text-gray-600 ml-1">{val}</span>}
              />
            </PieChart>
          </ResponsiveContainer>
        </DashboardCard>

        {/* Hourly Activity Grid (Heatmap) */}
        <DashboardCard 
          title="Activity Heatmap" 
          subtitle="Violation intensity by camera and hour"
          className="lg:col-span-2"
          action={
            <div className="flex items-center gap-2">
              <span className="text-[10px] font-medium text-gray-400 uppercase">Focus Date:</span>
              <input 
                type="date" 
                className="text-xs border border-gray-200 rounded px-2 py-1 outline-none text-black"
                value={heatmapDate}
                onChange={e => setHeatmapDate(e.target.value)}
              />
            </div>
          }
        >
          <div className="overflow-x-auto">
            <div className="min-w-[800px]">
              {/* Hour Labels */}
              <div className="flex mb-2">
                <div className="w-32 flex-shrink-0" />
                <div className="flex flex-1 justify-between text-[10px] text-gray-400 font-mono">
                  {heatmapHours.map(h => (
                    <div key={h} className="w-full text-center">
                      {h.toString().padStart(2, '0')}
                    </div>
                  ))}
                </div>
              </div>

              {/* Rows */}
              <div className="space-y-1">
                {heatmapRows.length === 0 ? (
                  <div className="py-8 text-center text-gray-400 text-sm">No activity recorded for this period.</div>
                ) : heatmapRows.map(row => (
                  <div key={row.key} className="flex items-center">
                    <div className="w-32 flex-shrink-0 text-xs font-medium text-gray-600 truncate pr-2" title={row.cameraName}>
                      <div className="flex items-center gap-1">
                        <span className="truncate">{row.cameraName}</span>
                        {row.isDeleted && (
                          <span className="px-1.5 py-0.5 rounded-full text-[9px] font-semibold bg-amber-100 text-amber-800 border border-amber-200">
                            Deleted
                          </span>
                        )}
                      </div>
                    </div>
                    <div className="flex flex-1 gap-1">
                      {heatmapHours.map(hour => {
                        const count = heatmapData.find(d =>
                          d.hour === hour &&
                          (d.cameraId || d.cameraName) === (row.cameraId || row.cameraName) &&
                          Boolean(d.isDeleted) === row.isDeleted
                        )?.count || 0;
                        let bgColor = 'bg-gray-50';
                        if (count > 0) bgColor = 'bg-indigo-100';
                        if (count > 5) bgColor = 'bg-indigo-300';
                        if (count > 10) bgColor = 'bg-indigo-500';
                        if (count > 20) bgColor = 'bg-indigo-700';
                        if (count > 50) bgColor = 'bg-indigo-900';

                        return (
                          <div
                            key={hour}
                            className={`w-full h-6 rounded-sm transition-colors ${bgColor} group relative`}
                          >
                            {count > 0 && (
                              <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 px-2 py-1 bg-gray-900 text-white text-[10px] rounded opacity-0 group-hover:opacity-100 pointer-events-none whitespace-nowrap z-10 shadow-xl">
                                {row.cameraName}{row.isDeleted ? ' (deleted camera, historical)' : ''} | {hour}:00: {count} violations
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>

              {/* Legend */}
              <div className="mt-6 flex items-center justify-end gap-3 text-[10px] font-medium text-gray-500">
                <span>Less</span>
                <div className="flex gap-1">
                  <div className="w-3 h-3 rounded-sm bg-gray-50 border border-gray-100" />
                  <div className="w-3 h-3 rounded-sm bg-indigo-100" />
                  <div className="w-3 h-3 rounded-sm bg-indigo-300" />
                  <div className="w-3 h-3 rounded-sm bg-indigo-500" />
                  <div className="w-3 h-3 rounded-sm bg-indigo-700" />
                  <div className="w-3 h-3 rounded-sm bg-indigo-900" />
                </div>
                <span>More</span>
              </div>
            </div>
          </div>
        </DashboardCard>
      </div>


    </div>
  );
}
