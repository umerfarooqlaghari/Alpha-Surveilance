import { MetadataRoute } from 'next';

const BASE_URL = process.env.NEXT_PUBLIC_SITE_URL || 'https://surveillance.alpha-devs.cloud';

export default function robots(): MetadataRoute.Robots {
    return {
        rules: [
            {
                userAgent: '*',
                allow: '/',
                disallow: [
                    '/admin/',
                    '/tenant/',
                    '/api/',
                    '/enroll/',
                    '/_next/',
                ],
            },
            // Explicit rules for AI Search & LLM Engines
            {
                userAgent: [
                    'GPTBot',
                    'ChatGPT-User',
                    'ClaudeBot',
                    'anthropic-ai',
                    'Google-Extended',
                    'Googlebot',
                    'PerplexityBot',
                    'Applebot-Extended',
                    'Bytespider',
                    'cohere-ai',
                    'Diffbot',
                    'FacebookBot',
                    'Meta-ExternalAgent',
                ],
                allow: [
                    '/',
                    '/llms.txt',
                    '/llms-full.txt',
                    '/sitemap.xml',
                ],
                disallow: [
                    '/admin/',
                    '/tenant/',
                    '/api/',
                    '/enroll/',
                ],
            },
        ],
        sitemap: `${BASE_URL}/sitemap.xml`,
        host: BASE_URL,
    };
}
