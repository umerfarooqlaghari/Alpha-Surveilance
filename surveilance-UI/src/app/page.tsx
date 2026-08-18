'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import Image from 'next/image';
import { 
    Shield, 
    Building2, 
    Mail, 
    Lock, 
    Tag, 
    Loader2, 
    Eye, 
    EyeOff, 
    ArrowRight,
    Phone,
    X,
    ExternalLink
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
                    const { slug, em, pw } = JSON.parse(saved);
                    setTenantSlug(slug || '');
                    setTenantEmail(em || '');
                    setTenantPassword(pw || '');
                    setRememberMe(true);
                }
            } else {
                const saved = localStorage.getItem(LS_SUPER_KEY);
                if (saved) {
                    const { em, pw } = JSON.parse(saved);
                    setAdminEmail(em || '');
                    setAdminPassword(pw || '');
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
            localStorage.setItem(LS_TENANT_KEY, JSON.stringify({ slug: tenantSlug, em: tenantEmail, pw: tenantPassword }));
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
            localStorage.setItem(LS_SUPER_KEY, JSON.stringify({ em: adminEmail, pw: adminPassword }));
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

    return (
        <div className="min-h-screen bg-[#020408] text-slate-200 font-sans selection:bg-purple-900 selection:text-white relative overflow-x-hidden">
            
            {/* Subdued Dark Ambient Glows */}
            <div className="absolute top-0 right-0 w-[500px] h-[500px] bg-purple-950/20 rounded-full blur-[160px] pointer-events-none" />
            <div className="absolute top-[50%] left-[-120px] w-[400px] h-[400px] bg-slate-900/30 rounded-full blur-[160px] pointer-events-none" />

            {/* ─────────────────────────────────────────────────────────────
                1. HEADER / NAVIGATION
               ───────────────────────────────────────────────────────────── */}
            <header className="fixed top-0 left-0 right-0 z-40 bg-[#020408]/90 backdrop-blur-md border-b border-slate-800/60">
                <div className="max-w-[1280px] mx-auto px-6 h-20 flex items-center justify-between">
                    
                    {/* Brand Logo */}
                    <div className="flex items-center gap-3">
                        <div className="w-9 h-9 rounded-xl bg-purple-950/80 border border-purple-800/40 p-0.5 shadow-md">
                            <div className="w-full h-full bg-[#020408] rounded-[10px] flex items-center justify-center">
                                <Shield className="w-4 h-4 text-purple-300/80" />
                            </div>
                        </div>
                        <span className="text-lg font-extrabold tracking-tight text-white flex items-center gap-1">
                            ALPHA <span className="text-purple-300/90">SURVEILLANCE</span>
                        </span>
                    </div>

                    {/* Nav Links */}
                    <nav className="hidden md:flex items-center gap-8 text-xs font-semibold text-slate-400">
                        <a href="#features" className="hover:text-purple-300 transition-colors">AI Vision</a>
                        <a href="#contact" className="hover:text-purple-300 transition-colors">Contact Us</a>
                        <a href="https://www.alpha-devs.cloud" target="_blank" rel="noopener noreferrer" className="hover:text-purple-300 transition-colors flex items-center gap-1">
                            Alpha Devs <ExternalLink className="w-3 h-3" />
                        </a>
                    </nav>

                    {/* Header CTA Button: Tenant Login (Dulled Dark Plum) */}
                    <div className="flex items-center gap-4">
                        <button
                            onClick={() => openModal('tenant')}
                            className="bg-purple-950/80 hover:bg-purple-900/90 text-purple-200 border border-purple-800/50 font-bold text-xs px-5 py-2 rounded-full shadow-md transition-all active:scale-95 flex items-center gap-2"
                        >
                            Tenant Login <ArrowRight className="w-3.5 h-3.5" />
                        </button>
                    </div>

                </div>
            </header>

            {/* ─────────────────────────────────────────────────────────────
                2. HERO SECTION (Subdued & Streamlined)
               ───────────────────────────────────────────────────────────── */}
            <section className="pt-36 pb-16 px-6 max-w-[1280px] mx-auto relative z-10">
                <div className="grid lg:grid-cols-12 gap-12 items-center">
                    
                    {/* Left Hero Content */}
                    <div className="lg:col-span-7 space-y-7">
                        
                        {/* Floating Pill Badge */}
                        <div className="inline-flex items-center gap-2.5 px-3.5 py-1 rounded-full bg-slate-900/80 border border-purple-900/40 text-xs font-semibold text-purple-300/80">
                            <span className="px-2 py-0.5 rounded-full bg-purple-950 text-purple-300 border border-purple-800/40 font-bold text-[10px] uppercase tracking-wider">
                                Platform Security
                            </span>
                            Next-Gen Autonomous AI Vision &rarr;
                        </div>

                        {/* Display Headline */}
                        <h1 className="text-4xl sm:text-5xl font-black tracking-tight leading-[1.12] text-white">
                            Defense Against <br />
                            <span className="bg-gradient-to-r from-slate-200 via-purple-300 to-slate-400 bg-clip-text text-transparent">
                                Digital Threats
                            </span>
                        </h1>

                        <p className="text-slate-400 text-sm sm:text-base max-w-lg leading-relaxed font-medium">
                            Protect your environments with reliable AI-powered computer vision and multi-tenant video analytics. Built for high-security facilities.
                        </p>

                        {/* Interactive Search / Prompt Input Box */}
                        <div className="bg-slate-900/60 border border-slate-800/80 rounded-2xl p-3 shadow-lg max-w-lg backdrop-blur-sm hover:border-purple-900/50 transition-all">
                            <label className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-1.5 px-1">
                                Ask AI Detector about RTSP violation rules...
                            </label>
                            <div className="flex items-center gap-2.5 bg-[#020408] border border-slate-800/80 rounded-xl px-3.5 py-2">
                                <input
                                    type="text"
                                    placeholder="Enter rule topic..."
                                    value={searchPrompt}
                                    onChange={(e) => setSearchPrompt(e.target.value)}
                                    className="bg-transparent text-xs text-slate-200 placeholder:text-slate-600 focus:outline-none flex-1 font-medium"
                                />
                                <button
                                    onClick={() => openModal('tenant')}
                                    className="w-7 h-7 rounded-lg bg-purple-950 hover:bg-purple-900 text-purple-200 border border-purple-800/50 flex items-center justify-center font-bold text-xs transition-all active:scale-95"
                                >
                                    ↑
                                </button>
                            </div>
                        </div>

                    </div>

                    {/* Right Hero Graphic Illustration (Muted Matte Violet Shield) */}
                    <div className="lg:col-span-5 relative flex justify-center">
                        <div className="relative w-full max-w-md rounded-2xl overflow-hidden border border-purple-950/60 bg-slate-950/40 p-2 shadow-xl group hover:scale-[1.01] transition-transform duration-500">
                            <Image
                                src="/muted_dark_violet_cyber_security.png"
                                alt="Alpha Surveillance Muted Matte Security Shield"
                                width={500}
                                height={500}
                                className="w-full h-auto rounded-xl object-cover"
                                priority
                            />
                            <div className="absolute inset-0 bg-gradient-to-t from-[#020408] via-transparent to-transparent pointer-events-none" />
                        </div>
                    </div>

                </div>
            </section>

            {/* ─────────────────────────────────────────────────────────────
                3. CONTACT US CARD SECTION (Subdued Matte Theme)
               ───────────────────────────────────────────────────────────── */}
            <section id="contact" className="py-16 px-6 max-w-[1280px] mx-auto border-t border-slate-800/60 relative z-10">
                <div className="bg-slate-900/50 border border-slate-800 hover:border-purple-900/50 rounded-3xl p-8 sm:p-10 backdrop-blur-sm relative overflow-hidden transition-all">
                    
                    <div className="grid md:grid-cols-12 gap-8 items-center">
                        <div className="md:col-span-7 space-y-3">
                            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-purple-950/60 border border-purple-800/30 text-purple-300/80 text-[11px] font-bold uppercase tracking-wider">
                                <Mail className="w-3.5 h-3.5 text-purple-400" /> Direct Contact
                            </div>
                            <h2 className="text-2xl sm:text-3xl font-black text-white tracking-tight">
                                Get In Touch With <span className="text-purple-300/90">Alpha Devs</span>
                            </h2>
                            <p className="text-slate-400 text-xs sm:text-sm leading-relaxed font-medium max-w-xl">
                                Reach out to our engineering team for enterprise onboarding, custom AI rule deployment, or dedicated platform assistance.
                            </p>
                        </div>

                        {/* Contact Info Cards */}
                        <div className="md:col-span-5 space-y-2.5">
                            {/* Email Card */}
                            <a 
                                href="mailto:info@alpha-devs.cloud"
                                className="flex items-center gap-3.5 bg-[#020408] border border-slate-800/90 hover:border-purple-800/60 rounded-2xl p-3.5 transition-all group"
                            >
                                <div className="w-9 h-9 rounded-xl bg-purple-950/60 border border-purple-800/40 flex items-center justify-center text-purple-300 group-hover:scale-105 transition-transform">
                                    <Mail className="w-4 h-4" />
                                </div>
                                <div>
                                    <span className="text-[10px] font-bold text-slate-500 block uppercase tracking-wider">Email Us</span>
                                    <span className="text-xs font-semibold text-slate-200 group-hover:text-purple-300 transition-colors">info@alpha-devs.cloud</span>
                                </div>
                            </a>

                            {/* Phone Card */}
                            <a 
                                href="tel:+923009243063"
                                className="flex items-center gap-3.5 bg-[#020408] border border-slate-800/90 hover:border-purple-800/60 rounded-2xl p-3.5 transition-all group"
                            >
                                <div className="w-9 h-9 rounded-xl bg-purple-950/60 border border-purple-800/40 flex items-center justify-center text-purple-300 group-hover:scale-105 transition-transform">
                                    <Phone className="w-4 h-4" />
                                </div>
                                <div>
                                    <span className="text-[10px] font-bold text-slate-500 block uppercase tracking-wider">Phone / WhatsApp</span>
                                    <span className="text-xs font-semibold text-slate-200 group-hover:text-purple-300 transition-colors">+923009243063</span>
                                </div>
                            </a>
                        </div>
                    </div>

                </div>
            </section>

            {/* ─────────────────────────────────────────────────────────────
                4. FOOTER WITH DEVELOPED BY LINK TO ALPHA-DEVS.CLOUD
               ───────────────────────────────────────────────────────────── */}
            <footer className="bg-[#020408] border-t border-slate-800/80 py-8 px-6 relative z-10">
                <div className="max-w-[1280px] mx-auto flex flex-col sm:flex-row items-center justify-between gap-4 text-slate-500 text-xs font-medium">
                    <div className="flex items-center gap-2">
                        <Shield className="w-3.5 h-3.5 text-purple-400/70" />
                        <span>&copy; {new Date().getFullYear()} Alpha Surveillance Systems. All rights reserved.</span>
                    </div>
                    <div className="text-slate-400 font-semibold flex items-center gap-1.5">
                        <span>Developed by</span>
                        <a 
                            href="https://www.alpha-devs.cloud" 
                            target="_blank" 
                            rel="noopener noreferrer" 
                            className="text-purple-300/90 hover:text-purple-200 font-bold underline decoration-purple-800/50 underline-offset-4 transition-colors flex items-center gap-1"
                        >
                            Alpha Devs <ExternalLink className="w-3 h-3" />
                        </a>
                    </div>
                </div>
            </footer>

            {/* ─────────────────────────────────────────────────────────────
                5. INTERACTIVE LOGIN MODAL (Tenant & SuperAdmin Switch)
               ───────────────────────────────────────────────────────────── */}
            {isLoginModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/85 backdrop-blur-sm animate-fadeIn">
                    
                    {/* Modal Content Window */}
                    <div className="relative w-full max-w-md bg-slate-900 border border-slate-800 rounded-3xl p-7 shadow-2xl text-white overflow-hidden">
                        
                        {/* Close Button */}
                        <button
                            onClick={() => setIsLoginModalOpen(false)}
                            className="absolute top-5 right-5 w-7 h-7 rounded-full bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-white flex items-center justify-center transition-colors"
                        >
                            <X className="w-3.5 h-3.5" />
                        </button>

                        {/* Modal Header Badge */}
                        <div className="text-center mb-5">
                            <div className="inline-flex items-center justify-center w-12 h-12 bg-purple-950/80 border border-purple-800/40 rounded-2xl mb-2.5 text-purple-300">
                                {loginTab === 'tenant' ? <Building2 className="w-6 h-6" /> : <Shield className="w-6 h-6" />}
                            </div>
                            <h3 className="text-xl font-extrabold text-white tracking-tight">
                                {loginTab === 'tenant' ? 'Tenant Admin Portal' : 'SuperAdmin Control Panel'}
                            </h3>
                            <p className="text-xs text-slate-400 mt-0.5 font-medium">
                                {loginTab === 'tenant'
                                    ? 'Sign in to access your organization workspace'
                                    : 'Sign in to access platform-wide administration'}
                            </p>
                        </div>

                        {/* Tab Switcher inside Modal */}
                        <div className="flex bg-[#020408] p-1 rounded-2xl border border-slate-800 mb-5">
                            <button
                                type="button"
                                onClick={() => { setLoginTab('tenant'); setError(''); }}
                                className={`flex-1 py-1.5 rounded-xl text-xs font-bold transition-all ${
                                    loginTab === 'tenant'
                                        ? 'bg-purple-950/90 text-purple-200 border border-purple-800/40 shadow-sm'
                                        : 'text-slate-400 hover:text-white'
                                }`}
                            >
                                Tenant Login
                            </button>
                            <button
                                type="button"
                                onClick={() => { setLoginTab('superadmin'); setError(''); }}
                                className={`flex-1 py-1.5 rounded-xl text-xs font-bold transition-all ${
                                    loginTab === 'superadmin'
                                        ? 'bg-purple-950/90 text-purple-200 border border-purple-800/40 shadow-sm'
                                        : 'text-slate-400 hover:text-white'
                                }`}
                            >
                                SuperAdmin Access
                            </button>
                        </div>

                        {/* Error Message */}
                        {error && (
                            <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300 font-medium">
                                {error}
                            </div>
                        )}

                        {/* TENANT LOGIN FORM */}
                        {loginTab === 'tenant' && (
                            <form onSubmit={handleTenantSubmit} className="space-y-3.5">
                                <div>
                                    <label className="block text-xs font-bold text-slate-400 mb-1">Organization Slug</label>
                                    <div className="relative">
                                        <Tag className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="text"
                                            value={tenantSlug}
                                            onChange={(e) => setTenantSlug(e.target.value)}
                                            required
                                            placeholder="your-organization"
                                            className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-3 py-2 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-slate-400 mb-1">Email Address</label>
                                    <div className="relative">
                                        <Mail className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="email"
                                            value={tenantEmail}
                                            onChange={(e) => setTenantEmail(e.target.value)}
                                            required
                                            placeholder="admin@organization.com"
                                            className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-3 py-2 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-slate-400 mb-1">Password</label>
                                    <div className="relative">
                                        <Lock className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                                        <input
                                            type={showPw ? 'text' : 'password'}
                                            value={tenantPassword}
                                            onChange={(e) => setTenantPassword(e.target.value)}
                                            required
                                            placeholder="••••••••"
                                            className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-9 py-2 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                        />
                                        <button
                                            type="button"
                                            onClick={() => setShowPw(!showPw)}
                                            className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-500 hover:text-slate-300"
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
                                        className="w-3.5 h-3.5 rounded border-slate-700 bg-[#020408] text-purple-500 focus:ring-purple-800 cursor-pointer"
                                    />
                                    <label htmlFor="rememberTenant" className="text-xs text-slate-400 cursor-pointer select-none">
                                        Remember credentials
                                    </label>
                                </div>

                                <button
                                    type="submit"
                                    disabled={isLoading}
                                    className="w-full bg-purple-950/80 hover:bg-purple-900/90 border border-purple-800/50 text-purple-200 font-bold py-2.5 rounded-xl text-xs transition-colors shadow-md disabled:opacity-50 flex items-center justify-center gap-2"
                                >
                                    {isLoading ? <><Loader2 className="w-3.5 h-3.5 animate-spin" /> Signing In...</> : 'Sign In to Tenant Portal'}
                                </button>

                                <div className="pt-2 text-center">
                                    <button
                                        type="button"
                                        onClick={() => { setLoginTab('superadmin'); setError(''); }}
                                        className="text-xs text-slate-400 hover:text-purple-300 transition-colors underline decoration-slate-800 underline-offset-4"
                                    >
                                        Are you a SuperAdmin? Switch to SuperAdmin Portal
                                    </button>
                                </div>
                            </form>
                        )}

                        {/* SUPERADMIN LOGIN FORM */}
                        {loginTab === 'superadmin' && (
                            <form onSubmit={handleSuperAdminSubmit} className="space-y-3.5">
                                <div>
                                    <label className="block text-xs font-bold text-slate-400 mb-1">SuperAdmin Email</label>
                                    <div className="relative">
                                        <Mail className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                                        <input
                                            type="email"
                                            value={adminEmail}
                                            onChange={(e) => setAdminEmail(e.target.value)}
                                            required
                                            placeholder="superadmin@system.local"
                                            className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-3 py-2 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                        />
                                    </div>
                                </div>

                                <div>
                                    <label className="block text-xs font-bold text-slate-400 mb-1">Master Password</label>
                                    <div className="relative">
                                        <Lock className="w-3.5 h-3.5 text-slate-500 absolute left-3 top-1/2 -translate-y-1/2" />
                                        <input
                                            type={showPw ? 'text' : 'password'}
                                            value={adminPassword}
                                            onChange={(e) => setAdminPassword(e.target.value)}
                                            required
                                            placeholder="••••••••"
                                            className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-9 py-2 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                        />
                                        <button
                                            type="button"
                                            onClick={() => setShowPw(!showPw)}
                                            className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-500 hover:text-slate-300"
                                        >
                                            {showPw ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                                        </button>
                                    </div>
                                </div>

                                <div className="flex items-center gap-2 pt-0.5">
                                    <input
                                        id="rememberSuper"
                                        type="checkbox"
                                        checked={rememberMe}
                                        onChange={(e) => setRememberMe(e.target.checked)}
                                        className="w-3.5 h-3.5 rounded border-slate-700 bg-[#020408] text-purple-500 focus:ring-purple-800 cursor-pointer"
                                    />
                                    <label htmlFor="rememberSuper" className="text-xs text-slate-400 cursor-pointer select-none">
                                        Remember master session
                                    </label>
                                </div>

                                <button
                                    type="submit"
                                    disabled={isLoading}
                                    className="w-full bg-purple-950/80 hover:bg-purple-900/90 border border-purple-800/50 text-purple-200 font-bold py-2.5 rounded-xl text-xs transition-colors shadow-md disabled:opacity-50 flex items-center justify-center gap-2"
                                >
                                    {isLoading ? <><Loader2 className="w-3.5 h-3.5 animate-spin" /> Authenticating...</> : 'Sign In as SuperAdmin'}
                                </button>

                                <div className="pt-2 text-center">
                                    <button
                                        type="button"
                                        onClick={() => { setLoginTab('tenant'); setError(''); }}
                                        className="text-xs text-slate-400 hover:text-purple-300 transition-colors underline decoration-slate-800 underline-offset-4"
                                    >
                                        Tenant Admin? Switch to Tenant Login
                                    </button>
                                </div>
                            </form>
                        )}

                    </div>
                </div>
            )}

        </div>
    );
}
