// Desktop notifications for browser
(function() {
    'use strict';

    let permission = Notification.permission;
    let isEnabled = false;

    // Request permission
    async function requestPermission() {
        if (permission === 'granted') {
            isEnabled = true;
            return true;
        }

        if (permission === 'denied') {
            console.warn('Desktop notifications are denied');
            return false;
        }

        try {
            permission = await Notification.requestPermission();
            isEnabled = permission === 'granted';
            return isEnabled;
        } catch (err) {
            console.error('Error requesting notification permission:', err);
            return false;
        }
    }

    // Show desktop notification
    function showDesktopNotification(notification) {
        // Check if tab is active - only show if tab is not active
        if (!document.hidden) {
            return; // Tab is active, don't show desktop notification
        }

        if (permission !== 'granted') {
            return;
        }

        const title = notification.isImportant ? `⭐ ${notification.title}` : notification.title;
        const options = {
            body: notification.message || '',
            icon: '/favicon.ico',
            badge: '/favicon.ico',
            tag: `notification-${notification.id}`,
            requireInteraction: notification.priority === 'Urgent',
            data: {
                url: notification.detailUrl || '/Notifications/Index',
                notificationId: notification.id
            }
        };

        try {
            const notif = new Notification(title, options);

            notif.onclick = function() {
                window.focus();
                if (notification.detailUrl && notification.detailUrl !== '#') {
                    window.location.href = notification.detailUrl;
                } else {
                    window.location.href = '/Notifications/Index';
                }
                notif.close();
            };

            // Auto close after 5 seconds (unless urgent)
            if (notification.priority !== 'Urgent') {
                setTimeout(() => {
                    notif.close();
                }, 5000);
            }
        } catch (err) {
            console.error('Error showing desktop notification:', err);
        }
    }

    // Check settings from server
    async function checkSettings() {
        try {
            const response = await fetch('/Notifications/GetSettings', {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            if (response.ok) {
                const settings = await response.json();
                isEnabled = settings.enableDesktopNotifications === true;
                
                if (isEnabled && permission === 'default') {
                    await requestPermission();
                }
            }
        } catch (err) {
            // Silently fail - settings will use defaults
            console.debug('Error checking notification settings:', err);
        }
    }

    // Public API
    window.desktopNotifications = {
        requestPermission: requestPermission,
        show: showDesktopNotification,
        isEnabled: () => isEnabled && permission === 'granted',
        checkSettings: checkSettings
    };

    // Global function for other scripts
    window.showDesktopNotification = showDesktopNotification;

    // Check settings on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', checkSettings);
    } else {
        checkSettings();
    }
})();



