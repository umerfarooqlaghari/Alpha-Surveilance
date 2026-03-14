'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import { Building2, Mail, Lock, Tag, Loader2, Eye, EyeOff } from 'lucide-react';
import Link from 'next/link';

const LS_KEY = 'tenant_remember';

export default function TenantAdminLogin() {
    const [tenantSlug, setTenantSlug] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [rememberMe, setRememberMe] = useState(false);
    const [showPw, setShowPw] = useState(false);
    const [error, setError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const { loginTenantAdmin, isAuthenticated, role, isLoading: authLoading } = useAuth();
    const router = useRouter();

    // Load saved credentials on mount
    useEffect(() => {
        try {
            const saved = localStorage.getItem(LS_KEY);
            if (saved) {
                const { slug, em, pw } = JSON.parse(saved);
                setTenantSlug(slug || '');
                setEmail(em || '');
                setPassword(pw || '');
                setRememberMe(true);
            }
        } catch { /* ignore */ }
    }, []);

    useEffect(() => {
        if (!authLoading && isAuthenticated && role === 'TenantAdmin') {
            router.push('/tenant/analytics');
        }
    }, [isAuthenticated, role, authLoading, router]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        if (rememberMe) {
            localStorage.setItem(LS_KEY, JSON.stringify({ slug: tenantSlug, em: email, pw: password }));
        } else {
            localStorage.removeItem(LS_KEY);
        }

        try {
            await loginTenantAdmin(email, password, tenantSlug);
        } catch (err: any) {
            setError(err.message || 'Login failed. Please check your credentials.');
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-purple-50 to-pink-100">
            <div className="max-w-md w-full mx-4">
                <div className="bg-white rounded-2xl shadow-xl p-8">
                    {/* Header */}
                    <div className="text-center mb-8">
                        <div className="inline-flex items-center justify-center w-16 h-16 bg-purple-100 rounded-full mb-4">
                            <Building2 className="w-8 h-8 text-purple-600" />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900">Tenant Admin Login</h1>
                        <p className="text-gray-600 mt-2">Access your organization's dashboard</p>
                    </div>

                    {/* Error */}
                    {error && (
                        <div className="mb-6 p-4 bg-red-50 border border-red-200 rounded-lg">
                            <p className="text-sm text-red-600">{error}</p>
                        </div>
                    )}

                    <form onSubmit={handleSubmit} className="space-y-5">
                        {/* Organization Slug */}
                        <div>
                            <label htmlFor="tenantSlug" className="block text-sm font-medium text-gray-700 mb-2">
                                Organization Slug
                            </label>
                            <div className="relative">
                                <Tag className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
                                <input id="tenantSlug" type="text" value={tenantSlug}
                                    onChange={e => setTenantSlug(e.target.value)} required
                                    className="w-full pl-10 pr-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-purple-500 focus:border-transparent text-black"
                                    placeholder="your-organization" />
                            </div>
                        </div>

                        {/* Email */}
                        <div>
                            <label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-2">
                                Email Address
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
                                <input id="email" type="email" value={email}
                                    onChange={e => setEmail(e.target.value)} required
                                    className="w-full pl-10 pr-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-purple-500 focus:border-transparent text-black"
                                    placeholder="admin@example.com" />
                            </div>
                        </div>

                        {/* Password */}
                        <div>
                            <label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-2">
                                Password
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
                                <input id="password" type={showPw ? 'text' : 'password'} value={password}
                                    onChange={e => setPassword(e.target.value)} required
                                    className="w-full pl-10 pr-11 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-purple-500 focus:border-transparent text-black"
                                    placeholder="••••••••" />
                                <button type="button" tabIndex={-1} onClick={() => setShowPw(p => !p)}
                                    className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 transition-colors">
                                    {showPw ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                                </button>
                            </div>
                        </div>

                        {/* Remember Me */}
                        <div className="flex items-center gap-2.5">
                            <input id="rememberMe" type="checkbox" checked={rememberMe}
                                onChange={e => setRememberMe(e.target.checked)}
                                className="w-4 h-4 rounded border-gray-300 text-purple-600 focus:ring-purple-500 cursor-pointer" />
                            <label htmlFor="rememberMe" className="text-sm text-gray-600 cursor-pointer select-none">
                                Remember me on this device
                            </label>
                        </div>

                        <button type="submit" disabled={isLoading}
                            className="w-full bg-purple-600 text-white py-3 rounded-lg font-medium hover:bg-purple-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2">
                            {isLoading ? <><Loader2 className="w-5 h-5 animate-spin" />Signing in...</> : 'Sign In'}
                        </button>
                    </form>

                    <div className="mt-6 text-center">
                        <p className="text-sm text-gray-600">
                            System Administrator?{' '}
                            <Link href="/admin/auth/login" className="text-purple-600 hover:text-purple-700 font-medium">
                                Login here
                            </Link>
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
}
