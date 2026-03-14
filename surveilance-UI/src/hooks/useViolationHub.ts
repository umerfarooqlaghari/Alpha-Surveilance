import { useEffect, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '@/contexts/AuthContext';

export const useViolationHub = () => {
    const { tenant, token } = useAuth();
    const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
    const [notifications, setNotifications] = useState<any[]>([]);

    useEffect(() => {
        if (!tenant?.id || !token) return;

        const bffUrl = process.env.NEXT_PUBLIC_BFF_URL || '';
        const hubUrl = `${bffUrl}/hubs/violations`.replace(/([^:]\/)\/+/g, "$1"); // Avoid double slashes

        const newConnection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                accessTokenFactory: () => token
            })
            .withAutomaticReconnect()
            .build();

        setConnection(newConnection);
    }, [tenant?.id, token]);

    useEffect(() => {
        if (!connection || !tenant?.id) return;

        let isMounted = true;

        const startConnection = async () => {
            if (connection.state === signalR.HubConnectionState.Disconnected) {
                try {
                    await connection.start();
                    if (isMounted) {
                        console.log('[SignalR] Connected successfully!');
                        await connection.invoke('JoinTenantGroup');
                    }
                } catch (err) {
                    console.error('[SignalR] Connection Failed: ', err);
                }
            }
        };

        // Listen for events
        connection.on('ReceiveViolation', (violation: any) => {
            console.log('🚨 [SignalR] Real-time violation received:', violation);
            setNotifications(prev => [violation, ...prev].slice(0, 50));
        });

        connection.onclose((error) => {
            console.warn('[SignalR] Connection closed:', error);
        });

        connection.onreconnecting((error) => {
            console.warn('[SignalR] Connection reconnecting:', error);
        });

        connection.onreconnected((connectionId) => {
            console.log('[SignalR] Connection re-established:', connectionId);
            if (isMounted) {
                connection.invoke('JoinTenantGroup').catch(err => console.error('[SignalR] Re-join group failed:', err));
            }
        });

        startConnection();

        return () => {
            isMounted = false;
            connection.off('ReceiveViolation');
            if (connection.state !== signalR.HubConnectionState.Disconnected) {
                connection.stop().catch(err => console.error('[SignalR] Stop failed:', err));
            }
        };
    }, [connection, tenant?.id]);

    return { connection, notifications };
};
