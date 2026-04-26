'use client';

import Link from 'next/link';
import Image from 'next/image';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import { LayoutDashboard, Camera, FileText, LogOut, BarChart3, Users, Video, AlertTriangle, LineChart, Mail, FolderOpen, Shield, Building2, ChevronLeft, ChevronRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';

export default function TenantLayout({ children }: { children: React.ReactNode }) {
    const pathname = usePathname();
    const { isAuthenticated, role, tenant, user, logout, isLoading } = useAuth();
    const router = useRouter();
    const [isSidebarOpen, setIsSidebarOpen] = useState(true);

    useEffect(() => {
        if (!isLoading && (!isAuthenticated || role !== 'TenantAdmin')) {
            if (pathname !== '/tenant/auth/login') {
                router.push('/tenant/auth/login');
            }
        } else if (!isLoading && isAuthenticated && role === 'TenantAdmin' && pathname === '/tenant') {
            router.push('/tenant/analytics');
        }
    }, [isAuthenticated, role, isLoading, router, pathname]);

    // Bypass protection for login page
    if (pathname === '/tenant/auth/login') {
        return <>{children}</>;
    }

    if (isLoading || !isAuthenticated || role !== 'TenantAdmin') {
        return (
            <div className="min-h-screen flex items-center justify-center bg-[#eef2f6]">
                <div className="text-gray-500 font-medium">Loading workspace...</div>
            </div>
        );
    }

    const navItems = [
        { name: 'Analytics', href: '/tenant/analytics', icon: LineChart },
        { name: 'Live feed', href: '/tenant/live-feed', icon: LayoutDashboard },
        { name: 'Violations', href: '/tenant/violations', icon: AlertTriangle },
        { name: 'Compliance', href: '/tenant/compliance', icon: Shield },
        { name: 'Employees', href: '/tenant/employees', icon: Users },
        { name: 'Cameras', href: '/tenant/cameras', icon: Video },
        { name: 'SOP Requests', href: '/tenant/requests', icon: FileText },
        { name: 'Emailing', href: '/tenant/emailing', icon: Mail },
        { name: 'File Manager', href: '/tenant/files', icon: FolderOpen },
    ];

    return (
        <div className="min-h-screen bg-[#eef2f6] flex overflow-hidden font-sans">
            {/* Floating Sidebar Panel */}
            <aside
                className={`relative m-4 mr-0 bg-white rounded-[2rem] shadow-sm border border-gray-100 flex flex-col transition-all duration-300 ease-in-out z-20 ${isSidebarOpen ? 'w-[260px]' : 'w-[88px]'
                    }`}
            >
                {/* Collapse Toggle */}
                <button
                    onClick={() => setIsSidebarOpen(!isSidebarOpen)}
                    className="absolute -right-3.5 top-20 bg-white border border-gray-200 rounded-full p-1.5 shadow-sm text-gray-400 hover:text-gray-600 z-50 transition-colors"
                >
                    {isSidebarOpen ? <ChevronLeft className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                </button>

                {/* macOS Windows Dots */}
                <div className={`flex gap-1.5 pt-6 pb-2 ${isSidebarOpen ? 'px-8' : 'justify-center'}`}>
                    <div className="w-3 h-3 rounded-full bg-[#ff5f56]"></div>
                    <div className="w-3 h-3 rounded-full bg-[#ffbd2e]"></div>
                    <div className="w-3 h-3 rounded-full bg-[#27c93f]"></div>
                </div>

                {/* Profile Section */}
                <div className={`flex items-center mt-4 mb-4 ${isSidebarOpen ? 'px-8 gap-3' : 'justify-center flex-col gap-2 px-2'}`}>
                    <div className="relative w-12 h-12 rounded-full overflow-hidden border-2 border-blue-50 flex-shrink-0">
                        {tenant?.logoUrl ? (
                            <Image
                                src={tenant.logoUrl}
                                alt={`${tenant.tenantName} logo`}
                                fill
                                className="object-cover"
                            />
                        ) : (
                            <div className="w-full h-full bg-blue-50 flex items-center justify-center">
                                <Building2 className="w-6 h-6 text-blue-500" />
                            </div>
                        )}
                    </div>

                    {isSidebarOpen && (
                        <div className="overflow-hidden">
                            <p className="text-xs text-gray-500 font-medium whitespace-nowrap">Good Day 👋</p>
                            <h2 className="text-sm font-bold text-gray-900 truncate">
                                {user?.fullName || tenant?.tenantName || 'Tenant Admin'}
                            </h2>
                        </div>
                    )}
                </div>

                <div className="px-6 py-2">
                    <div className="border-t border-gray-100 w-full mb-4"></div>
                </div>

                <div className={`text-xs font-semibold text-gray-400 mb-2 whitespace-nowrap overflow-hidden transition-all ${isSidebarOpen ? 'px-8' : 'text-center'
                    }`}>
                    Menu: <span className="text-gray-600 font-bold">{navItems.length}</span>
                </div>

                {/* Navigation Items */}
                <nav className={`flex-1 overflow-y-auto space-y-1.5 ${isSidebarOpen ? 'px-4' : 'px-3'}`}>
                    {navItems.map((item) => {
                        const Icon = item.icon;
                        const isActive = pathname === item.href;

                        return (
                            <Link
                                key={item.href}
                                href={item.href}
                                title={!isSidebarOpen ? item.name : undefined}
                                className={`flex items-center rounded-2xl transition-all duration-200 group ${isActive
                                        ? 'bg-[#3b82f6] text-white shadow-md shadow-blue-500/20'
                                        : 'text-gray-500 hover:text-gray-900 hover:bg-gray-50'
                                    } ${isSidebarOpen ? 'px-4 py-3 gap-3' : 'justify-center py-3'}`}
                            >
                                <Icon className={`w-5 h-5 flex-shrink-0 transition-transform ${!isActive && 'group-hover:scale-110'}`} />
                                {isSidebarOpen && (
                                    <span className={`font-medium text-sm transition-opacity duration-200`}>
                                        {item.name}
                                    </span>
                                )}
                            </Link>
                        );
                    })}
                </nav>

                {/* Logout Button */}
                <div className="p-4 mt-auto">
                    <button
                        onClick={logout}
                        title={!isSidebarOpen ? "Logout" : undefined}
                        className={`w-full flex items-center rounded-2xl transition-all duration-200 text-gray-500 hover:text-red-600 hover:bg-red-50 group
                            ${isSidebarOpen ? 'px-4 py-3 gap-3' : 'justify-center py-3'}
                        `}
                    >
                        <LogOut className="w-5 h-5 flex-shrink-0 transition-transform group-hover:scale-110" />
                        {isSidebarOpen && (
                            <span className="font-medium text-sm">Logout</span>
                        )}
                    </button>
                </div>
            </aside>

            {/* Main Content Area */}
            <main className="flex-1 flex flex-col min-w-0 h-screen overflow-hidden">
                <div className="flex-1 p-6 lg:p-8 overflow-y-auto">
                    {children}
                </div>
            </main>
        </div>
    );
}
