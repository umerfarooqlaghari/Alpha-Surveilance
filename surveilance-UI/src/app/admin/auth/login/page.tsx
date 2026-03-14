'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import { Shield, Mail, Lock, Loader2, Eye, EyeOff } from 'lucide-react';
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
                const { em, pw } = JSON.parse(saved);
                setEmail(em || '');
                setPassword(pw || '');
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
            localStorage.setItem(LS_KEY, JSON.stringify({ em: email, pw: password }));
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
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-blue-50 to-indigo-100">
            <div className="max-w-md w-full mx-4">
                <div className="bg-white rounded-2xl shadow-xl p-8">
                    {/* Header */}
                    <div className="text-center mb-8">
                        <div className="inline-flex items-center justify-center w-16 h-16 bg-blue-100 rounded-full mb-4">
                            <Shield className="w-8 h-8 text-blue-600" />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900">SuperAdmin Login</h1>
                        <p className="text-gray-600 mt-2">Access the administration panel</p>
                    </div>

                    {/* Error */}
                    {error && (
                        <div className="mb-6 p-4 bg-red-50 border border-red-200 rounded-lg">
                            <p className="text-sm text-red-600">{error}</p>
                        </div>
                    )}

                    <form onSubmit={handleSubmit} className="space-y-5">
                        {/* Email */}
                        <div>
                            <label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-2">
                                Email Address
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-black" />
                                <input id="email" type="email" value={email}
                                    onChange={e => setEmail(e.target.value)} required
                                    className="w-full pl-10 pr-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900"
                                    placeholder="admin@example.com" />
                            </div>
                        </div>

                        {/* Password */}
                        <div>
                            <label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-2">
                                Password
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-black" />
                                <input id="password" type={showPw ? 'text' : 'password'} value={password}
                                    onChange={e => setPassword(e.target.value)} required
                                    className="w-full pl-10 pr-11 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent text-gray-900"
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
                                className="w-4 h-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500 cursor-pointer" />
                            <label htmlFor="rememberMe" className="text-sm text-gray-600 cursor-pointer select-none">
                                Remember me on this device
                            </label>
                        </div>

                        <button type="submit" disabled={isLoading}
                            className="w-full bg-blue-600 text-white py-3 rounded-lg font-medium hover:bg-blue-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2">
                            {isLoading ? <><Loader2 className="w-5 h-5 animate-spin" />Signing in...</> : 'Sign In'}
                        </button>
                    </form>

                    <div className="mt-6 text-center">
                        <p className="text-sm text-gray-600">
                            Tenant Administrator?{' '}
                            <Link href="/tenant/auth/login" className="text-blue-600 hover:text-blue-700 font-medium">
                                Login here
                            </Link>
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
}
