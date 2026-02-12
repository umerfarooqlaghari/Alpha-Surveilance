'use client';

import Link from 'next/link';
import Image from 'next/image';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import { LayoutDashboard, Camera, FileText, LogOut, BarChart3 } from 'lucide-react';
import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function TenantLayout({ children }: { children: React.ReactNode }) {
    const pathname = usePathname();
    const { isAuthenticated, role, tenant, logout, isLoading } = useAuth();
    const router = useRouter();

    useEffect(() => {
        if (!isLoading && (!isAuthenticated || role !== 'TenantAdmin')) {
            if (pathname !== '/tenant/auth/login') {
                router.push('/tenant/auth/login');
            }
        }
    }, [isAuthenticated, role, isLoading, router, pathname]);

    // Bypass protection for login page
    if (pathname === '/tenant/auth/login') {
        return <>{children}</>;
    }

    if (isLoading || !isAuthenticated || role !== 'TenantAdmin') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <div className="text-gray-500">Loading...</div>
            </div>
        );
    }

    const navItems = [
        { href: '/tenant/dashboard', label: 'Dashboard', icon: LayoutDashboard },
        { href: '/tenant/analytics', label: 'Analytics', icon: BarChart3 },
        { href: '/tenant/cameras', label: 'Cameras', icon: Camera },
        { href: '/tenant/violations', label: 'Violations', icon: FileText },
    ];

    return (
        <div className="min-h-screen bg-gray-50">
            {/* Header */}
            <header className="bg-white border-b border-gray-200 sticky top-0 z-10">
                <div className="px-6 py-4 flex justify-between items-center">
                    <div>
                        <div className="flex items-center gap-3">
                            {tenant?.logoUrl && (
                                <div className="relative w-10 h-10 rounded-full overflow-hidden border border-gray-200">
                                    <Image
                                        src={tenant.logoUrl}
                                        alt={`${tenant.tenantName} logo`}
                                        fill
                                        className="object-cover"
                                    />
                                </div>
                            )}
                            <h1 className="text-2xl font-bold text-gray-900">
                                {tenant?.tenantName || 'Tenant Dashboard'}
                            </h1>
                        </div>
                    </div>
                    <button
                        onClick={logout}
                        className="flex items-center gap-2 px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <LogOut className="w-4 h-4" />
                        Logout
                    </button>
                </div>
            </header>

            <div className="flex">
                {/* Sidebar */}
                <aside className="w-64 bg-white border-r border-gray-200 min-h-[calc(100vh-73px)] sticky top-[73px]">
                    <nav className="p-4 space-y-1">
                        {navItems.map((item) => {
                            const Icon = item.icon;
                            const isActive = pathname === item.href;

                            return (
                                <Link
                                    key={item.href}
                                    href={item.href}
                                    className={`flex items-center gap-3 px-4 py-3 rounded-lg transition-colors ${isActive
                                        ? 'bg-purple-50 text-purple-700 font-medium'
                                        : 'text-gray-700 hover:bg-gray-100'
                                        }`}
                                >
                                    <Icon className="w-5 h-5" />
                                    <span>{item.label}</span>
                                </Link>
                            );
                        })}
                    </nav>
                </aside>

                {/* Main Content */}
                <main className="flex-1 p-8">
                    {children}
                </main>
            </div>
        </div>
    );
}
