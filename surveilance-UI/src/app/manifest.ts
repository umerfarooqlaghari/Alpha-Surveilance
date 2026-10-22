import { MetadataRoute } from 'next';

export default function manifest(): MetadataRoute.Manifest {
    return {
        name: 'Alpha Surveillance - AI Video & Safety Platform',
        short_name: 'Alpha Surveillance',
        description: 'Next-generation AI computer vision platform for real-time workplace safety, PPE compliance, and video analytics.',
        start_url: '/',
        display: 'standalone',
        background_color: '#f6f5f1',
        theme_color: '#18181b',
        icons: [
            {
                src: '/alpha-logo.jpg',
                sizes: 'any',
                type: 'image/jpeg',
            },
        ],
    };
}
