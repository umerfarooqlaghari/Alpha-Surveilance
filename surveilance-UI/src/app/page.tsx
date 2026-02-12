import Link from 'next/link';
import { Shield, Building2, ChevronRight } from 'lucide-react';

export default function LandingPage() {
  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 to-slate-800 flex flex-col items-center justify-center p-4">
      {/* Hero Section */}
      <div className="text-center mb-12">
        <h1 className="text-4xl md:text-5xl font-bold text-white mb-4 tracking-tight">
          Alpha Surveillance
          <span className="text-blue-500">.</span>
        </h1>
        <p className="text-slate-400 text-lg md:text-xl max-w-2xl mx-auto">
          Next-generation AI-powered surveillance and violation detection system.
          Secure, scalable, and multi-tenant.
        </p>
      </div>

      {/* Login Portals */}
      <div className="grid md:grid-cols-2 gap-6 w-full max-w-4xl">
        {/* Super Admin Card */}
        <Link
          href="/admin/auth/login"
          className="group relative bg-slate-800/50 hover:bg-slate-800 border border-slate-700 hover:border-blue-500/50 rounded-2xl p-8 transition-all duration-300 backdrop-blur-sm"
        >
          <div className="absolute top-0 right-0 p-4 opacity-0 group-hover:opacity-100 transition-opacity">
            <ChevronRight className="w-6 h-6 text-blue-500" />
          </div>

          <div className="bg-blue-500/10 w-16 h-16 rounded-xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform duration-300">
            <Shield className="w-8 h-8 text-blue-500" />
          </div>

          <h2 className="text-2xl font-bold text-white mb-2">Super Admin</h2>
          <p className="text-slate-400 mb-6">
            System configuration, tenant management, and platform oversight.
          </p>

          <span className="text-blue-400 font-medium group-hover:text-blue-300 flex items-center gap-2">
            Access Portal &rarr;
          </span>
        </Link>

        {/* Tenant Admin Card */}
        <Link
          href="/tenant/auth/login"
          className="group relative bg-slate-800/50 hover:bg-slate-800 border border-slate-700 hover:border-purple-500/50 rounded-2xl p-8 transition-all duration-300 backdrop-blur-sm"
        >
          <div className="absolute top-0 right-0 p-4 opacity-0 group-hover:opacity-100 transition-opacity">
            <ChevronRight className="w-6 h-6 text-purple-500" />
          </div>

          <div className="bg-purple-500/10 w-16 h-16 rounded-xl flex items-center justify-center mb-6 group-hover:scale-110 transition-transform duration-300">
            <Building2 className="w-8 h-8 text-purple-500" />
          </div>

          <h2 className="text-2xl font-bold text-white mb-2">Tenant Admin</h2>
          <p className="text-slate-400 mb-6">
            Organization dashboard, camera management, and violation monitoring.
          </p>

          <span className="text-purple-400 font-medium group-hover:text-purple-300 flex items-center gap-2">
            Access Portal &rarr;
          </span>
        </Link>
      </div>

      {/* Footer */}
      <div className="mt-16 text-slate-500 text-sm">
        &copy; {new Date().getFullYear()} Alpha Surveillance Systems. All rights reserved.
      </div>
    </div>
  );
}
