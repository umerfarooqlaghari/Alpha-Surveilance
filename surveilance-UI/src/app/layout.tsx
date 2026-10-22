import type { Metadata } from "next";
import Script from "next/script";
import { Analytics } from "@vercel/analytics/next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "@/contexts/AuthContext";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

const BASE_URL = process.env.NEXT_PUBLIC_SITE_URL || 'https://surveillance.alpha-devs.cloud';

export const metadata: Metadata = {
  metadataBase: new URL(BASE_URL),
  title: {
    default: "Alpha Surveillance | AI Vision & Workplace Safety Intelligence",
    template: "%s | Alpha Surveillance",
  },
  description:
    "Next-generation enterprise AI computer vision platform. Real-time workplace safety monitoring, automated PPE compliance detection, ergonomics posture analysis, and sub-second WebRTC CCTV surveillance.",
  keywords: [
    "AI Surveillance",
    "Computer Vision Workplace Safety",
    "PPE Detection AI",
    "OSHA Safety Compliance AI",
    "Real-Time Video Analytics",
    "CCTV AI Monitoring",
    "Workplace Ergonomics Detection",
    "Industrial Safety AI",
    "Automated Violation Detection",
    "Cloudflare Stream WebRTC CCTV",
    "Multi-Tenant Surveillance Platform",
    "Safety Helmet Hard Hat Detection",
    "High Visibility Vest Detection",
    "Construction Site Safety AI",
    "Restaurant Kitchen Hygiene Vision AI",
    "Video Management System VMS AI",
    "Real-Time Anomaly Detection",
  ],
  authors: [{ name: "Alpha Surveillance Team", url: BASE_URL }],
  creator: "Alpha Surveillance",
  publisher: "Alpha Surveillance",
  alternates: {
    canonical: "/",
  },
  openGraph: {
    title: "Alpha Surveillance | AI Vision & Workplace Safety Intelligence",
    description:
      "Enterprise AI computer vision platform for real-time workplace safety, automated PPE compliance, and low-latency CCTV video analytics.",
    url: BASE_URL,
    siteName: "Alpha Surveillance",
    images: [
      {
        url: "/alpha-logo.jpg",
        width: 800,
        height: 800,
        alt: "Alpha Surveillance AI Safety Platform",
      },
    ],
    locale: "en_US",
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "Alpha Surveillance | Real-Time AI Vision Platform",
    description:
      "Autonomous computer vision for workplace safety, PPE enforcement, and CCTV stream intelligence.",
    images: ["/alpha-logo.jpg"],
  },
  robots: {
    index: true,
    follow: true,
    nocache: false,
    googleBot: {
      index: true,
      follow: true,
      "max-video-preview": -1,
      "max-image-preview": "large",
      "max-snippet": -1,
    },
  },
};

const jsonLd = {
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "Organization",
      "@id": `${BASE_URL}/#organization`,
      name: "Alpha Surveillance",
      url: BASE_URL,
      logo: `${BASE_URL}/alpha-logo.jpg`,
      email: "info@alpha-devs.cloud",
      description:
        "Enterprise AI computer vision and real-time surveillance platform for workplace safety compliance.",
    },
    {
      "@type": "SoftwareApplication",
      "@id": `${BASE_URL}/#software`,
      name: "Alpha Surveillance Platform",
      applicationCategory: "SecurityApplication",
      operatingSystem: "Web, Cloud, Linux Edge",
      offers: {
        "@type": "Offer",
        price: "0",
        priceCurrency: "USD",
      },
      description:
        "Real-time AI video analytics system detecting PPE violations, ergonomics posture, and industrial safety infractions from RTSP/WebRTC camera feeds.",
      featureList: [
        "Real-Time Safety Helmet and Hard Hat Detection",
        "High-Visibility Safety Vest Compliance",
        "Restaurant Hygiene and Food Safety Detection",
        "Sub-Second Low Latency WebRTC Video Streams",
        "Multi-Tenant Workspace Management",
        "Automated TimescaleDB Audit Trails & PDF Reports",
      ],
    },
    {
      "@type": "WebSite",
      "@id": `${BASE_URL}/#website`,
      url: BASE_URL,
      name: "Alpha Surveillance",
      publisher: {
        "@id": `${BASE_URL}/#organization`,
      },
    },
  ],
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <head>
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
        />
      </head>
      <body
        className={`${geistSans.variable} ${geistMono.variable} antialiased`}
      >
        <AuthProvider>
          {children}
        </AuthProvider>
        <Analytics />
        <Script
          id="cf-turnstile-script"
          src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit"
          strategy="afterInteractive"
        />
      </body>
    </html>
  );
}
