import type { NextConfig } from "next";

/**
 * .NET Aspire automatically injects environment variables for referenced services.
 * The variable name follows the pattern: services__<service-name>__http__0
 * Replace 'violation-api' with the name you used in your AppHost Program.cs
 */
const API_URL = process.env["NEXT_PUBLIC_BFF_URL"] || process.env["services__bff__https__0"] || process.env["services__bff__http__0"] || "http://localhost:5002";

const nextConfig: NextConfig = {
  /* Enables the new React 19 / Next.js 15 Compiler */
  reactCompiler: true,
  images: {
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'res.cloudinary.com',
        port: '',
        pathname: '/**',
      },
    ],
  },

  /**
   * Rewrites act as a proxy. 
   * When your frontend calls '/api/violations', Next.js forwards it to the 
   * .NET backend URL provided by Aspire. This prevents CORS issues.
   */
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${API_URL}/api/:path*`,
      },
      {
        source: "/hubs/:path*",
        destination: `${API_URL}/hubs/:path*`,
      },
    ];
  },
};

export default nextConfig;