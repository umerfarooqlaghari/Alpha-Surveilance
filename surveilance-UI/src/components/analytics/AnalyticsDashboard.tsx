import { useEffect, useRef, useState } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  BarChart, Bar, PieChart, Pie, Cell, Legend,
} from 'recharts';
import {
  Loader2, Calendar, Video, RefreshCw, CheckCircle,
} from 'lucide-react';
import { getAnalytics, AnalyticsResponse, getViolations, Violation } from '@/lib/api/tenant/violations';
import { getCameras } from '@/lib/api/tenant/cameras';

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

  // Global Filters
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [selectedCamera, setSelectedCamera] = useState('');
  const [cameras, setCameras] = useState<{ id: string; name: string }[]>([]);
  const [cameraMap, setCameraMap] = useState<Record<string, string>>({});

  // Local Chart State
  const [trendRange, setTrendRange] = useState<'7d' | '30d'>('30d');
  const [cameraLimit, setCameraLimit] = useState<number>(5);

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
      setRecentViolations(violations ?? []);
      // initial analytics load — no filters yet
      await fetchAnalyticsData({});
    } catch (error) {
      console.error('Initialization failed', error);
      setLoading(false);
    }
  };

  const fetchAnalyticsData = async (
    filters: { startDate?: string; endDate?: string; cameraId?: string } = {}
  ) => {
    setLoading(true);
    try {
      const response = await getAnalytics({
        startDate: filters.startDate,
        endDate: filters.endDate,
        cameraId: filters.cameraId,
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
      startDate: startDate || undefined,
      endDate: endDate ? `${endDate}T23:59:59` : undefined,
      cameraId: selectedCamera || undefined,
    });
  }, [startDate, endDate, selectedCamera]);

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
  const hourlyData = data.hourlyHeatmap ?? [];
  const trendData = trendRange === '7d' ? allTrends.slice(-7) : allTrends;
  const cameraData = allCameras.slice(0, cameraLimit);

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
          <button onClick={() => fetchAnalyticsData()} className="p-2 hover:bg-gray-100 rounded-lg text-gray-600">
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

      <DashboardCard title="Recent Activity" subtitle="Latest 5 detected violations">
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
                  <td className="px-4 py-3 font-medium text-gray-900">{v.type}</td>
                  <td className="px-4 py-3 text-gray-700 font-medium">
                    {cameraMap[v.cameraId ?? ''] || v.cameraName || v.cameraId || '—'}
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

        {/* Severity Breakdown (1/3 width) */}
        <DashboardCard title="Severity Distribution">
          <ResponsiveContainer width="100%" height={300}>
            <PieChart>
              <Pie
                data={bySeverity}
                cx="50%" cy="50%"
                innerRadius={60} outerRadius={80}
                paddingAngle={5}
                dataKey="count" nameKey="severity"
              >
                {bySeverity.map((entry, index) => {
                  let color = THEME.colors.primary;
                  if (entry.severity === 'Critical') color = THEME.colors.danger;
                  if (entry.severity === 'High') color = THEME.colors.warning;
                  if (entry.severity === 'Low') color = THEME.colors.success;
                  return <Cell key={`cell-${index}`} fill={color} strokeWidth={0} />;
                })}
              </Pie>
              <Tooltip content={<CustomTooltip />} />
              <Legend
                verticalAlign="bottom" height={36} iconType="circle"
                formatter={val => <span className="text-xs font-medium text-gray-600 ml-1">{val}</span>}
              />
            </PieChart>
          </ResponsiveContainer>
        </DashboardCard>
      </div>

      {/* Secondary Charts Row */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

        {/* Top Cameras */}
        <DashboardCard
          title="Top Violating Cameras"
          action={
            <div className="flex items-center gap-1 text-xs font-medium text-gray-500 bg-gray-100 rounded-md px-1">
              <button
                onClick={() => setCameraLimit(5)}
                className={`px-2 py-1 rounded ${cameraLimit === 5 ? 'bg-white shadow-sm text-gray-900' : 'hover:text-gray-900'}`}
              >
                Top 5
              </button>
              <button
                onClick={() => setCameraLimit(10)}
                className={`px-2 py-1 rounded ${cameraLimit === 10 ? 'bg-white shadow-sm text-gray-900' : 'hover:text-gray-900'}`}
              >
                Top 10
              </button>
            </div>
          }
        >
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={cameraData} layout="vertical" margin={{ left: 0 }}>
              <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke={THEME.colors.grid} />
              <XAxis type="number" hide />
              <YAxis
                dataKey="cameraName" type="category"
                stroke={THEME.colors.axisLabel} fontSize={11} width={110}
                tickLine={false} axisLine={false}
              />
              <Tooltip content={<CustomTooltip />} cursor={{ fill: '#F9FAFB' }} />
              <Bar dataKey="count" fill={THEME.colors.primary} radius={[0, 4, 4, 0]} barSize={24} name="Violations" />
            </BarChart>
          </ResponsiveContainer>
        </DashboardCard>

        {/* Hourly Heatmap */}
        <DashboardCard title="Hourly Activity Heatmap">
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={hourlyData}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={THEME.colors.grid} />
              <XAxis
                dataKey="hour"
                stroke={THEME.colors.axisLabel} fontSize={11}
                tickFormatter={val => `${val}:00`}
                tickLine={false} axisLine={false}
              />
              <Tooltip content={<CustomTooltip />} cursor={{ fill: '#F9FAFB' }} />
              <Bar dataKey="count" fill={THEME.colors.primary} radius={[4, 4, 0, 0]} barSize={32} name="Activity" />
            </BarChart>
          </ResponsiveContainer>
        </DashboardCard>
      </div>


    </div>
  );
}
