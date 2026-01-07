// BEMART Keyboard Shortcuts System

class ShortcutManager {
    constructor() {
        this.shortcuts = new Map();
        this.enabled = true;
        this.init();
    }

    init() {
        document.addEventListener('keydown', (e) => this.handleKeyPress(e));
        this.registerDefaultShortcuts();
    }

    // Register a keyboard shortcut
    register(keys, callback, description = '') {
        const key = this.normalizeKey(keys);
        this.shortcuts.set(key, { callback, description, keys });
    }

    // Normalize key combination
    normalizeKey(keys) {
        return keys.toLowerCase()
            .replace('ctrl', 'control')
            .replace('cmd', 'meta')
            .split('+')
            .sort()
            .join('+');
    }

    // Handle keypress event
    handleKeyPress(e) {
        if (!this.enabled) return;

        // Don't trigger shortcuts in input fields (except ESC)
        const tagName = e.target.tagName.toLowerCase();
        const isInput = ['input', 'textarea', 'select'].includes(tagName) || 
                       e.target.isContentEditable;
        
        if (isInput && e.key !== 'Escape') return;

        // Build key combination
        const keys = [];
        if (e.ctrlKey || e.metaKey) keys.push('control');
        if (e.altKey) keys.push('alt');
        if (e.shiftKey) keys.push('shift');
        
        let key = e.key.toLowerCase();
        if (key !== 'control' && key !== 'alt' && key !== 'shift' && key !== 'meta') {
            keys.push(key);
        }

        const keyCombo = keys.sort().join('+');
        const shortcut = this.shortcuts.get(keyCombo);

        if (shortcut) {
            e.preventDefault();
            shortcut.callback(e);
        }
    }

    // Get all registered shortcuts
    getAll() {
        return Array.from(this.shortcuts.entries()).map(([key, data]) => ({
            key,
            ...data
        }));
    }

    // Register default BEMART shortcuts
    registerDefaultShortcuts() {
        // Ctrl+K: Quick search/command palette
        this.register('Control+K', (e) => {
            const searchInput = document.querySelector('#quick-search, input[type="search"], input[placeholder*="Tìm"]');
            if (searchInput) {
                searchInput.focus();
                searchInput.select();
            }
        }, 'Tìm kiếm nhanh');

        // Ctrl+N: New item (context-aware)
        this.register('Control+N', (e) => {
            const newBtn = document.querySelector('[data-shortcut="new"], .btn-new, button:contains("Thêm")');
            if (newBtn) {
                newBtn.click();
            }
        }, 'Tạo mới');

        // Ctrl+S: Save form
        this.register('Control+S', (e) => {
            const saveBtn = document.querySelector('button[type="submit"], .btn-save, [data-shortcut="save"]');
            if (saveBtn) {
                saveBtn.click();
            }
        }, 'Lưu');

        // ESC: Close modals
        this.register('Escape', (e) => {
            // Close Bootstrap modals
            const openModal = document.querySelector('.modal.show');
            if (openModal) {
                const modal = bootstrap.Modal.getInstance(openModal);
                if (modal) modal.hide();
                return;
            }

            // Close custom overlays
            const overlay = document.querySelector('.overlay.show, [data-overlay]');
            if (overlay) {
                overlay.classList.remove('show');
                return;
            }

            // Close chatbot
            const chatbotPanel = document.getElementById('chatbot-panel');
            if (chatbotPanel && !chatbotPanel.classList.contains('hidden')) {
                chatbotPanel.classList.add('hidden');
            }
        }, 'Đóng modal/overlay');

        // Ctrl+/: Show shortcuts help
        this.register('Control+/', (e) => {
            this.showHelp();
        }, 'Hiển thị phím tắt');

        // ?: Show help (alternative)
        this.register('shift+/', (e) => {
            this.showHelp();
        }, 'Hiển thị phím tắt');

        // Alt+1-9: Quick navigation
        for (let i = 1; i <= 9; i++) {
            this.register(`Alt+${i}`, (e) => {
                const navLinks = document.querySelectorAll('aside a[href]:not([href="#"])');
                if (navLinks[i - 1]) {
                    navLinks[i - 1].click();
                }
            }, `Điều hướng nhanh ${i}`);
        }

        // Ctrl+H: Go home
        this.register('Control+H', (e) => {
            window.location.href = '/Home/Dashboard';
        }, 'Trang chủ');

        // Ctrl+L: Focus on nav/menu
        this.register('Control+L', (e) => {
            const firstNavLink = document.querySelector('aside a[href]:not([href="#"])');
            if (firstNavLink) {
                firstNavLink.focus();
            }
        }, 'Focus menu');
    }

    // Show help modal
    showHelp() {
        // Check if modal exists
        let modal = document.getElementById('shortcuts-help-modal');
        
        if (!modal) {
            // Create modal
            modal = document.createElement('div');
            modal.id = 'shortcuts-help-modal';
            modal.className = 'modal fade';
            modal.innerHTML = `
                <div class="modal-dialog modal-lg">
                    <div class="modal-content">
                        <div class="modal-header bg-slate-900 text-white">
                            <h5 class="modal-title">
                                <i class="bi bi-keyboard me-2"></i>
                                Phím tắt hệ thống
                            </h5>
                            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body">
                            <div class="row g-3" id="shortcuts-list"></div>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Đóng</button>
                        </div>
                    </div>
                </div>
            `;
            document.body.appendChild(modal);
        }

        // Populate shortcuts list
        const listContainer = modal.querySelector('#shortcuts-list');
        listContainer.innerHTML = '';

        this.getAll().forEach(shortcut => {
            if (!shortcut.description) return;

            const col = document.createElement('div');
            col.className = 'col-md-6';
            
            const keys = shortcut.keys.split('+').map(k => {
                return `<kbd class="px-2 py-1 rounded bg-slate-100 text-slate-800 border border-slate-300 text-sm font-mono">${k.toUpperCase()}</kbd>`;
            }).join(' + ');

            col.innerHTML = `
                <div class="d-flex align-items-center gap-3 p-2 rounded border">
                    <div class="flex-shrink-0">${keys}</div>
                    <div class="flex-grow-1 text-slate-600">${shortcut.description}</div>
                </div>
            `;
            
            listContainer.appendChild(col);
        });

        // Show modal
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    }

    // Enable/disable shortcuts
    setEnabled(enabled) {
        this.enabled = enabled;
    }
}

// Global instance
window.Shortcuts = new ShortcutManager();

// Add visual indicator for focusable elements
document.addEventListener('DOMContentLoaded', function() {
    // Add keyboard navigation hints
    document.querySelectorAll('aside a[href]:not([href="#"])').forEach((link, index) => {
        if (index < 9) {
            const hint = document.createElement('span');
            hint.className = 'shortcut-hint text-xs text-slate-400 ml-auto';
            hint.textContent = `Alt+${index + 1}`;
            hint.style.fontSize = '10px';
            hint.style.opacity = '0';
            hint.style.transition = 'opacity 0.2s';
            link.appendChild(hint);
            
            // Show on hover
            link.addEventListener('mouseenter', () => hint.style.opacity = '0.7');
            link.addEventListener('mouseleave', () => hint.style.opacity = '0');
        }
    });

    // Add help button to header
    const header = document.querySelector('header .flex.items-center.gap-5');
    if (header && !document.getElementById('shortcuts-help-btn')) {
        const helpBtn = document.createElement('button');
        helpBtn.id = 'shortcuts-help-btn';
        helpBtn.type = 'button';
        helpBtn.className = 'inline-flex items-center gap-1 text-slate-600 hover:text-slate-900 text-sm';
        helpBtn.innerHTML = '<i class="bi bi-keyboard"></i>';
        helpBtn.title = 'Phím tắt (Ctrl+/)';
        helpBtn.addEventListener('click', () => Shortcuts.showHelp());
        header.insertBefore(helpBtn, header.firstChild);
    }
});


