// BEMART Toast Notification System
// Lightweight toast notification library

class ToastManager {
    constructor() {
        this.container = null;
        this.init();
    }

    init() {
        // Create container if not exists
        if (!document.getElementById('toast-container')) {
            this.container = document.createElement('div');
            this.container.id = 'toast-container';
            this.container.className = 'toast-container';
            document.body.appendChild(this.container);
        } else {
            this.container = document.getElementById('toast-container');
        }
    }

    show(message, type = 'info', duration = 5000) {
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        
        const icon = this.getIcon(type);
        const closeBtn = '<button class="toast-close" onclick="this.parentElement.remove()">×</button>';
        
        toast.innerHTML = `
            <div class="toast-icon">${icon}</div>
            <div class="toast-content">
                <div class="toast-message">${this.escapeHtml(message)}</div>
            </div>
            ${closeBtn}
        `;

        this.container.appendChild(toast);

        // Trigger animation
        setTimeout(() => toast.classList.add('toast-show'), 10);

        // Auto dismiss
        if (duration > 0) {
            setTimeout(() => {
                this.dismiss(toast);
            }, duration);
        }

        return toast;
    }

    dismiss(toast) {
        toast.classList.remove('toast-show');
        toast.classList.add('toast-hide');
        setTimeout(() => {
            if (toast.parentElement) {
                toast.remove();
            }
        }, 300);
    }

    getIcon(type) {
        const icons = {
            success: '<i class="bi bi-check-circle-fill"></i>',
            error: '<i class="bi bi-x-circle-fill"></i>',
            warning: '<i class="bi bi-exclamation-triangle-fill"></i>',
            info: '<i class="bi bi-info-circle-fill"></i>'
        };
        return icons[type] || icons.info;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Convenience methods
    success(message, duration) {
        return this.show(message, 'success', duration);
    }

    error(message, duration) {
        return this.show(message, 'error', duration);
    }

    warning(message, duration) {
        return this.show(message, 'warning', duration);
    }

    info(message, duration) {
        return this.show(message, 'info', duration);
    }
}

// Global instance
window.Toast = new ToastManager();

// Auto-show toasts from TempData (server-side)
document.addEventListener('DOMContentLoaded', function() {
    // Check for toast data in meta tags or data attributes
    const toastData = document.querySelector('[data-toast]');
    if (toastData) {
        try {
            const data = JSON.parse(toastData.getAttribute('data-toast'));
            if (data.message) {
                Toast.show(data.message, data.type || 'info', data.duration || 5000);
            }
        } catch (e) {
            console.error('Error parsing toast data:', e);
        }
    }

    // Legacy TempData support
    const tempMsg = document.querySelector('[data-temp-msg]');
    if (tempMsg) {
        const message = tempMsg.getAttribute('data-temp-msg');
        const type = tempMsg.getAttribute('data-temp-type') || 'success';
        Toast.show(message, type);
    }

    const tempError = document.querySelector('[data-temp-error]');
    if (tempError) {
        const message = tempError.getAttribute('data-temp-error');
        Toast.error(message);
    }
});


