// Sound notifications
(function() {
    'use strict';

    const sounds = {
        default: '/sounds/notification-default.mp3',
        urgent: '/sounds/notification-urgent.mp3',
        info: '/sounds/notification-info.mp3'
    };

    let audioCache = {};
    let isEnabled = true;
    let soundType = 'default';

    // Lazy load audio files - only create when needed
    function getAudio(key) {
        if (!audioCache[key]) {
            const audio = new Audio(sounds[key]);
            // Handle 404 errors gracefully - don't log to console
            audio.addEventListener('error', function(e) {
                // Silently fail - sound file doesn't exist
                audioCache[key] = null; // Mark as failed
            }, { once: true });
            audioCache[key] = audio;
        }
        return audioCache[key];
    }

    // Play notification sound
    function playNotificationSound(priority = 'Normal') {
        if (!isEnabled) return;

        // Determine sound type based on priority
        let soundKey = 'default';
        if (priority === 'Urgent' || priority === 'High') {
            soundKey = 'urgent';
        } else if (priority === 'Low') {
            soundKey = 'info';
        } else {
            soundKey = soundType; // Use user's preferred sound type
        }

        // Lazy load audio - only create when needed
        const audio = getAudio(soundKey) || getAudio('default');
        if (audio) {
            try {
                audio.currentTime = 0; // Reset to start
                audio.play().catch(err => {
                    // Silently fail - don't log to console
                });
            } catch (err) {
                // Silently fail - don't log to console
            }
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
                isEnabled = settings.enableSound === true;
                soundType = settings.soundType || 'default';
            }
        } catch (err) {
            // Silently fail - settings will use defaults
            console.debug('Error checking sound settings:', err);
        }
    }

    // Public API
    window.soundNotifications = {
        play: playNotificationSound,
        isEnabled: () => isEnabled,
        setEnabled: (enabled) => { isEnabled = enabled; },
        setSoundType: (type) => { soundType = type; },
        checkSettings: checkSettings
    };

    // Global function for other scripts
    window.playNotificationSound = playNotificationSound;

    // Check settings on load (don't preload sounds - lazy load only)
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            checkSettings();
        });
    } else {
        checkSettings();
    }
})();



