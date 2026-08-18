'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import { Shield, Mail, Lock, Loader2, Eye, EyeOff, ArrowLeft } from 'lucide-react';
import Link from 'next/link';

const LS_KEY = 'superadmin_remember';

export default function SuperAdminLogin() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [rememberMe, setRememberMe] = useState(false);
    const [showPw, setShowPw] = useState(false);
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const { loginSuperAdmin, isAuthenticated, role, isLoading: authLoading } = useAuth();
    const router = useRouter();

    // Load saved credentials on mount
    useEffect(() => {
        try {
            const saved = localStorage.getItem(LS_KEY);
            if (saved) {
                const { em } = JSON.parse(saved);
                setEmail(em || '');
                setRememberMe(true);
            }
        } catch { /* ignore */ }
    }, []);

    useEffect(() => {
        if (!authLoading && isAuthenticated && role === 'SuperAdmin') {
            router.push('/admin');
        }
    }, [isAuthenticated, role, authLoading, router]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        if (rememberMe) {
            localStorage.setItem(LS_KEY, JSON.stringify({ em: email }));
        } else {
            localStorage.removeItem(LS_KEY);
        }

        try {
            await loginSuperAdmin(email, password);
        } catch (err: any) {
            setError(err.message || 'Login failed. Please check your credentials.');
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center bg-[#020408] text-white p-4 relative overflow-hidden">
            {/* Background Subdued Ambient Glow */}
            <div className="absolute top-0 right-0 w-[500px] h-[500px] bg-purple-950/20 rounded-full blur-[160px] pointer-events-none" />
            <div className="absolute bottom-0 left-0 w-[400px] h-[400px] bg-slate-900/40 rounded-full blur-[160px] pointer-events-none" />

            <div className="max-w-md w-full relative z-10">
                {/* Back to Home Link */}
                <div className="mb-6">
                    <Link href="/" className="inline-flex items-center gap-2 text-xs font-semibold text-slate-400 hover:text-purple-300 transition-colors">
                        <ArrowLeft className="w-4 h-4" /> Back to Alpha Surveillance
                    </Link>
                </div>

                <div className="bg-slate-900/90 border border-slate-800 rounded-3xl p-8 shadow-2xl backdrop-blur-md">
                    {/* Header */}
                    <div className="text-center mb-8">
                        <div className="inline-flex items-center justify-center w-14 h-14 bg-purple-950/80 border border-purple-800/40 rounded-2xl mb-3 text-purple-300">
                            <Shield className="w-7 h-7" />
                        </div>
                        <h1 className="text-2xl font-black text-white tracking-tight">SuperAdmin Login</h1>
                        <p className="text-xs text-slate-400 mt-1 font-medium">Access system administration panel</p>
                    </div>

                    {/* Error */}
                    {error && (
                        <div className="mb-6 p-3.5 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-300 font-medium">
                            {error}
                        </div>
                    )}

                    <form onSubmit={handleSubmit} className="space-y-4">
                        {/* Email */}
                        <div>
                            <label htmlFor="email" className="block text-xs font-bold text-slate-400 mb-1">
                                Email Address
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input id="email" type="email" value={email}
                                    onChange={e => setEmail(e.target.value)} required
                                    className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-3 py-2.5 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                    placeholder="superadmin@system.local" />
                            </div>
                        </div>

                        {/* Password */}
                        <div>
                            <label htmlFor="password" className="block text-xs font-bold text-slate-400 mb-1">
                                Password
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input id="password" type={showPw ? 'text' : 'password'} value={password}
                                    onChange={e => setPassword(e.target.value)} required
                                    className="w-full bg-[#020408] border border-slate-800 rounded-xl pl-9 pr-9 py-2.5 text-xs text-white placeholder:text-slate-600 focus:outline-none focus:border-purple-800 transition-colors"
                                    placeholder="••••••••" />
                                <button type="button" tabIndex={-1} onClick={() => setShowPw(p => !p)}
                                    className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-500 hover:text-slate-300 transition-colors">
                                    {showPw ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                                </button>
                            </div>
                        </div>

                        {/* Remember Me */}
                        <div className="flex items-center gap-2 pt-1">
                            <input id="rememberMe" type="checkbox" checked={rememberMe}
                                onChange={e => setRememberMe(e.target.checked)}
                                className="w-3.5 h-3.5 rounded border-slate-700 bg-[#020408] text-purple-500 focus:ring-purple-800 cursor-pointer" />
                            <label htmlFor="rememberMe" className="text-xs text-slate-400 cursor-pointer select-none">
                                Remember email
                            </label>
                        </div>

                        <button type="submit" disabled={isLoading}
                            className="w-full bg-purple-950/80 hover:bg-purple-900/90 border border-purple-800/50 text-purple-200 py-3 rounded-xl text-xs font-bold transition-colors shadow-md disabled:opacity-50 flex items-center justify-center gap-2">
                            {isLoading ? <><Loader2 className="w-4 h-4 animate-spin" />Signing in...</> : 'Sign In'}
                        </button>
                    </form>

                    <div className="mt-6 text-center">
                        <p className="text-xs text-slate-400">
                            Tenant Administrator?{' '}
                            <Link href="/tenant/auth/login" className="text-purple-300/90 hover:text-purple-200 font-bold underline decoration-slate-800 underline-offset-4">
                                Tenant Login
                            </Link>
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
}
