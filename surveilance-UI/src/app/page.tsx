'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import {
    Shield,
    Building2,
    Mail,
    Lock,
    Tag,
    Loader2,
    Eye,
    EyeOff,
    ArrowUpRight,
    Copy,
    Check,
    Phone,
    X,
    ExternalLink,
    Cpu,
    Video,
    Clock,
    Layers,
    Sliders,
    Zap
} from 'lucide-react';

const LS_TENANT_KEY = 'tenant_remember';
const LS_SUPER_KEY = 'superadmin_remember';

export default function LandingPage() {
    // Login Modal State
    const [isLoginModalOpen, setIsLoginModalOpen] = useState(false);
    const [loginTab, setLoginTab] = useState<'tenant' | 'superadmin'>('tenant');

    // Tenant Login Form State
    const [tenantSlug, setTenantSlug] = useState('');
    const [tenantEmail, setTenantEmail] = useState('');
    const [tenantPassword, setTenantPassword] = useState('');

    // SuperAdmin Login Form State
    const [adminEmail, setAdminEmail] = useState('');
    const [adminPassword, setAdminPassword] = useState('');

    // Shared Form States
    const [rememberMe, setRememberMe] = useState(false);
    const [showPw, setShowPw] = useState(false);
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [copiedEmail, setCopiedEmail] = useState(false);

    // Prompt Search State
    const [searchPrompt, setSearchPrompt] = useState('');

    const { loginTenantAdmin, loginSuperAdmin, isAuthenticated, role, isLoading: authLoading } = useAuth();
    const router = useRouter();

    // Redirect if already authenticated
    useEffect(() => {
        if (!authLoading && isAuthenticated) {
            if (role === 'SuperAdmin') {
                router.push('/admin');
            } else if (role === 'TenantAdmin') {
                router.push('/tenant/analytics');
            }
        }
    }, [isAuthenticated, role, authLoading, router]);

    // Restore saved credentials on tab change
    useEffect(() => {
        try {
            if (loginTab === 'tenant') {
                const saved = localStorage.getItem(LS_TENANT_KEY);
                if (saved) {
                    const { slug, em } = JSON.parse(saved);
                    setTenantSlug(slug || '');
                    setTenantEmail(em || '');
                    setRememberMe(true);
                }
            } else {
                const saved = localStorage.getItem(LS_SUPER_KEY);
                if (saved) {
                    const { em } = JSON.parse(saved);
                    setAdminEmail(em || '');
                    setRememberMe(true);
                }
            }
        } catch { /* ignore */ }
    }, [loginTab]);

    const handleTenantSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        if (rememberMe) {
            localStorage.setItem(LS_TENANT_KEY, JSON.stringify({ slug: tenantSlug, em: tenantEmail }));
        } else {
            localStorage.removeItem(LS_TENANT_KEY);
        }

        try {
            await loginTenantAdmin(tenantEmail, tenantPassword, tenantSlug);
        } catch (err: any) {
            setError(err.message || 'Login failed. Please check your tenant credentials.');
        } finally {
            setIsLoading(false);
        }
    };

    const handleSuperAdminSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        if (rememberMe) {
            localStorage.setItem(LS_SUPER_KEY, JSON.stringify({ em: adminEmail }));
        } else {
            localStorage.removeItem(LS_SUPER_KEY);
        }

        try {
            await loginSuperAdmin(adminEmail, adminPassword);
        } catch (err: any) {
            setError(err.message || 'Login failed. Please check your SuperAdmin credentials.');
        } finally {
            setIsLoading(false);
        }
    };

    const openModal = (tab: 'tenant' | 'superadmin' = 'tenant') => {
        setLoginTab(tab);
        setError('');
        setIsLoginModalOpen(true);
    };

    const handleCopyEmail = () => {
        navigator.clipboard.writeText('info@alpha-devs.cloud');
        setCopiedEmail(true);
        setTimeout(() => setCopiedEmail(false), 2000);
    };

    return (
        <div className="w-full min-h-screen bg-[#f6f5f1] text-[#1a1a1a] font-sans antialiased selection:bg-[#18181b] selection:text-white">
            <main className="max-w-[1400px] mx-auto px-4 sm:px-6 lg:px-8 py-6 flex flex-col gap-6 w-full">

            {/* ─────────────────────────────────────────────────────────────
                1. TOP HERO CARD CONTAINER (Matching user reference card)
               ───────────────────────────────────────────────────────────── */}
            <section className="bg-white rounded-[32px] sm:rounded-[44px] border border-[#e7e5df] shadow-[0_4px_32px_rgba(0,0,0,0.02)] p-6 sm:p-10 md:p-14 flex flex-col justify-between relative overflow-hidden">
                
                {/* Header / Nav Bar inside Card */}
                <header className="flex flex-col sm:flex-row items-center justify-between gap-4 w-full mb-12 sm:mb-16">
                    
                    {/* Left: Email contact pill + Quick actions */}
                    <div className="flex items-center gap-2 bg-[#f8f7f4] border border-[#e8e6e1] px-3.5 py-1.5 rounded-full text-xs text-[#525252]">
                        <span className="font-medium text-[#262626]">info@alpha-devs.cloud</span>
                        <button
                            onClick={handleCopyEmail}
                            title="Copy email to clipboard"
                            className="inline-flex items-center gap-1 bg-white hover:bg-neutral-100 border border-[#e2e0da] text-[#171717] px-2.5 py-0.5 rounded-full font-semibold transition-all active:scale-95 text-[11px]"
                        >
                            {copiedEmail ? (
                                <>
                                    <Check className="w-3 h-3 text-emerald-600" />
                                    <span>Copied</span>
                                </>
                            ) : (
                                <>
                                    <Copy className="w-3 h-3 text-neutral-500" />
                                    <span>Copy</span>
                                </>
                            )}
                        </button>
                        <a
                            href="https://www.alpha-devs.cloud"
                            target="_blank"
                            rel="noopener noreferrer"
                            className="bg-white hover:bg-neutral-100 border border-[#e2e0da] text-[#171717] px-2.5 py-0.5 rounded-full font-semibold transition-all text-[11px]"
                        >
                            Alpha Devs
                        </a>
                    </div>

                    {/* Right: Minimal Navigation Links */}
                    <div className="flex items-center gap-6 text-xs font-semibold text-[#737373]">
                        <a href="#capabilities" className="hover:text-[#171717] transition-colors">Capabilities</a>
                        <a href="#contact" className="hover:text-[#171717] transition-colors">Contact</a>
                        <button
                            onClick={() => openModal('superadmin')}
                            className="hover:text-[#171717] transition-colors font-medium cursor-pointer"
                        >
                            SuperAdmin
                        </button>
                    </div>
                </header>

                {/* Hero Center Display Content */}
                <div className="flex flex-col items-center text-center max-w-3xl mx-auto my-4 sm:my-8 space-y-6">
                    
                    {/* Status Avatar / Security Badge */}
                    <div className="inline-flex items-center gap-2.5 bg-[#f8f7f4] border border-[#e8e6e1] px-3.5 py-1.5 rounded-full shadow-2xs">
                        <div className="w-6 h-6 rounded-full bg-[#18181b] flex items-center justify-center text-white text-xs font-bold">
                            <Shield className="w-3.5 h-3.5" />
                        </div>
                        <div className="flex items-center gap-1.5 text-xs font-medium text-[#404040]">
                            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                            <span>Alpha Surveillance 2.0</span>
                        </div>
                    </div>

                    {/* Editorial Headline */}
                    <h1 className="text-3xl sm:text-5xl md:text-6xl font-bold tracking-tight text-[#171717] leading-[1.14]">
                        Autonomous vision, safety intelligence, and attendance.
                    </h1>

                    <p className="text-sm sm:text-base text-[#737373] max-w-xl font-normal leading-relaxed">
                        Next-generation multi-tenant video analytics. Real-time RTSP violation detection, FILO shift tracking, and edge AI compute.
                    </p>

                    {/* Hero Pill Action Buttons */}
                    <div className="flex flex-wrap items-center justify-center gap-3 pt-2">
                        <button
                            onClick={() => openModal('tenant')}
                            className="inline-flex items-center gap-2 bg-[#18181b] hover:bg-[#27272a] text-white px-6 py-3 rounded-full text-xs sm:text-sm font-semibold shadow-xs transition-all active:scale-95 cursor-pointer"
                        >
                            <span>Tenant Portal</span>
                            <ArrowUpRight className="w-4 h-4" />
                        </button>
                        <button
                            onClick={() => openModal('superadmin')}
                            className="inline-flex items-center gap-2 bg-[#f8f7f4] hover:bg-[#eeede9] border border-[#e2e0da] text-[#171717] px-5 py-3 rounded-full text-xs sm:text-sm font-semibold transition-all active:scale-95 cursor-pointer"
                        >
                            <span>SuperAdmin Access</span>
                            <ArrowUpRight className="w-4 h-4 text-[#737373]" />
                        </button>
                    </div>

                </div>

                {/* Bottom Technology & Capabilities Badges Bar (matching client banner in reference) */}
                <div className="w-full pt-16 sm:pt-20 border-t border-[#f0eee9] mt-12 sm:mt-16">
                    <p className="text-[11px] font-semibold text-center text-[#a3a3a3] uppercase tracking-wider mb-6">
                        Integrated Edge & Vision Technologies
                    </p>
                    <div className="flex flex-wrap items-center justify-center gap-6 sm:gap-10 text-xs font-semibold text-[#737373] tracking-wide">
                        <span className="flex items-center gap-2">
                            <Video className="w-4 h-4 text-[#a3a3a3]" /> RTSP WebRTC
                        </span>
                        <span className="flex items-center gap-2">
                            <Cpu className="w-4 h-4 text-[#a3a3a3]" /> YOLO Vision
                        </span>
                        <span className="flex items-center gap-2">
                            <Layers className="w-4 h-4 text-[#a3a3a3]" /> Human Re-ID
                        </span>
                        <span className="flex items-center gap-2">
                            <Clock className="w-4 h-4 text-[#a3a3a3]" /> FILO Attendance
                        </span>
                        <span className="flex items-center gap-2">
                            <Shield className="w-4 h-4 text-[#a3a3a3]" /> Geofence Polygons
                        </span>
                        <span className="flex items-center gap-2">
                            <Zap className="w-4 h-4 text-[#a3a3a3]" /> AWS S3 & SQS
                        </span>
                    </div>
                </div>

            </section>

            {/* ─────────────────────────────────────────────────────────────
                2. CAPABILITIES / SERVICES SECTION (4 Minimalist Columns)
               ───────────────────────────────────────────────────────────── */}
            <section id="capabilities" className="py-8 sm:py-12 px-2">
                <div className="max-w-3xl mx-auto text-center mb-10 sm:mb-14">
                    <div className="inline-flex items-center bg-white border border-[#e8e6e1] px-3.5 py-1 rounded-full text-xs font-semibold text-[#525252] shadow-2xs mb-4">
                        Capabilities
                    </div>
                    <h2 className="text-2xl sm:text-3xl md:text-4xl font-bold tracking-tight text-[#171717]">
                        Built for enterprise facilities, manufacturing, and high-security zones.
                    </h2>
                </div>

                {/* 4 Feature Columns (styled identically to reference design) */}
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 sm:gap-6">
                    
                    {/* Card 1 */}
                    <div className="bg-white rounded-[28px] p-6 sm:p-7 border border-[#e7e5df] shadow-[0_2px_16px_rgba(0,0,0,0.02)] flex flex-col justify-between space-y-4 hover:border-[#d4d2cb] transition-colors">
                        <div className="w-10 h-10 rounded-2xl bg-[#f8f7f4] border border-[#e8e6e1] flex items-center justify-center text-[#171717]">
                            <Cpu className="w-5 h-5" />
                        </div>
                        <div>
                            <h3 className="text-base font-bold text-[#171717] mb-1.5">Edge AI Vision</h3>
                            <p className="text-xs text-[#737373] leading-relaxed">
                                Real-time neural network inference running at the local network edge. Instant detection of SOP safety breaches without latency.
                            </p>
                        </div>
                    </div>

                    {/* Card 2 */}
                    <div className="bg-white rounded-[28px] p-6 sm:p-7 border border-[#e7e5df] shadow-[0_2px_16px_rgba(0,0,0,0.02)] flex flex-col justify-between space-y-4 hover:border-[#d4d2cb] transition-colors">
                        <div className="w-10 h-10 rounded-2xl bg-[#f8f7f4] border border-[#e8e6e1] flex items-center justify-center text-[#171717]">
                            <Clock className="w-5 h-5" />
                        </div>
                        <div>
                            <h3 className="text-base font-bold text-[#171717] mb-1.5">FILO Attendance</h3>
                            <p className="text-xs text-[#737373] leading-relaxed">
                                Automated First-In, Last-Out attendance tracking using biometric Human Re-ID embeddings and directional camera roles.
                            </p>
                        </div>
                    </div>

                    {/* Card 3 */}
                    <div className="bg-white rounded-[28px] p-6 sm:p-7 border border-[#e7e5df] shadow-[0_2px_16px_rgba(0,0,0,0.02)] flex flex-col justify-between space-y-4 hover:border-[#d4d2cb] transition-colors">
                        <div className="w-10 h-10 rounded-2xl bg-[#f8f7f4] border border-[#e8e6e1] flex items-center justify-center text-[#171717]">
                            <Sliders className="w-5 h-5" />
                        </div>
                        <div>
                            <h3 className="text-base font-bold text-[#171717] mb-1.5">Geofenced SOP Rules</h3>
                            <p className="text-xs text-[#737373] leading-relaxed">
                                Draw interactive exclusion polygons, configure dwell-time thresholds, and enforce PPE rules directly on camera viewports.
                            </p>
                        </div>
                    </div>

                    {/* Card 4 */}
                    <div className="bg-white rounded-[28px] p-6 sm:p-7 border border-[#e7e5df] shadow-[0_2px_16px_rgba(0,0,0,0.02)] flex flex-col justify-between space-y-4 hover:border-[#d4d2cb] transition-colors">
                        <div className="w-10 h-10 rounded-2xl bg-[#f8f7f4] border border-[#e8e6e1] flex items-center justify-center text-[#171717]">
                            <Shield className="w-5 h-5" />
                        </div>
                        <div>
                            <h3 className="text-base font-bold text-[#171717] mb-1.5">Multi-Tenant Cloud</h3>
                            <p className="text-xs text-[#737373] leading-relaxed">
                                Complete tenant data isolation, encrypted RTSP credentials, automated Brevo email dispatches, and scalable AWS S3 storage.
                            </p>
                        </div>
                    </div>

                </div>
            </section>

            {/* ─────────────────────────────────────────────────────────────
                3. INTERACTIVE AI QUERY / TOPOLOGY PROMPT BAR
               ───────────────────────────────────────────────────────────── */}
            <section className="bg-white rounded-[32px] sm:rounded-[40px] border border-[#e7e5df] p-6 sm:p-8 shadow-[0_4px_24px_rgba(0,0,0,0.02)] flex flex-col sm:flex-row items-center justify-between gap-4">
                <div className="space-y-1 text-center sm:text-left">
                    <p className="text-xs font-semibold text-[#a3a3a3] uppercase tracking-wider">Interactive Architecture</p>
                    <p className="text-sm font-bold text-[#171717]">Need custom AI vision models or edge hardware deployment?</p>
                </div>
                <div className="flex items-center gap-2.5 w-full sm:w-auto">
                    <div className="relative flex-1 sm:w-80">
                        <input
                            type="text"
                            placeholder="e.g. Forklift safety, helmet compliance..."
                            value={searchPrompt}
                            onChange={(e) => setSearchPrompt(e.target.value)}
                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-full px-4 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                        />
                    </div>
                    <button
                        onClick={() => openModal('tenant')}
                        className="bg-[#18181b] hover:bg-[#27272a] text-white px-4 py-2.5 rounded-full text-xs font-semibold shadow-xs transition-all active:scale-95 cursor-pointer whitespace-nowrap"
                    >
                        Inquire ↗
                    </button>
                </div>
            </section>

            {/* ─────────────────────────────────────────────────────────────
                4. BOTTOM CONTACT / CTA CARD (Matching reference bottom card)
               ───────────────────────────────────────────────────────────── */}
            <section id="contact" className="bg-white rounded-[32px] sm:rounded-[44px] border border-[#e7e5df] shadow-[0_4px_32px_rgba(0,0,0,0.02)] p-8 sm:p-12 md:p-16 text-center flex flex-col items-center justify-center space-y-6">
                
                {/* Handshake / Collaboration icon */}
                <div className="w-12 h-12 rounded-2xl bg-[#f8f7f4] border border-[#e8e6e1] flex items-center justify-center text-[#171717] shadow-2xs">
                    <Building2 className="w-6 h-6" />
                </div>

                <div className="space-y-2 max-w-xl">
                    <h2 className="text-2xl sm:text-4xl font-bold tracking-tight text-[#171717]">
                        Deploy Alpha Surveillance in your facility
                    </h2>
                    <p className="text-xs sm:text-sm text-[#737373] leading-relaxed">
                        Connect with our engineering team for enterprise edge onboarding, customized SOP violation rules, or dedicated platform assistance.
                    </p>
                </div>

                {/* Pill Action Buttons */}
                <div className="flex flex-wrap items-center justify-center gap-3 pt-2">
                    <a
                        href="mailto:info@alpha-devs.cloud"
                        className="inline-flex items-center gap-2 bg-[#18181b] hover:bg-[#27272a] text-white px-5 py-2.5 rounded-full text-xs font-semibold shadow-xs transition-all active:scale-95"
                    >
                        <Mail className="w-3.5 h-3.5" />
                        <span>Email Us</span>
                    </a>
                    <a
                        href="https://wa.me/923009243063"
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-2 bg-[#f8f7f4] hover:bg-[#eeede9] border border-[#e2e0da] text-[#171717] px-5 py-2.5 rounded-full text-xs font-semibold transition-all active:scale-95"
                    >
                        <Phone className="w-3.5 h-3.5" />
                        <span>WhatsApp / Call</span>
                    </a>
                </div>

            </section>

            {/* ─────────────────────────────────────────────────────────────
                5. MINIMAL FOOTER
               ───────────────────────────────────────────────────────────── */}
            <footer className="py-6 px-4 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs text-[#a3a3a3] font-medium">
                <p>&copy; {new Date().getFullYear()} Alpha Surveillance Systems. All rights reserved.</p>
                <div className="flex items-center gap-6">
                    <a
                        href="https://www.alpha-devs.cloud"
                        target="_blank"
                        rel="noopener noreferrer"
                        className="hover:text-[#171717] transition-colors flex items-center gap-1 font-semibold text-[#737373]"
                    >
                        Engineered by Alpha Devs <ExternalLink className="w-3 h-3" />
                    </a>
                </div>
            </footer>
            </main>

            {/* ─────────────────────────────────────────────────────────────
                6. ELEGANT LIGHT LOGIN MODAL
               ───────────────────────────────────────────────────────────── */}
            {isLoginModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-xs animate-fadeIn">
                    
                    {/* Modal Card */}
                    <div className="relative w-full max-w-md bg-white border border-[#e7e5df] rounded-[32px] p-7 sm:p-8 shadow-[0_16px_48px_rgba(0,0,0,0.1)] text-[#171717] overflow-hidden">
                        
                        {/* Close Button */}
                        <button
                            onClick={() => setIsLoginModalOpen(false)}
                            className="absolute top-6 right-6 w-8 h-8 rounded-full bg-[#f8f7f4] hover:bg-[#eae8e3] text-[#737373] hover:text-[#171717] flex items-center justify-center transition-colors cursor-pointer"
                        >
                            <X className="w-4 h-4" />
                        </button>

                        {/* Modal Header */}
                        <div className="text-center mb-6">
                            <div className="inline-flex items-center justify-center w-12 h-12 bg-[#f8f7f4] border border-[#e8e6e1] rounded-2xl mb-3 text-[#171717]">
                                {loginTab === 'tenant' ? <Building2 className="w-6 h-6" /> : <Shield className="w-6 h-6" />}
                            </div>
                            <h3 className="text-xl font-bold tracking-tight text-[#171717]">
                                {loginTab === 'tenant' ? 'Tenant Organization Portal' : 'SuperAdmin Control Panel'}
                            </h3>
                            <p className="text-xs text-[#737373] mt-1 font-normal">
                                {loginTab === 'tenant'
                                    ? 'Sign in to access your organization workspace'
                                    : 'Sign in to access global platform administration'}
                            </p>
                        </div>

                        {/* Minimal Tab Switcher inside Modal */}
                        <div className="flex bg-[#f8f7f4] p-1 rounded-full border border-[#e8e6e1] mb-5">
                            <button
                                type="button"
                                onClick={() => { setLoginTab('tenant'); setError(''); }}
                                className={`flex-1 py-1.5 rounded-full text-xs font-semibold transition-all cursor-pointer ${
                                    loginTab === 'tenant'
                                        ? 'bg-[#18181b] text-white shadow-xs'
                                        : 'text-[#737373] hover:text-[#171717]'
                                }`}
                            >
                                Tenant Portal
                            </button>
                            <button
                                type="button"
                                onClick={() => { setLoginTab('superadmin'); setError(''); }}
                                className={`flex-1 py-1.5 rounded-full text-xs font-semibold transition-all cursor-pointer ${
                                    loginTab === 'superadmin'
                                        ? 'bg-[#18181b] text-white shadow-xs'
                                        : 'text-[#737373] hover:text-[#171717]'
                                }`}
                            >
                                SuperAdmin
                            </button>
                        </div>

                        {/* Error Message */}
                        {error && (
                            <div className="mb-4 p-3 bg-rose-50 border border-rose-200 rounded-2xl text-xs text-rose-700 font-medium">
                                {error}
                            </div>
                        )}

                        {/* TENANT LOGIN FORM */}
                        {loginTab === 'tenant' && (
                            <form onSubmit={handleTenantSubmit} className="space-y-4">
                                <div>
                                    <label className="block text-xs font-bold text-[#404040] mb-1">Organization Slug</label>
                                    <div className="relative">
                                        <Tag className="w-3.5 h-3.5 text-[#a3a3a3] absolute left-3.5 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="text"
                                            value={tenantSlug}
                                            onChange={(e) => setTenantSlug(e.target.value)}
                                            required
                                            placeholder="your-organization"
                                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-xl pl-9 pr-3.5 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-[#404040] mb-1">Email Address</label>
                                    <div className="relative">
                                        <Mail className="w-3.5 h-3.5 text-[#a3a3a3] absolute left-3.5 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="email"
                                            value={tenantEmail}
                                            onChange={(e) => setTenantEmail(e.target.value)}
                                            required
                                            placeholder="admin@organization.com"
                                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-xl pl-9 pr-3.5 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-[#404040] mb-1">Password</label>
                                    <div className="relative">
                                        <Lock className="w-3.5 h-3.5 text-[#a3a3a3] absolute left-3.5 top-1/2 -translate-y-1/2" />
                                        <input
                                            type={showPw ? 'text' : 'password'}
                                            value={tenantPassword}
                                            onChange={(e) => setTenantPassword(e.target.value)}
                                            required
                                            placeholder="••••••••"
                                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-xl pl-9 pr-9 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                                        />
                                        <button
                                            type="button"
                                            onClick={() => setShowPw(!showPw)}
                                            className="absolute right-3.5 top-1/2 -translate-y-1/2 text-[#a3a3a3] hover:text-[#171717] cursor-pointer"
                                        >
                                            {showPw ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                                        </button>
                                    </div>
                                </div>

                                <div className="flex items-center gap-2 pt-0.5">
                                    <input
                                        id="rememberTenant"
                                        type="checkbox"
                                        checked={rememberMe}
                                        onChange={(e) => setRememberMe(e.target.checked)}
                                        className="w-3.5 h-3.5 rounded border-[#d4d2cb] bg-white text-[#18181b] focus:ring-[#18181b] cursor-pointer"
                                    />
                                    <label htmlFor="rememberTenant" className="text-xs text-[#737373] cursor-pointer select-none font-medium">
                                        Remember organization & email
                                    </label>
                                </div>

                                <button
                                    type="submit"
                                    disabled={isLoading}
                                    className="w-full bg-[#18181b] hover:bg-[#27272a] text-white py-3 rounded-full text-xs font-semibold shadow-xs transition-all active:scale-[0.99] flex items-center justify-center gap-2 disabled:opacity-50 cursor-pointer"
                                >
                                    {isLoading ? (
                                        <>
                                            <Loader2 className="w-3.5 h-3.5 animate-spin" />
                                            <span>Authenticating...</span>
                                        </>
                                    ) : (
                                        <>
                                            <span>Sign In to Tenant Portal</span>
                                            <ArrowUpRight className="w-3.5 h-3.5" />
                                        </>
                                    )}
                                </button>
                            </form>
                        )}

                        {/* SUPERADMIN LOGIN FORM */}
                        {loginTab === 'superadmin' && (
                            <form onSubmit={handleSuperAdminSubmit} className="space-y-4">
                                <div>
                                    <label className="block text-xs font-bold text-[#404040] mb-1">SuperAdmin Email</label>
                                    <div className="relative">
                                        <Mail className="w-3.5 h-3.5 text-[#a3a3a3] absolute left-3.5 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="email"
                                            value={adminEmail}
                                            onChange={(e) => setAdminEmail(e.target.value)}
                                            required
                                            placeholder="superadmin@alphasurveilance.com"
                                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-xl pl-9 pr-3.5 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-[#404040] mb-1">Password</label>
                                    <div className="relative">
                                        <Lock className="w-3.5 h-3.5 text-[#a3a3a3] absolute left-3.5 top-1/2 -translate-y-1/2" />
                                        <input
                                            type={showPw ? 'text' : 'password'}
                                            value={adminPassword}
                                            onChange={(e) => setAdminPassword(e.target.value)}
                                            required
                                            placeholder="••••••••"
                                            className="w-full bg-[#f8f7f4] border border-[#e8e6e1] rounded-xl pl-9 pr-9 py-2.5 text-xs text-[#171717] placeholder:text-[#a3a3a3] focus:outline-none focus:border-[#18181b] transition-colors font-medium"
                                        />
                                        <button
                                            type="button"
                                            onClick={() => setShowPw(!showPw)}
                                            className="absolute right-3.5 top-1/2 -translate-y-1/2 text-[#a3a3a3] hover:text-[#171717] cursor-pointer"
                                        >
                                            {showPw ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                                        </button>
                                    </div>
                                </div>

                                <div className="flex items-center gap-2 pt-0.5">
                                    <input
                                        id="rememberAdmin"
                                        type="checkbox"
                                        checked={rememberMe}
                                        onChange={(e) => setRememberMe(e.target.checked)}
                                        className="w-3.5 h-3.5 rounded border-[#d4d2cb] bg-white text-[#18181b] focus:ring-[#18181b] cursor-pointer"
                                    />
                                    <label htmlFor="rememberAdmin" className="text-xs text-[#737373] cursor-pointer select-none font-medium">
                                        Remember credentials
                                    </label>
                                </div>

                                <button
                                    type="submit"
                                    disabled={isLoading}
                                    className="w-full bg-[#18181b] hover:bg-[#27272a] text-white py-3 rounded-full text-xs font-semibold shadow-xs transition-all active:scale-[0.99] flex items-center justify-center gap-2 disabled:opacity-50 cursor-pointer"
                                >
                                    {isLoading ? (
                                        <>
                                            <Loader2 className="w-3.5 h-3.5 animate-spin" />
                                            <span>Verifying credentials...</span>
                                        </>
                                    ) : (
                                        <>
                                            <span>Access SuperAdmin Control</span>
                                            <ArrowUpRight className="w-3.5 h-3.5" />
                                        </>
                                    )}
                                </button>
                            </form>
                        )}

                    </div>
                </div>
            )}

        </div>
    );
}
