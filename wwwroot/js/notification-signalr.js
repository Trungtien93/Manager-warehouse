// SignalR client for real-time notifications
(function() {
    'use strict';

    let connection = null;
    let isConnected = false;
    let reconnectAttempts = 0;
    const maxReconnectAttempts = 5;
    let reconnectTimeout = null;

    // Initialize SignalR connection
    function initSignalR() {
        if (typeof signalR === 'undefined') {
            console.warn('SignalR library not loaded');
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl("/notificationHub")
            .withAutomaticReconnect()
            .build();

        // Connection events
        connection.onclose(() => {
            isConnected = false;
            console.log('SignalR connection closed');
            attemptReconnect();
        });

        connection.onreconnecting(() => {
            isConnected = false;
            console.log('SignalR reconnecting...');
        });

        connection.onreconnected(() => {
            isConnected = true;
            reconnectAttempts = 0;
            console.log('SignalR reconnected');
            joinUserGroup();
        });

        // Listen for notifications
        connection.on("ReceiveNotification", function(notification) {
            console.log('Received notification:', notification);
            handleNewNotification(notification);
        });

        // Start connection
        startConnection();
    }

    // Start SignalR connection
    async function startConnection() {
        try {
            await connection.start();
            isConnected = true;
            reconnectAttempts = 0;
            console.log('SignalR connected');
            joinUserGroup();
        } catch (err) {
            console.error('Error starting SignalR:', err);
            isConnected = false;
            attemptReconnect();
        }
    }

    // Join user group
    async function joinUserGroup() {
        if (!isConnected || !connection) return;

        try {
            // Get userId from page (should be set in _Layout.cshtml)
            const userId = window.currentUserId;
            if (userId) {
                await connection.invoke("JoinUserGroup", userId);
                console.log('Joined user group:', userId);
            }
        } catch (err) {
            console.error('Error joining user group:', err);
        }
    }

    // Handle new notification
    function handleNewNotification(notification) {
        // Update badge count
        if (typeof updateNotificationBadge === 'function') {
            updateNotificationBadge();
        }

        // Reload notifications if dropdown is open
        if (typeof loadNotifications === 'function') {
            loadNotifications();
        }

        // Show desktop notification
        if (typeof showDesktopNotification === 'function') {
            showDesktopNotification(notification);
        }

        // Play sound
        if (typeof playNotificationSound === 'function') {
            playNotificationSound(notification.priority || 'Normal');
        }

        // Trigger custom event for other scripts
        const event = new CustomEvent('notificationReceived', { detail: notification });
        window.dispatchEvent(event);
    }

    // Attempt to reconnect
    function attemptReconnect() {
        if (reconnectAttempts >= maxReconnectAttempts) {
            console.warn('Max reconnect attempts reached');
            return;
        }

        reconnectAttempts++;
        const delay = Math.min(1000 * Math.pow(2, reconnectAttempts), 30000); // Exponential backoff, max 30s

        if (reconnectTimeout) {
            clearTimeout(reconnectTimeout);
        }

        reconnectTimeout = setTimeout(() => {
            if (!isConnected && connection) {
                console.log(`Attempting to reconnect (${reconnectAttempts}/${maxReconnectAttempts})...`);
                startConnection();
            }
        }, delay);
    }

    // Public API
    window.notificationSignalR = {
        isConnected: () => isConnected,
        reconnect: () => {
            reconnectAttempts = 0;
            if (connection) {
                startConnection();
            }
        },
        disconnect: async () => {
            if (connection) {
                await connection.stop();
                isConnected = false;
            }
        }
    };

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initSignalR);
    } else {
        initSignalR();
    }
})();



