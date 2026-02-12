import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect, useState } from "react";
import Cookies from "js-cookie";

export interface ViolationNotification {
    id: string;
    type: string;
    severity: string;
    timestamp: string;
    framePath: string;
    cameraId: string;
}

export const useSignalR = (tenantId: string) => {
    const [connection, setConnection] = useState<HubConnection | null>(null);
    const [notifications, setNotifications] = useState<ViolationNotification[]>([]);

    useEffect(() => {
        // 1. Get Token from Cookie
        const token = Cookies.get("token") || "";

        const newConnection = new HubConnectionBuilder()
            .withUrl("/hubs/violations", {
                // 2. Attach Token for Auth
                accessTokenFactory: () => token
            })
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Information)
            .build();

        setConnection(newConnection);
    }, []);

    useEffect(() => {
        if (connection) {
            connection
                .start()
                .then(() => {
                    console.log("Connected to SignalR Hub");

                    // Join the Tenant Group
                    connection.invoke("JoinTenantGroup", tenantId);

                    // Listen for events
                    connection.on("ReceiveViolation", (notification: ViolationNotification) => {
                        console.log("New Violation Received:", notification);
                        setNotifications((prev) => [notification, ...prev]);
                    });
                })
                .catch((e) => console.log("Connection failed: ", e));
        }
    }, [connection, tenantId]);

    return { notifications };
};
